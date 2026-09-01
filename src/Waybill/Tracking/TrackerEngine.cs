using System.Collections.Concurrent;
using Newtonsoft.Json;
using SCSSdkClient;
using SCSSdkClient.Object;
using Waybill.Storage;

namespace Waybill.Tracking;

/// <summary>
/// The live side of the app: reads shared memory, records the raw session file,
/// runs the JobTracker, persists finished deliveries, and keeps the in-progress
/// job on disk so a restart can resume it.
///
/// Deliberately UI-agnostic - it raises events and exposes status, so the same
/// engine drives both the window and the console output.
/// </summary>
public class TrackerEngine : IDisposable {
    private static JsonSerializerSettings Recording(bool keepEvents) => new() {
        ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
        NullValueHandling = NullValueHandling.Ignore,
        Formatting = Formatting.None,
        ContractResolver = new RecordingContract(keepEvents),
    };

    // Ordinary snapshots leave the event block out; the extra line written when an
    // event fires keeps it. See RecordingContract for why.
    private readonly JsonSerializerSettings _tickJson = Recording(keepEvents: false);
    private readonly JsonSerializerSettings _eventJson = Recording(keepEvents: true);

    /// <summary>
    /// Decides what a recorded line leaves out. Nothing dropped here is information:
    /// every one of these is either computed from a value that stays, or a copy of
    /// one. A recording is the evidence a delivery can be recomputed from, so
    /// anything the game actually measured is kept even when nothing reads it yet.
    /// </summary>
    private sealed class RecordingContract : Newtonsoft.Json.Serialization.DefaultContractResolver {
        private readonly bool _keepEvents;

        public RecordingContract(bool keepEvents) => _keepEvents = keepEvents;

        protected override Newtonsoft.Json.Serialization.JsonProperty CreateProperty(
            System.Reflection.MemberInfo member, MemberSerialization serialization) {
            var property = base.CreateProperty(member, serialization);
            var owner = member.DeclaringType;
            var name = member.Name;

            var derived =
                // A speed is stored three times over: m/s, and that times 3.6 and
                // times 2.25. Km/h is the one everything reads, and the other two
                // follow from it.
                (owner == typeof(SCSTelemetry.Movement) && name is "Value" or "Mph")
                // Date is the same count of game minutes written out as a date.
                || (owner == typeof(SCSTelemetry.Time) && name == "Date")
                // Cabin camera geometry: fixed offsets, and one point expressed in
                // four different spaces. The world position is the one the tracker
                // reads and the only one that cannot be derived from the others.
                || (owner == typeof(SCSTelemetry.PositionData) && name != "HeadPositionInWorldSpace")
                // Event payloads hold their last value indefinitely, so on an
                // ordinary tick they describe something that already happened. Only
                // the line tagged with the event is believed, and that line keeps
                // them; on every other line they are a stale copy.
                || (!_keepEvents && owner == typeof(SCSTelemetry) && name == "GamePlay");

            if (derived) property.ShouldSerialize = _ => false;
            return property;
        }
    }

    private readonly object _gate = new();
    private readonly JobTracker _tracker = new();
    private readonly DeliveryStore _store;
    private readonly StreamWriter _writer;
    private readonly string _inProgressPath;

    // Finished lines waiting to reach the disk. Tracking must never wait on a write:
    // it used to flush every line inside the same lock that does the measuring, so a
    // disk busy elsewhere on the machine delayed the next measured sample, and the
    // hole that left was recorded as `client_gap` against the driver. Measuring now
    // hands the line over and carries on.
    private readonly BlockingCollection<string> _pending = new(20000);
    private readonly Thread _scribe;
    // Long enough that flushing is not per line, short enough that a crash costs a
    // couple of seconds of recording rather than the buffer.
    private readonly TimeSpan _flushEvery = TimeSpan.FromSeconds(2);

    private SCSSdkTelemetry? _telemetry;
    private SCSTelemetry? _last;
    private DateTime _lastSnapshot = DateTime.MinValue;
    private DateTime _lastStateSave = DateTime.MinValue;
    private readonly TimeSpan _snapshotEvery = TimeSpan.FromSeconds(1);
    private readonly TimeSpan _saveStateEvery = TimeSpan.FromSeconds(5);

    public string SessionPath { get; }
    public string DbPath => _store.DbPath;
    public long TickCount { get; private set; }
    public long LineCount { get; private set; }
    public int DeliveriesThisRun { get; private set; }
    public JobInfo? ActiveJob { get; private set; }
    public JobState? ActiveState => _tracker.ActiveState;

    /// <summary>Where the truck is roaming with nothing on the hook, and which way it
    /// points. Both come straight from the tracker; the live page needs them to draw a
    /// map when there is no delivery to draw.</summary>
    public FreeroamState? Roaming => _tracker.Roaming;

    public double? Facing => _tracker.Facing;

    /// <summary>
    /// Shows a finished delivery as though it were being driven now.
    ///
    /// The live page is the one part of Waybill that cannot be photographed or shown
    /// to anybody without a game running and a load on the hook, which makes it the
    /// one part nobody can see before they install it. This puts a real delivery,
    /// read back out of the database and cut off part way, into the page that would
    /// be showing it: real figures, real route, real drawing code, and nothing
    /// invented.
    ///
    /// It changes nothing else. No telemetry is read, nothing is written, and the
    /// moment a game does connect the tracker goes back to what the game says.
    /// </summary>
    public void ShowDemo(JobInfo job, JobState state) {
        ActiveJob = job;
        _demo = true;
        _tracker.ShowDemo(state);
        // Nothing on disk is touched. A demonstration is never saved in the first
        // place, and an unfinished delivery waiting for its driver has nothing to do
        // with somebody looking at a finished one.
    }

    /// <summary>Whether the job on the page is a demonstration. Nothing about one is
    /// written down: it is a finished delivery being shown twice, and saved it would
    /// become a second delivery that never happened, counted in the totals of a
    /// driver who only wanted to look at the page.</summary>
    private bool _demo;
    public string? StartupError { get; private set; }

    /// <summary>An unfinished job was found on disk and is waiting to be picked back
    /// up. Distinguishes a restart mid delivery, where nothing is missed, from a first
    /// start with the game already running, where the drive so far is already lost.</summary>
    public bool HasPendingResume { get; private set; }

    /// <summary>
    /// Whether a game is there right now, rather than whether one ever was.
    ///
    /// The plugin clears its own flag on the way out, and the poll keeps running after
    /// that, so a game closing is noticed within a tenth of a second. The staleness
    /// check behind it is for the other way out: a game that dies without clearing
    /// anything leaves the last values sitting in shared memory, and then the only
    /// evidence is that nobody is writing to it any more.
    /// </summary>
    public bool Connected => _sdkActive && DateTime.UtcNow - _lastPoll < TimeSpan.FromSeconds(15);
    private volatile bool _sdkActive;
    private DateTime _lastPoll = DateTime.MinValue;

    /// <summary>Which world a delivery finished now should be recorded as. Answered by
    /// the app from its settings; null means the driver has not said.</summary>
    public Func<string, string>? WorldForNewDelivery { get; set; }

    /// <summary>Where the truck was last seen, or nothing once the game has gone.</summary>
    public (float X, float Z)? Where => _tracker.Where;

    /// <summary>Which game that was. Empty once there is none, which is what lets the
    /// live page put the other game's map up the moment it starts.</summary>
    public string WhereGame => _tracker.WhereGame;

    public event Action<string>? Message;
    public event Action<JobInfo>? JobStarted;
    public event Action<JobInfo>? JobResumed;
    public event Action<JobRecord>? JobFinished;

    /// <summary>Something that happened mid drive, as it happens. Carries the figure
    /// with it, so the log can say how much the fine was rather than only that there
    /// was one.</summary>
    public event Action<JobEvent>? Noted;

    public TrackerEngine(DeliveryStore store) {
        _store = store;

        // Next to the database rather than under bin/: recordings are data, and a
        // rebuild or `dotnet clean` used to wipe them along with the build output.
        var outDir = Path.Combine(DeliveryStore.DefaultDir(), "sessions");
        Directory.CreateDirectory(outDir);
        SessionPath = Path.Combine(outDir, $"session-{DateTime.Now:yyyyMMdd-HHmmss}.jsonl");
        // AutoFlush off on purpose: the scribe below decides when to flush, off the
        // measuring thread.
        _writer = new StreamWriter(SessionPath) { AutoFlush = false };

        _inProgressPath = Path.Combine(DeliveryStore.DefaultDir(), "in-progress.json");

        _scribe = new Thread(Scribe) { Name = "waybill-recorder", IsBackground = true };
        _scribe.Start();
    }

    /// <summary>Takes finished lines off the queue and puts them on disk. The only
    /// thread that touches the writer, so nothing here needs a lock.</summary>
    private void Scribe() {
        var lastFlush = DateTime.UtcNow;
        try {
            foreach (var line in _pending.GetConsumingEnumerable()) {
                _writer.WriteLine(line);
                // Flushing once the queue is empty keeps the recording within a line
                // or two of live during ordinary play, while a burst still gets
                // written out in one go.
                if (_pending.Count == 0 || DateTime.UtcNow - lastFlush >= _flushEvery) {
                    _writer.Flush();
                    lastFlush = DateTime.UtcNow;
                }
            }
            _writer.Flush();
        } catch (Exception ex) {
            // A recording that cannot be written is worth saying out loud, but it must
            // not take the tracking down with it: the delivery still reaches the
            // database, it just loses the raw evidence behind it.
            Message?.Invoke($"{Waybill.Strings.T("msg.recordingFailed")}: {ex.Message}");
        }
    }

    /// <summary>Loads any interrupted job and connects to the game's shared memory.
    /// Returns false when shared memory can't be opened (StartupError says why).</summary>
    public bool Start() {
        LoadInProgress();

        // Recordings whose app was killed before it could pack them.
        var packed = SessionFiles.CompressOrphans(Path.GetDirectoryName(SessionPath)!, SessionPath);
        if (packed > 0) Message?.Invoke($"{Waybill.Strings.T("msg.packedOld")}: {packed}");

        // Poll at 100 ms so that no gameplay event flag flip is missed.
        _telemetry = new SCSSdkTelemetry(100);
        if (_telemetry.Error != null) {
            StartupError = _telemetry.Error.Message;
            return false;
        }

        _telemetry.Data += (data, updated) => {
            _last = data;
            _lastPoll = DateTime.UtcNow;
            var was = _sdkActive;
            _sdkActive = data.SdkActive;
            // The game going away is worth handling once, at the moment it happens:
            // whatever was being roamed is closed where it was last seen, and the map
            // stops claiming a truck that is not there.
            if (was && !_sdkActive) NoticeGone();
            if (!_sdkActive || !updated) return;
            TickCount++;
            if (DateTime.UtcNow - _lastSnapshot >= _snapshotEvery) {
                _lastSnapshot = DateTime.UtcNow;
                Write("tick", data);
            }
        };

        Hook("JobStarted", h => _telemetry.JobStarted += h);
        Hook("JobDelivered", h => _telemetry.JobDelivered += h);
        Hook("JobCancelled", h => _telemetry.JobCancelled += h);
        Hook("Fined", h => _telemetry.Fined += h);
        Hook("Tollgate", h => _telemetry.Tollgate += h);
        Hook("Ferry", h => _telemetry.Ferry += h);
        Hook("Train", h => _telemetry.Train += h);
        // The pump opening and closing carry no amount and are not always true:
        // servicing the truck at a dealer fires both with the tank untouched. Only
        // paying for fuel is real, and only that reaches the tracker.
        Hook("RefuelStart", h => _telemetry.RefuelStart += h);
        Hook("RefuelEnd", h => _telemetry.RefuelEnd += h);
        Hook("RefuelPayed", h => _telemetry.RefuelPayed += h);

        return true;
    }

    /// <summary>Records the event. Nothing is said about it here: the log is fed
    /// from <see cref="Noted"/> instead, which carries the figures and has already
    /// been through the tracker, so a refuel that moved no fuel never reaches it.
    /// What is worth keeping as evidence and what is worth telling someone about
    /// are different questions.</summary>
    private void Hook(string name, Action<EventHandler> subscribe) {
        subscribe((_, _) => Write(name, _last));
    }

    /// <summary>
    /// Hands the tracker the news that the game has gone.
    ///
    /// Nothing is written to the recording for it: a game closing is not something that
    /// happened in the world, it is the world ending. What it does do is close whatever
    /// stretch of free driving was open, at the last place it was seen, and forget where
    /// the truck was, so that starting the other game does not begin with a truck parked
    /// in the wrong country.
    /// </summary>
    private void NoticeGone() {
        lock (_gate) {
            var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            foreach (var ev in _tracker.Update(null, nowMs)) {
                if (ev.Type == TrackerEventType.FreeroamFinished && ev.Freeroam != null) {
                    try { _store.SaveFreeroam(ev.Freeroam); } catch { /* the game leaving is not worth failing over */ }
                }
            }
        }
        Message?.Invoke(Waybill.Strings.T("msg.gameGone"));
    }

    private void Write(string kind, SCSTelemetry? data) {
        if (data == null) return;
        lock (_gate) {
            // Tracking runs before Trim() strips fields it doesn't need - keeps the
            // two independent so a future Trim() change can't silently break tracking.
            var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            ProcessForTracking(kind, data, nowMs);

            Trim(data);
            var line = JsonConvert.SerializeObject(new { t = nowMs, kind, d = data },
                kind == "tick" ? _tickJson : _eventJson);
            // Handed to the scribe rather than written here. Serialising stays on this
            // thread because the object is about to be reused by the next poll, and it
            // costs a fraction of what waiting for the disk did.
            if (!_pending.IsAddingCompleted) _pending.Add(line);
            LineCount++;
        }
    }

    private void ProcessForTracking(string kind, SCSTelemetry data, long nowMs) {
        var snap = Adapter.ToSnapshot(data, kind);
        foreach (var ev in _tracker.Update(snap, nowMs)) {
            if (ev.Type == TrackerEventType.JobStarted && ev.Job != null) {
                ActiveJob = ev.Job;
                JobStarted?.Invoke(ev.Job);
                SaveInProgress(force: true);
            }
            if (ev.Type == TrackerEventType.Noted && ev.Note != null) {
                Noted?.Invoke(ev.Note);
            }
            if (ev.Type == TrackerEventType.JobResumed && ev.Job != null) {
                ActiveJob = ev.Job;
                JobResumed?.Invoke(ev.Job);
            }
            if (ev.Type == TrackerEventType.FreeroamFinished && ev.Freeroam != null) {
                _store.SaveFreeroam(ev.Freeroam);
            }
            if (ev.Type == TrackerEventType.JobFinished && ev.Record != null) {
                ActiveJob = null;
                // Which world it was driven in, if the driver has said which one they
                // play. Asked here rather than carried through the tracker, since it is
                // nothing the telemetry knows and nothing a replay could work out.
                ev.Record.MapWorld = WorldForNewDelivery?.Invoke(ev.Record.Game) ?? "";
                _store.SaveDelivery(ev.Record);
                DeliveriesThisRun++;
                ClearInProgress(ev.Record.Game);
                JobFinished?.Invoke(ev.Record);
            }
        }
        SaveInProgress();
    }

    // The SDK always exposes 10 trailer slots with full per-wheel physics arrays even
    // when only one trailer is attached, plus truck fields nothing here reads. Together
    // that was about 60% of every line's size (~31 KB/line observed). Keep this in sync
    // with Adapter if either starts reading a field the other drops.
    // The SDK declares these properties non-nullable, so clearing them warns. That is
    // exactly the intent here: this object is on its way to being serialized and
    // never read again, so the fields are dropped rather than written out.
#pragma warning disable CS8625 // Cannot convert null literal to non-nullable reference type
    private static void Trim(SCSTelemetry data) {
        data.TrailerValues = (data.TrailerValues ?? Array.Empty<SCSTelemetry.Trailer>())
            .Where(t => t.Attached)
            .ToArray();
        foreach (var t in data.TrailerValues) {
            t.AccelerationValues = null;
            t.WheelsConstant = null;
            t.Wheelvalues = null;
        }

        var cur = data.TruckValues?.CurrentValues;
        if (cur != null) {
            cur.MotorValues = null;
            cur.LightsValues = null;
            cur.WheelsValues = null;
            cur.AccelerationValues = null;
        }

        // Gearbox/wheel/warning specs are fixed for the whole session (same truck),
        // not per-tick state, so repeating them on every line was pure waste.
        var consts = data.TruckValues?.ConstantsValues;
        if (consts != null) {
            consts.MotorValues = null;
            consts.WheelsValues = null;
            consts.WarningFactorValues = null;
        }

        data.Substances = null;
        data.ControlValues = null;
    }
#pragma warning restore CS8625

    /// <summary>
    /// Hands whatever was left unfinished back to the tracker.
    ///
    /// One per game, and no time limit on either. A delivery half driven in Europe is
    /// still half driven a fortnight later, and playing America in between says nothing
    /// about it: the two games keep their own trucks, their own worlds and their own
    /// jobs, so Waybill keeps them apart as well. The fingerprint match in the tracker
    /// is what decides whether the offer that comes back is the same one; nothing is
    /// written off for having waited.
    /// </summary>
    private void LoadInProgress() {
        foreach (var path in UnfinishedFiles()) {
            try {
                var saved = JsonConvert.DeserializeObject<JobState>(File.ReadAllText(path));
                if (saved is null || string.IsNullOrEmpty(saved.Fingerprint)) continue;
                _tracker.PrepareResume(saved);
                HasPendingResume = true;
                Message?.Invoke($"{Waybill.Strings.T("msg.unfinishedFound")}: {saved.Job.SourceCity} -> {saved.Job.DestinationCity} ({saved.DistanceKm:0.0} km)");
            } catch (Exception ex) {
                // A truncated or corrupt file must never stop the recorder from starting.
                Message?.Invoke($"{Waybill.Strings.T("msg.unfinishedUnreadable")}: {ex.Message}");
            }
        }
    }

    /// <summary>Every unfinished job on disk, newest first, and the one written before
    /// there was a file per game, which is read once and then replaced by one.</summary>
    private IEnumerable<string> UnfinishedFiles() {
        var folder = Path.GetDirectoryName(_inProgressPath)!;
        var stem = Path.GetFileNameWithoutExtension(_inProgressPath);
        var found = new List<string>();
        if (File.Exists(_inProgressPath)) found.Add(_inProgressPath);
        try {
            found.AddRange(Directory.GetFiles(folder, stem + "-*.json"));
        } catch { /* nothing to resume is not a reason to fail to start */ }
        return found;
    }

    /// <summary>Where a game's unfinished job lives. Beside the old single file rather
    /// than in place of it, so a version that knew only one is not confused by the
    /// ones it does not.</summary>
    private string UnfinishedPathFor(string game) {
        var folder = Path.GetDirectoryName(_inProgressPath)!;
        var stem = Path.GetFileNameWithoutExtension(_inProgressPath);
        var tidy = string.IsNullOrWhiteSpace(game) ? "unknown" : game.ToLowerInvariant();
        return Path.Combine(folder, $"{stem}-{tidy}.json");
    }

    public void SaveInProgress(bool force = false) {
        var state = _tracker.ActiveState;
        if (state == null) return;
        var path = UnfinishedPathFor(state.Game);
        // A demonstration is never written down. Saved here it would be picked up as
        // an unfinished job by the next start, found to be days old, and written off
        // as a delivery: the driver would come back to a cancelled duplicate of a run
        // they had already made, sitting in their history and counted in their totals.
        if (_demo) return;
        if (!force && DateTime.UtcNow - _lastStateSave < _saveStateEvery) return;
        _lastStateSave = DateTime.UtcNow;
        try {
            // Write-then-replace, so an interrupted write can't leave a half-written
            // file where a complete one used to be.
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonConvert.SerializeObject(state));
            File.Move(tmp, path, overwrite: true);
            // The single file from before there was one per game, once its contents
            // have somewhere better to live.
            if (File.Exists(_inProgressPath)) File.Delete(_inProgressPath);
        } catch (Exception ex) {
            Message?.Invoke($"{Waybill.Strings.T("msg.stateSaveFailed")}: {ex.Message}");
        }
    }

    /// <summary>Forgets the unfinished job of whichever game just finished one. The
    /// other game's, if it has one, is none of this one's business.</summary>
    private void ClearInProgress(string game = "") {
        try {
            foreach (var path in game.Length > 0
                         ? new[] { UnfinishedPathFor(game) }
                         : UnfinishedFiles().ToArray()) {
                if (File.Exists(path)) File.Delete(path);
            }
        } catch { /* it will be overwritten or ignored on the next start */ }
    }

    public void Dispose() {
        _telemetry?.Dispose();
        // Flush the in-progress job unthrottled, so quitting mid-delivery keeps the
        // last few seconds too and the next start resumes from the real position.
        lock (_gate) SaveInProgress(force: true);

        // A stretch driven with nothing on the hook has no event to end it, so the
        // one still open when the app closes would otherwise be lost.
        lock (_gate) {
            foreach (var stretch in _tracker.FinishRoaming()) {
                try { _store.SaveFreeroam(stretch); } catch { /* shutting down anyway */ }
            }
        }

        // Let the scribe drain what is still queued before the file is closed under
        // it, otherwise quitting loses the last seconds of the recording. Bounded, so
        // a jammed disk delays closing rather than preventing it.
        _pending.CompleteAdding();
        var drained = _scribe.Join(TimeSpan.FromSeconds(10));
        try {
            _writer.Flush();
            _writer.Dispose();
        } catch (Exception ex) when (!drained) {
            // The scribe outlasted its window and still holds the writer. Closing
            // anyway is right: the alternative is refusing to shut down.
            Message?.Invoke($"{Waybill.Strings.T("msg.recordingFailed")}: {ex.Message}");
        }

        // The recording is finished now, so pack it away (see SessionFiles).
        SessionFiles.Compress(SessionPath);
        GC.SuppressFinalize(this);
    }
}
