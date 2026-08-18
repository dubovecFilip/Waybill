using Waybill.Storage;
using Waybill.Tracking;

namespace Waybill;

/// <summary>
/// The page the window opens on: what is happening now, what happened lately, and
/// the handful of settings worth changing without going looking for them.
///
/// It exists because every other page answers a question you have to already have.
/// The list answers "which delivery", the map answers "where", statistics answers
/// "how much" - and the live page answers "what is the tracker doing", in the
/// tracker's own terms, log and all. None of them answers "what is going on",
/// which is what someone alt-tabbing out of the cab actually wants to know.
///
/// So nothing here is new information. It is the three things already kept, shown
/// small: the drive in progress with its route drawn as it goes, the last few
/// deliveries, and the settings. Anything that needs more room than that has a
/// page of its own, one click away.
/// </summary>
public partial class MainForm {
    private readonly Panel _homePage = new();
    private readonly Label _homeHeadline = new();
    private readonly Label _homeDetail = new();
    private readonly Label _homeProgress = new();
    private readonly Panel _homeTrack = new();
    private readonly Panel _homeFill = new();
    private readonly Panel _homeLead = new();
    private readonly Panel _homeRecent = new();
    private RouteView? _homeMap;

    /// <summary>What the miniature is currently showing, so it is only redrawn when
    /// there is something new to draw. The live route grows by one point a second
    /// and the page refreshes twice a second, so redrawing every time would be three
    /// wasted redraws out of four.</summary>
    private string _homeShowing = "";
    private int _homeShown = -1;

    private const int RecentRows = 5;

    private Panel BuildHomePage() {
        _homePage.Dock = DockStyle.Fill;
        _homePage.BackColor = Canvas;
        _homePage.Padding = new Padding(16, 8, 16, 16);
        _homePage.AutoScroll = true;
        _homePage.Controls.Clear();

        var stack = new List<Control> {
            HomeHeading(Strings.T("home.onTheRoad")),
            BuildHomeJob(),
            HomeHeading(Strings.T("home.lately")),
            BuildHomeRecent(),
            HomeHeading(Strings.T("home.settings")),
            BuildHomeSettings(),
        };

        // Docked children stack in reverse of adding, so the page goes in backwards.
        for (var i = stack.Count - 1; i >= 0; i--) _homePage.Controls.Add(stack[i]);
        return _homePage;
    }

    private static Control HomeHeading(string text) => new Label {
        Dock = DockStyle.Top, Height = 36, Text = text, BackColor = Canvas,
        ForeColor = Ink, Font = new Font("Segoe UI", 10.5F, FontStyle.Bold),
        Padding = new Padding(0, 10, 0, 0),
    };

    /// <summary>
    /// The drive in progress: where it is going, what it is carrying, how far along
    /// it is, and the route so far drawn beside it.
    ///
    /// No log. The live page has one and it belongs there, where someone is checking
    /// whether the tracker is seeing the game. Here it would be a wall of lines
    /// nobody reads for a glance that takes a second.
    /// </summary>
    private Control BuildHomeJob() {
        // The miniature is painted on the same near black the page is, so without a
        // hairline round the whole thing the right half of the card reads as a hole
        // in the page rather than as part of the card.
        var frame = new Panel { Dock = DockStyle.Top, Height = 252, BackColor = Line, Padding = new Padding(1) };
        var card = new Panel { Dock = DockStyle.Fill, BackColor = Surface };

        _homeMap = NewMap(CurrentUnits());
        _homeMap.Dock = DockStyle.Right;
        _homeMap.Width = 430;
        _homeMap.ShowMarks = false;
        _homeMap.EmptyText = Strings.T("home.noRoute");
        _homeMap.Hint = "";
        // A miniature is for looking at, not for working with. Everything the big
        // maps offer is on the pages that own them, one click away.
        _homeMap.Cursor = Cursors.Default;

        var edge = new Panel { Dock = DockStyle.Right, Width = 1, BackColor = Line };

        var text = new Panel { Dock = DockStyle.Fill, BackColor = Surface, Padding = new Padding(20, 18, 20, 18) };

        _homeHeadline.Dock = DockStyle.Top;
        _homeHeadline.Height = 40;
        _homeHeadline.Font = new Font("Segoe UI", 15F, FontStyle.Bold);
        _homeHeadline.ForeColor = Ink;
        _homeHeadline.AutoEllipsis = true;
        _homeHeadline.Text = Strings.T("live.noJob");

        _homeDetail.Dock = DockStyle.Top;
        _homeDetail.Height = 26;
        _homeDetail.ForeColor = Muted;
        _homeDetail.AutoEllipsis = true;

        _homeProgress.Dock = DockStyle.Top;
        _homeProgress.Height = 26;
        _homeProgress.ForeColor = Ink;
        _homeProgress.Padding = new Padding(0, 6, 0, 0);

        _homeTrack.Dock = DockStyle.Top;
        _homeTrack.Height = 8;
        _homeTrack.Margin = new Padding(0);
        _homeTrack.BackColor = Raised;
        _homeLead.Dock = DockStyle.Left;
        _homeLead.Width = 0;
        _homeLead.BackColor = Muted;
        _homeFill.Dock = DockStyle.Left;
        _homeFill.Width = 0;
        _homeFill.BackColor = Accent;
        _homeTrack.Controls.Add(_homeFill);
        _homeTrack.Controls.Add(_homeLead);

        var open = MakeButton(Strings.T("home.openLive"), () => ShowPage("live"));
        var buttons = new FlowLayoutPanel {
            Dock = DockStyle.Bottom, Height = 40, BackColor = Surface,
            Padding = new Padding(0, 8, 0, 0), WrapContents = false,
        };
        buttons.Controls.Add(open);

        // Bottom-up again, and the spacers are what keep the bar off the text above
        // it without every label carrying its own margin.
        text.Controls.Add(buttons);
        text.Controls.Add(_homeProgress);
        text.Controls.Add(_homeTrack);
        text.Controls.Add(new Panel { Dock = DockStyle.Top, Height = 10, BackColor = Surface });
        text.Controls.Add(_homeDetail);
        text.Controls.Add(_homeHeadline);

        card.Controls.Add(text);
        card.Controls.Add(edge);
        card.Controls.Add(_homeMap);
        frame.Controls.Add(card);
        return frame;
    }

    private Control BuildHomeRecent() {
        _homeRecent.Dock = DockStyle.Top;
        _homeRecent.Height = RecentRows * 34;
        _homeRecent.BackColor = Canvas;
        return _homeRecent;
    }

    /// <summary>The last few deliveries, each opening its own card. The same rows as
    /// the list, without the columns that only matter when comparing one drive with
    /// another.</summary>
    private void FillHomeRecent() {
        _homeRecent.SuspendLayout();
        _homeRecent.Controls.Clear();

        var recent = _rows.Take(RecentRows).ToList();
        if (recent.Count == 0) {
            _homeRecent.Controls.Add(new Label {
                Dock = DockStyle.Top, Height = 34, Text = Strings.T("home.nothingYet"),
                ForeColor = Muted, Padding = new Padding(16, 8, 0, 0), BackColor = Canvas,
            });
            _homeRecent.ResumeLayout();
            return;
        }

        var u = CurrentUnits();
        var lines = new List<Control>();
        foreach (var row in recent) lines.Add(RecentRow(row, u));
        for (var i = lines.Count - 1; i >= 0; i--) _homeRecent.Controls.Add(lines[i]);
        _homeRecent.ResumeLayout();
    }

    private Control RecentRow(DeliveryRow row, Units u) {
        var line = new Panel {
            Dock = DockStyle.Top, Height = 32, BackColor = Surface,
            Margin = new Padding(0), Cursor = Cursors.Hand,
        };

        // The same gutter the list has, so a row means the same thing in both places.
        var gutter = new Panel { Dock = DockStyle.Left, Width = GutterWidth, BackColor = Canvas };
        gutter.Paint += (_, e) => {
            var g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var brush = new SolidBrush(VerdictColour(row.Stav));
            g.FillEllipse(brush, (GutterWidth - StripeWidth - 9) / 2f, (gutter.Height - 9) / 2f, 9, 9);
            if (row.Special) {
                HazardStripes(g, new RectangleF(gutter.Width - StripeWidth, 0, StripeWidth, gutter.Height), 210);
            }
        };

        Label Cell(string text, int width, Color colour, DockStyle dock, ContentAlignment align) => new() {
            Dock = dock, Width = width, Text = text, ForeColor = colour, AutoEllipsis = true,
            TextAlign = align, Padding = new Padding(10, 0, 10, 0),
        };

        var pay = Cell(u.FormatMoney(row.Zarobok), 110, Ink, DockStyle.Right, ContentAlignment.MiddleRight);
        var distance = Cell(row.Vzdialenost, 100, Muted, DockStyle.Right, ContentAlignment.MiddleRight);
        var when = Cell(row.Datum.ToString("dd.MM."), 60, Muted, DockStyle.Left, ContentAlignment.MiddleLeft);
        var route = Cell($"{row.Odkial}  →  {row.Kam}", 0, Ink, DockStyle.Fill, ContentAlignment.MiddleLeft);
        var cargo = Cell(row.Naklad, 170, Muted, DockStyle.Right, ContentAlignment.MiddleLeft);

        foreach (var part in new Control[] { line, when, route, cargo, distance, pay }) {
            part.Click += (_, _) => ShowDetail(row.Id);
            part.MouseEnter += (_, _) => line.BackColor = Raised;
            part.MouseLeave += (_, _) => line.BackColor = Surface;
            if (part != line) part.Cursor = Cursors.Hand;
        }
        // A docked child added later sits closer to the edge, so the rightmost
        // column goes in last. Money at the far right, where the eye looks for it.
        line.Controls.Add(route);
        line.Controls.Add(cargo);
        line.Controls.Add(distance);
        line.Controls.Add(pay);
        line.Controls.Add(when);
        line.Controls.Add(gutter);
        return line;
    }

    /// <summary>
    /// The three settings worth reaching for, as switches rather than as menus.
    ///
    /// They are all still under Settings, and the ones that are genuinely rare stay
    /// there alone. These three are the ones somebody changes while looking at their
    /// own numbers: which units the figures are in, which language they are in, and
    /// whether the drive is on the Discord profile.
    /// </summary>
    private Control BuildHomeSettings() {
        var box = new Panel { Dock = DockStyle.Top, Height = 3 * 44, BackColor = Surface };

        var units = new TriSwitch(
            Strings.T("menu.units.metric.short"), Strings.T("home.unitsAuto"), Strings.T("menu.units.imperial.short")) {
            Position = _settings.Units switch { "metric" => -1, "imperial" => 1, _ => 0 },
        };
        units.Changed += (_, _) => {
            _settings.Units = units.Position switch { -1 => "metric", 1 => "imperial", _ => "auto" };
            _settings.Save();
            ReloadHistory();
            ReloadStats();
        };

        var languages = Strings.All.ToList();
        var language = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
        foreach (var (code, name) in languages) {
            var pick = MakeButton(name, () => {
                if (_settings.Language == code) return;
                _settings.Language = code;
                _settings.Save();
                Strings.Language = code;
                BuildLayout();
            });
            if (_settings.Language == code) {
                pick.BackColor = AccentSoft;
                pick.ForeColor = Accent;
            }
            language.Controls.Add(pick);
        }

        var discord = new TriSwitch(Strings.T("home.off"), "", Strings.T("home.on"));
        // Two states, not three: the middle is skipped so the switch cannot be left
        // saying nothing about something that is either on or off.
        discord.Position = _settings.DiscordPresence ? 1 : -1;
        discord.Changed += (_, _) => {
            if (discord.Position == 0) { discord.Position = _settings.DiscordPresence ? 1 : -1; return; }
            _settings.DiscordPresence = discord.Position > 0;
            _settings.Save();
            if (_settings.DiscordPresence && string.IsNullOrWhiteSpace(_settings.DiscordAppId)) {
                AddLog(Strings.T("discord.needsAppId"));
            }
            StartDiscord();
        };

        var rows = new List<Control> {
            SettingRow(Strings.T("menu.units"), units),
            SettingRow(Strings.T("menu.language"), language),
            SettingRow(Strings.T("menu.discordPresence"), discord),
        };
        for (var i = rows.Count - 1; i >= 0; i--) box.Controls.Add(rows[i]);
        return box;
    }

    private static Control SettingRow(string label, Control control) {
        var row = new Panel { Dock = DockStyle.Top, Height = 44, BackColor = Surface, Padding = new Padding(20, 8, 20, 8) };
        var holder = new Panel { Dock = DockStyle.Fill, BackColor = Surface };
        control.Location = new Point(0, 0);
        holder.Controls.Add(control);
        row.Controls.Add(holder);
        row.Controls.Add(new Label {
            Dock = DockStyle.Left, Width = 210, Text = label, ForeColor = Muted,
            TextAlign = ContentAlignment.MiddleLeft,
        });
        return row;
    }

    /// <summary>
    /// Brings the page up to date with whatever the tracker can see. Called from the
    /// same half-second tick the live page uses, so it says the same thing.
    /// </summary>
    private void RefreshHome() {
        if (_homeMap is null || _engine is null) return;

        var job = _engine.ActiveJob;
        var state = _engine.ActiveState;
        var u = CurrentUnits();

        if (job is null || state is null) {
            _homeHeadline.Text = _engine.Connected ? Strings.T("live.noJob") : Strings.T("live.waitingGame");
            _homeHeadline.ForeColor = Muted;
            _homeDetail.Text = "";
            _homeProgress.Text = "";
            _homeTrack.Visible = false;
            ShowHomeRoute(null, "");
            return;
        }

        _homeHeadline.Text = $"{job.SourceCity}  →  {job.DestinationCity}";
        _homeHeadline.ForeColor = Ink;
        _homeDetail.Text = $"{job.Cargo} · {u.MassTonnes(job.CargoMassKg):0.0} {u.MassUnit}"
                         + $"   ·   {Strings.T("live.reward")} {u.FormatMoney(job.Income)}";

        // The same split the live page draws: the run out to the trailer at the head
        // of the bar in its own shade, the loaded leg measured against the plan.
        var driven = state.DistanceKm;
        var planned = job.PlannedDistanceKm;
        var toLoad = state.DistanceToLoadKm;
        var loaded = Math.Max(0, driven - toLoad);
        var ratio = planned > 0 ? Math.Clamp(loaded / planned, 0, 1) : 0;
        var whole = planned + toLoad;
        var lead = whole > 0 ? Math.Clamp(toLoad / whole, 0, 1) : 0;

        _homeTrack.Visible = true;
        var track = _homeTrack.ClientSize.Width;
        _homeLead.Width = (int)(track * lead);
        _homeFill.Width = (int)(track * (1 - lead) * ratio);
        _homeProgress.Text = $"{u.Distance(loaded):0.0} / {u.Distance(planned):0} {u.DistanceUnit}   ·   {ratio * 100:0} %"
            + (toLoad > 0.05 ? $"   (+{u.Distance(toLoad):0.0} {Strings.T("live.toLoad")})" : "");

        ShowHomeRoute(state, state.Game);
    }

    /// <summary>How many new points are worth a redraw. One a second is how fast
    /// the route grows, and redrawing for each of them means rebuilding the whole
    /// picture every second for a line that moved by a few pixels.</summary>
    private const int HomeRouteStep = 10;

    /// <summary>
    /// Draws the drive so far, with the cities this profile knows behind it.
    ///
    /// Only the drive itself: the rest of the history is a thousand times more line
    /// than this miniature can show, and it would have to be taken apart again on
    /// every redraw. Cities cost nothing and are what make a line mean somewhere.
    ///
    /// The view is refitted each time, so the frame grows with the drive rather than
    /// the truck wandering off the edge of it.
    /// </summary>
    private void ShowHomeRoute(JobState? state, string game) {
        if (_homeMap is null) return;

        var points = state?.TripPoints ?? new List<TripPoint>();
        var key = state is null ? "" : state.JobUid;
        var same = key == _homeShowing;
        if (same && points.Count < _homeShown + HomeRouteStep) return;
        _homeShowing = key;
        _homeShown = points.Count;

        if (points.Count < 2) {
            _homeMap.Show(Array.Empty<RouteLayer>(), 0, new List<CityAnchor>());
            return;
        }

        var live = new RouteLayer {
            // Deliveries carry their database id and this drive has none yet, so it
            // takes one no row can hold rather than borrowing somebody else's.
            Id = -1,
            Points = points.Select(p => new RoutePoint(p.AtMs, (float)p.X, (float)p.Z, (float)p.SpeedKmh)).ToList(),
        };
        var cities = game.Length > 0 ? RoutesFor(game).Cities : new List<CityAnchor>();
        _homeMap.Show(new List<RouteLayer> { live }, live.Id, cities);
    }
}
