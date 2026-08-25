
namespace Waybill.Tracking;

public class TrackerConfig {
    // Anything faster than this between two ticks is treated as a teleport.
    public double TeleportSpeedKmh = 400;
    // Below this a negative odometer delta is just float noise, not a real reversal.
    public double OdometerSlackKm = 0.05;
    // Below this the tick is ignored entirely, protects against duplicate polls.
    public double MinTickMs = 100;
    // Above this a gap is treated as a client freeze or a reconnect.
    public double MaxTickMs = 10000;
    // How much over the posted limit counts as speeding.
    public double SpeedingToleranceKmh = 5;
    // Driving style is a separate question from "was the limit exceeded at all".
    // Everybody drifts a little over, and being a few km/h above for a while is not
    // what anyone means by driving like a pirate, so the style measure only counts
    // being clearly over. Ten above stays acceptable; past that it is deliberate.
    public double StyleSpeedingToleranceKmh = 10;
    // Share of driving time clearly over the limit that separates the two styles.
    public double StyleSpeedingShareMax = 0.05;
    // Collisions barely enter into it: one bad moment on a long haul says nothing
    // about how someone drives. Only a run of them does.
    public int StyleCollisionsMax = 3;
    // Same reasoning for fines. One is a moment of inattention on a long run, not a
    // way of driving, so it takes a handful of them to say anything.
    public int StyleFinesMax = 3;
    // The odometer counts simulated km at the game's compressed time rate, so at
    // highway speed a single ~1s poll legitimately advances it by ~0.4-0.7 km.
    // This cap is set well above that: it only catches gross manipulation, not
    // ordinary driving.
    public double MaxOdometerJumpKm = 5.0;
    // Game time advances ~13x real time, and only in whole-minute steps. A tick
    // covering more than this means the clock jumped (sleep, ferry, teleport)
    // rather than time simply passing, so it is left out of the speed integral.
    public double MaxTickGameMinutes = 5.0;
    // Ceiling on how fast the game clock can run compared to real time. The measured
    // rate is about 19x, so this is generously above ordinary play and far below a
    // sleep, which advances the clock by hours in seconds. Used to tell a gap the
    // app is responsible for from one the game caused by fast forwarding.
    public double MaxGameMinutesPerRealMinute = 60;
    // Used only when a recording predates the game reporting its own time scale.
    // Every recording measured so far reports 20.
    public double AssumedTimeScale = 20;
    // Game time is published in whole minutes, so an advance of one minute over a
    // gap of seconds says nothing: it means a minute boundary fell inside the gap.
    public double GameClockResolutionMin = 1;
    // How much of the expected advance counts as the clock having kept running.
    // Half leaves room for the rounding at both ends of a short gap without
    // accepting a clock that plainly stood still.
    public double RunningClockShare = 0.5;
    // Ordinary driving wears the truck by ~0.0006% per tick, very consistently.
    // An impact is orders of magnitude above that, so 0.1% in a single tick
    // separates the two with a lot of room to spare.
    public double CollisionDamageStep = 0.001;
    // Ceiling on the distance a resume may credit for time the app was closed.
    // Long enough to cover a genuinely long absence, short enough that a stale
    // file or a different truck's odometer can't invent a whole delivery.
    public double MaxResumeBridgeKm = 2000;
    // A Quick Job swaps the player into a company truck and teleports them to the
    // pickup point, but odometer/position can take a few extra ticks to settle
    // onto the new truck's real values. Teleport/odometer checks stay soft for
    // this long after a job starts so settle-in isn't mistaken for cheating.
    public double JobStartGraceMs = 15000;
    // The SDK clears JobValues/OnJob on the tick BEFORE JobCancelled actually
    // fires (observed 19ms apart, but a slow poll cycle could stretch that) -
    // closing the job the instant its data disappears means the real
    // cancelled/delivered event arrives one tick later to find _current already
    // null and gets silently dropped, turning a clean cancellation into a
    // falsely-rejected "unresolved" job. Wait this long for the proper event
    // before giving up and closing it as unresolved for real.
    public double MissingJobGraceMs = 3000;
    // Loading a save from before the job was accepted makes the job data vanish
    // a moment after the load, with no cancellation event behind it. Within this
    // window of a load, a job going missing is the load's doing rather than a
    // completion event that failed to arrive.
    public double SaveLoadWindowMs = 10000;
}

public enum TrackerEventType { JobStarted, JobResumed, JobFinished, Noted, FreeroamFinished }

/// <summary>What is being gathered about a stretch driven with nothing on the
/// hook. Not serialised to disk mid-drive the way a job is: losing a freeroam
/// stretch to a crash costs a line on a map, while losing a delivery costs work.</summary>
public class FreeroamState {
    public string Game = "";
    public long StartedAtMs;
    public long LastAtMs;
    public double LastOdometerKm;
    public double DistanceKm;
    public List<TripPoint> Points = new();
}

public class TrackerEvent {
    public TrackerEventType Type;
    public JobInfo? Job; // JobStarted / JobResumed
    public JobRecord? Record; // JobFinished
    public FreeroamRecord? Freeroam; // FreeroamFinished
    /// <summary>Something that just happened during the drive: a fine, a collision,
    /// a refuel. The same entry that ends up on the delivery's timeline.</summary>
    public JobEvent? Note; // Noted
}

/// <summary>
/// Everything accumulated for the job currently being driven. Public and plainly
/// serializable on purpose: it is written to disk while a job is in progress so a
/// restart mid-delivery can pick the job back up instead of losing it (which is
/// what produced the "unresolved" records in the early recordings).
/// </summary>
public class JobState {
    public string JobUid = "";
    public string Fingerprint = "";
    public long StartedAtMs;
    public double StartedAtGameMin;
    public string Game = "";
    public string GameVersion = "";
    public JobInfo Job = new();
    public string TruckMake = "";
    public string TruckModel = "";
    public string TruckId = "";
    public string? TrailerName;
    public string TrailerId = "";
    /// <summary>The coupled set at its longest, and how worn each unit was when it
    /// was first seen and last seen. Keyed by plate, which is unique per unit.</summary>
    public List<TrailerUnit> TrailerChain = new();
    public Dictionary<string, double> TrailerStartWear = new();
    public Dictionary<string, double> TrailerLastWear = new();

    public double StartTruckWear;
    public double StartTrailerWear;
    public double StartFuelL;
    /// <summary>Fuel added since the last pump receipt, which is what the next
    /// one is for. Reset when it is claimed.</summary>
    public double PendingRefuelL;

    public double DistanceKm;
    public double WorldDistanceKm;
    public double SimSpeedDistanceKm;
    public double DrivingGameMinutes;
    public double FuelUsedL;
    public long DrivingMs;
    public long PausedMs;
    public long SpeedingMs;
    /// <summary>Time spent clearly over the limit, not merely over it. Feeds the
    /// driving style; <see cref="SpeedingMs"/> stays the strict measure.</summary>
    public long HardSpeedingMs;
    public double TopSpeedKmh;
    public List<FineRecord> Fines = new();
    public double Tolls;
    public int Ferries;
    public int Refuels;
    public int Collisions;
    public int RestStops;
    public double RestMinutes;
    public long CruiseControlMs;
    public List<Anomaly> Anomalies = new();
    public List<JobEvent> Timeline = new();
    public List<TripPoint> TripPoints = new();
    /// <summary>Whether the load has been hitched up yet. Only the first coupling of
    /// a job is worth marking; dropping and re-hitching later is the same load.</summary>
    public bool TrailerCoupled;
    /// <summary>Whether the cargo is on the trailer yet. Separate from the coupling
    /// because which of the two comes last depends on the kind of job.</summary>
    public bool CargoOn;
    /// <summary>When both became true, which is when the delivery starts being about
    /// the consignment. Zero until then.</summary>
    public long LoadOnAtMs;
    /// <summary>The part of <see cref="DistanceKm"/> driven before the load was on.
    ///
    /// A World of Trucks contract starts the odometer where the offer was accepted,
    /// so getting to the trailer counts as the job. It is real driving and stays in
    /// the total, but it is not the consignment moving, and the game plans its route
    /// from the load rather than from the driver: measured over this history the
    /// planned figure matches the loaded leg to within about a percent, while the
    /// total ran up to twelve percent over it. Kept apart so progress can be shown
    /// against the thing the plan actually describes.</summary>
    public double DistanceToLoadKm;
    public long? MissingJobSinceMs;
    /// <summary>When an earlier save was last loaded, so a job that disappears in the
    /// wake of one is recognised as gone with the save rather than unresolved.</summary>
    public long? SaveLoadedAtMs;
    /// <summary>Last odometer reading seen. The odometer is absolute and cumulative,
    /// so on resume the difference against it recovers everything driven while the
    /// app was closed - without it a restart silently loses that distance.</summary>
    public double LastOdometerKm;
}

/// <summary>
/// Job lifecycle state machine. Deliberately free of any IO - feed it normalised
/// Snapshots and it emits job started/resumed/finished events.
///
/// Regression testing is done by replaying recorded sessions through this same
/// code: `--replay &lt;file&gt;` for a whole drive, `--test-resume &lt;file&gt; &lt;line&gt;` to
/// simulate a restart mid-delivery.
/// </summary>
public class JobTracker {
    private enum State { Idle, Accepted, Driving }

    private readonly TrackerConfig _config;
    private State _state = State.Idle;
    private JobState? _current;
    /// <summary>Driving with nothing on the hook, gathered the same way a job is.
    /// Null whenever a load is being pulled.</summary>
    private FreeroamState? _roam;
    private JobState? _pendingResume;
    private Snapshot? _prev;
    private long? _prevAtMs;

    public JobTracker(TrackerConfig? config = null) {
        _config = config ?? new TrackerConfig();
    }

    /// <summary>State of the job in progress, or null when idle. Persist this while
    /// driving so a restart can hand it back via <see cref="PrepareResume"/>.</summary>
    public JobState? ActiveState => _current;

    /// <summary>Puts a job in front of the tracker without any telemetry behind it,
    /// for the demonstration mode. See <see cref="TrackerEngine.ShowDemo"/>.</summary>
    public void ShowDemo(JobState state) => _current = state;

    /// <summary>Offer a previously persisted job back to the tracker. It is only
    /// adopted if the next job the game reports is the same one (same fingerprint);
    /// anything else is ignored, so a stale file can't attach itself to a new job.</summary>
    public void PrepareResume(JobState state) => _pendingResume = state;

    /// <summary>Feed one telemetry snapshot. Returns job_started/job_finished events.</summary>
    public List<TrackerEvent> Update(Snapshot? snap, long nowMs) {
        var outEvents = new List<TrackerEvent>();

        if (snap == null || !snap.Connected) {
            _prev = null;
            _prevAtMs = null;
            // The game went away. Whatever was being roamed ends where it was last
            // seen rather than joining itself to the next session days later.
            if (CloseRoam() is { } lost) {
                outEvents.Add(new TrackerEvent { Type = TrackerEventType.FreeroamFinished, Freeroam = lost });
            }
            return outEvents;
        }

        var prev = _prev;
        var prevAt = _prevAtMs;
        _prev = snap;
        _prevAtMs = nowMs;

        // First tick after a connect has nothing to compare against.
        if (prev == null || prevAt == null) return outEvents;

        var dtMs = nowMs - prevAt.Value;

        // The SDK fires its events on the very poll that produced the last snapshot,
        // so an event line lands 0 to 20 ms behind the tick before it. Dropping a
        // snapshot that close used to throw the event away with it: a fine issued at
        // 0 ms showed up in the live log, because that is raised separately, and then
        // never reached the delivery or the statistics. Only the movement half of a
        // snapshot is worthless this close together, never the event half.
        var carriesEvent = snap.Events.JobDelivered != null || snap.Events.JobCancelled != null
            || snap.Events.Fined != null || snap.Events.TollgatePaid != null
            || snap.Events.FerryUsed != null || snap.Events.TrainUsed != null
            || snap.Events.RefuelPaid != null;
        var instant = dtMs < _config.MinTickMs;
        if (instant && !carriesEvent) return outEvents;

        var gap = dtMs > _config.MaxTickMs;
        var fp = Fingerprint(snap.Job);
        var same = SameJob(snap.Job);

        // Loading an earlier save is the one thing that moves the game clock
        // backwards. Time never runs backwards while driving, and a teleport does
        // not touch the clock at all, which makes the sign of this delta the way
        // to tell an honest reload from the position jump it otherwise looks
        // exactly like. Both readings have to be real: the clock reads 0 until the
        // world finishes loading, and a 0 would look like an enormous rewind.
        // Detected here rather than in Accumulate() because a save from before the
        // job was accepted takes the job data with it, and that path never reaches
        // accumulation.
        var reloaded = prev.GameTimeMin > 0 && snap.GameTimeMin > 0 && snap.GameTimeMin < prev.GameTimeMin;
        if (reloaded && _current != null) _current.SaveLoadedAtMs = nowMs;

        // A finished job wins over everything else this tick.
        var delivered = snap.Events.JobDelivered;
        var cancelled = snap.Events.JobCancelled;

        if (_current != null && (delivered != null || cancelled != null)) {
            Accumulate(snap, prev, dtMs, gap, reloaded, instant, nowMs);
            var record = Close(snap, delivered != null ? "delivered" : "cancelled",
                delivered != null ? (object)delivered : cancelled!, nowMs);
            outEvents.Add(new TrackerEvent { Type = TrackerEventType.JobFinished, Record = record });
            _state = State.Idle;
            _current = null;
            return outEvents;
        }

        // New job accepted. Whatever was being driven up to now was not a delivery,
        // so it is closed off here rather than being swallowed by the job.
        if (fp != null && same != SameJob(_current?.Job)) {
            if (CloseRoam() is { } roamed) {
                outEvents.Add(new TrackerEvent { Type = TrackerEventType.FreeroamFinished, Freeroam = roamed });
            }
            if (_current != null) {
                // The old job vanished without an event. Close it as unresolved and flag it.
                var record = Close(prev, LostOutcome(nowMs), null, nowMs);
                outEvents.Add(new TrackerEvent { Type = TrackerEventType.JobFinished, Record = record });
            }

            // Same job we were driving before the restart? Pick it back up with
            // everything already accumulated rather than starting from zero. Every
            // part of the job has to match, so a leftover file from a different one
            // is simply dropped; only the payout is left out of the comparison,
            // because the game moves it while you drive.
            if (_pendingResume != null && SameJob(_pendingResume.Job) == same) {
                _current = _pendingResume;
                _pendingResume = null;
                _current.MissingJobSinceMs = null;
                _state = State.Driving;

                // Bridge the downtime. The odometer is absolute, so the difference
                // against the last reading covers everything driven while the app was
                // closed. Only when it is still the same truck and the step is sane -
                // a different truck's odometer would be meaningless here. Flagged so
                // the unobserved stretch stays visible in the record.
                var bridgeKm = snap.Truck.OdometerKm - _current.LastOdometerKm;
                var sameTruck = snap.Truck.Make == _current.TruckMake && snap.Truck.Model == _current.TruckModel;
                if (sameTruck && bridgeKm > 0 && bridgeKm < _config.MaxResumeBridgeKm) {
                    _current.DistanceKm += bridgeKm;
                    if (!_current.TrailerCoupled || !_current.CargoOn) _current.DistanceToLoadKm += bridgeKm;
                    _current.Anomalies.Add(new Anomaly { AtMs = nowMs, Code = "resume_gap", Delta = bridgeKm });
                }
                _current.LastOdometerKm = snap.Truck.OdometerKm;

                outEvents.Add(new TrackerEvent { Type = TrackerEventType.JobResumed, Job = _current.Job });
                return outEvents;
            }
            _pendingResume = null;

            Open(snap, fp, nowMs);
            _state = State.Accepted;
            outEvents.Add(new TrackerEvent { Type = TrackerEventType.JobStarted, Job = _current!.Job });
            return outEvents;
        }

        // Job data disappeared. The SDK is known to clear JobValues/OnJob on the
        // tick BEFORE actually firing JobCancelled (observed 19ms apart - a slow
        // poll cycle could stretch that further), so closing this the instant the
        // data vanishes means the real completion event arrives one tick later to
        // find _current already null and gets silently dropped - a clean
        // cancellation/delivery turns into a falsely-rejected "unresolved" job.
        // Give the real event a short window to show up first.
        if (fp == null && _current != null) {
            _current.MissingJobSinceMs ??= nowMs;
            if (nowMs - _current.MissingJobSinceMs.Value < _config.MissingJobGraceMs) {
                return outEvents;
            }
            var record = Close(prev, LostOutcome(nowMs), null, nowMs);
            outEvents.Add(new TrackerEvent { Type = TrackerEventType.JobFinished, Record = record });
            _state = State.Idle;
            _current = null;
            return outEvents;
        }

        if (_current == null) {
            Roam(snap, prev, gap, nowMs);
            foreach (var done in _pendingRoam) {
                outEvents.Add(new TrackerEvent { Type = TrackerEventType.FreeroamFinished, Freeroam = done });
            }
            _pendingRoam.Clear();
            return outEvents;
        }

        _current.MissingJobSinceMs = null;

        // Anything the accumulation adds to the timeline is raised as it happens, so
        // the live log can say what occurred instead of only the delivery's card
        // learning about it once the job is over.
        var timelineWas = _current.Timeline.Count;
        Accumulate(snap, prev, dtMs, gap, reloaded, instant, nowMs);
        for (var i = timelineWas; i < _current.Timeline.Count; i++) {
            outEvents.Add(new TrackerEvent { Type = TrackerEventType.Noted, Note = _current.Timeline[i] });
        }

        if (_state == State.Accepted && _current.DistanceKm > 0.05) {
            _state = State.Driving;
        }

        return outEvents;
    }

    /// <summary>
    /// Gathers a stretch driven with nothing on the hook.
    ///
    /// The same odometer the deliveries are measured with, and the same refusals: a
    /// step too large for driving is a teleport or a different truck's reading and
    /// is not travel. There is no verdict at the end of this and nothing to cheat,
    /// so nothing is flagged; a step that cannot be believed is simply not counted.
    ///
    /// A hole in the recording ends the stretch and starts another. Driving from a
    /// garage in one city to a garage in another over two evenings is two lines on
    /// the map, and joining them would draw a road between them that was never
    /// taken.
    /// </summary>
    private void Roam(Snapshot snap, Snapshot prev, bool gap, long nowMs) {
        // Nothing to measure against until the world has finished loading.
        if (snap.Paused || snap.Truck.OdometerKm <= 0 || prev.Truck.OdometerKm <= 0) return;

        if (_roam != null && (gap || _roam.Game != snap.Game)) {
            var broken = CloseRoam();
            if (broken != null) _pendingRoam.Add(broken);
        }

        if (_roam == null) {
            _roam = new FreeroamState {
                Game = snap.Game,
                StartedAtMs = nowMs,
                LastOdometerKm = snap.Truck.OdometerKm,
            };
        }

        var step = snap.Truck.OdometerKm - _roam.LastOdometerKm;
        if (step > 0 && step <= _config.MaxOdometerJumpKm) _roam.DistanceKm += step;
        _roam.LastOdometerKm = snap.Truck.OdometerKm;
        _roam.LastAtMs = nowMs;
        _roam.Points.Add(new TripPoint { AtMs = nowMs, X = snap.PosX, Y = snap.PosY, Z = snap.PosZ, SpeedKmh = snap.Truck.SpeedKmh });
    }

    /// <summary>Ends the current stretch, if it went anywhere. Standing in a garage
    /// for ten minutes is not a journey, and a history full of hundred metre stubs
    /// would bury the ones worth looking at.</summary>
    private FreeroamRecord? CloseRoam() {
        var r = _roam;
        _roam = null;
        if (r == null || r.DistanceKm < MinFreeroamKm || r.Points.Count < 3) return null;
        return new FreeroamRecord {
            Game = r.Game,
            StartedAtMs = r.StartedAtMs,
            EndedAtMs = r.LastAtMs,
            DistanceKm = Math.Round(r.DistanceKm, 3),
            TripPoints = r.Points,
        };
    }

    /// <summary>Below this a stretch is manoeuvring, not travelling.</summary>
    public const double MinFreeroamKm = 0.5;

    /// <summary>Stretches finished by a break in the recording, waiting to be handed
    /// out with the next batch of events.</summary>
    private readonly List<FreeroamRecord> _pendingRoam = new();

    /// <summary>Ends whatever is still open, for shutdown and for the end of a
    /// replay. Without it the last stretch of every session is thrown away.</summary>
    public List<FreeroamRecord> FinishRoaming() {
        var all = new List<FreeroamRecord>(_pendingRoam);
        _pendingRoam.Clear();
        if (CloseRoam() is { } last) all.Add(last);
        return all;
    }

    /// <summary>Distance covered so far, how much of it was driven before the load
    /// was on, and the job's planned distance, for a live progress display. Null when
    /// no job is active.</summary>
    public (double DistanceKm, double DistanceToLoadKm, double PlannedDistanceKm)? Progress() =>
        _current == null ? null
            : (_current.DistanceKm, _current.DistanceToLoadKm, _current.Job.PlannedDistanceKm);

    /// <summary>How long an unfinished job stays resumable. A week covers coming back
    /// to a delivery after a crash, a reinstall or simply a break; past that the offer
    /// it belongs to is long gone and the job is written off.</summary>
    public const double ResumeMaxAgeHours = 24 * 7;

    /// <summary>Closes a job that was left hanging past the resume window, from the
    /// state persisted on disk alone. There is no final snapshot to measure against,
    /// so what was accumulated before the interruption is what gets reported: the
    /// distance is real driving and belongs in the history, it simply never arrived.</summary>
    public static JobRecord CloseAbandoned(JobState j, long nowMs) {
        var record = new JobRecord {
            JobUid = j.JobUid,
            Outcome = "cancelled",
            JobType = Adapter.MarketName(j.Job.Market),
            SpecialTransport = j.Job.SpecialJob,
            Game = j.Game,
            GameVersion = j.GameVersion,
            StartedAtMs = j.StartedAtMs,
            FinishedAtMs = nowMs,
            RealDurationMs = j.DrivingMs,
            GameDurationMin = Math.Round(j.DrivingGameMinutes, 1),
            SourceCity = j.Job.SourceCity,
            SourceCompany = j.Job.SourceCompany,
            DestinationCity = j.Job.DestinationCity,
            DestinationCompany = j.Job.DestinationCompany,
            Cargo = j.Job.Cargo,
            CargoId = j.Job.CargoId,
            CargoMassKg = j.Job.CargoMassKg,
            PlannedDistanceKm = j.Job.PlannedDistanceKm,
            OfferedIncome = j.Job.Income,
            TruckMake = j.TruckMake,
            TruckModel = j.TruckModel,
            TruckId = j.TruckId,
            TrailerName = j.TrailerName,
            TrailerId = j.TrailerId,
            TrailerChainType = ChainType(j),
            TrailerOwned = j.TrailerChain.Any(u => u.IsOwned),
            TrailerUnits = TrailerUnits(j),
            DistanceKm = Math.Round(j.DistanceKm, 3),
            DistanceToLoadKm = Math.Round(j.DistanceToLoadKm, 3),
            WorldDistanceKm = Math.Round(j.WorldDistanceKm, 3),
            SimSpeedDistanceKm = Math.Round(j.SimSpeedDistanceKm, 3),
            DrivingGameMinutes = Math.Round(j.DrivingGameMinutes, 1),
            FuelUsedL = Math.Round(j.FuelUsedL, 2),
            AvgConsumptionLper100 = j.DistanceKm > 0.1 ? Math.Round(j.FuelUsedL / j.DistanceKm * 100, 2) : null,
            TopSpeedKmh = Math.Round(j.TopSpeedKmh, 1),
            DrivingMs = j.DrivingMs,
            PausedMs = j.PausedMs,
            SpeedingShare = j.DrivingMs > 0 ? Math.Round((double)j.SpeedingMs / j.DrivingMs, 4) : 0,
            HardSpeedingShare = j.DrivingMs > 0 ? Math.Round((double)j.HardSpeedingMs / j.DrivingMs, 4) : 0,
            Fines = j.Fines,
            TollsPaid = j.Tolls,
            FerriesUsed = j.Ferries,
            Refuels = j.Refuels,
            Collisions = j.Collisions,
            RestStops = j.RestStops,
            RestMinutes = Math.Round(j.RestMinutes, 1),
            CruiseControlShare = j.DrivingMs > 0 ? Math.Round((double)j.CruiseControlMs / j.DrivingMs, 4) : 0,
            Anomalies = j.Anomalies,
            Timeline = j.Timeline,
            TripPoints = j.TripPoints,
        };
        record.DrivingStyle = j.Fines.Count < 3
            && record.HardSpeedingShare < 0.05
            && j.Collisions < 3 ? "clean" : "spirited";
        record.Anomalies.Add(new Anomaly { AtMs = nowMs, Code = "abandoned" });
        // Not rejected: nothing here suggests the driving was faked, only that it was
        // never finished, which the cancelled outcome already says.
        record.Validation = new Validation { Flags = { "abandoned" }, Status = "review" };
        return record;
    }

    /// <summary>What to call a job that disappeared without a completion event. A save
    /// loaded moments earlier explains it: that save predates the job being accepted,
    /// so in the game's own history the delivery no longer exists. Nothing was skipped
    /// and nothing failed to arrive, which is why this must not be reported as a
    /// missing completion event and rejected as one.</summary>
    private string LostOutcome(long nowMs) =>
        _current?.SaveLoadedAtMs is long at && nowMs - at < _config.SaveLoadWindowMs
            ? "reloaded"
            : "unresolved";

    /// <summary>
    /// The cargo went on or came off between these two snapshots. Loading and
    /// unloading skip the game clock forward the same way a sleep does, and this is
    /// what tells them apart: one flatbed load of scrapped cars moved the clock 25
    /// minutes a minute into the job and was recorded as the driver having slept.
    /// </summary>
    /// <summary>
    /// Keeps the coupled set and how each unit of it fared. The set is remembered at
    /// its longest rather than at the end, because a triple is only a triple while
    /// everything is hitched, and telemetry drops units during a loading screen.
    /// Wear is read per unit, since the game only ever shows the worst of them.
    /// </summary>
    private static void TrackTrailerChain(JobState j, Snapshot snap) {
        var units = snap.Trailer.Units;
        if (units.Count == 0) return;

        foreach (var unit in units) {
            var key = UnitKey(unit);
            // First sight of a unit is its condition on being hitched, so damage
            // over this job is measured from there and a trailer handed over already
            // scratched does not count against the driver.
            if (!j.TrailerStartWear.ContainsKey(key)) j.TrailerStartWear[key] = unit.Wear;
            j.TrailerLastWear[key] = unit.Wear;
        }

        if (units.Count >= j.TrailerChain.Count) j.TrailerChain = units;
    }

    /// <summary>The configuration as the game names it. Only the leading unit
    /// carries it; a dolly and the sections behind it report nothing.</summary>
    private static string ChainType(JobState j) =>
        j.TrailerChain.FirstOrDefault(u => u.ChainType.Length > 0)?.ChainType ?? "";

    private static List<TrailerUnitRecord> TrailerUnits(JobState j) =>
        j.TrailerChain.Select(u => {
            var key = UnitKey(u);
            var start = j.TrailerStartWear.TryGetValue(key, out var s) ? s : u.Wear;
            var last = j.TrailerLastWear.TryGetValue(key, out var l) ? l : u.Wear;
            return new TrailerUnitRecord {
                Id = u.Id,
                Name = u.Name,
                Plate = u.Plate,
                BodyType = u.BodyType,
                Kind = u.IsDolly ? "dolly" : "trailer",
                Owned = u.IsOwned,
                // Never negative: a unit repaired mid job would otherwise read as
                // having taken away damage.
                Damage = Math.Max(0, Math.Round(last - start, 6)),
            };
        }).ToList();

    private static string UnitKey(TrailerUnit unit) =>
        unit.Plate.Length > 0 ? unit.Plate : unit.Id;

    private static bool CargoChangedHands(Snapshot snap, Snapshot prev) =>
        (snap.Job?.CargoLoaded ?? false) != (prev.Job?.CargoLoaded ?? false);

    /// <summary>
    /// The delivery's identity, computed from what makes it that delivery: the game,
    /// the moment the job was accepted, and the offer itself. It used to be a fresh
    /// guid, which meant a delivery had no identity at all: replaying the same
    /// recording produced a different one every time, so a rebuild could match
    /// nothing and had to delete the history before it could put any of it back.
    ///
    /// Kept to the same 32 hex characters a guid was written as, so nothing that
    /// already stores one has to care.
    /// </summary>
    private static string MakeUid(string game, string fingerprint, long startedAtMs) {
        var seed = $"{game}|{startedAtMs}|{fingerprint}";
        var hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(seed));
        return Convert.ToHexString(hash, 0, 16).ToLowerInvariant();
    }

    /// <summary>
    /// What the job is, for the identifier it will be stored under. Includes the
    /// payout because it is part of the offer that was accepted, and because the
    /// identifier has to keep deriving to the same value from the same recording
    /// forever: a rebuild that derived new ones would insert every delivery again
    /// instead of updating it.
    /// </summary>
    private static string? Fingerprint(JobInfo? job) {
        if (job == null) return null;
        return string.Join("|", job.SourceCity, job.SourceCompany, job.DestinationCity, job.DestinationCompany, job.Cargo, job.Income, job.DeadlineMin);
    }

    /// <summary>
    /// Whether two sightings are the same job, which is a different question from
    /// what the job is.
    ///
    /// Everything the fingerprint has except the payout. The payout is not a
    /// property of the job, it is the game's running estimate of what the job will
    /// pay, and it moves while you drive: one haul here was offered 111422 in the
    /// afternoon and 108668 the same evening, and was paid 74412 in the end.
    ///
    /// Comparing on it cost a delivery its first leg. The job was accepted and
    /// driven for 377 km, the game was closed for the evening, and when it came back
    /// the payout had drifted, so the job that was offered back did not look like
    /// the job that had been saved. The resume was refused and a second delivery
    /// opened over the top of the first. Worse, the same comparison decides whether
    /// the job in front of us is still the one we are driving, so a long enough haul
    /// could have been split in two without the game ever being closed.
    /// </summary>
    private static string? SameJob(JobInfo? job) {
        if (job == null) return null;
        return string.Join("|", job.SourceCity, job.SourceCompany, job.DestinationCity, job.DestinationCompany, job.Cargo, job.DeadlineMin);
    }

    private static double DistanceKmBetween(Snapshot a, Snapshot b) {
        var dx = a.PosX - b.PosX;
        var dy = a.PosY - b.PosY;
        var dz = a.PosZ - b.PosZ;
        return Math.Sqrt(dx * dx + dy * dy + dz * dz) / 1000.0;
    }

    private void Open(Snapshot snap, string fp, long nowMs) {
        _current = new JobState {
            JobUid = MakeUid(snap.Game, fp, nowMs),
            Fingerprint = fp,
            StartedAtMs = nowMs,
            StartedAtGameMin = snap.GameTimeMin,
            Game = snap.Game,
            GameVersion = snap.GameVersion,
            Job = snap.Job!,
            TruckMake = snap.Truck.Make,
            TruckModel = snap.Truck.Model,
            TruckId = snap.Truck.TruckId,
            TrailerName = snap.Trailer.Attached ? snap.Trailer.Name : null,
            TrailerId = snap.Trailer.TrailerId,
            StartFuelL = snap.Truck.FuelL,
            StartTruckWear = snap.Truck.Wear.Total(),
            // Not gated on Attached: TrailerInfo.Wear is already 0 when there's no
            // trailer (see Adapter), so gating on Attached here would only differ
            // when the trailer is detached mid-job - and then Close() would compare
            // against a zeroed baseline and report a bogus negative damage figure.
            StartTrailerWear = snap.Trailer.Wear,
            LastOdometerKm = snap.Truck.OdometerKm,
            // Already hitched when the job began, which is how a quick job starts:
            // the truck is placed at the depot with the load on. There was no
            // coupling to mark, and a later re-hitch is not one either.
            TrailerCoupled = snap.Trailer.Attached,
            CargoOn = snap.Job?.CargoLoaded ?? false,
        };
        // Only once the truck is really in the world. A job is very often accepted
        // over the loading screen, and until it finishes the game reports the
        // position as a placeholder a metre from the world origin with the odometer
        // reading exactly 0. Accumulate() already refuses to record a point on such
        // a tick, and this is the one path that used to slip past it.
        //
        // It mattered more than one bad point sounds. It was the first point of the
        // route, so it was where the map drew the pickup, where the frame stretched
        // to, and where the city anchor learned that city's position from. One
        // delivery in five started at the origin and dragged its city with it.
        if (snap.Truck.OdometerKm > 0) {
            _current.TripPoints.Add(new TripPoint { AtMs = nowMs, X = snap.PosX, Y = snap.PosY, Z = snap.PosZ, SpeedKmh = snap.Truck.SpeedKmh });
        }
    }

    private List<Anomaly> Accumulate(Snapshot snap, Snapshot prev, long dtMs, bool gap, bool reloaded, bool instant, long nowMs) {
        var j = _current!;
        var found = new List<Anomaly>();

        // The moment the load was hitched up, which is not the moment the job began.
        // A World of Trucks contract spawns its trailer when the offer is taken and
        // starts counting kilometres there, so the driver can be a city away from it
        // and drive to it under the job. Where the job started says nothing about
        // where the cargo was; this does.
        //
        // Checked before the instant guard below, because a coupling that happened
        // to land on a duplicate poll would otherwise be missed entirely: the next
        // tick compares against a snapshot that is already coupled.
        if (snap.Trailer.Attached && !prev.Trailer.Attached && !j.TrailerCoupled) {
            j.TrailerCoupled = true;
            j.Timeline.Add(new JobEvent { AtMs = nowMs, Type = "trailer_coupled" });
        }

        // And the moment the cargo went on, which is a different moment. Pulling your
        // own trailer you are hitched up long before the dock, so the coupling says
        // nothing about when the consignment started moving; on a contract the
        // trailer is waiting already loaded and the coupling is the whole of it.
        // Recording both lets the later of the two stand for the load being on,
        // whichever kind of job it was.
        if ((snap.Job?.CargoLoaded ?? false) && !j.CargoOn) {
            j.CargoOn = true;
            j.Timeline.Add(new JobEvent { AtMs = nowMs, Type = "cargo_loaded" });
        }

        // The instant the consignment starts moving. Damage is measured from here
        // rather than from the job being accepted: a scratch picked up on the way to
        // the trailer is the driver's, not the load's, and it is no longer counted as
        // a collision either, so leaving it in the damage would report an impact that
        // the delivery says never happened.
        if (j.LoadOnAtMs == 0 && j.TrailerCoupled && j.CargoOn) {
            j.LoadOnAtMs = nowMs;
            j.StartTruckWear = snap.Truck.Wear.Total();
        }

        // Same instant as the previous snapshot, kept only because it carries an
        // event. There is no interval to measure anything over: distance would be
        // noise, and the teleport check divides by the elapsed time, so any position
        // difference at all would come out as an implausible speed and reject an
        // honest delivery. Take the event and nothing else.
        if (instant) {
            RecordEvents(snap, j, found, (nowMs - j.StartedAtMs) < _config.JobStartGraceMs, nowMs);
            Record(found, j, nowMs);
            return found;
        }

        if (snap.Paused) {
            j.PausedMs += dtMs;
            return found;
        }

        // An earlier save was loaded (see the clock check in Update). The truck is
        // back at a point it already passed, so everything between there and here is
        // about to be driven a second time. Wind the counters back by as much as the
        // game wound the truck back, and the delivery ends up reporting the distance
        // the game itself will report on arrival instead of counting that stretch
        // twice. Nothing here is treated as cheating: reloading is ordinary single
        // player play, and the position jump it causes is the reason the teleport
        // check must not see this tick.
        if (reloaded) {
            var rewindMin = prev.GameTimeMin - snap.GameTimeMin;
            var rewindKm = Math.Max(0, prev.Truck.OdometerKm - snap.Truck.OdometerKm);

            // Both series are in simulated km and Validate() cross-checks one against
            // the other, so they have to be wound back together or the check would
            // fire on the discrepancy the rewind itself created.
            j.DistanceKm = Math.Max(0, j.DistanceKm - rewindKm);
            j.SimSpeedDistanceKm = Math.Max(0, j.SimSpeedDistanceKm - rewindKm);
            // Only while still empty: a reload after the hitch winds back loaded
            // kilometres, which were never in this figure to begin with.
            if (!j.TrailerCoupled || !j.CargoOn) j.DistanceToLoadKm = Math.Max(0, j.DistanceToLoadKm - rewindKm);
            j.DrivingGameMinutes = Math.Max(0, j.DrivingGameMinutes - rewindMin);

            // The save gives the fuel back along with the distance. Leaving it spent
            // while its kilometres are gone would report the whole rewound stretch as
            // consumption with nothing driven for it, which shows up as a consumption
            // figure the truck never had.
            j.FuelUsedL = Math.Max(0, j.FuelUsedL - Math.Max(0, snap.Truck.FuelL - prev.Truck.FuelL));

            // Damage is reported against the state at job start, so a save from before
            // an impact leaves the truck less damaged than it started and the delivery
            // reports a negative figure. Lower the baseline to what the save actually
            // shows: the damage reported is then the damage the truck really carries.
            j.StartTruckWear = Math.Min(j.StartTruckWear, snap.Truck.Wear.Total());
            j.StartTrailerWear = Math.Min(j.StartTrailerWear, snap.Trailer.Wear);

            // A save can carry a different truck than the one being driven a moment
            // ago, and the record would otherwise keep naming the old one.
            if (snap.Truck.Make != prev.Truck.Make || snap.Truck.Model != prev.Truck.Model) {
                j.TruckMake = snap.Truck.Make;
                j.TruckModel = snap.Truck.Model;
                j.TruckId = snap.Truck.TruckId;
            }

            j.LastOdometerKm = snap.Truck.OdometerKm;
            found.Add(new Anomaly { Code = "save_loaded", Delta = Math.Round(rewindKm, 3), DtMs = (long)rewindMin });
            if (j.LoadOnAtMs != 0) j.Timeline.Add(new JobEvent { AtMs = nowMs, Type = "save_loaded", Value = Math.Round(rewindMin, 0), Detail = Waybill.Strings.T("unit.gameMinutes") });
            Record(found, j, nowMs);
            return found;
        }

        // The SDK reports an odometer of exactly 0, with position frozen at a
        // placeholder, while the truck has not yet spawned into the world (e.g.
        // right after accepting a job, during the loading screen). The tick where
        // real telemetry first appears then looks like a multi-hundred-km/h
        // teleport. Treat that one-time transition as a warmup, not movement.
        if (prev.Truck.OdometerKm == 0 || snap.Truck.OdometerKm == 0) {
            found.Add(new Anomaly { Code = "telemetry_warmup" });
            Record(found, j, nowMs);
            return found;
        }

        // A Quick Job swaps the player into a company-owned truck for the
        // duration of the job (own truck -> job truck), teleporting them to the
        // pickup point and auto-filling the new truck's tank. That produces a
        // huge position/odometer/fuel jump on the tick where the truck identity
        // changes, none of which is real driving or a paid refuel.
        if (snap.Truck.Make != prev.Truck.Make || snap.Truck.Model != prev.Truck.Model) {
            found.Add(new Anomaly {
                Code = "vehicle_swap",
                From = $"{prev.Truck.Make} {prev.Truck.Model}",
                To = $"{snap.Truck.Make} {snap.Truck.Model}",
            });
            j.TruckMake = snap.Truck.Make;
            j.TruckModel = snap.Truck.Model;
            j.TruckId = snap.Truck.TruckId;
            j.StartFuelL = snap.Truck.FuelL;
            j.StartTruckWear = snap.Truck.Wear.Total();
            Record(found, j, nowMs);
            return found;
        }

        // One route sample per real tick, stored from day one even before any
        // map/replay UI exists to read it back (see TripPoint).
        j.TripPoints.Add(new TripPoint { AtMs = nowMs, X = snap.PosX, Y = snap.PosY, Z = snap.PosZ, SpeedKmh = snap.Truck.SpeedKmh });

        // Odometer/position can still take a few extra ticks to settle onto the
        // new truck's real values after a Quick Job swap (see vehicle_swap
        // above), so teleport/odometer checks stay soft for a short grace period
        // after the job starts.
        var inGrace = (nowMs - j.StartedAtMs) < _config.JobStartGraceMs;

        // Two different units are in play and they must never be mixed:
        //
        //   * The odometer, the speedometer, the job's planned distance, the payout
        //     and the delivery screen are all in the game's "simulated km".
        //   * World-space position is in real map metres, and the map is compressed
        //     (~13.5x on the routes measured here), so it yields a much smaller number.
        //
        // Both are internally consistent - measured on one real drive: odometer
        // 176.60 km and speed x game-time 175.62 km (game reported 176), versus
        // position 13.08 km and speed x real-time 12.94 km. Distance reported for a
        // delivery is the simulated one, so the odometer leads and position is kept
        // separately for teleport detection and future route/map work.
        var movedKm = DistanceKmBetween(snap, prev);
        var impliedKmh = movedKm / (dtMs / 3600000.0);
        var teleported = !gap && impliedKmh > _config.TeleportSpeedKmh;

        if (teleported) {
            found.Add(new Anomaly {
                Code = inGrace ? "job_start_transition" : "teleport",
                ImpliedKmh = Math.Round(impliedKmh),
                MovedKm = movedKm,
            });
        } else {
            j.WorldDistanceKm += movedKm;
        }

        var odoDelta = snap.Truck.OdometerKm - prev.Truck.OdometerKm;
        if (odoDelta < -_config.OdometerSlackKm) {
            found.Add(new Anomaly { Code = "odometer_reverse", Delta = odoDelta });
        } else if (odoDelta > _config.MaxOdometerJumpKm) {
            // Never counted as distance either way - a jump this size is a different
            // truck's odometer, not driving. But it is only *evidence* of anything
            // when it can't be explained: the reading settles onto the assigned
            // truck's own odometer a few ticks AFTER make/model already changed
            // (jumps of 50k-840k km observed, all just after job start), and a
            // polling gap also leaves a legitimately large step behind.
            found.Add(new Anomaly {
                Code = (inGrace || gap) ? "odometer_settle" : "odometer_jump",
                Delta = odoDelta,
                Allowed = _config.MaxOdometerJumpKm,
            });
        } else if (!teleported && odoDelta > 0) {
            j.DistanceKm += odoDelta;
            // Before the hitch this is the driver getting to the load, not the load
            // moving. Counted in both, because it is real driving, but kept apart so
            // progress can be measured against the plan, which describes only the
            // loaded leg.
            if (!j.TrailerCoupled || !j.CargoOn) j.DistanceToLoadKm += odoDelta;
        }

        // Kept current on every tick so a resume can measure against it (see the
        // bridge in Update()); also covers the settle/teleport branches above, where
        // the new reading is the one a later resume must compare to.
        j.LastOdometerKm = snap.Truck.OdometerKm;

        // Independent second opinion on the same simulated km, for the cross-check
        // in Validate(). Game time only has 1-minute resolution, so per tick this is
        // mostly zero and only meaningful once summed over a whole job.
        //
        // The same guard yields game time actually spent driving: sleeping jumps the
        // clock ~9 hours in one tick and gets skipped, so this stays a sane
        // denominator for average speed where raw start-to-finish game time is not
        // (one recorded 21-minute drive spanned 19 game hours because of a rest stop).
        var dtGameHours = (snap.GameTimeMin - prev.GameTimeMin) / 60.0;
        if (!gap && dtGameHours > 0 && dtGameHours < _config.MaxTickGameMinutes / 60.0) {
            j.SimSpeedDistanceKm += snap.Truck.SpeedKmh * dtGameHours;
            j.DrivingGameMinutes += dtGameHours * 60.0;
        } else if (!gap && dtGameHours * 60.0 >= _config.MaxTickGameMinutes) {
            // The clock leapt while real time barely moved: the driver slept, took a
            // ferry or train, or the game loaded the cargo. The first is the only one
            // that is resting, and the other two say so for themselves.
            var jumpedMin = dtGameHours * 60.0;
            var isTransport = snap.Events.FerryUsed != null || snap.Events.TrainUsed != null;
            var cargoMoved = CargoChangedHands(snap, prev);
            if (cargoMoved) found.Add(new Anomaly { AtMs = nowMs, Code = "cargo_handling", DtMs = dtMs, Delta = jumpedMin });
            if (!isTransport && !cargoMoved && j.LoadOnAtMs != 0) {
                j.RestStops += 1;
                j.RestMinutes += jumpedMin;
                j.Timeline.Add(new JobEvent { AtMs = nowMs, Type = "rest", Value = Math.Round(jumpedMin, 1) });
            }
        }

        // Cruise control usage - driving-style metadata, per the roadmap. Purely
        // informational: this project never penalises an assist.
        if (snap.CruiseControlOn) j.CruiseControlMs += dtMs;

        // A sudden damage step is an impact. Recorded as metadata for safety stats
        // (see project_vision) - it never invalidates a delivery, it just gets
        // counted and shown. Warmup and vehicle swaps return earlier, so the big
        // artificial damage steps those cause never reach this.
        var truckDamageStep = snap.Truck.Wear.Total() - prev.Truck.Wear.Total();

        // Only when a trailer was reported on both sides. It vanishes from telemetry
        // during a loading screen, and a missing trailer reads as undamaged, so its
        // return looked like an impact worth 0.14% and was counted as a collision
        // that never happened.
        var trailerDamageStep = snap.Trailer.Present && prev.Trailer.Present
            ? snap.Trailer.Wear - prev.Trailer.Wear
            : 0;
        // The load can take a knock the vehicles do not, so it is asked about too,
        // but only to name what was hit. What counts as an impact stays the truck and
        // the trailer, so this does not quietly start finding collisions that were
        // never found before.
        var cargoDamageStep = snap.Trailer.Present && prev.Trailer.Present
            ? snap.Trailer.CargoDamage - prev.Trailer.CargoDamage
            : 0;
        var damageStep = Math.Max(truckDamageStep, trailerDamageStep);
        if (damageStep > _config.CollisionDamageStep) {
            found.Add(new Anomaly { Code = "collision", Delta = damageStep });
            // Still noted as an anomaly, which is debugging detail about the drive,
            // but only counted against the delivery once the load is on it.
            if (j.LoadOnAtMs != 0) {
                j.Collisions += 1;
                j.Timeline.Add(new JobEvent {
                    AtMs = nowMs, Type = "collision", Value = Math.Round(damageStep * 100, 3),
                    Detail = Hit(truckDamageStep, trailerDamageStep, cargoDamageStep, _config.CollisionDamageStep),
                });
            }
        }

        TrackTrailerChain(j, snap);

        // Fuel. An increase means a refuel, not negative consumption; the rise is
        // kept rather than ignored, because it is the only measure of how much went in.
        var fuelDelta = prev.Truck.FuelL - snap.Truck.FuelL;
        if (fuelDelta > 0) j.FuelUsedL += fuelDelta;
        else j.PendingRefuelL += -fuelDelta;

        j.DrivingMs += dtMs;
        if (snap.Truck.SpeedKmh > j.TopSpeedKmh) j.TopSpeedKmh = snap.Truck.SpeedKmh;

        if (snap.SpeedLimitKmh > 0 && snap.Truck.SpeedKmh > snap.SpeedLimitKmh + _config.SpeedingToleranceKmh) {
            j.SpeedingMs += dtMs;
        }
        if (snap.SpeedLimitKmh > 0 && snap.Truck.SpeedKmh > snap.SpeedLimitKmh + _config.StyleSpeedingToleranceKmh) {
            j.HardSpeedingMs += dtMs;
        }

        RecordEvents(snap, j, found, inGrace, nowMs);

        if (gap) {
            // No ticks are written while the game is paused (the SDK stops updating
            // its timestamp), so opening the map or a menu leaves exactly the same
            // hole in the recording as the app freezing would. The game clock tells
            // them apart: it keeps running during a real client gap and stands still
            // during a pause. Measured across 24 gaps in two recordings, 22 were
            // pauses - flagging those as an unstable client made almost every honest
            // delivery land in "review".
            var gameMinutesPassed = snap.GameTimeMin - prev.GameTimeMin;

            // How far the clock should have moved if the game had been running the
            // whole time, at the rate the game itself reports.
            var scale = snap.GameTimeScale > 0 ? snap.GameTimeScale : _config.AssumedTimeScale;
            var expectedGameMinutes = (dtMs / 60000.0) * scale;

            // Two conditions, and both matter. The clock has to have moved a real
            // share of what running would have moved it, and it has to have moved
            // further than its own resolution: game time steps in whole minutes, so
            // over a gap of a few seconds it reads either 0 or 1 depending on
            // whether a minute boundary happened to fall inside, and 1 there is luck
            // rather than evidence. Comparing against real time instead called every
            // one of those a stalled client: five short pauses in one session were
            // flagged that way, each having advanced the clock by exactly 1 minute
            // where running at 20x would have advanced it by three or more.
            //
            // Where the two cannot be told apart, this reads it as a pause. Not being
            // able to prove the app stalled is not evidence that it did.
            var clockRan = gameMinutesPassed > _config.GameClockResolutionMin
                        && gameMinutesPassed >= expectedGameMinutes * _config.RunningClockShare;
            var wasPaused = !clockRan;

            // The third case: the clock leapt far further than the real gap could
            // account for even at the game's compressed rate. That is the game fast
            // forwarding through a sleep, a ferry or a train, and the loading screen
            // it plays is exactly what left the hole. One observed sleep advanced the
            // clock 900 game minutes across 11 real seconds and was being reported as
            // an unstable client, while the rest stop itself went unrecorded, because
            // rest detection above only looks at ticks that are not gaps.
            var affordableGameMinutes = Math.Max((dtMs / 60000.0) * _config.MaxGameMinutesPerRealMinute, _config.MaxTickGameMinutes);
            var fastForward = !wasPaused && gameMinutesPassed > affordableGameMinutes;

            if (fastForward) {
                found.Add(new Anomaly { Code = "fast_forward_gap", DtMs = dtMs, Delta = gameMinutesPassed });
                var isTransport = snap.Events.FerryUsed != null || snap.Events.TrainUsed != null;
                var cargoMoved = CargoChangedHands(snap, prev);
                if (cargoMoved) {
                    found.Add(new Anomaly { AtMs = nowMs, Code = "cargo_handling", DtMs = dtMs, Delta = gameMinutesPassed });
                }
                if (!isTransport && !cargoMoved && j.LoadOnAtMs != 0) {
                    j.RestStops += 1;
                    j.RestMinutes += gameMinutesPassed;
                    j.Timeline.Add(new JobEvent { AtMs = nowMs, Type = "rest", Value = Math.Round(gameMinutesPassed, 1) });
                }
            } else {
                found.Add(new Anomaly { Code = wasPaused ? "paused_gap" : "client_gap", DtMs = dtMs });
            }
        }

        Record(found, j, nowMs);
        return found;
    }

    /// <summary>The gameplay events carried by one snapshot. Separate from the rest of
    /// accumulation because a snapshot can arrive in the same instant as the previous
    /// one and still carry an event that has to be kept (see the instant branch).</summary>
    /// <summary>
    /// What happened while the load was on.
    ///
    /// Nothing before that belongs to the delivery. Getting to the trailer is the
    /// driver's own business, and a fine collected on the way there, or a tank filled
    /// before the job really began, has nothing to do with the consignment. It is not
    /// counted and it is not written down, so the figures on the card and the entries
    /// under them are about the same stretch of road.
    /// </summary>
    private static void RecordEvents(Snapshot snap, JobState j, List<Anomaly> found, bool inGrace, long nowMs) {
        if (j.LoadOnAtMs == 0) return;
        var e = snap.Events;
        if (e.Fined != null) {
            j.Fines.Add(new FineRecord { Amount = e.Fined.Amount, Offence = e.Fined.Offence });
            j.Timeline.Add(new JobEvent { AtMs = nowMs, Type = "fine", Value = e.Fined.Amount, Detail = e.Fined.Offence });
        }
        if (e.TollgatePaid != null) {
            j.Tolls += e.TollgatePaid.Amount;
            j.Timeline.Add(new JobEvent { AtMs = nowMs, Type = "tollgate", Value = e.TollgatePaid.Amount });
        }
        if (e.FerryUsed != null) {
            j.Ferries += 1;
            j.Timeline.Add(new JobEvent { AtMs = nowMs, Type = "ferry", Value = e.FerryUsed.Price, Detail = $"{e.FerryUsed.Source} -> {e.FerryUsed.Target}" });
        }
        if (e.TrainUsed != null) {
            j.Ferries += 1;
            j.Timeline.Add(new JobEvent { AtMs = nowMs, Type = "train", Value = e.TrainUsed.Price, Detail = $"{e.TrainUsed.Source} -> {e.TrainUsed.Target}" });
        }
        if (e.RefuelPaid != null) {
            var litres = j.PendingRefuelL;
            j.PendingRefuelL = 0;
            if (inGrace) {
                // A Quick Job auto-fills the assigned truck's tank as part of the
                // swap (see vehicle_swap above) and fires RefuelStart/End/Payed for
                // it a few ticks later, well inside the job-start grace window - the
                // player never touched a pump. Log it so it's still visible for
                // debugging, but don't count it as a player refuel, or every quick
                // job would show a refuel that never happened.
                found.Add(new Anomaly { Code = "system_refuel", Delta = e.RefuelPaid.Amount });
            } else {
                j.Refuels += 1;
                j.Timeline.Add(new JobEvent {
                    AtMs = nowMs, Type = "refuel", Value = e.RefuelPaid.Amount,
                    Litres = litres > 0.5 ? litres : null,
                });
            }
        }
    }

    /// <summary>
    /// What an impact landed on, as identifiers rather than words: the vehicle that
    /// took the step being reported, and the load beside it when that was shaken as
    /// well. A percentage on its own says how bad it was and nothing about what it
    /// happened to, which on a triple with a fragile load is most of the question.
    /// </summary>
    private static string Hit(double truck, double trailer, double cargo, double threshold) {
        var parts = new List<string> { truck >= trailer ? "truck" : "trailer" };
        if (cargo > threshold) parts.Add("cargo");
        return string.Join(",", parts);
    }

    private static void Record(List<Anomaly> found, JobState j, long nowMs) {
        foreach (var a in found) {
            a.AtMs = nowMs;
            j.Anomalies.Add(a);
        }
    }

    private JobRecord Close(Snapshot snap, string outcome, object? payload, long nowMs) {
        var j = _current!;
        var record = new JobRecord {
            JobUid = j.JobUid,
            Outcome = outcome,
            JobType = Adapter.MarketName(j.Job.Market),
            SpecialTransport = j.Job.SpecialJob,
            Game = j.Game,
            GameVersion = j.GameVersion,
            StartedAtMs = j.StartedAtMs,
            FinishedAtMs = nowMs,
            RealDurationMs = nowMs - j.StartedAtMs,
            // GameTime reads 0 until the world finishes loading, so a job opened
            // during that warmup would otherwise "last" the entire absolute game
            // clock (15503 minutes seen on one recording). Fall back to the time
            // actually spent driving in that case.
            GameDurationMin = j.StartedAtGameMin > 0 ? snap.GameTimeMin - j.StartedAtGameMin : Math.Round(j.DrivingGameMinutes, 1),
            SourceCity = j.Job.SourceCity,
            SourceCompany = j.Job.SourceCompany,
            DestinationCity = j.Job.DestinationCity,
            DestinationCompany = j.Job.DestinationCompany,
            Cargo = j.Job.Cargo,
            CargoId = j.Job.CargoId,
            CargoMassKg = j.Job.CargoMassKg,
            PlannedDistanceKm = j.Job.PlannedDistanceKm,
            OfferedIncome = j.Job.Income,
            TruckMake = j.TruckMake,
            TruckModel = j.TruckModel,
            TruckId = j.TruckId,
            TrailerName = j.TrailerName,
            TrailerId = j.TrailerId,
            TrailerChainType = ChainType(j),
            TrailerOwned = j.TrailerChain.Any(u => u.IsOwned),
            TrailerUnits = TrailerUnits(j),
            DistanceKm = Math.Round(j.DistanceKm, 3),
            DistanceToLoadKm = Math.Round(j.DistanceToLoadKm, 3),
            WorldDistanceKm = Math.Round(j.WorldDistanceKm, 3),
            SimSpeedDistanceKm = Math.Round(j.SimSpeedDistanceKm, 3),
            DrivingGameMinutes = Math.Round(j.DrivingGameMinutes, 1),
            FuelUsedL = Math.Round(j.FuelUsedL, 2),
            AvgConsumptionLper100 = j.DistanceKm > 0.1 ? Math.Round(j.FuelUsedL / j.DistanceKm * 100, 2) : null,
            TopSpeedKmh = Math.Round(j.TopSpeedKmh, 1),
            DrivingMs = j.DrivingMs,
            PausedMs = j.PausedMs,
            SpeedingShare = j.DrivingMs > 0 ? Math.Round((double)j.SpeedingMs / j.DrivingMs, 4) : 0,
            TruckDamage = Math.Round(snap.Truck.Wear.Total() - j.StartTruckWear, 4),
            TrailerDamage = Math.Round(snap.Trailer.Wear - j.StartTrailerWear, 4),
            Fines = j.Fines,
            TollsPaid = j.Tolls,
            FerriesUsed = j.Ferries,
            Refuels = j.Refuels,
            Collisions = j.Collisions,
            RestStops = j.RestStops,
            RestMinutes = Math.Round(j.RestMinutes, 1),
            CruiseControlShare = j.DrivingMs > 0 ? Math.Round((double)j.CruiseControlMs / j.DrivingMs, 4) : 0,
            Anomalies = j.Anomalies,
            Timeline = j.Timeline,
            TripPoints = j.TripPoints,
        };

        // JobValues.DeliveryTime is the absolute in-game minute the delivery window
        // closes, so comparing it against the clock at finish gives the lateness the
        // game itself already charged for. Statistics only - never a rejection.
        if (j.Job.DeadlineMin > 0 && snap.GameTimeMin > 0) {
            record.MinutesLate = Math.Round(snap.GameTimeMin - j.Job.DeadlineMin, 1);
            record.LateDelivery = record.MinutesLate > 0;
        }

        if (outcome == "delivered" && payload is JobDeliveredEvent jd) {
            record.Revenue = jd.Revenue;
            record.DeliveredCargoDamage = jd.CargoDamage;
            record.AutoparkUsed = jd.AutoparkUsed;
            record.ReportedDistanceKm = jd.DistanceKm;
            record.DeliveryTimeMin = jd.DeliveryTimeMin;
        }
        if (outcome == "cancelled" && payload is JobCancelledEvent jc) {
            record.Penalty = jc.Penalty;
        }

        record.HardSpeedingShare = j.DrivingMs > 0 ? Math.Round((double)j.HardSpeedingMs / j.DrivingMs, 4) : 0;
        record.DrivingStyle = Style(record, j.Fines.Count, j.Collisions);
        record.Validation = Validate(record);
        return record;
    }

    /// <summary>Which of the two ways this delivery was driven. Someone keeping to the
    /// rules and someone treating the road as a track are not the same category and
    /// their numbers should not be compared, but neither of them is doing anything
    /// wrong, so this never touches the verdict.
    ///
    /// Read from what was measured rather than declared up front, which means it can
    /// be derived again for every past delivery from its recording.</summary>
    private string Style(JobRecord record, int fines, int collisions) {
        var clean = fines < _config.StyleFinesMax
            && record.HardSpeedingShare < _config.StyleSpeedingShareMax
            && collisions < _config.StyleCollisionsMax;
        return clean ? "clean" : "spirited";
    }

    /// <summary>
    /// Turns the raw anomaly list into a verdict. Per this project's philosophy
    /// (see project_vision memory), a delivery is never hard-invalidated for a
    /// driving assist or noisy telemetry field - only for the few signals that
    /// are strong, direct evidence nothing was actually driven. Everything else
    /// stays visible in Anomalies/flags for review, never blocks the delivery.
    /// </summary>
    private static Validation Validate(JobRecord record) {
        var flags = new List<string>();

        if (record.Outcome == "unresolved") flags.Add("no_completion_event");
        // A job shorter than this is almost certainly a bug or an abuse attempt.
        if (record.DistanceKm < 0.5) flags.Add("distance_too_short");

        var teleports = record.Anomalies.Count(a => a.Code == "teleport");
        var odometerJumps = record.Anomalies.Count(a => a.Code == "odometer_jump");
        var gaps = record.Anomalies.Count(a => a.Code == "client_gap");

        if (teleports > 0) flags.Add("teleport_detected");
        if (odometerJumps > 0) flags.Add("odometer_manipulation");
        if (gaps > 2) flags.Add("unstable_client");
        if (record.TopSpeedKmh > 180) flags.Add("implausible_top_speed");

        // Cross-check the odometer against speed integrated over game time. Both are
        // in simulated km and come from different fields, so a driver faking one has
        // to keep the other consistent with it. On a clean recorded drive these landed
        // within 0.6% of each other (176.60 vs 175.62 km). Only meaningful once the
        // job is long enough that game time's 1-minute resolution stops dominating.
        if (record.DistanceKm > 5 && record.SimSpeedDistanceKm > 0) {
            var ratio = record.DistanceKm / record.SimSpeedDistanceKm;
            if (ratio < 0.75 || ratio > 1.33) flags.Add("distance_inconsistent");
        }

        // The game reports its own distance on delivery, in the same simulated km the
        // odometer counts, so this is now a like-for-like comparison and a genuinely
        // strong signal: skipping the drive can't produce a matching odometer delta.
        if (record.ReportedDistanceKm is > 0 && record.DistanceKm > 0) {
            var ratio = record.DistanceKm / record.ReportedDistanceKm.Value;
            if (ratio < 0.8 || ratio > 1.25) flags.Add("distance_mismatch");
        }

        // A job that simply stopped existing is not evidence of anything being faked.
        // It happens whenever a drive is abandoned: another profile is loaded, the
        // game is quit, it crashes. Rejection is for a delivery being claimed without
        // the driving behind it, and this claims nothing - the outcome is not
        // "delivered" and there is no payout on it to inflate. So it stays visible as
        // a flag and lands in review, without calling an honest drive a fake.
        var hard = new[] { "teleport_detected", "distance_too_short", "odometer_manipulation" };
        // A job that went away with a loaded save was never completed and is not a
        // delivery anyone is claiming, so there is nothing here to reject. Whatever
        // was flagged stays visible, it just cannot invalidate a drive that the game
        // itself has already erased from its own history.
        var rejected = record.Outcome != "reloaded" && flags.Any(hard.Contains);

        return new Validation {
            Flags = flags,
            Status = rejected ? "rejected" : (flags.Count > 0 ? "review" : "accepted"),
        };
    }
}

