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

// Numbers read the same everywhere: 283.0 km, not 283,0 km. Left to the machine's
// own locale a Slovak Windows writes a comma, which then disagrees with the units,
// the documentation and every figure in an exported CSV. Dates are unaffected: the
// dots in "dd.MM.yyyy" are literals, and ":" is the time separator in this culture
// too.
System.Globalization.CultureInfo.DefaultThreadCurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
Thread.CurrentThread.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;

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
    var result = Rebuild.Run(rebuildStore);
    Console.WriteLine($"Zaloha pred prestavbou: {result.BackupPath}");
    Console.WriteLine($"Prepocitanych: {result.Removed}");
    Console.WriteLine($"Ponechanych (nahravka uz neexistuje): {result.Kept}");
    Console.WriteLine($"Prestavanych z {result.Recordings} nahravok: {result.Deliveries} zasielok");
    Console.WriteLine($"Jazdy mimo zakazky: {result.Freeroam} usekov, {result.FreeroamKm:0.0} km");
    foreach (var skipped in result.Skipped) Console.WriteLine($"Necitatelna nahravka: {skipped}");
    return;
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

// `Waybill.exe --export-sheet <id> [path]` writes one delivery as the document the
// app is named after. At 300 dpi, because the point of a sheet is that it can be
// printed; a delivery that spills past one page writes numbered files.
if (args.Length >= 2 && args[0] == "--export-sheet") {
    if (!long.TryParse(args[1], out var sheetId)) {
        Console.WriteLine("Pouzitie: --export-sheet <id> [subor.png]");
        return;
    }
    using var sheetStore = new DeliveryStore();
    var detail = sheetStore.Detail(sheetId);
    if (detail is null) {
        Console.WriteLine($"Zasielka {sheetId} neexistuje.");
        return;
    }
    var units = Units.For(Settings.Load().Units, detail.Game);
    var sheetPath = args.Length >= 3
        ? args[2]
        : Path.Combine(DeliveryStore.DefaultDir(), WaybillSheet.SuggestedName(detail));
    var points = sheetStore.RoutesForGame(detail.Game).Routes.TryGetValue(sheetId, out var pts)
        ? pts
        : new List<RoutePoint>();
    foreach (var file in WaybillSheet.Save(detail, sheetStore.TimelineRows(sheetId), points, units, sheetPath, 300f)) {
        Console.WriteLine($"Ulozene: {file}");
    }
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
    var roam = statsStore.FreeroamTotals();

    // Aggregates span every game played, so they follow the most recent one.
    var u = Units.For(ConsoleFormat.UnitSetting, statsStore.MostRecentGame());

    Console.WriteLine(sinceMs.HasValue ? "Statistiky (obdobie)" : "Statistiky (vsetko)");
    Console.WriteLine("====================");
    Console.WriteLine($"zasielok spolu: {s.TotalDeliveries}  (accepted {s.Accepted}, review {s.Review}, rejected {s.Rejected})");
    Console.WriteLine($"vzdialenost: {u.FormatDistance(s.TotalDistanceKm)}");
    if (roam.DistanceKm > 0) {
        Console.WriteLine($"mimo zakazky: {u.FormatDistance(roam.DistanceKm)} ({roam.Stretches} usekov)");
        Console.WriteLine($"spolu najazdene: {u.FormatDistance(s.TotalDistanceKm + roam.DistanceKm)}");
    }
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

// Anything that escapes an event handler would otherwise take the window down
// with no explanation, or leave it wedged. Show it and write it down instead:
// a tracker that quietly dies mid-delivery is worse than one that complains.
AppDomain.CurrentDomain.UnhandledException += (_, e) => ReportCrash(e.ExceptionObject as Exception, fatal: true);

void ReportCrash(Exception? ex, bool fatal) {
    var text = ex?.ToString() ?? "neznáma chyba";
    try {
        var log = Path.Combine(DeliveryStore.DefaultDir(), "errors.log");
        Directory.CreateDirectory(DeliveryStore.DefaultDir());
        File.AppendAllText(log, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {text}{Environment.NewLine}{Environment.NewLine}");
    } catch {
        // Logging must never be the thing that brings the app down.
    }

    MessageBox.Show(
        (fatal ? "Waybill musí skončiť kvôli chybe:\n\n" : "Nastala chyba:\n\n") + (ex?.Message ?? "neznáma chyba")
        + "\n\nPodrobnosti sú v errors.log v priečinku s databázou.",
        "Waybill", MessageBoxButtons.OK, MessageBoxIcon.Error);
}

// One window at a time. Two of them poll the same shared memory and both record
// and save, and since each invents its own id for a job, the same drive lands in
// the database twice as two unrelated rows. The mutex is machine wide and the
// handle lives as long as the process, so it is released even on a hard kill.
using var single = new Mutex(initiallyOwned: true, "Waybill.SingleInstance", out var isOnly);
if (!isOnly) {
    MessageBox.Show(Strings.T("msg.alreadyRunning"), "Waybill", MessageBoxButtons.OK, MessageBoxIcon.Information);
    return;
}

// The window has to run on a single threaded COM apartment. Every file dialog is
// a shell COM object, and on a multithreaded apartment showing one disables the
// owner window and then never returns: no dialog ever appears, the message loop
// stops, and Windows ends the process as an AppHang - which is not an exception,
// so nothing reaches the handlers above and errors.log stays empty. An entry
// point normally declares this with [STAThread], but this file uses top level
// statements and the compiler generates its entry point without one, so the
// window gets a thread that carries the apartment instead.
var ui = new Thread(RunWindow) { Name = "waybill-ui" };
ui.SetApartmentState(ApartmentState.STA);
ui.Start();
ui.Join();

void RunWindow() {
    // Before any window exists: it decides which theme the system draws scrollbars
    // and other non-client parts in for this process.
    MainForm.UseDarkAppMode();

    // Registered here rather than next to the AppDomain handler: this one applies
    // to whichever thread adds it, so on the main thread it would never fire for
    // anything thrown inside the message loop.
    Application.ThreadException += (_, e) => ReportCrash(e.Exception, fatal: false);
    Application.Run(new MainForm());
}
