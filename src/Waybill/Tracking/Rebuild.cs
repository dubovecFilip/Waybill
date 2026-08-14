using System.Linq;
using Waybill.Storage;

namespace Waybill.Tracking;

/// <summary>
/// Re-derives every tracked delivery from its recording. Detection improves over
/// time (the odometer units, pause against gap, a loaded save against a teleport),
/// and a row written by an older build keeps that build's verdict forever
/// otherwise. Lossless, because every tracked delivery has a recording behind it.
/// Imported rows are left alone: nothing can regenerate those.
///
/// Shared by the CLI and the window so both do exactly the same thing.
/// </summary>
public static class Rebuild {
    public class Result {
        public string BackupPath = "";
        public int Removed;
        public int Recordings;
        public int Deliveries;
        /// <summary>Recordings that could not be read, with the reason.</summary>
        public List<string> Skipped = new();
    }

    public static Result Run(DeliveryStore store) {
        var result = new Result { BackupPath = store.Backup() };
        result.Removed = store.DeleteTrackedDeliveries();

        var sessionDir = Path.Combine(DeliveryStore.DefaultDir(), "sessions");
        var recordings = Directory.Exists(sessionDir)
            ? Directory.GetFiles(sessionDir).Where(f => f.EndsWith(".jsonl") || f.EndsWith(".jsonl.gz")).OrderBy(f => f).ToArray()
            : Array.Empty<string>();
        result.Recordings = recordings.Length;

        foreach (var recording in recordings) {
            try {
                result.Deliveries += ReplayInto(recording, store);
            } catch (Exception ex) {
                // The tracked rows are already gone by this point, so one damaged
                // recording must not abandon the rest of them half restored. Note it
                // and carry on, and the caller can say which one was unreadable.
                result.Skipped.Add($"{Path.GetFileName(recording)}: {ex.Message}");
            }
        }
        return result;
    }

    /// <summary>Replays one recording through the tracker, saving what it finishes.</summary>
    public static int ReplayInto(string path, DeliveryStore store) {
        var tracker = new JobTracker();
        var saved = 0;
        foreach (var raw in SessionFiles.ReadLines(path)) {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            Newtonsoft.Json.Linq.JObject parsed;
            try { parsed = Newtonsoft.Json.Linq.JObject.Parse(raw); } catch { continue; }
            var ts = (long?)parsed["t"] ?? 0;
            var kind = (string?)parsed["kind"] ?? "tick";
            if (parsed["d"] is not Newtonsoft.Json.Linq.JObject d) continue;

            foreach (var ev in tracker.Update(Adapter.FromRecordedJson(d, kind), ts)) {
                if (ev.Type == TrackerEventType.JobFinished && ev.Record != null) {
                    store.SaveDelivery(ev.Record);
                    saved++;
                }
            }
        }
        return saved;
    }
}
