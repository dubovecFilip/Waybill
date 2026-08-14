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
    private readonly JsonSerializerSettings _jsonSettings = new() {
        ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
        NullValueHandling = NullValueHandling.Ignore,
        Formatting = Formatting.None,
    };

    private readonly object _gate = new();
    private readonly JobTracker _tracker = new();
    private readonly DeliveryStore _store;
    private readonly StreamWriter _writer;
    private readonly string _inProgressPath;

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
    public string? StartupError { get; private set; }

    /// <summary>True once the game has actually pushed telemetry through.</summary>
    public bool Connected { get; private set; }

    public event Action<string>? Message;
    public event Action<JobInfo>? JobStarted;
    public event Action<JobInfo>? JobResumed;
    public event Action<JobRecord>? JobFinished;

    public TrackerEngine(DeliveryStore store) {
        _store = store;

        // Next to the database rather than under bin/: recordings are data, and a
        // rebuild or `dotnet clean` used to wipe them along with the build output.
        var outDir = Path.Combine(DeliveryStore.DefaultDir(), "sessions");
        Directory.CreateDirectory(outDir);
        SessionPath = Path.Combine(outDir, $"session-{DateTime.Now:yyyyMMdd-HHmmss}.jsonl");
        _writer = new StreamWriter(SessionPath) { AutoFlush = true };

        _inProgressPath = Path.Combine(DeliveryStore.DefaultDir(), "in-progress.json");
    }

    /// <summary>Loads any interrupted job and connects to the game's shared memory.
    /// Returns false when shared memory can't be opened (StartupError says why).</summary>
    public bool Start() {
        LoadInProgress();

        // Recordings whose app was killed before it could pack them.
        var packed = SessionFiles.CompressOrphans(Path.GetDirectoryName(SessionPath)!, SessionPath);
        if (packed > 0) Message?.Invoke($"Zabalenych starsich nahravok: {packed}");

        // Poll at 100 ms so that no gameplay event flag flip is missed.
        _telemetry = new SCSSdkTelemetry(100);
        if (_telemetry.Error != null) {
            StartupError = _telemetry.Error.Message;
            return false;
        }

        _telemetry.Data += (data, updated) => {
            _last = data;
            Connected = true;
            if (!updated) return;
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
        Hook("RefuelStart", h => _telemetry.RefuelStart += h);
        Hook("RefuelEnd", h => _telemetry.RefuelEnd += h);
        Hook("RefuelPayed", h => _telemetry.RefuelPayed += h);

        return true;
    }

    private void Hook(string name, Action<EventHandler> subscribe) {
        subscribe((_, _) => {
            Write(name, _last);
            Message?.Invoke($"[{DateTime.Now:HH:mm:ss}] {name}");
        });
    }

    private void Write(string kind, SCSTelemetry? data) {
        if (data == null) return;
        lock (_gate) {
            // Tracking runs before Trim() strips fields it doesn't need - keeps the
            // two independent so a future Trim() change can't silently break tracking.
            var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            ProcessForTracking(kind, data, nowMs);

            Trim(data);
            var line = JsonConvert.SerializeObject(new { t = nowMs, kind, d = data }, _jsonSettings);
            _writer.WriteLine(line);
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
            if (ev.Type == TrackerEventType.JobResumed && ev.Job != null) {
                ActiveJob = ev.Job;
                JobResumed?.Invoke(ev.Job);
            }
            if (ev.Type == TrackerEventType.JobFinished && ev.Record != null) {
                ActiveJob = null;
                _store.SaveDelivery(ev.Record);
                DeliveriesThisRun++;
                ClearInProgress();
                JobFinished?.Invoke(ev.Record);
            }
        }
        SaveInProgress();
    }

    // The SDK always exposes 10 trailer slots with full per-wheel physics arrays even
    // when only one trailer is attached, plus truck fields nothing here reads. Together
    // that was about 60% of every line's size (~31 KB/line observed). Keep this in sync
    // with Adapter if either starts reading a field the other drops.
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

    private void LoadInProgress() {
        if (!File.Exists(_inProgressPath)) return;
        try {
            var saved = JsonConvert.DeserializeObject<JobState>(File.ReadAllText(_inProgressPath));
            // The fingerprint match in the tracker is the real guard, but an ancient
            // file is dropped outright: a job abandoned days ago is not one being
            // continued, and this removes any chance of it latching onto a
            // coincidentally identical job offer much later.
            var ageHours = saved == null ? 0 : (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - saved.StartedAtMs) / 3600000.0;
            if (saved != null && !string.IsNullOrEmpty(saved.Fingerprint) && ageHours < 24) {
                _tracker.PrepareResume(saved);
                Message?.Invoke($"Nedokoncena zakazka: {saved.Job.SourceCity} -> {saved.Job.DestinationCity} ({saved.DistanceKm:0.0} km) - nadviaze sa, ak v nej budes pokracovat.");
            } else if (saved != null) {
                Message?.Invoke($"Stara nedokoncena zakazka ({ageHours:0} h) sa ignoruje.");
                File.Delete(_inProgressPath);
            }
        } catch (Exception ex) {
            // A truncated/corrupt file must never stop the recorder from starting.
            Message?.Invoke($"Rozpracovanu zakazku sa nepodarilo nacitat, pokracujem bez nej: {ex.Message}");
        }
    }

    public void SaveInProgress(bool force = false) {
        var state = _tracker.ActiveState;
        if (state == null) return;
        if (!force && DateTime.UtcNow - _lastStateSave < _saveStateEvery) return;
        _lastStateSave = DateTime.UtcNow;
        try {
            // Write-then-replace, so an interrupted write can't leave a half-written
            // file where a complete one used to be.
            var tmp = _inProgressPath + ".tmp";
            File.WriteAllText(tmp, JsonConvert.SerializeObject(state));
            File.Move(tmp, _inProgressPath, overwrite: true);
        } catch (Exception ex) {
            Message?.Invoke($"Stav zakazky sa nepodarilo ulozit: {ex.Message}");
        }
    }

    private void ClearInProgress() {
        try {
            if (File.Exists(_inProgressPath)) File.Delete(_inProgressPath);
        } catch { /* it will be overwritten or ignored on the next start */ }
    }

    public void Dispose() {
        _telemetry?.Dispose();
        // Flush the in-progress job unthrottled, so quitting mid-delivery keeps the
        // last few seconds too and the next start resumes from the real position.
        lock (_gate) SaveInProgress(force: true);
        _writer.Flush();
        _writer.Dispose();

        // The recording is finished now, so pack it away (see SessionFiles).
        SessionFiles.Compress(SessionPath);
        GC.SuppressFinalize(this);
    }
}
