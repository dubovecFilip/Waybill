using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Waybill.Storage;

namespace Waybill.Tracking;

/// <summary>
/// A sitting at the wheel, rather than a delivery or a day.
///
/// Waybill already writes one recording per run of the app, which is very nearly
/// the right unit: it starts when the driver sits down and ends when they get up.
/// Very nearly, because the app gets closed and reopened for reasons that have
/// nothing to do with stopping, a crash, a restart, a quick look at something else,
/// and counting each of those as a fresh sitting would cut an evening into thirds.
///
/// So runs that follow one another closely are the same session. An hour is the
/// default gap, and it is a preference rather than a constant: it is the one number
/// here that somebody made up. A run that is still open has no gap after it at all,
/// which is why leaving the app running through a long break keeps the session
/// going: what ends a session is the driver leaving, not the driving stopping.
///
/// The recordings stay one file per run. A session spanning three of them is a fact
/// about when somebody drove, not about how the tape was cut, and forcing the two to
/// agree would mean unpacking a finished recording to append to it.
/// </summary>
public static class Sessions {
    /// <summary>How long a break between runs still counts as the same sitting.</summary>
    public const int DefaultGapMinutes = 60;

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

            var (first, last, ticks) = Span(path);
            if (ticks == 0) continue;
            store.RememberRecording(name, first, last, ticks);
        }
    }

    /// <summary>When a recording starts and ends, and how much is in it. A line that
    /// will not parse is skipped rather than fatal: a recording cut off by a crash is
    /// still worth what is readable in it.</summary>
    private static (long First, long Last, int Ticks) Span(string path) {
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
                if (first == 0) first = t;
                last = t;
                ticks++;
            }
        } catch {
            // A recording that cannot be read at all simply has no session in it.
        }
        return (first, last, ticks);
    }

    /// <summary>The sittings themselves, newest first, with what was driven in each.</summary>
    public static List<SessionRow> List(DeliveryStore store, int gapMinutes = DefaultGapMinutes) {
        var files = store.Recordings();
        if (files.Count == 0) return new List<SessionRow>();

        var gap = gapMinutes * 60_000L;
        var windows = new List<(long From, long To, int Runs)>();
        foreach (var f in files.OrderBy(f => f.First)) {
            if (windows.Count > 0 && f.First - windows[^1].To <= gap) {
                var last = windows[^1];
                windows[^1] = (last.From, Math.Max(last.To, f.Last), last.Runs + 1);
            } else {
                windows.Add((f.First, f.Last, 1));
            }
        }

        var rows = windows.Select(w => store.SessionTotals(w.From, w.To, w.Runs)).ToList();
        rows.Reverse();
        return rows;
    }
}
