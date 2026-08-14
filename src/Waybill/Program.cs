using Newtonsoft.Json;
using SCSSdkClient;
using SCSSdkClient.Object;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Waybill;
using Waybill.Storage;
using Waybill.Tracking;

// This is a WinExe, so it owns no console. When started with arguments from a
// terminal, attach to that terminal's console so the CLI output lands where the
// user typed the command instead of vanishing.
if (args.Length > 0) NativeConsole.AttachToParent();

// One saved preference drives both the window and the CLI, so `--list` and the
// grid never disagree about what units the user asked for.
ConsoleFormat.UnitSetting = Settings.Load().Units;

// Raw telemetry recorder + live delivery tracker.
// Writes one JSON object per line into sessions\session-<timestamp>.jsonl
// (kept as a raw debug/replay trail: { "t": unix ms, "kind": "tick" | event
// name, "d": full telemetry object }), while every one of those same lines is
// also fed through JobTracker so deliveries are recognised, printed, and
// saved to the local SQLite database without anything else needing to run.
//
// Snapshots are written once per second, plus one extra snapshot every time a
// gameplay event fires, so no event can be missed between two ticks.

// Offline regression mode: `Waybill.exe --replay path\to\session.jsonl [--save]`
// replays a previously recorded file through the same Adapter+JobTracker the
// live path uses below, with no shared memory and no game required. Mirrors
// This is the regression harness: rerun an old recording after changing the
// tracker and check the numbers still come out the same.
// Pass --save to also persist any recognised deliveries into the real database
// (e.g. to backfill history from old recordings) - a bare --replay never
// touches the database, so re-running it to check output is always safe.
if (args.Length >= 2 && args[0] == "--replay") {
    ReplayFile(args[1], save: args.Contains("--save"));
    return;
}

if (args.Length >= 3 && args[0] == "--test-resume") {
    TestResume(args[1], int.Parse(args[2]));
    return;
}

void TestResume(string path, int splitIdx) {
    var lines = SessionFiles.ReadLines(path).Where(l => !string.IsNullOrWhiteSpace(l)).ToArray();

    JobRecord? Run(IEnumerable<string> rows, JobTracker t) {
        JobRecord? last = null;
        foreach (var raw in rows) {
            var parsed = Newtonsoft.Json.Linq.JObject.Parse(raw);
            var ts = (long?)parsed["t"] ?? 0;
            var kind = (string?)parsed["kind"] ?? "tick";
            if (parsed["d"] is not Newtonsoft.Json.Linq.JObject dd) continue;
            foreach (var ev in t.Update(Adapter.FromRecordedJson(dd, kind), ts)) {
                if (ev.Type == TrackerEventType.JobFinished) last = ev.Record;
            }
        }
        return last;
    }

    var whole = Run(lines, new JobTracker());

    var t1 = new JobTracker();
    Run(lines.Take(splitIdx), t1);
    var mid = t1.ActiveState;
    Console.WriteLine($"stav pri preruseni (riadok {splitIdx}): " + (mid == null ? "ZIADNA aktivna zakazka" : $"{mid.Job.SourceCity} -> {mid.Job.DestinationCity}, {mid.DistanceKm:0.000} km"));

    var t2 = new JobTracker();
    if (mid != null) {
        // Round-trip through JSON exactly like the real restart does.
        var restored = JsonConvert.DeserializeObject<JobState>(JsonConvert.SerializeObject(mid));
        if (restored != null) t2.PrepareResume(restored);
    }
    var resumed = Run(lines.Skip(splitIdx), t2);

    Console.WriteLine();
    Console.WriteLine($"jeden suvisly beh:  {(whole == null ? "-" : $"{whole.DistanceKm:0.000} km, {whole.Outcome}, {whole.Validation.Status}, pokuty {whole.Fines.Count}, kolizie {whole.Collisions}, body trasy {whole.TripPoints.Count}")}");
    Console.WriteLine($"s prerusenim:       {(resumed == null ? "-" : $"{resumed.DistanceKm:0.000} km, {resumed.Outcome}, {resumed.Validation.Status}, pokuty {resumed.Fines.Count}, kolizie {resumed.Collisions}, body trasy {resumed.TripPoints.Count}")}");
}

// `Waybill.exe --list [n]` prints the n most recent saved deliveries
// (default 20) without needing to launch the game at all.
if (args.Length >= 1 && args[0] == "--list") {
    var n = args.Length >= 2 && int.TryParse(args[1], out var parsedN) ? parsedN : 20;
    ListDeliveries(n);
    return;
}

// `Waybill.exe --stats [days]` - all-time by default, or the last N days.
if (args.Length >= 1 && args[0] == "--stats") {
    long? since = args.Length >= 2 && int.TryParse(args[1], out var days)
        ? DateTimeOffset.UtcNow.AddDays(-days).ToUnixTimeMilliseconds()
        : null;
    PrintStats(since);
    return;
}

// `Waybill.exe --rebuild` re-derives every tracked delivery from its recording.
// Detection improves over time (the odometer units, the pause-vs-gap distinction),
// and rows written by an older build keep their old verdict forever otherwise.
// Lossless because every tracked delivery has a recording behind it; imported rows
// are left alone, since nothing can regenerate those.
if (args.Length >= 1 && args[0] == "--rebuild") {
    using var rebuildStore = new DeliveryStore();
    var backupPath = rebuildStore.Backup();
    Console.WriteLine($"Zaloha pred prestavbou: {backupPath}");

    var removed = rebuildStore.DeleteTrackedDeliveries();
    Console.WriteLine($"Zmazanych sledovanych zaznamov: {removed}");

    var sessionDir = Path.Combine(DeliveryStore.DefaultDir(), "sessions");
    var recordings = Directory.Exists(sessionDir)
        ? Directory.GetFiles(sessionDir).Where(f => f.EndsWith(".jsonl") || f.EndsWith(".jsonl.gz")).OrderBy(f => f).ToArray()
        : Array.Empty<string>();

    var rebuilt = 0;
    foreach (var recording in recordings) {
        rebuilt += ReplayInto(recording, rebuildStore);
    }
    Console.WriteLine($"Prestavanych z {recordings.Length} nahravok: {rebuilt} zasielok");
    return;
}

int ReplayInto(string path, DeliveryStore store) {
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

// `Waybill.exe --import-trucksbook <export.csv>`
if (args.Length >= 2 && args[0] == "--import-trucksbook") {
    using var importStore = new DeliveryStore();
    var result = new TrucksBookImport(importStore).Import(args[1]);
    Console.WriteLine($"Importovanych: {result.Imported}   uz existovalo: {result.Skipped}");
    if (result.Uncredited > 0) {
        Console.WriteLine($"Z toho {result.Uncredited} zasielok TrucksBook nezapocital ({result.UncreditedKm:0} km) - Waybill ich zapocitava.");
    }
    foreach (var problem in result.Problems) Console.WriteLine("  ! " + problem);
    return;
}

// `Waybill.exe --backup [path]` / `--restore <path>`
if (args.Length >= 1 && args[0] == "--backup") {
    using var backupStore = new DeliveryStore();
    var dest = backupStore.Backup(args.Length >= 2 ? args[1] : null);
    Console.WriteLine($"Zaloha ulozena: {dest}");
    return;
}

if (args.Length >= 2 && args[0] == "--restore") {
    try {
        var safety = DeliveryStore.RestoreFromBackup(args[1]);
        Console.WriteLine($"Databaza obnovena zo zalohy: {args[1]}");
        Console.WriteLine($"Povodna databaza odlozena sem: {safety}");
    } catch (Exception ex) {
        Console.WriteLine($"Obnova zlyhala, databaza ostala nezmenena: {ex.Message}");
    }
    return;
}

// `Waybill.exe --export csv|json [path]`
if (args.Length >= 2 && args[0] == "--export") {
    var format = args[1] == "json" ? "json" : "csv";
    var path = args.Length >= 3 ? args[2] : Path.Combine(DeliveryStore.DefaultDir(), $"deliveries-{DateTime.Now:yyyyMMdd-HHmmss}.{format}");
    using var exportStore = new DeliveryStore();
    exportStore.Export(path, format);
    Console.WriteLine($"Exportovane do: {path}");
    return;
}

void ReplayFile(string path, bool save) {
    using var saveStore = save ? new DeliveryStore() : null;
    var replayTracker = new JobTracker();
    var finished = 0;
    foreach (var raw in SessionFiles.ReadLines(path)) {
        if (string.IsNullOrWhiteSpace(raw)) continue;
        Newtonsoft.Json.Linq.JObject parsed;
        try {
            parsed = Newtonsoft.Json.Linq.JObject.Parse(raw);
        } catch {
            continue;
        }

        var t = (long?)parsed["t"] ?? 0;
        var kind = (string?)parsed["kind"] ?? "tick";
        if (parsed["d"] is not Newtonsoft.Json.Linq.JObject d) continue;

        var snap = Adapter.FromRecordedJson(d, kind);
        foreach (var ev in replayTracker.Update(snap, t)) {
            if (ev.Type == TrackerEventType.JobStarted && ev.Job != null) ConsoleFormat.PrintJobStarted(ev.Job, replayTracker.ActiveState?.Game);
            if (ev.Type == TrackerEventType.JobFinished && ev.Record != null) {
                ConsoleFormat.PrintJobFinished(ev.Record);
                saveStore?.SaveDelivery(ev.Record);
                finished++;
            }
        }
    }
    Console.WriteLine($"Hotovo. Zasielok: {finished}{(save ? " (ulozene do databazy)" : "")}");
}

void ListDeliveries(int limit) {
    using var listStore = new DeliveryStore();
    Console.WriteLine($"Databaza: {listStore.DbPath}");
    Console.WriteLine();
    foreach (var row in listStore.RecentDeliveries(limit, ConsoleFormat.UnitSetting)) {
        Console.WriteLine(row);
    }
}

void PrintStats(long? sinceMs) {
    using var statsStore = new DeliveryStore();
    var s = statsStore.GetStats(sinceMs);

    // Aggregates span every game played, so they follow the most recent one.
    var u = Units.For(ConsoleFormat.UnitSetting, statsStore.MostRecentGame());

    Console.WriteLine(sinceMs.HasValue ? "Statistiky (obdobie)" : "Statistiky (vsetko)");
    Console.WriteLine("====================");
    Console.WriteLine($"zasielok spolu: {s.TotalDeliveries}  (accepted {s.Accepted}, review {s.Review}, rejected {s.Rejected})");
    Console.WriteLine($"vzdialenost: {u.FormatDistance(s.TotalDistanceKm)}");
    Console.WriteLine($"zarobok: {u.FormatMoney(s.TotalRevenue)}");
    Console.WriteLine($"palivo: {u.FormatVolume(s.TotalFuelL)}");

    var realHours = s.TotalDrivingMs / 3600000.0;
    var gameHours = s.TotalGameMinutes / 60.0;
    Console.WriteLine($"cas za volantom: {realHours:0.0} h realneho casu ({gameHours:0.0} h herneho)");
    // Distances are simulated km, so they pair with game hours - dividing by real
    // hours would report the time-compression factor as speed (~770 km/h).
    if (gameHours > 0.01) {
        Console.WriteLine($"priemerna rychlost: {u.FormatSpeed(s.TimedDistanceKm / gameHours)}");
    }

    Console.WriteLine($"kolizie: {s.TotalCollisions}   meskania: {s.LateDeliveries}   pokuty spolu: {u.FormatMoney(s.TotalFines)}");

    if (s.FavoriteTruck != null) Console.WriteLine($"oblubeny tahac: {s.FavoriteTruck}");
    if (s.FavoriteRoute != null) Console.WriteLine($"oblubena trasa: {s.FavoriteRoute}");
    if (s.FavoriteCargo != null) Console.WriteLine($"oblubeny naklad: {s.FavoriteCargo}");
}


// No arguments: this is the normal way to run it - open the window and let the
// engine record in the background. Everything above is the CLI, which stays
// available for scripting and for checking history without launching the game.
Application.SetHighDpiMode(HighDpiMode.SystemAware);
Application.EnableVisualStyles();
Application.SetCompatibleTextRenderingDefault(false);
Application.Run(new MainForm());
