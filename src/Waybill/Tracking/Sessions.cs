using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Waybill.Storage;

namespace Waybill.Tracking;

/// <summary>
/// A sitting at the wheel, rather than a delivery or a day.
///
/// Waybill writes one recording per run of the app, which is very nearly the right
/// unit: it starts when the driver sits down and ends when they get up. Very nearly,
/// because the app gets closed and reopened for reasons that have nothing to do with
/// stopping, a crash, a restart, a quick look at something else, and counting each of
/// those as a fresh sitting would cut an evening into thirds.
///
/// So the rule is about the driving rather than about the app: a sitting breaks
/// wherever the telemetry goes quiet for longer than the chosen gap, an hour by
/// default. That covers both ways of stopping with one rule. Closing Waybill leaves a
/// gap between two recordings; closing the game leaves a gap inside one, because the
/// recording only advances while the game is running. Neither is special.
///
/// The gap is a preference rather than a constant, since it is the one number here
/// that somebody made up. Recordings are cut into stretches at a much smaller gap
/// when they are measured, and the stretches are put back together against the
/// preference when the list is asked for, so changing it regroups the whole history
/// without reading a file again.
///
/// The recordings stay one file per run. A sitting spanning three of them is a fact
/// about when somebody drove, not about how the tape was cut, and forcing the two to
/// agree would mean unpacking a finished recording to append to it.
/// </summary>
public static class Sessions {
    /// <summary>How long the telemetry can go quiet and still be the same sitting.</summary>
    public const int DefaultGapMinutes = 60;

    /// <summary>
    /// The gap a recording is cut at when it is measured.
    ///
    /// Small enough that no real break hides under it and large enough that nothing
    /// ordinary reaches it: the tracker writes a line a second while the game runs,
    /// so three minutes of silence means the game was shut, not that something
    /// stuttered. Cutting here rather than at the driver's own gap is what lets that
    /// gap be changed later without reading every recording again.
    /// </summary>
    private const long SegmentGapMs = 3 * 60_000;

    /// <summary>
    /// How much driving a stretch needs before it is one at all.
    ///
    /// Starting Waybill while the game is running writes a recording whether or not
    /// anybody drives, so a look at yesterday's figures leaves a stretch one second
    /// long. Six of those inside one evening turned two interruptions into eight, and
    /// none of them held a metre of road. Half a minute is longer than any accident
    /// of starting and stopping and shorter than anything worth calling a drive.
    /// </summary>
    private const int LeastTicks = 30;

    /// <summary>
    /// Brings the record of what each recording covers up to date.
    ///
    /// Reading a recording to its end is the only way to learn when it ends, so what
    /// is learned is kept: a file already measured is never opened again. The one
    /// still being written is the exception, since it grows, and it is cheap to
    /// remeasure because it is the only one not yet compressed.
    /// </summary>
    public static void Scan(DeliveryStore store, string folder) {
        if (!Directory.Exists(folder)) return;

        var known = store.KnownRecordings();
        foreach (var path in Directory.EnumerateFiles(folder, "session-*")) {
            var name = Path.GetFileName(path);
            var live = name.EndsWith(SessionFiles.Extension, StringComparison.OrdinalIgnoreCase);
            if (!live && known.Contains(name)) continue;

            var spans = Spans(path);
            if (spans.Count == 0) continue;
            store.RememberRecording(name, spans);
        }
    }

    /// <summary>
    /// The stretches of driving inside one recording, and how much is in each.
    ///
    /// A line that will not parse is skipped rather than fatal: a recording cut off by
    /// a crash is still worth whatever is readable in it.
    /// </summary>
    private static List<(long First, long Last, int Ticks)> Spans(string path) {
        var spans = new List<(long, long, int)>();
        long first = 0, last = 0;
        var ticks = 0;
        try {
            foreach (var line in SessionFiles.ReadLines(path)) {
                var at = line.IndexOf("\"t\":", StringComparison.Ordinal);
                if (at < 0) continue;
                var from = at + 4;
                var to = from;
                while (to < line.Length && (char.IsDigit(line[to]) || line[to] == '-')) to++;
                if (to == from || !long.TryParse(line[from..to], out var t)) continue;

                if (first == 0) {
                    first = t;
                } else if (t - last > SegmentGapMs) {
                    spans.Add((first, last, ticks));
                    first = t;
                    ticks = 0;
                }
                last = t;
                ticks++;
            }
        } catch {
            // A recording that cannot be read at all simply has no sitting in it.
        }
        if (first != 0) spans.Add((first, last, ticks));
        return spans;
    }

    /// <summary>The sittings themselves, newest first, with what was driven in each.</summary>
    public static List<SessionRow> List(DeliveryStore store, int gapMinutes = DefaultGapMinutes) {
        var stretches = store.Recordings();
        if (stretches.Count == 0) return new List<SessionRow>();

        var gap = gapMinutes * 60_000L;
        var windows = new List<(long From, long To)>();
        foreach (var f in stretches.Where(f => f.Ticks >= LeastTicks).OrderBy(f => f.First)) {
            if (windows.Count > 0 && f.First - windows[^1].To <= gap) {
                windows[^1] = (windows[^1].From, Math.Max(windows[^1].To, f.Last));
            } else {
                windows.Add((f.First, f.Last));
            }
        }

        var rows = windows.Select(w => store.SessionTotals(w.From, w.To)).ToList();
        rows.Reverse();
        return rows;
    }
}
