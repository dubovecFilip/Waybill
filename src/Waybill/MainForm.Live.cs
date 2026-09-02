using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Waybill.Tracking;

namespace Waybill;

/// <summary>
/// The page a driver looks at with the game running.
///
/// Composed rather than assembled: a header line, a hero panel carrying the two cities
/// with the drive between them and a strip of six figures along its foot, and beneath
/// that the route drawing beside a rail of what has happened. The log, which used to
/// hold a third of the width, is behind a toggle.
///
/// Everything here is painted from one small model that the refresh fills in. A label
/// per figure would be thirty controls, each with its own font and padding, and every
/// one of them redrawn twice a second.
/// </summary>
public partial class MainForm {
    /// <summary>What the page is showing. Filled by the refresh, read by the painters,
    /// and never touched by anything else.</summary>
    private sealed class LiveShown {
        public bool Attached;
        public bool OnJob;
        public string From = "", FromUnder = "", To = "", ToUnder = "";
        public float Part;
        public string Driven = "", Percent = "", Wheel = "";
        public List<Tile> Tiles = new();
        public List<Step> Rail = new();
        public string Empty = "";
    }

    private sealed class Tile {
        public string Label = "", Figure = "", Under = "";
        public Color Ink = Look.Ink;
    }

    private sealed class Step {
        public string What = "", Detail = "";
        public Color Hue = Look.Route;
    }

    private readonly LiveShown _live = new();

    private Panel? _liveHeader, _liveHero, _liveRail, _liveLogWell;
    private Button? _logToggle;
    private bool _logOpen;

    // ---------- the page ----------

    private Control BuildLiveHeader() {
        var head = new Panel { Dock = DockStyle.Top, Height = 34, BackColor = Look.Window };
        head.Paint += (_, e) => PaintLiveHeader(e.Graphics, head);

        var toggle = MakeQuietButton(Strings.T("live.log"), () => {
            _logOpen = !_logOpen;
            if (_liveLogWell is { } well) well.Visible = _logOpen;
            _logToggle!.ForeColor = _logOpen ? Look.Accent : Look.Muted;
        });
        toggle.Width = 76;
        toggle.Height = 26;
        _logToggle = toggle;
        head.Controls.Add(toggle);
        head.Resize += (_, _) => toggle.Location = new Point(head.Width - toggle.Width, (head.Height - toggle.Height) / 2);
        toggle.Location = new Point(head.Width - toggle.Width, (head.Height - toggle.Height) / 2);
        _liveHeader = head;
        return head;
    }

    private void PaintLiveHeader(Graphics g, Panel head) {
        g.Clear(Look.Window);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        var y = (head.Height - Look.PageHeading.Height) / 2f;
        Look.Text(g, Strings.T("job.onTheRoad"), Look.PageHeading, Look.Ink, 0, y);
        var at = Look.Measure(g, Strings.T("job.onTheRoad"), Look.PageHeading).Width + 14;

        if (_live.OnJob) {
            var size = Look.PillSize(g, Strings.T("live.jobPill"));
            Look.Pill(g, new PointF(at, (head.Height - size.Height) / 2), Strings.T("live.jobPill"), Look.Accent);
        }

        if (_live.Wheel.Length > 0) {
            var right = head.Width - (_logToggle?.Width ?? 0) - 16;
            Look.TextRight(g, _live.Wheel, Look.Small, Look.Dim, right, (head.Height - Look.Small.Height) / 2f);
        }
    }

    /// <summary>
    /// The hero: where the load came from, where it is going, how far along it is, and
    /// six figures about the drive along the foot.
    /// </summary>
    private Control BuildLiveHero() {
        var hero = new Panel { Dock = DockStyle.Top, Height = 152, BackColor = Look.Window, Padding = new Padding(0, 8, 0, 0) };
        var panel = new Panel { Dock = DockStyle.Fill, BackColor = Look.Raised };
        panel.Paint += (_, e) => PaintLiveHero(e.Graphics, panel);
        hero.Controls.Add(panel);
        _liveHero = panel;

        // The two launch buttons live on the hero while there is no game to talk about,
        // which is the one thing worth offering on a page with nothing to say.
        var launch = new FlowLayoutPanel {
            AutoSize = true, BackColor = Color.Transparent, WrapContents = false, Location = new Point(20, 74),
        };
        foreach (var game in new[] { SimGame.Ets2, SimGame.Ats }) {
            var installed = GameLauncher.IsInstalled(game);
            var start = MakeButton("▷    " + GameLauncher.DisplayName(game), () => LaunchGame(game));
            start.Enabled = installed;
            start.Margin = new Padding(0, 0, 10, 0);
            if (!installed) _tips.SetToolTip(start, Strings.T("msg.gameNotFound"));
            launch.Controls.Add(start);
        }
        _jobLaunch = launch;
        panel.Controls.Add(launch);
        return hero;
    }

    private void PaintLiveHero(Graphics g, Panel panel) {
        g.Clear(Look.Window);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        var box = new RectangleF(0, 0, panel.Width - 1, panel.Height - 1);
        Look.Surface(g, box, Look.Raised, Look.Border);

        if (!_live.OnJob) {
            Look.Text(g, _live.From, Look.Semi(20), Look.Muted, 20, 26);
            Look.Text(g, _live.Empty, Look.Body, Look.Dim, 20, 54);
            return;
        }

        // The two ends, each under a small capital label, the origin left and the
        // destination right so the drive reads across the panel the way it happened.
        Look.Tracked(g, Strings.T("live.from").ToUpperInvariant(), Look.Label, Look.Dim, 20, 16);
        Look.Text(g, _live.From, Look.Semi(22), Look.Ink, 20, 32);
        Look.Text(g, _live.FromUnder, Look.Caption, Look.Dim, 20, 62);

        var right = panel.Width - 20;
        var toLabel = Look.TrackedWidth(g, Strings.T("live.to").ToUpperInvariant(), Look.Label);
        Look.Tracked(g, Strings.T("live.to").ToUpperInvariant(), Look.Label, Look.Dim, right - toLabel, 16);
        Look.TextRight(g, _live.To, Look.Semi(22), Look.Ink, right, 32);
        Look.TextRight(g, _live.ToUnder, Look.Caption, Look.Dim, right, 62);

        // The track between them, with the truck riding the head of the fill.
        var fromWide = Math.Max(Look.Measure(g, _live.From, Look.Semi(22)).Width,
                                Look.Measure(g, _live.FromUnder, Look.Caption).Width);
        var toWide = Math.Max(Look.Measure(g, _live.To, Look.Semi(22)).Width,
                              Look.Measure(g, _live.ToUnder, Look.Caption).Width);
        var trackLeft = 20 + fromWide + 28;
        var trackRight = right - toWide - 28;
        if (trackRight - trackLeft > 80) {
            var track = new RectangleF(trackLeft, 40, trackRight - trackLeft, 7);
            Look.Track(g, track, _live.Part, truck: true, ring: Look.Raised);
            Look.Text(g, _live.Driven, Look.Small, Look.Secondary, trackLeft, 58);
            Look.TextRight(g, _live.Percent, Look.Small, Look.Accent, trackRight, 58);
        }

        // The strip of six along the foot, on a hairline grid: equal widths, butted
        // together, so the eye reads a row of figures rather than six floating cards.
        var strip = new RectangleF(1, panel.Height - 62, panel.Width - 2, 61);
        using (var line = new Pen(Look.Hairline)) {
            g.DrawLine(line, strip.X, strip.Y, strip.Right, strip.Y);
        }
        if (_live.Tiles.Count == 0) return;

        var wide = strip.Width / _live.Tiles.Count;
        for (var i = 0; i < _live.Tiles.Count; i++) {
            var tile = _live.Tiles[i];
            var at = new RectangleF(strip.X + i * wide, strip.Y, wide, strip.Height);
            if (i > 0) {
                using var line = new Pen(Look.Hairline);
                g.DrawLine(line, at.X, at.Y + 10, at.X, at.Bottom - 10);
            }
            Look.FigureTile(g, new RectangleF(at.X + 14, at.Y + 11, at.Width - 20, at.Height - 16),
                            tile.Label, tile.Figure, tile.Under, tile.Ink, figureFont: Look.FigureSmall);
        }
    }

    /// <summary>The rail of what has happened, and the log well under it when it is
    /// asked for.</summary>
    private Control BuildLiveSide() {
        // The strip of what was noticed lately used to live at the foot of the column
        // and is read from disk once; the rail is where it is shown now.
        if (_feedLines.Count == 0) ReadFeed();

        var side = new Panel { Dock = DockStyle.Right, Width = 268, BackColor = Look.Window, Padding = new Padding(14, 0, 0, 0) };

        var well = new Panel { Dock = DockStyle.Bottom, Height = 132, BackColor = Look.Window, Padding = new Padding(0, 12, 0, 0), Visible = _logOpen };
        var wellInner = new Panel { Dock = DockStyle.Fill, BackColor = Look.Well, Padding = new Padding(10, 8, 4, 8) };
        _log.Dock = DockStyle.Fill;
        _log.BorderStyle = BorderStyle.None;
        _log.BackColor = Look.Well;
        _log.ForeColor = Look.Dim;
        _log.Font = Look.Mono11;
        _log.IntegralHeight = false;
        _log.SelectionMode = SelectionMode.None;
        wellInner.Controls.Add(_log);
        well.Controls.Add(wellInner);
        _liveLogWell = well;

        var rail = new Panel { Dock = DockStyle.Fill, BackColor = Look.Panel };
        rail.Paint += (_, e) => PaintRail(e.Graphics, rail);
        _liveRail = rail;

        side.Controls.Add(rail);
        side.Controls.Add(well);
        return side;
    }

    private void PaintRail(Graphics g, Panel rail) {
        g.Clear(Look.Window);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        var box = new RectangleF(0, 0, rail.Width - 1, rail.Height - 1);
        Look.Surface(g, box, Look.Panel, Look.Border);
        Look.Tracked(g, Strings.T("live.happened").ToUpperInvariant(), Look.Label, Look.Dim, 14, 13);

        if (_live.Rail.Count == 0) {
            Look.Text(g, Strings.T("live.nothingYet"), Look.Body, Look.Faint, 14, 44);
            return;
        }

        var y = 42f;
        const float step = 40f;
        for (var i = 0; i < _live.Rail.Count && y < rail.Height - 20; i++) {
            var one = _live.Rail[i];
            // The oldest entry on the rail is drawn in dim ink, since it is the one
            // about to fall off the end of it.
            var last = i == _live.Rail.Count - 1 || y + step >= rail.Height - 20;
            Look.RailStep(g, new PointF(22, y + 7), last ? Look.Dim : one.Hue, last, step);
            Look.Text(g, Look.Clip(g, one.What, Look.Strong, rail.Width - 60), Look.Strong,
                      last ? Look.Muted : Look.Secondary, 38, y);
            Look.Text(g, Look.Clip(g, one.Detail, Look.Caption, rail.Width - 60), Look.Caption, Look.Dim, 38, y + 17);
            y += step;
        }
    }

    /// <summary>A quiet button: a control tone, a hairline edge, one word. Used where a
    /// page needs an affordance that is not the one thing it is for.</summary>
    private Button MakeQuietButton(string text, Action click) {
        var b = new Button {
            Text = text, AutoSize = false, Height = 28, Width = 96,
            FlatStyle = FlatStyle.Flat, BackColor = Look.Chrome, ForeColor = Look.Muted,
            Font = Look.Small, Cursor = Cursors.Hand, TabStop = false,
        };
        b.FlatAppearance.BorderColor = Look.Border;
        b.FlatAppearance.MouseOverBackColor = Look.Control;
        b.Click += (_, _) => click();
        return b;
    }

    // ---------- what the page is showing ----------

    /// <summary>
    /// Fills the model behind the page from the job in progress, or from the absence of
    /// one, and asks the three painted panels to draw themselves again.
    /// </summary>
    private void ShowLive(JobInfo? job, JobState? state, Units u) {
        _live.Attached = _engine?.Connected ?? false;
        _live.OnJob = job is not null && state is not null;

        if (job is null || state is null) {
            _live.From = _live.Attached ? Strings.T("live.noJob") : Strings.T("live.waitingGame");
            _live.Empty = _live.Attached ? Strings.T("live.betweenJobs") : Strings.T("live.startOne");
            _live.Wheel = "";
            _live.Tiles.Clear();
            _live.Rail = FeedSteps();
            RepaintLive();
            return;
        }

        _live.From = _settings.CityRegions ? Places.Say(state.Game, job.SourceCity, job.SourceCityId) : job.SourceCity;
        _live.To = _settings.CityRegions ? Places.Say(state.Game, job.DestinationCity, job.DestinationCityId) : job.DestinationCity;
        _live.FromUnder = job.SourceCompany;
        _live.ToUnder = job.DestinationCompany;

        var driven = state.DistanceKm;
        var planned = job.PlannedDistanceKm;
        var toLoad = state.DistanceToLoadKm;
        var loaded = Math.Max(0, driven - toLoad);
        var ratio = planned > 0 ? loaded / planned : 0;
        _live.Part = (float)Math.Clamp(ratio, 0, 1);
        _live.Driven = $"{u.Distance(loaded):0.0} {Strings.T("live.ofDriven")} {u.Distance(planned):0} {u.DistanceUnit}";
        _live.Percent = planned > 0
            ? $"{ratio * 100:0} %  ·  {u.Distance(Math.Max(0, planned - loaded)):0.0} {u.DistanceUnit} {Strings.T("live.toGo")}"
            : "";
        _live.Wheel = $"{Strings.T("live.atTheWheel")} {Span(state.DrivingMs)}";

        var speed = state.TripPoints.Count > 0 ? state.TripPoints[^1].SpeedKmh : 0;
        var truckDamage = Math.Max(0, state.LastTruckWear - state.StartTruckWear) * 100;
        var loadDamage = Math.Max(0, state.LastTrailerWear - state.StartTrailerWear) * 100;
        var fines = state.Fines.Sum(f => f.Amount);

        _live.Tiles = new List<Tile> {
            new() { Label = Strings.T("live.speed"), Figure = u.FormatSpeed(speed),
                    Under = $"{Strings.T("live.top")} {u.FormatSpeed(state.TopSpeedKmh)}" },
            new() { Label = Strings.T("live.fuel"), Figure = u.FormatVolume(state.FuelUsedL),
                    Under = state.Refuels > 0 ? $"{state.Refuels}× {Strings.T("live.refuelled")}" : Strings.T("live.noRefuel") },
            new() { Label = Strings.T("live.loadDamage"), Figure = $"{loadDamage:0.0} %",
                    Under = loadDamage < 0.05 ? Strings.T("live.notAScratch") : Strings.T("live.sinceLoading"),
                    Ink = loadDamage < 0.05 ? Look.Whole : Look.Accent },
            new() { Label = Strings.T("live.truckDamage"), Figure = $"{truckDamage:0.0} %",
                    Under = $"{state.Collisions}× {Strings.T("live.collisions")}",
                    Ink = truckDamage < 0.05 ? Look.Whole : Look.Accent },
            new() { Label = Strings.T("live.fines"), Figure = u.FormatMoney(fines),
                    Under = $"{state.Fines.Count}× {Strings.T("live.fined")}",
                    Ink = fines > 0 ? Look.Lost : Look.Ink },
            new() { Label = Strings.T("live.pay"), Figure = u.FormatMoney(job.Income),
                    Under = Strings.T("live.onArrival") },
        };

        _live.Rail = JobSteps(state);
        RepaintLive();
    }

    private void RepaintLive() {
        _liveHeader?.Invalidate();
        _liveHero?.Invalidate();
        _liveRail?.Invalidate();
        if (_jobLaunch is { } launch) launch.Visible = !_live.OnJob && !_live.Attached;
    }

    private static string Span(long ms) {
        var t = TimeSpan.FromMilliseconds(ms);
        return t.TotalHours >= 1 ? $"{(int)t.TotalHours} h {t.Minutes:00} min" : $"{t.Minutes} min";
    }

    /// <summary>What has happened on this job, newest first, as the rail draws it.</summary>
    private List<Step> JobSteps(JobState state) {
        var steps = new List<Step>();
        foreach (var e in Enumerable.Reverse(state.Timeline).Take(12)) {
            var when = DateTimeOffset.FromUnixTimeMilliseconds(e.AtMs).LocalDateTime.ToString("HH:mm");
            var detail = e.Detail ?? "";
            var hue = e.Type switch {
                "fine" => Look.Lost,
                "collision" => Look.Lost,
                "refuel" => Look.Route,
                "rest" => Look.Route,
                "cargo_loaded" or "trailer_coupled" => Look.Accent,
                _ => Look.Route,
            };
            steps.Add(new Step { What = EventWord(e), Detail = detail.Length > 0 ? $"{when} · {detail}" : when, Hue = hue });
        }
        return steps;
    }

    /// <summary>The same rail with nothing on the hook: the strip of what was noticed
    /// lately, which used to sit at the foot of the sidebar.</summary>
    private List<Step> FeedSteps() =>
        _feedLines.OrderByDescending(line => line.At).Take(8).Select(line => new Step {
            What = line.Text,
            Detail = line.At.ToString("dd.MM HH:mm") + (line.Detail.Length > 0 ? $" · {line.Detail}" : ""),
            Hue = line.Kind == Noticed.Award ? Look.Accent : line.Kind == Noticed.Delivered ? Look.Whole : Look.Route,
        }).ToList();

    private static string EventWord(JobEvent e) => Strings.T("event." + e.Type) is { } named && !named.StartsWith("event.")
        ? named : e.Type.Replace('_', ' ');
}
