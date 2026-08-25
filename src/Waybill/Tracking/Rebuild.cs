using Waybill.Storage;

namespace Waybill.Tracking;

/// <summary>
/// Re-derives tracked deliveries from their recordings. Detection improves over
/// time (the odometer units, pause against gap, a loaded save against a teleport),
/// and a row written by an older build keeps that build's verdict forever
/// otherwise. Imported rows are left alone: nothing can regenerate those.
///
/// It only touches the periods it can actually re-derive. A delivery whose
/// recording is gone is outside every recording's span, and is kept exactly as it
/// is rather than deleted in the hope that something would replace it. A delivery
/// has no stable identity of its own to match on, so the span is what stands in
/// for one.
///
/// Everything is replayed before anything is deleted, so a recording that turns
/// out to be unreadable halfway through cannot leave the history half restored.
///
/// Shared by the CLI and the window so both do exactly the same thing.
/// </summary>
public static class Rebuild {
    public class Result {
        public string BackupPath = "";
        public int Removed;
        /// <summary>Tracked deliveries left untouched because no recording covers
        /// the time they were driven.</summary>
        public int Kept;
        public int Recordings;
        public int Deliveries;
        /// <summary>Stretches driven with nothing on the hook, and how far.</summary>
        public int Freeroam;
        public double FreeroamKm;
        /// <summary>Recordings that could not be read, with the reason.</summary>
        public List<string> Skipped = new();
    }

    public static Result Run(DeliveryStore store) {
        var result = new Result();

        var sessionDir = Path.Combine(DeliveryStore.DefaultDir(), "sessions");
        var recordings = Directory.Exists(sessionDir)
            ? Directory.GetFiles(sessionDir).Where(f => f.EndsWith(".jsonl") || f.EndsWith(".jsonl.gz")).OrderBy(f => f).ToArray()
            : Array.Empty<string>();
        result.Recordings = recordings.Length;

        // Read everything first. Nothing in the database moves until every recording
        // has been through the tracker, so an unreadable one costs its own deliveries
        // and nothing else.
        var records = new List<JobRecord>();
        var roaming = new List<FreeroamRecord>();
        var spans = new List<(long From, long To)>();
        // A job carried from one recording into the next, because a delivery does not
        // have to fit inside one sitting: quit for the evening halfway to the drop and
        // it lives in two files. Replaying each of them from nothing gave the first
        // half to a record that was thrown away unfinished and started the second half
        // from zero, which is a delivery missing however far it got on the first day.
        // The recordings are named after their start and sorted, so this walks them in
        // the order they were driven.
        JobState? carried = null;
        foreach (var recording in recordings) {
            try {
                var (found, roamed, span, left) = Replay(recording, carried);
                records.AddRange(found);
                roaming.AddRange(roamed);
                if (span is { } s) spans.Add(s);
                carried = left;
            } catch (Exception ex) {
                result.Skipped.Add($"{Path.GetFileName(recording)}: {ex.Message}");
            }
        }
        result.Deliveries = records.Count;

        result.BackupPath = store.Backup();
        var before = store.CountTrackedDeliveries();
        result.Removed = store.DeleteTrackedDeliveriesWithin(spans);
        result.Kept = before - result.Removed;
        store.DeleteFreeroamWithin(spans);
        foreach (var record in records) store.SaveDelivery(record);
        foreach (var stretch in roaming) store.SaveFreeroam(stretch);
        result.Freeroam = roaming.Count;
        result.FreeroamKm = roaming.Sum(r => r.DistanceKm);
        return result;
    }

    /// <summary>Replays one recording, returning what finished in it, the stretch of
    /// time it covers, and whatever job was still being driven when it ended.</summary>
    private static (List<JobRecord> Records, List<FreeroamRecord> Roaming, (long From, long To)? Span, JobState? Left) Replay(string path, JobState? carried) {
        var tracker = new JobTracker();
        if (carried != null) {
            // Through JSON, the way a restart hands it over, so a rebuild cannot
            // quietly do better than the app it is rebuilding for.
            var restored = Newtonsoft.Json.JsonConvert.DeserializeObject<JobState>(
                Newtonsoft.Json.JsonConvert.SerializeObject(carried));
            if (restored != null) tracker.PrepareResume(restored);
        }
        var records = new List<JobRecord>();
        var roaming = new List<FreeroamRecord>();
        long first = 0, last = 0;

        foreach (var raw in SessionFiles.ReadLines(path)) {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            Newtonsoft.Json.Linq.JObject parsed;
            try { parsed = Newtonsoft.Json.Linq.JObject.Parse(raw); } catch { continue; }
            var ts = (long?)parsed["t"] ?? 0;
            var kind = (string?)parsed["kind"] ?? "tick";
            if (parsed["d"] is not Newtonsoft.Json.Linq.JObject d) continue;

            if (ts > 0) {
                if (first == 0) first = ts;
                last = ts;
            }

            foreach (var ev in tracker.Update(Adapter.FromRecordedJson(d, kind), ts)) {
                if (ev.Type == TrackerEventType.JobFinished && ev.Record != null) records.Add(ev.Record);
                if (ev.Type == TrackerEventType.FreeroamFinished && ev.Freeroam != null) roaming.Add(ev.Freeroam);
            }
        }

        // The recording simply stops; nothing tells the tracker that the last stretch
        // has ended, so it is asked for whatever it is still holding.
        roaming.AddRange(tracker.FinishRoaming());

        // Whatever is still being driven goes to the next recording rather than being
        // dropped here.
        return (records, roaming, first > 0 ? (first, last) : null, tracker.ActiveState);
    }

}
