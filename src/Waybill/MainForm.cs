using System.Data;
using System.Reflection;
using System.Drawing.Drawing2D;
using System.Diagnostics;
using Waybill.Integrations;
using Waybill.Storage;
using Waybill.Tracking;

namespace Waybill;

/// <summary>
/// The whole UI: a live panel on top (what the engine is doing right now), a
/// sidebar to choose between the delivery history and the statistics, and the
/// chosen page filling the rest. The engine runs in this same process, so
/// starting the app is all the user does.
/// </summary>
/// <summary>Menus are drawn by the framework from a fixed colour table, so a dark
/// window needs its own or the drop-downs stay white.</summary>

public partial class MainForm : Form {
    // The palette lives in Look, one place for the whole window. These are the names
    // this file has always used, pointed at it: Canvas is the page behind the panels,
    // Surface a panel on it, Raised the step above that.
    private static readonly Color Canvas = Look.Window;
    private static readonly Color Surface = Look.Panel;
    private static readonly Color Raised = Look.Raised;
    private static readonly Color Line = Look.Border;
    private static readonly Color Ink = Look.Ink;
    private static readonly Color Muted = Look.Muted;
    // Amber rather than blue: it is the colour of a truck's indicators and warning
    // boards, and it stays legible on a dark ground where blue goes muddy.
    /// <summary>What a cell says when there is nothing to say. A dash rather than an
    /// empty cell: a column of figures with holes in it reads as a column that failed
    /// to load, and a dash reads as an answer.</summary>
    private const string Nothing = "\u2014";

    private static readonly Color Accent = Look.Accent;
    /// <summary>The accent as a wash rather than as paint, flattened against the chrome
    /// it is drawn on, since a control's BackColor cannot be translucent.</summary>
    private static readonly Color AccentSoft = Color.FromArgb(38, 32, 22);

    private readonly DeliveryStore _store = new();
    private readonly Settings _settings = Settings.Load();
    private TrackerEngine? _engine;
    private DiscordPresence? _discord;

    private readonly Label _status = new();
    private readonly Label _jobLine = new();
    private readonly Label _jobDetail = new();
    private readonly Panel _progressTrack = new();
    private readonly Panel _progressFill = new();
    private readonly Panel _progressLead = new();
    private readonly Panel _progressOver = new();
    private readonly Label _progressText = new();
    private readonly ListBox _log = new();

    private readonly DataGridView _grid = new();

    /// <summary>The history itself, drawn rather than bound. See DeliveryList.</summary>
    private readonly DeliveryList _list = new();
    private Panel? _historyHead;
    private Panel? _historyLegend;

    /// <summary>The trucks and the sittings, both stacks of cards. See CardStack.</summary>
    private readonly CardStack _truckCards = new();
    private readonly CardStack _sessionCards = new();

    /// <summary>The routes of the game the map is showing, beside the drawing.</summary>
    private readonly CardStack _mapList = new();

    /// <summary>The three things the history can be narrowed to, each on or off.</summary>
    private Panel? _mapBar;
    private string _mapNote = "";

    /// <summary>The three things the history can be narrowed to, each on or off.</summary>
    private readonly Chip _oversizeChip = new();
    private readonly Chip _lateChip = new();
    private readonly Chip _damagedChip = new();

    private void OnMapRouteOpened(object? behind) {
        if (behind is long id) ShowDetail(id);
    }
    private Panel? _truckHead;
    private Panel? _sessionHead;
    private string _truckTotals = "";
    private string _sessionTotals = "";

    /// <summary>What the page says beside its own name: how much history there is, in
    /// the units of whichever game is being shown.</summary>
    private string _historyTotals = "";
    private readonly TextBox _search = new();
    private readonly TriSwitch _gameFilter =
        new(GameName("Ets2"), Strings.T("filter.both"), GameName("Ats"));
    private readonly TriSwitch _cargoFilter =
        new(Strings.T("filter.ordinary"), Strings.T("filter.both"), Strings.T("filter.oversize"));
    private readonly TableLayoutPanel _statsGrid = new();

    /// <summary>Which sidebar page is showing. Kept across a language change, which
    /// rebuilds every control from scratch.</summary>
    private string _page = "live";
    private readonly Panel _content = new();
    private readonly Panel _detailPage = new();
    /// <summary>Which delivery the card is showing, so the window can put it back
    /// after being rebuilt. Changing the language rebuilds everything, and the card
    /// was the one page that came back in the language it was built in.</summary>
    private long? _detailId;
    /// <summary>The timeline column, which lives at width zero until asked for.</summary>
    private Panel? _detailSide;
    /// <summary>The handle down its left edge, hidden while the column is shut.</summary>
    private Panel? _detailGrip;
    /// <summary>How wide the column opens. Set by dragging the handle and kept for
    /// the rest of the session, so a driver who wants the log wide gets it wide on
    /// the next delivery too rather than pulling it out again each time.</summary>
    private int _timelineWidth = 480;
    /// <summary>How tall the drawings are inside that column, the log taking the
    /// rest. Kept the same way and for the same reason.</summary>
    private int _routeHeight = 396;
    private RouteView? _cardMap;
    private HeightView? _cardProfile;
    private System.Windows.Forms.Timer? _detailSlide;

    private List<DeliveryRow> _rows = new();
    private readonly Dictionary<string, GameRoutes> _routes = new();
    /// <summary>For the map's glyph buttons, which have no room to say in words what
    /// they do.</summary>
    private readonly ToolTip _tips = new();
    private RouteView? _mapPage;
    private readonly Picker _mapGame = new();
    /// <summary>
    /// What each entry of <see cref="_mapGame"/> stands for: the game as the database
    /// spells it, and which of that game's maps to draw under it.
    ///
    /// One entry per game while a game has one map or none, which is every ordinary
    /// case. A game with several worlds gets one entry each, so choosing which world
    /// to look at is the same act as choosing which game, rather than a setting
    /// somewhere else that the page silently obeys.
    /// </summary>
    private List<(string Game, string Map)> _mapGames = new();

    /// <summary>The column of pages, kept so the map can have the window to itself.</summary>
    private Panel? _sidebar;

    /// <summary>
    /// Whether the drive in progress has the window to itself.
    ///
    /// Not the screen: this is still a window, still movable, still one alt-tab from
    /// whatever else is open. What goes are the parts that answer questions nobody is
    /// asking while driving, which is everything except the map and how far along the
    /// delivery is.
    /// </summary>
    private bool _focusMode;

    public MainForm() {
        Text = "Waybill";
        Width = 1100;
        Height = 720;
        MinimumSize = new Size(900, 560);
        Font = new Font("Segoe UI", 9F);
        StartPosition = FormStartPosition.CenterScreen;
        DoubleBuffered = true;

        // The window icon comes from the same .ico the exe is built with, so the
        // taskbar, alt-tab and the title bar all match.
        // Beside the exe when the project was built the ordinary way, and out of
        // the exe itself when it was published as a single file, where nothing is
        // beside it to find. Without the second path the published build ran under
        // the blank default icon while carrying its own the whole time.
        try {
            var iconPath = Path.Combine(AppContext.BaseDirectory, "waybill.ico");
            if (File.Exists(iconPath)) Icon = new Icon(iconPath);
            else if (Environment.ProcessPath is { } exe) Icon = Icon.ExtractAssociatedIcon(exe);
        } catch { /* a missing icon is not worth failing over */ }

        Strings.Language = _settings.Language;

        // Before anything asks where the games are, including the menu built below.
        foreach (var game in new[] { SimGame.Ats, SimGame.Ets2 }) {
            GameLauncher.SetOverride(game, _settings.PathFor(game));
        }

        BuildLayout();
        KeyPreview = true;
        KeyDown += (_, k) => {
            if (k.KeyCode == Keys.F11 || (k.KeyCode == Keys.Escape && _focusMode)) {
                ToggleFocus();
                k.Handled = true;
            }
        };

        Load += (_, _) => {
            UseDarkTitleBar();
            UseDarkScrollbars(this);
            StartEngine();
        };
        // Rebuilt controls are new windows, so they need asking again.
        //
        // The repaint afterwards is for the delivery list. Its very first painting
        // comes out with pale vertical lines between some of the columns, which no
        // later painting draws: they went away a cell at a time as the list was
        // clicked through, and a resize cleared them all at once. Posting the
        // repaint rather than asking for it here is the whole point, since asking
        // now is asking before that first painting has happened.
        Shown += (_, _) => UseDarkScrollbars(this);
        _grid.Paint += DrawTheListAgainOnce;
        FormClosing += (_, _) => {
            _engine?.Dispose();
            _discord?.Dispose();
        };

        var timer = new System.Windows.Forms.Timer { Interval = 500 };
        timer.Tick += (_, _) => RefreshJob();
        timer.Start();
    }

    /// <summary>
    /// Builds every control from scratch. Called again when the language changes:
    /// column headers, placeholders and menu entries are all baked into controls at
    /// creation time, so rebuilding is far less error prone than hunting down each
    /// piece of text to reassign.
    /// </summary>
    private void BuildLayout() => Quiet(this, () => {
        Controls.Clear();
        BackColor = Canvas;

        var content = BuildContent();
        var sidebar = BuildSidebarColumn();
        var bar = BuildTitleBar();
        _sidebar = sidebar;
        sidebar.Visible = !_focusMode;
        bar.Visible = !_focusMode;

        // Docked children stack in reverse order of adding, so the filling one goes
        // in first and the outermost edges last.
        Controls.Add(content);
        Controls.Add(sidebar);
        Controls.Add(bar);

        // Every panel, table and grid in the window paints into a buffer from here
        // on, which is what keeps a redraw from showing its working.
        SmoothPainting(this);
        Retype(this);

        ReloadHistory();
        ReloadStats();
        // The card is drawn once when a delivery is opened rather than by ShowPage,
        // so a rebuilt window would show the page it was on with the old card still
        // on it, in the language it was built in.
        if (_page == "detail" && _detailId is { } open) ShowDetail(open);
        ShowPage(_page);
        UseDarkScrollbars(this);
    });

    // ---------- sidebar ----------

    /// <summary>Deliveries and statistics used to be tabs. As a sidebar they read as
    /// two places in the app rather than two folders inside one, and the labels get
    /// room to be words instead of cramped tab strips.</summary>
    // ---------- redrawing without showing the working ----------

    private const int WmSetRedraw = 0x000B;
    private const int RdwInvalidate = 0x0001;
    private const int RdwErase = 0x0004;
    private const int RdwFrame = 0x0400;
    private const int RdwAllChildren = 0x0080;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hwnd, int message, IntPtr wparam, IntPtr lparam);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool RedrawWindow(IntPtr hwnd, IntPtr rect, IntPtr region, int flags);

    /// <summary>
    /// Rebuilds the contents of a panel without the rebuilding being visible.
    ///
    /// Half of this window is panels that are emptied and filled again: the strip of
    /// what just happened, the awards, the figures, a delivery's card. Emptied, a
    /// panel paints itself bare, and each control added afterwards paints itself as it
    /// arrives, so a rebuild that takes twenty milliseconds is twenty milliseconds of
    /// the page flashing and half drawn rows appearing in it.
    ///
    /// Painting is switched off at the window itself for the length of the rebuild and
    /// switched back on with one redraw of the lot, so what the driver sees is the old
    /// contents and then the new ones.
    /// </summary>
    private static void Quiet(Control host, Action rebuild) {
        if (!host.IsHandleCreated) {
            host.SuspendLayout();
            rebuild();
            host.ResumeLayout(true);
            return;
        }
        SendMessage(host.Handle, WmSetRedraw, IntPtr.Zero, IntPtr.Zero);
        try {
            host.SuspendLayout();
            rebuild();
            host.ResumeLayout(true);
        } finally {
            SendMessage(host.Handle, WmSetRedraw, new IntPtr(1), IntPtr.Zero);
            RedrawWindow(host.Handle, IntPtr.Zero, IntPtr.Zero,
                         RdwInvalidate | RdwErase | RdwFrame | RdwAllChildren);
        }
    }

    /// <summary>
    /// Turns on double buffering for a control and everything inside it.
    ///
    /// A plain panel, a table layout and a grid all paint straight to the screen,
    /// which is what leaves the torn edges and the flash of the background between a
    /// panel being cleared and its ground being painted. The property that fixes it is
    /// protected on every one of them, so it is set the only way from outside.
    /// </summary>
    /// <remarks>
    /// A table is left out of this on purpose. It already buffers its own painting,
    /// and made to do it twice it drew a near black hairline down the right edge of
    /// every cell and along the bottom of every row: a black grid over a dark window,
    /// worst against the quiet gutter where the verdict dots are. What it looks like
    /// is cells in black frames.
    /// </remarks>
    private static void SmoothPainting(Control root) {
        var flag = typeof(Control).GetProperty("DoubleBuffered",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        // A panel only invalidates the strip a resize uncovers, which is right for a
        // panel that draws nothing and wrong for every panel in this window: a heading
        // pushed against the right edge, a figure at the end of a row and a pill at the
        // end of the bar are all drawn from the width, so the old drawing stayed behind
        // as the window narrowed and the page filled with ghosts of itself.
        var whole = typeof(Control).GetProperty("ResizeRedraw",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        void Walk(Control c) {
            if (c is Panel or TableLayoutPanel or FlowLayoutPanel) {
                try { flag?.SetValue(c, true, null); } catch { /* not worth failing over */ }
                try { whole?.SetValue(c, true, null); } catch { /* nor this */ }
            }
            foreach (Control child in c.Controls) Walk(child);
        }
        Walk(root);
    }

    // ---------- what just happened ----------

    /// <summary>What kind of thing a line in the strip is, which is the mark beside
    /// it.</summary>
    private enum Noticed {
        /// <summary>A load taken on: the trailer hitched, or the cargo put aboard.</summary>
        Started,
        /// <summary>A delivery handed over.</summary>
        Delivered,
        /// <summary>An award earned.</summary>
        Award,
    }

    private readonly Panel _feed = new();
    private readonly List<(DateTime At, Noticed Kind, string Text, string Detail)> _feedLines = new();

    /// <summary>How many of the last things that happened are kept in front of the
    /// driver. Five, because the strip has to be able to hold all of them at once
    /// without pushing the pages out of the sidebar.</summary>
    private const int FeedLines = 5;

    private const int FeedLineHeight = 32;

    /// <summary>
    /// The foot of the sidebar, saying what just happened.
    ///
    /// The log on the live page answers "is the tracker seeing the game", which is a
    /// different question and belongs to that page. This answers "did it notice what I
    /// just did", and it has to be legible from whichever page the driver happens to be
    /// on, so it lives beside all of them rather than on any one.
    ///
    /// It takes no room until there is something to say and never more than five lines
    /// of it. Each line carries a mark for what kind of thing it was, since in a column
    /// this narrow the words get cut and the mark never does.
    /// </summary>
    private Control BuildFeed() {
        if (_feedLines.Count == 0) ReadFeed();
        _feed.Dock = DockStyle.Bottom;
        _feed.BackColor = Surface;
        _feed.Padding = new Padding(0, 6, 0, 6);
        _feed.Height = 0;
        _feed.Controls.Clear();
        DrawFeed();
        return _feed;
    }

    /// <summary>
    /// Notes one thing worth noticing.
    ///
    /// Coupling is the moment on a hired trailer, since it comes already loaded and
    /// hitching it is the whole of taking the load on. With the driver's own trailer
    /// they were hitched up long before the dock, so the moment is the cargo going
    /// on. Recording whichever of the two actually meant something keeps the strip to
    /// things that happened rather than things that were reported.
    /// </summary>
    private void FeedNote(JobEvent e) {
        var owned = _engine?.ActiveState?.TrailerChain.Any(u => u.IsOwned) ?? false;
        var text = e.Type switch {
            "trailer_coupled" when !owned => Strings.T("feed.coupled"),
            "cargo_loaded" when owned => Strings.T("feed.loaded"),
            _ => null,
        };
        if (text is not null) Happened(Noticed.Started, text);
    }

    private void Happened(Noticed kind, string text, string detail = "") {
        _feedLines.Add((DateTime.Now, kind, text, detail));
        while (_feedLines.Count > FeedLines) _feedLines.RemoveAt(0);
        KeepFeed();
        DrawFeed();
    }

    /// <summary>Where the last few things that happened are kept between runs.</summary>
    private static string FeedPath => Path.Combine(DeliveryStore.DefaultDir(), "noticed.json");

    /// <summary>
    /// Writes the strip down, so closing the window does not wipe the evening.
    ///
    /// An award earned an hour ago is still the most recent thing that happened, and a
    /// strip that empties itself every time the app is opened says nothing at all for
    /// the first hour of every session. Five lines of text; nothing here is worth a
    /// table in the database.
    /// </summary>
    private void KeepFeed() {
        // A demonstration is a picture of a delivery that already happened, put on the
        // live page so that page can be looked at with no game running. What it says
        // in the strip belongs to that picture and not to the driver's own evening,
        // which is why it is shown and not written down.
        if (DemoDelivery is not null) return;
        try {
            var kept = _feedLines.Select(l => new FeedLineOnDisk {
                At = l.At, Kind = l.Kind.ToString(), Text = l.Text, Detail = l.Detail,
            }).ToList();
            File.WriteAllText(FeedPath, Newtonsoft.Json.JsonConvert.SerializeObject(kept, Newtonsoft.Json.Formatting.Indented));
        } catch {
            // A strip that failed to save is not worth interrupting a drive for.
        }
    }

    private void ReadFeed() {
        try {
            if (!File.Exists(FeedPath)) return;
            var kept = Newtonsoft.Json.JsonConvert.DeserializeObject<List<FeedLineOnDisk>>(File.ReadAllText(FeedPath));
            if (kept is null) return;
            _feedLines.Clear();
            foreach (var line in kept.TakeLast(FeedLines)) {
                _feedLines.Add((line.At,
                    Enum.TryParse<Noticed>(line.Kind, out var kind) ? kind : Noticed.Started,
                    line.Text, line.Detail));
            }
        } catch {
            // Unreadable means an empty strip, which is where it started anyway.
        }
    }

    private sealed class FeedLineOnDisk {
        public DateTime At { get; set; }
        public string Kind { get; set; } = "";
        public string Text { get; set; } = "";
        public string Detail { get; set; } = "";
    }

    private void DrawFeed() => Quiet(_feed, () => {
        _feed.Controls.Clear();

        // Newest at the top, so a new line always arrives in the same place and the
        // older ones move down and off rather than the whole strip shifting.
        foreach (var line in _feedLines) _feed.Controls.Add(FeedLine(line));

        _feed.Height = _feedLines.Count == 0 ? 0 : _feedLines.Count * FeedLineHeight + 14;
    });

    private Control FeedLine((DateTime At, Noticed Kind, string Text, string Detail) line) {
        var row = new Panel {
            Dock = DockStyle.Top, Height = FeedLineHeight, BackColor = Surface,
            Padding = new Padding(0, 2, 12, 2),
        };

        var said = new Label {
            Dock = DockStyle.Top, Height = 15, Text = line.Text, ForeColor = Ink, AutoEllipsis = true,
            Font = new Font("Segoe UI", 8.5F), TextAlign = ContentAlignment.MiddleLeft,
        };
        // The date as well when it was not today, or a line kept from last night
        // reads as something that happened an hour ago.
        var clock = line.At.Date == DateTime.Today ? line.At.ToString("HH:mm") : line.At.ToString("dd.MM HH:mm");
        var when = line.Detail.Length > 0 ? $"{clock}  ·  {line.Detail}" : clock;
        var under = new Label {
            Dock = DockStyle.Top, Height = 13, Text = when, ForeColor = Muted, AutoEllipsis = true,
            Font = new Font("Segoe UI", 7.5F), TextAlign = ContentAlignment.MiddleLeft,
        };

        // The words get cut in a column this narrow, so the whole of it is a breath
        // away under the pointer.
        var full = line.Detail.Length > 0 ? $"{line.Text}  ·  {line.Detail}" : line.Text;
        _tips.SetToolTip(said, full);
        _tips.SetToolTip(under, full);

        row.Controls.Add(under);
        row.Controls.Add(said);
        row.Controls.Add(FeedMark(line.Kind));
        return row;
    }

    /// <summary>
    /// The mark down the left of a line: what kind of thing it was.
    ///
    /// A hollow ring for a load taken on, a filled one for a load handed over, and a
    /// star for an award. Shape rather than colour alone, so the three are told apart
    /// by somebody who does not see the colours.
    /// </summary>
    private Control FeedMark(Noticed kind) {
        var mark = new Panel { Dock = DockStyle.Left, Width = 22, BackColor = Surface };
        mark.Paint += (_, e) => {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var middle = new PointF(11, mark.Height / 2f);
            switch (kind) {
                case Noticed.Started: {
                    using var edge = new Pen(Accent, 1.6f);
                    e.Graphics.DrawEllipse(edge, middle.X - 4, middle.Y - 4, 8, 8);
                    break;
                }
                case Noticed.Delivered: {
                    using var full = new SolidBrush(Accent);
                    e.Graphics.FillEllipse(full, middle.X - 4, middle.Y - 4, 8, 8);
                    break;
                }
                default: {
                    using var full = new SolidBrush(Accent);
                    e.Graphics.FillPolygon(full, Star(middle, 6, 2.4f));
                    break;
                }
            }
        };
        return mark;
    }

    /// <summary>A four pointed star, which reads as an award at eight pixels where a
    /// five pointed one turns to mush.</summary>
    private static PointF[] Star(PointF middle, float far, float near) {
        var points = new PointF[8];
        for (var i = 0; i < 8; i++) {
            var reach = i % 2 == 0 ? far : near;
            var turn = Math.PI / 4 * i - Math.PI / 2;
            points[i] = new PointF(
                middle.X + (float)(Math.Cos(turn) * reach),
                middle.Y + (float)(Math.Sin(turn) * reach));
        }
        return points;
    }

    // ---------- what is worth having done ----------

    private readonly Panel _awardsPage = new();
    private readonly Panel _awardsList = new();
    private readonly Panel _awardsHead = new();
    private List<Tracking.AwardStanding> _awards = new();
    private Tracking.AwardProfile _profile = new();

    /// <summary>
    /// The awards, earned and not, under a line saying where the driver stands.
    ///
    /// A list rather than a grid: what matters about an award is its name, the one line
    /// saying what it takes, and how far off it is, and none of those is a column of
    /// figures. The ones still to come carry a bar, because "34 of 50" says more than
    /// either number on its own.
    /// </summary>
    private Panel BuildAwardsPage() {
        _awardsPage.Dock = DockStyle.Fill;
        _awardsPage.BackColor = Canvas;
        _awardsPage.Padding = new Padding(16, 12, 16, 16);

        _awardsList.Dock = DockStyle.Fill;
        _awardsList.BackColor = Canvas;
        _awardsList.AutoScroll = true;

        _awardsHead.Dock = DockStyle.Top;
        _awardsHead.Height = 92;
        _awardsHead.BackColor = Surface;
        _awardsHead.Padding = new Padding(18, 14, 18, 14);

        _awardsPage.Controls.Add(_awardsList);
        _awardsPage.Controls.Add(new Panel { Dock = DockStyle.Top, Height = 12, BackColor = Canvas });
        _awardsPage.Controls.Add(_awardsHead);
        return _awardsPage;
    }

    /// <summary>
    /// Works out where every award stands and writes down what has been earned.
    ///
    /// Run when a delivery has just finished, anything newly earned is said in the strip
    /// along the foot: it can only have been that drive that earned it. Run for any
    /// other reason it is a catching-up pass over the whole history and stays quiet,
    /// since a first run would otherwise announce forty awards at once for driving that
    /// happened weeks ago.
    /// </summary>
    private void ReloadAwards(bool announce = false) {
        var (standings, profile) = Tracking.Awards.Measure(_store.AwardDeliveries());
        var stored = _store.EarnedAwards();

        foreach (var s in standings) {
            stored.TryGetValue(s.Award.Id, out var was);
            var newly = s.TimesEarned - was.Times;

            // Never fewer than what was written down. If a rule is rewritten later and
            // measures less, the record stands: this project does not take back what a
            // driver has already done.
            if (was.Times > s.TimesEarned) {
                s.TimesEarned = was.Times;
                s.FirstAt ??= was.First;
                s.LastAt ??= was.Last;
                s.DeliveryId ??= was.DeliveryId;
            }
            if (s.TimesEarned == 0) continue;

            if (was.Times > 0 && was.First < s.FirstAt) s.FirstAt = was.First;
            _store.EarnAward(s.Award.Id, s.TimesEarned, s.FirstAt ?? DateTime.Now,
                             s.LastAt ?? DateTime.Now, s.DeliveryId);
            if (announce && newly > 0) {
                var times = s.TimesEarned > 1 ? $"  {s.TimesEarned}×" : "";
                Happened(Noticed.Award, $"{s.Award.Name}{times}", $"+{s.Award.Xp} XP");
            }
        }

        _awards = standings;
        _profile = profile;
        DrawAwards();
    }

    /// <summary>Where the driver stands: the level, what it took to get there, and how
    /// much of the set has been found.</summary>
    private void DrawAwards() => Quiet(_awardsPage, () => {
        _awardsHead.Controls.Clear();
        _awardsHead.BackColor = Look.Chrome;
        _awardsHead.Paint -= PaintAwardsHead;
        _awardsHead.Paint += PaintAwardsHead;
        DrawAwardList();
        return;

    });

    private void DrawAwardList() {
        _awardsList.Controls.Clear();

        // Two sets rather than four shelves: what has been found, and what is still to
        // come. A driver opening this page is asking one of those two questions, and
        // splitting the same seventy two by game answered neither of them first.
        var found = _awards.Where(a => a.Earned)
                           .OrderByDescending(a => a.LastAt ?? DateTime.MinValue).ToList();
        var waiting = _awards.Where(a => !a.Earned)
                             .OrderByDescending(a => a.Award.Threshold > 1 ? a.Progress / a.Award.Threshold : 0)
                             .ToList();

        var rows = new List<Control>();
        if (found.Count > 0) {
            rows.Add(AwardShelf(Strings.T("award.found"), $"{found.Count} {Strings.T("award.ofSome")} {_awards.Count}"));
            rows.AddRange(found.Select(AwardRow));
        }
        if (waiting.Count > 0) {
            rows.Add(AwardShelf(Strings.T("award.stillToCome"), $"{waiting.Count} {Strings.T("award.left")}"));
            rows.AddRange(waiting.Select(AwardRow));
        }

        // Two columns of panels under each shelf heading, which is what the awards are
        // shaped like: a name, a sentence, a figure. One column down a wide window left
        // two thirds of it empty and made a set of seventy two look like a list of
        // seventy two chores.
        var stacked = new List<Control>();
        for (var i = 0; i < rows.Count; i++) {
            if (rows[i].Tag as string == "shelf") {
                stacked.Add(rows[i]);
                continue;
            }
            // A panel carrying a track under its sentence needs the room for it; one
            // that is already found does not, and found awards spaced for a track that
            // is not there read as a list with holes in it.
            var second = i + 1 < rows.Count && rows[i + 1].Tag as string != "shelf" ? rows[i + 1] : null;
            var tall = rows[i].Tag as string == "track" || second?.Tag as string == "track" ? 80 : 62;
            var pair = new TableLayoutPanel {
                Dock = DockStyle.Top, Height = tall, BackColor = Look.Window, ColumnCount = 2, RowCount = 1,
                Margin = new Padding(0), Padding = new Padding(0),
            };
            pair.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            pair.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            // Without a row style the row sizes itself to the panel rather than the
            // panel to the row, and a panel that thinks it is a hundred tall inside a
            // cell of eighty draws its own foot and its track below the cut.
            pair.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            pair.Controls.Add(rows[i], 0, 0);
            if (second is not null) {
                pair.Controls.Add(second, 1, 0);
                i++;
            }
            stacked.Add(pair);
        }

        // Docked children stack in reverse, so the list goes in backwards.
        for (var i = stacked.Count - 1; i >= 0; i--) _awardsList.Controls.Add(stacked[i]);
        UseDarkScrollbars(_awardsList);
        UseDarkScrollbars(_awardsPage);
        SmoothPainting(_awardsList);
        Retype(_awardsList);
    }

    /// <summary>
    /// The head of the awards page: the level as a tile, what has been earned, the
    /// track to the next one, and how much of the set has been found.
    /// </summary>
    private void PaintAwardsHead(object? sender, PaintEventArgs e) {
        if (sender is not Control head) return;
        var g = e.Graphics;
        g.Clear(Look.Chrome);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        var needs = Math.Max(1, _profile.LevelTo - _profile.LevelFrom);
        var into = Math.Clamp(_profile.Xp - _profile.LevelFrom, 0, needs);
        var middle = head.Height / 2f;

        // The level, as a tile rather than a word: it is the one figure on this page
        // that is not measured in anything.
        var tile = new RectangleF(20, middle - 23, 46, 46);
        Look.FillRounded(g, tile, Look.RadiusPanel, Look.Accent);
        var number = _profile.Level.ToString();
        var wide = Look.Measure(g, number, Look.Semi(20)).Width;
        Look.Text(g, number, Look.Semi(20), Look.Window, tile.X + (tile.Width - wide) / 2, tile.Y + 11);

        Look.Text(g, $"{Strings.T("award.level")} {_profile.Level}", Look.Semi(17), Look.Ink, tile.Right + 16, middle - 20);
        Look.Text(g, $"{_profile.Xp:N0} XP {Strings.T("award.earnedLower")} · {_profile.Earned} {Strings.T("award.inTotalLower")}",
                  Look.Caption, Look.Dim, tile.Right + 16, middle + 4);

        // The found count at the right end, under its own label.
        var found = $"{_profile.Unique} / {_profile.Possible}";
        Look.Tracked(g, Strings.T("award.unique").ToUpperInvariant(), Look.Label, Look.Dim,
                     head.Width - 20 - Look.TrackedWidth(g, Strings.T("award.unique").ToUpperInvariant(), Look.Label), middle - 22);
        Look.TextRight(g, found, Look.Semi(20), Look.Ink, head.Width - 20, middle - 6);

        // And the track between the two, with a figure under each of its ends.
        var left = tile.Right + 220;
        var right = head.Width - 150;
        if (right - left < 120) return;
        Look.Track(g, new RectangleF(left, middle - 9, right - left, 7), into / (float)needs);
        Look.Text(g, $"{into:N0} / {needs:N0} XP", Look.Caption, Look.Dim, left, middle + 6);
        Look.TextRight(g, $"{needs - into:N0} XP {Strings.T("award.toLevel")} {_profile.Level + 1}",
                       Look.Caption, Look.Dim, right, middle + 6);
    }

    /// <summary>A set opens with its name and how many are in it, and nothing else:
    /// the panels under it say the rest.</summary>
    private Control AwardShelf(string title, string count) {
        var head = new Panel { Dock = DockStyle.Top, Height = 38, BackColor = Look.Window, Tag = "shelf" };
        head.Paint += (_, e) => {
            var g = e.Graphics;
            g.Clear(Look.Window);
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            var y = head.Height - 22;
            Look.Text(g, title, Look.Strong, Look.Ink, 0, y);
            Look.Text(g, count, Look.Caption, Look.Dim, Look.Measure(g, title, Look.Strong).Width + 12, y + 2);
        };
        return head;
    }

    /// <summary>
    /// One award, drawn as a panel of its own.
    ///
    /// Found ones sit on the panel tone with an amber tick, their name in capitals and
    /// the day they were found under it. The ones still to come sit a step darker with
    /// a closed mark, dim ink and a grey track saying how far off they are. Painted
    /// rather than stacked out of five labels, since a name in capitals with tracking,
    /// a figure at the right and a track under both is one drawing, not five controls.
    /// </summary>
    private Control AwardRow(Tracking.AwardStanding s) {
        // A secret is not named until it is found, or there would be nothing to find.
        var hidden = s.Award.Secret && !s.Earned;
        var ground = s.Earned ? Look.Panel : Look.Well;

        var name = hidden ? "? ? ?" : s.Award.Name;
        var says = hidden ? Strings.T("award.hidden") : Strings.T("award." + s.Award.Id);
        var worth = $"{(s.TimesEarned > 1 ? s.TotalXp : s.Award.Xp):N0} XP";
        var toGo = !s.Earned && !hidden && s.Award.Threshold > 1;
        var share = toGo ? Math.Min(1, s.Progress / s.Award.Threshold) : 0;
        var figures = toGo ? $"{AwardFigure(s.Award, s.Progress)} / {AwardFigure(s.Award, s.Award.Threshold)}" : "";

        var row = new Panel {
            Dock = DockStyle.Fill, BackColor = Look.Window, Margin = new Padding(0, 0, 12, 10),
            Tag = toGo ? "track" : "plain",
        };
        row.Paint += (_, e) => {
            var g = e.Graphics;
            g.Clear(Look.Window);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            var box = new RectangleF(0, 0, row.Width, row.Height);
            Look.Surface(g, box, ground, s.Earned ? Look.Hairline : Look.Tint(Look.Border, 70));

            // The mark: a ticked box for what has been done, a closed one for what has
            // not, so the two sets read apart before a word of them is read.
            // Level with the name rather than with the middle of the panel: the mark and
            // the word it marks are one line, and a panel that carries a track underneath
            // is taller at the bottom, not at the top.
            var mark = new RectangleF(14, 11, 18, 18);
            if (s.Earned) {
                Look.FillRounded(g, mark, 5, Look.Tint(Look.Accent, 18));
                Look.DrawRounded(g, mark, 5, Look.TintEdge(Look.Accent, 45));
                using var tick = new Pen(Look.Accent, 1.8f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
                g.DrawLines(tick, new[] {
                    new PointF(mark.X + 4.5f, mark.Y + 9),
                    new PointF(mark.X + 7.5f, mark.Y + 12.5f),
                    new PointF(mark.X + 13.5f, mark.Y + 5.5f),
                });
            } else {
                Look.DrawRounded(g, mark, 5, Look.Border);
                using var shackle = new Pen(Look.Dim, 1.5f);
                g.DrawArc(shackle, mark.X + 5.5f, mark.Y + 3.5f, 7, 7, 180, 180);
                using var body = new SolidBrush(Look.Dim);
                g.FillRectangle(body, mark.X + 4.5f, mark.Y + 8.5f, 9, 6.5f);
            }

            var left = mark.Right + 12;
            var wideWorth = Look.Measure(g, worth, Look.CaptionSemi).Width;
            var room = row.Width - left - wideWorth - 26;

            // The name in the same small capitals every label in this window wears,
            // one step larger because it is the name of the thing rather than of a
            // figure beside it.
            var wideName = Look.Tracked(g, name.ToUpperInvariant(), Look.Semi(12.5f),
                                        s.Earned ? Look.Ink : Look.Muted, left, 12, 0.9f);
            if (s.TimesEarned > 1) {
                Look.Text(g, $"{s.TimesEarned}×", Look.CaptionSemi, Look.Accent, left + wideName + 8, 13);
            }
            Look.TextRight(g, worth, Look.CaptionSemi, s.Earned ? Look.Accent : Look.Dim, row.Width - 16, 12);

            var under = toGo ? says : s.Earned ? $"{says}   ·   {s.LastAt:dd.MM.yyyy}" : says;
            Look.Text(g, Look.Clip(g, under, Look.Caption, room), Look.Caption,
                      s.Earned ? Look.Muted : Look.Dim, left, 33);

            if (!toGo) return;

            // How far off it is, in grey: amber in this window means something has been
            // earned, and this one has not been.
            var wideFigures = Look.Measure(g, figures, Look.Caption).Width;
            var track = new RectangleF(left, row.Height - 16, Math.Max(60, room - wideFigures - 12), 3);
            Look.FillRounded(g, track, 1.5f, Look.Hairline);
            if (share > 0) Look.FillRounded(g, new RectangleF(track.X, track.Y, (float)(track.Width * share), 3), 1.5f, Look.Dim);
            Look.Text(g, figures, Look.Caption, Look.Dim, track.Right + 12, row.Height - 23);
        };
        return row;
    }
    /// <summary>A threshold written the way the driver reads it. Distance is the one
    /// that matters: Europe counts in kilometres and America in miles, and neither is
    /// ever turned into the other.</summary>
    private string AwardFigure(Tracking.Award a, double value) => a.Unit switch {
        "km" => $"{value:N0} km",
        "miles" => $"{value:N0} mi",
        "money" => Units.For(_settings.Units, a.Game.Length > 0 ? a.Game : null).FormatMoney(value),
        _ => $"{value:N0}",
    };

    // ---------- one truck against another ----------

    private readonly DataGridView _truckGrid = new();

    /// <summary>
    /// One panel per truck, in the order they have been used.
    ///
    /// A grid of fourteen columns answered every question about a truck equally, which
    /// is another way of saying it answered none of them first. What a driver asks is
    /// which truck they use, how far it has gone and what it has cost; the rest is a
    /// figure at the right end of its own card.
    /// </summary>
    private Panel BuildTrucksPage() {
        var page = new Panel { Dock = DockStyle.Fill, BackColor = Look.Window, Padding = new Padding(Look.PagePad, 10, Look.PagePad, Look.PagePad) };

        var head = new Panel { Dock = DockStyle.Top, Height = 34, BackColor = Look.Window };
        head.Paint += (_, e) => {
            var g = e.Graphics;
            g.Clear(Look.Window);
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            Look.Text(g, Strings.T("tab.trucks"), Look.PageHeading, Look.Ink, 0, 6);
            Look.TextRight(g, _truckTotals, Look.Small, Look.Dim, head.Width, 10);
        };
        _truckHead = head;

        _truckCards.Dock = DockStyle.Fill;
        _truckCards.CardHeight = 76;
        _truckCards.EmptyText = Strings.T("trucks.empty");

        page.Controls.Add(_truckCards);
        page.Controls.Add(head);
        return page;
    }

    private void ReloadTrucks() {
        var trucks = _store.TruckTotals();
        // The column down the left carries the count, and this is the one place that
        // knows it without asking the database a second time.
        _navTrucks = trucks.Count;
        foreach (var t in trucks) {
            var u = Units.For(_settings.Units, t.Hra);
            t.Vzdialenost = u.FormatDistance(t.DistanceKm);
            t.Odmena = u.FormatMoney(t.Zarobok);
            t.Zarobok = u.Money(t.Zarobok);
            // Each row in the unit its own truck drinks: a battery is filled with
            // kilowatt hours and a tank with litres, and a column that converted one
            // into the other would be inventing a fuel neither of them uses.
            t.Palivo = t.Elektricky ? Units.FormatEnergy(t.PalivoRaw) : u.FormatVolume(t.PalivoRaw);
            t.Priemer = t.SpeedKmh > 0 ? u.FormatSpeed(t.SpeedKmh) : Nothing;
            t.Pokuty = u.FormatMoney(t.PokutyRaw);
            t.PokutyRaw = u.Money(t.PokutyRaw);
            // Per delivery, not in total: what the truck takes on a run, rather than
            // what has happened to it since it was bought.
            t.Poskodenie = $"{t.DamagePerJob * 100:0.00} %";
            t.Styl = $"{t.Zasielky - t.Ostro} / {t.Ostro}";
        }

        // One card each, most used first, with the share of all deliveries this truck
        // has pulled drawn across the middle. Damage is tinted by what it means rather
        // than by how large it is: under a percent a run is a truck coming home whole.
        var most = Math.Max(1, trucks.Sum(t => t.Zasielky));
        _truckTotals = $"{trucks.Count} {Strings.T("trucks.haveWorked")}";
        _truckHead?.Invalidate();

        _truckCards.Show(trucks.Select((t, place) => new CardStack.Card {
            Title = t.Kamion,
            // The one it is worth saying something about is the one it is driven in.
            // Electric is the other thing a truck can be that changes what its figures
            // even mean, since a battery is filled with kilowatt hours.
            Tag = place == 0 ? Strings.T("trucks.mostUsed") : t.Elektricky ? Strings.T("trucks.electric") : "",
            Under = $"{GameName(t.Hra)} · {t.Kolizie}× {Strings.T("live.collisions")} · {t.Zasielky - t.Ostro} {Strings.T("trucks.cleanRuns")}",
            Share = (float)t.Zasielky / most,
            ShareLabel = $"{t.Zasielky} {Strings.T("list.deliveries")}",
            SharePercent = $"{t.Zasielky * 100 / most} %",
            Figures = new List<CardStack.Figure> {
                new() { Label = Strings.T("col.distance"), Value = t.Vzdialenost },
                new() { Label = Strings.T("trucks.earned"), Value = t.Odmena,
                        Ink = t.Zarobok < 0 ? Look.Lost : Look.Ink },
                new() { Label = Strings.T("live.fines"), Value = t.Pokuty,
                        Ink = t.PokutyRaw > 0 ? Look.Lost : Look.Ink },
                new() { Label = Strings.T("trucks.damage"), Value = t.Poskodenie,
                        Ink = t.DamagePerJob >= 0.02 ? Look.Lost : t.DamagePerJob >= 0.01 ? Look.Accent : Look.Whole },
            },
        }));
    }

    /// <summary>
    /// Says on hover what a column heading has no room to say.
    ///
    /// A heading is one or two words or it wraps, and one or two words cannot carry
    /// "the damage this one takes on an average delivery, rather than in total".
    /// So the rule for the whole window is the short word in the heading and the
    /// sentence under the pointer, on the cells as well as on the heading, since
    /// somebody who wonders about a figure points at the figure.
    /// </summary>
    /// <summary>
    /// Hands one column whatever width is left over.
    ///
    /// Every column keeps the width it was given, and none of them redistributes when
    /// the window is resized, which is the whole point of setting them by hand. What
    /// was left over was a band of empty panel down the right of every table, which
    /// read as a table that had failed to finish.
    ///
    /// The one named here takes it. Always a column of words rather than the last one
    /// along, since the slack is only worth having where something is being cut short:
    /// three hundred pixels handed to "Delivered" is the same empty band moved inside
    /// the table. It never shrinks below the width it was given, so a narrow window
    /// still scrolls sideways rather than squeezing.
    /// </summary>
    private static void FillToEdge(DataGridView grid, string column) {
        if (grid.Columns[column] is not { Visible: true } wide) return;
        wide.MinimumWidth = wide.Width;
        wide.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
    }

    private static void Explain(DataGridView grid, string column, string key) {
        if (grid.Columns[column] is not { } col) return;
        col.ToolTipText = Strings.T(key);
        col.HeaderCell.ToolTipText = Strings.T(key);
    }

    private void OnTrucksBound(object? sender, DataGridViewBindingCompleteEventArgs e) {
        var captions = new Dictionary<string, string> {
            [nameof(TruckRow.Kamion)] = Strings.T("col.truck"),
            [nameof(TruckRow.Zasielky)] = Strings.T("sess.deliveries"),
            [nameof(TruckRow.Vzdialenost)] = Strings.T("col.distance"),
            [nameof(TruckRow.Odmena)] = Strings.T("sess.earned"),
            [nameof(TruckRow.Palivo)] = Strings.T("truck.fuel"),
            [nameof(TruckRow.Priemer)] = Strings.T("sess.speed"),
            [nameof(TruckRow.Pokuty)] = Strings.T("col.fines"),
            [nameof(TruckRow.Kolizie)] = Strings.T("col.collisions"),
            [nameof(TruckRow.Poskodenie)] = Strings.T("detail.damage"),
            [nameof(TruckRow.Styl)] = Strings.T("col.style"),
        };
        foreach (DataGridViewColumn col in _truckGrid.Columns) {
            if (captions.TryGetValue(col.DataPropertyName, out var caption)) col.HeaderText = caption;
        }

        var order = new[] {
            nameof(TruckRow.Kamion), nameof(TruckRow.Zasielky), nameof(TruckRow.Vzdialenost),
            nameof(TruckRow.Odmena), nameof(TruckRow.Palivo), nameof(TruckRow.Priemer),
            nameof(TruckRow.Pokuty), nameof(TruckRow.Kolizie), nameof(TruckRow.Poskodenie),
            nameof(TruckRow.Styl),
        };
        // The damage column is wide enough for its own heading in every language it
        // has one in: "Damage / job" and "Poškodenie / zák." both wrapped onto a
        // second line at the width the figure alone would have needed.
        var widths = new[] { 172, 84, 96, 96, 104, 76, 88, 84, 96, 78 };
        for (var i = 0; i < order.Length; i++) {
            if (_truckGrid.Columns[order[i]] is not { } col) continue;
            col.DisplayIndex = i;
            col.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
            col.Width = widths[i];
            if (i > 0) col.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
        }
        FillToEdge(_truckGrid, nameof(TruckRow.Kamion));
        Explain(_truckGrid, nameof(TruckRow.Poskodenie), "why.truckDamage");
        Explain(_truckGrid, nameof(TruckRow.Palivo), "why.truckFuel");
        Explain(_truckGrid, nameof(TruckRow.Priemer), "why.speed");
        Explain(_truckGrid, nameof(TruckRow.Styl), "why.style");
        Explain(_truckGrid, nameof(TruckRow.Pokuty), "why.fines");

        foreach (var hidden in new[] {
            nameof(TruckRow.DistanceKm), nameof(TruckRow.Zarobok), nameof(TruckRow.PalivoRaw),
            nameof(TruckRow.SpeedKmh), nameof(TruckRow.PokutyRaw), nameof(TruckRow.DamagePerJob),
            nameof(TruckRow.Elektricky), nameof(TruckRow.Hra), nameof(TruckRow.Ostro),
        }) {
            if (_truckGrid.Columns[hidden] is { } col) col.Visible = false;
        }
    }

    // ---------- sittings at the wheel ----------

    private readonly DataGridView _sessionGrid = new();
    private readonly Panel _sessionSide = new();
    private List<SessionRow> _sessions = new();

    /// <summary>
    /// One panel per sitting at the wheel, newest first.
    ///
    /// A sitting is a shape rather than a row of figures: when it ran, where it went,
    /// and what the hours in it went on. The bar across the middle says the last of
    /// those without a single number: driving, resting, and everything else.
    /// </summary>
    private Panel BuildSessionsPage() {
        var page = new Panel { Dock = DockStyle.Fill, BackColor = Look.Window, Padding = new Padding(Look.PagePad, 10, Look.PagePad, Look.PagePad) };

        var head = new Panel { Dock = DockStyle.Top, Height = 34, BackColor = Look.Window };
        head.Paint += (_, e) => {
            var g = e.Graphics;
            g.Clear(Look.Window);
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            Look.Text(g, Strings.T("tab.sessions"), Look.PageHeading, Look.Ink, 0, 6);
            Look.TextRight(g, _sessionTotals, Look.Small, Look.Dim, head.Width, 10);
        };
        _sessionHead = head;

        _sessionCards.Dock = DockStyle.Fill;
        _sessionCards.CardHeight = 76;
        _sessionCards.EmptyText = Strings.T("sessions.empty");

        page.Controls.Add(_sessionCards);
        page.Controls.Add(head);
        return page;
    }

    private void ReloadSessions() {
        // The recording being written right now is measured again every time, since
        // it is the one that grows. The rest are read once and remembered.
        try {
            Tracking.Sessions.Scan(_store, Path.Combine(DeliveryStore.DefaultDir(), "sessions"));
        } catch {
            // A folder that cannot be read means no new sittings, not a broken page.
        }

        _sessions = Tracking.Sessions.List(_store, _settings.SessionGapMinutes);
        _navSessions = _sessions.Count;
        foreach (var s in _sessions) {
            var u = Units.For(_settings.Units, s.Hra);
            s.DurationMs = s.ToMs - s.FromMs;
            s.Trvanie = Units.Duration(s.DurationMs / 60000.0);
            // A sitting with nothing delivered in it has no distance and no earnings,
            // and no game to read them in either: "0 km" there is a figure about a
            // game that was never started, and a currency picked out of the air.
            var drove = s.DistanceKm > 0.05 || s.FreeroamKm > 0.05;
            s.Vzdialenost = drove ? u.FormatDistance(s.DistanceKm) : Nothing;
            s.Odmena = drove ? u.FormatMoney(s.Zarobok) : Nothing;
            s.Zarobok = u.Money(s.Zarobok);
            // Against the hours the game counted, not the hours the chair was warm:
            // dividing by real time reports the time compression as speed.
            var hours = s.GameMinutes / 60.0;
            s.SpeedKmh = hours > 0.01 ? s.DistanceKm / hours : 0;
            s.Priemer = s.SpeedKmh > 0 ? u.FormatSpeed(s.SpeedKmh) : Nothing;
            s.Oddych = s.RestMinutes > 0.5 ? Units.Duration(s.RestMinutes) : Nothing;
        }

        // Every column written as words sorts on the figure behind it. Clicking
        // "lasted" has to put an hour and a half above fifty minutes, and it cannot do
        // that by comparing the words they are written as.
        // One card per sitting, newest first: when it was and how long it ran on the
        // left, the cities it went through in the middle over a bar of what the hours
        // went on, and three figures at the right.
        _sessionTotals = $"{_sessions.Count} {Strings.T("sessions.sittings")}";
        _sessionHead?.Invalidate();

        _sessionCards.Show(_sessions.Select(one => {
            var u = Units.For(_settings.Units, one.Hra);
            var drove = _rows.Where(r => r.Datum <= one.Do && r.Dokoncene >= one.Od)
                             .OrderBy(r => r.Datum).ToList();
            var chain = drove.Select(r => r.Odkial).Concat(drove.Count > 0 ? new[] { drove[^1].Kam } : Array.Empty<string>())
                             .Distinct().ToList();

            // What the hours went on, as three blocks: driving, resting, and the rest
            // of the sitting, which is menus, ferries and standing about.
            // Measured in the game's own minutes, which is what a sitting is counted in.
            // The wheel is split between the deliveries and the driving off them, the
            // bunk is its own block, and whatever is left is left empty rather than
            // filled with a colour that would have to mean "the rest".
            var minutes = Math.Max(1, one.GameMinutes);
            var wheel = one.DistanceKm > 0 && one.SpeedKmh > 0
                ? (one.DistanceKm + one.FreeroamKm) / one.SpeedKmh * 60 : 0;
            var driving = Math.Clamp(wheel / minutes, 0, 1);
            var offTheJob = one.DistanceKm + one.FreeroamKm > 0
                ? one.FreeroamKm / (one.DistanceKm + one.FreeroamKm) : 0;
            var resting = Math.Clamp(one.RestMinutes / minutes, 0, 1 - driving);

            return new CardStack.Card {
                Title = one.Od.ToString("dd.MM.yyyy"),
                Under = $"{one.Od:HH:mm} → {one.Do:HH:mm}",
                Middle = chain.Count > 0 ? string.Join("  →  ", chain) : Strings.T("sessions.nothingDriven"),
                Bar = new List<CardStack.Block> {
                    new() { Part = (float)(driving * (1 - offTheJob)), Hue = Look.Accent },
                    new() { Part = (float)(driving * offTheJob), Hue = Look.Route },
                    new() { Part = (float)resting, Hue = Look.Slate },
                },
                Figures = new List<CardStack.Figure> {
                    new() { Label = Strings.T("sessions.atTheWheel"), Value = one.Trvanie },
                    new() { Label = Strings.T("list.deliveries"), Value = one.Zasielky.ToString() },
                    new() { Label = Strings.T("col.distance"), Value = one.Vzdialenost },
                },
            };
        }));
    }

    private void OnSessionsBound(object? sender, DataGridViewBindingCompleteEventArgs e) {
        // A column heading of this grid, not a caption borrowed from somewhere else.
        // The statistics tiles are drawn in capitals, so their words are stored in
        // lower case, and read straight into a grid they came out as "deliveries"
        // beside "Lasted". One word each where a word will do, so nothing wraps onto
        // a second line and no heading is wider than its column.
        var captions = new Dictionary<string, string> {
            [nameof(SessionRow.Od)] = Strings.T("sess.began"),
            [nameof(SessionRow.Trvanie)] = Strings.T("sess.lasted"),
            [nameof(SessionRow.Zasielky)] = Strings.T("sess.deliveries"),
            [nameof(SessionRow.Vzdialenost)] = Strings.T("col.distance"),
            [nameof(SessionRow.Odmena)] = Strings.T("sess.earned"),
            [nameof(SessionRow.Priemer)] = Strings.T("sess.speed"),
            [nameof(SessionRow.Oddych)] = Strings.T("sess.rest"),
        };
        foreach (DataGridViewColumn col in _sessionGrid.Columns) {
            if (captions.TryGetValue(col.DataPropertyName, out var caption)) col.HeaderText = caption;
        }
        if (_sessionGrid.Columns[nameof(SessionRow.Od)] is { } began) {
            began.DefaultCellStyle.Format = "dd.MM.yy HH:mm";
        }
        // Read left to right the way the sitting is asked about: when, how long, and
        // then what came of it. Bound properties come out in the order they are
        // declared, which put the number of restarts second.
        var order = new[] {
            nameof(SessionRow.Od), nameof(SessionRow.Trvanie), nameof(SessionRow.Zasielky),
            nameof(SessionRow.Vzdialenost), nameof(SessionRow.Odmena), nameof(SessionRow.Priemer),
            nameof(SessionRow.Oddych),
        };
        // Wide enough for the longest thing each holds, not for its heading: rest
        // reads "10 h 05 min" on a sitting with two sleeps in it, and cut to "10 h 0"
        // it says nothing at all.
        var widths = new[] { 124, 92, 92, 104, 104, 84, 92 };
        for (var i = 0; i < order.Length; i++) {
            if (_sessionGrid.Columns[order[i]] is not { } col) continue;
            col.DisplayIndex = i;
            col.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
            col.Width = widths[i];
        }
        foreach (var numeric in new[] {
            nameof(SessionRow.Zasielky), nameof(SessionRow.Vzdialenost),
            nameof(SessionRow.Odmena), nameof(SessionRow.Priemer),
        }) {
            if (_sessionGrid.Columns[numeric] is { } c) c.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
        }
        FillToEdge(_sessionGrid, nameof(SessionRow.Od));

        // The two that mean something particular on this page, and the three that
        // mean what they do everywhere else.
        Explain(_sessionGrid, nameof(SessionRow.Zasielky), "why.sessDeliveries");
        Explain(_sessionGrid, nameof(SessionRow.Vzdialenost), "why.sessDistance");
        Explain(_sessionGrid, nameof(SessionRow.Trvanie), "why.sessLasted");
        Explain(_sessionGrid, nameof(SessionRow.Priemer), "why.speed");
        Explain(_sessionGrid, nameof(SessionRow.Oddych), "why.sessRest");

        foreach (var hidden in new[] {
            nameof(SessionRow.Do), nameof(SessionRow.FromMs), nameof(SessionRow.ToMs),
            nameof(SessionRow.DistanceKm), nameof(SessionRow.Zarobok), nameof(SessionRow.Hra),
            nameof(SessionRow.GameMinutes), nameof(SessionRow.RestMinutes), nameof(SessionRow.FreeroamKm),
            nameof(SessionRow.DurationMs), nameof(SessionRow.SpeedKmh),
        }) {
            if (_sessionGrid.Columns[hidden] is { } col) col.Visible = false;
        }
        if (_sessionGrid.Rows.Count > 0 && _sessionGrid.SelectedRows.Count == 0) {
            _sessionGrid.Rows[0].Selected = true;
        }
    }

    /// <summary>What was driven in the sitting that is selected, beside the list of
    /// them. The deliveries are already loaded for the history page, so this is the
    /// same rows read a second way rather than another trip to the database.</summary>
    private void OnSessionPicked(object? sender, EventArgs e) =>
        Quiet(_sessionSide, FillSessionSide);

    private void FillSessionSide() {
        _sessionSide.Controls.Clear();
        if (_sessionGrid.CurrentRow?.DataBoundItem is not SessionRow picked) return;

        var from = DateTimeOffset.FromUnixTimeMilliseconds(picked.FromMs).LocalDateTime;
        var to = DateTimeOffset.FromUnixTimeMilliseconds(picked.ToMs).LocalDateTime;
        // Overlapping, not beginning inside. An evening spent halfway through a haul
        // that started yesterday is an evening of driving that delivery, and a panel
        // headed "driven in this one" that leaves it out is answering another
        // question entirely.
        var inside = _rows.Where(r => r.Datum <= to && r.Dokoncene >= from).ToList();

        var lines = new List<Control>();
        foreach (var row in inside) {
            var id = row.Id;
            var line = new Panel { Dock = DockStyle.Top, Height = 46, BackColor = Surface, Padding = new Padding(14, 6, 14, 6), Cursor = Cursors.Hand };
            var where = new Label {
                Dock = DockStyle.Top, Height = 20, Text = $"{row.Odkial}  →  {row.Kam}",
                ForeColor = Ink, AutoEllipsis = true,
            };
            var what = new Label {
                Dock = DockStyle.Top, Height = 16, ForeColor = Muted, AutoEllipsis = true,
                Font = new Font("Segoe UI", 8F),
                // The date as well when it began before the sitting did, or a haul
                // picked up this morning reads as though it started at six last night.
                Text = $"{(row.Datum.Date == from.Date ? row.Datum.ToString("HH:mm") : row.Datum.ToString("dd.MM HH:mm"))}"
                     + $"   ·   {row.Naklad}   ·   {row.Vzdialenost}   ·   {row.Odmena}",
            };
            line.Controls.Add(what);
            line.Controls.Add(where);
            foreach (Control part in new Control[] { line, where, what }) {
                part.Click += (_, _) => ShowDetail(id);
            }
            lines.Add(line);
        }

        if (lines.Count == 0) {
            lines.Add(new Label {
                Dock = DockStyle.Top, Height = 30, Text = Strings.T("sess.nothing"),
                ForeColor = Muted, BackColor = Surface, Padding = new Padding(14, 6, 0, 0),
            });
        }

        // Docked children stack in reverse, so the list goes in backwards.
        for (var i = lines.Count - 1; i >= 0; i--) _sessionSide.Controls.Add(lines[i]);
        _sessionSide.Controls.Add(CardHeading(Strings.T("sess.inThisOne")));
        UseDarkScrollbars(_sessionSide);
        SmoothPainting(_sessionSide);
    }

    /// <summary>Between a column too narrow for its own words and one that has eaten
    /// the page beside it.</summary>
    private static int SidebarWidth(int asked) => Math.Clamp(asked, 176, 380);

    /// <summary>
    /// The build, as the project file spells it.
    ///
    /// Read from the assembly rather than written out here, so there is one place a
    /// version is set and no chance of the window claiming a different one from the
    /// executable it is. The informational version is the one that carries the
    /// project's own string; anything a build server appends after a plus sign is
    /// not something a driver needs to read.
    /// </summary>
    private static string AppVersion {
        get {
            var asm = typeof(MainForm).Assembly;
            var said = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (said is { Length: > 0 }) {
                var plus = said.IndexOf('+');
                return "v" + (plus > 0 ? said[..plus] : said);
            }
            return asm.GetName().Version is { } v ? $"v{v.Major}.{v.Minor}.{v.Build}" : "";
        }
    }

    private void ShowPage(string page) {
        _page = page;

        // Every page is hidden but the one asked for. The strip along the foot is not
        // a page and carries no tag, so it is left alone: it belongs to all of them.
        foreach (Control c in _content.Controls) {
            c.Visible = (string?)c.Tag == page;
        }

        // The column is painted rather than built out of buttons, so it is asked to
        // draw itself again instead of being restyled control by control.
        RefreshFrame();

        // A scrolling panel only takes the dark bars once it has a handle, and a page
        // gets one the first time it is shown. Asked again here, which is the moment
        // that is true for whichever page has just come up.
        UseDarkScrollbars(_content);
    }

    private Panel BuildContent() {
        _content.Dock = DockStyle.Fill;
        _content.BackColor = Canvas;
        _content.Controls.Clear();

        var live = BuildJobPage();
        live.Tag = "live";
        _detailPage.Dock = DockStyle.Fill;
        _detailPage.BackColor = Canvas;
        _detailPage.Padding = new Padding(16);
        _detailPage.AutoScroll = true;
        _detailPage.Tag = "detail";
        _content.Controls.Add(_detailPage);
        var deliveries = BuildHistoryPage();
        deliveries.Tag = "deliveries";
        var sessions = BuildSessionsPage();
        sessions.Tag = "sessions";
        var trucks = BuildTrucksPage();
        trucks.Tag = "trucks";
        var awards = BuildAwardsPage();
        awards.Tag = "awards";
        var map = BuildMapPage();
        map.Tag = "map";
        var stats = BuildStatsPage();
        stats.Tag = "stats";

        _content.Controls.Add(live);
        _content.Controls.Add(deliveries);
        _content.Controls.Add(sessions);
        _content.Controls.Add(trucks);
        _content.Controls.Add(awards);
        _content.Controls.Add(map);
        _content.Controls.Add(stats);
        return _content;
    }

    /// <summary>
    /// Every drive of one game on one picture.
    ///
    /// A second way through the same history: the list answers when something was
    /// driven, this answers where, and sometimes that is the half a driver
    /// remembers. Pointing at a route names it, clicking one opens its card, so the
    /// map is a way into the history rather than an ornament beside it.
    ///
    /// One game at a time, and that is not a convenience. ATS and ETS2 number their
    /// worlds separately, so the same coordinates mean unrelated places in the two;
    /// drawn together they would overlap into nonsense.
    /// </summary>
    private Panel BuildMapPage() {
        var page = new Panel { Dock = DockStyle.Fill, BackColor = Canvas, Padding = new Padding(16) };

        var map = new RouteView {
            Dock = DockStyle.Fill,
            EmptyText = Strings.T("map.noneGame"),
            Hint = Strings.T("map.hintHistory"),
        };
        map.DescribeRoute = id => _rows.FirstOrDefault(r => r.Id == id) is { } row
            ? $"{row.Odkial}  →  {row.Kam}      {row.Datum:dd.MM.yyyy}   {row.Vzdialenost}"
            : "";
        map.RouteChosen += ShowDetail;
        _mapPage = map;

        _mapGame.MenuMaker = () => new ContextMenuStrip {
            BackColor = Look.Chrome, ForeColor = Look.Ink, ShowImageMargin = false,
            Renderer = new ToolStripProfessionalRenderer(new DarkMenuColours()), Font = Look.Small,
        };
        _mapGame.Changed -= OnMapGameChanged;
        _mapGame.Changed += OnMapGameChanged;

        // The page names itself, says how much is on it, and offers the window.
        var bar = new Panel { Dock = DockStyle.Top, Height = 40, BackColor = Look.Window };
        bar.Paint += (_, e) => {
            var g = e.Graphics;
            g.Clear(Look.Window);
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            Look.Text(g, Strings.T("tab.map"), Look.PageHeading, Look.Ink, 0, 8);
            var at = Look.Measure(g, Strings.T("tab.map"), Look.PageHeading).Width + 14;
            Look.Text(g, _mapNote, Look.Small, Look.Dim, at, 12);
        };
        _mapBar = bar;

        var full = MakeQuietButton(Strings.T("map.fullScreen"), ToggleFocus);
        full.Width = 110;
        bar.Controls.Add(full);
        bar.Controls.Add(_mapGame);
        void PlaceMapBar() {
            full.Location = new Point(bar.ClientSize.Width - full.Width, 4);
            _mapGame.Location = new Point(full.Left - _mapGame.Width - 12, 5);
        }
        bar.Resize += (_, _) => PlaceMapBar();
        PlaceMapBar();

        // The drawing keeps the same corner every panel in this window has, which a
        // rectangular control cannot do on its own: the control is given a rounded
        // region, and the frame behind it paints the edge around that shape.
        var frame = new Panel { Dock = DockStyle.Fill, BackColor = Look.Window, Padding = new Padding(1) };
        frame.Paint += (_, e) => {
            var g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(Look.Window);
            Look.Surface(g, new RectangleF(0, 0, frame.Width, frame.Height), Look.Window, Look.Border);
        };
        frame.Resize += (_, _) => RoundOff(map, Look.RadiusPanel);
        frame.Controls.Add(map);
        RoundOff(map, Look.RadiusPanel);
        MapButtons(frame, map, null);

        // Two entries, laid over the bottom left corner of the drawing: which line is
        // the one being pointed at, and what the rest of them are.
        var legend = new Panel { Width = 210, Height = 30, BackColor = Look.Window };
        legend.Paint += (_, e) => {
            var g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            Look.Surface(g, new RectangleF(0, 0, legend.Width, legend.Height), Look.Tint(Look.Window, 88), Look.Hairline, Look.RadiusControl);
            var at = 12f;
            foreach (var (hue, word) in new[] { (Look.Accent, Strings.T("map.picked")), (Look.Slate, Strings.T("map.everyOther")) }) {
                using var pen = new Pen(hue, 2f);
                g.DrawLine(pen, at, legend.Height / 2f, at + 14, legend.Height / 2f);
                Look.Text(g, word, Look.Caption, Look.Dim, at + 20, legend.Height / 2f - 7);
                at += 20 + Look.Measure(g, word, Look.Caption).Width + 14;
            }
        };
        frame.Controls.Add(legend);
        legend.BringToFront();
        void PlaceLegend() => legend.Location = new Point(12, frame.ClientSize.Height - legend.Height - 12);
        frame.Resize += (_, _) => PlaceLegend();
        PlaceLegend();

        // The routes as a list beside the drawing, since a line on a map says where but
        // not when, and the two together are how somebody finds the drive they mean.
        _mapList.Width = 250;
        _mapList.CardHeight = 44;
        _mapList.EmptyText = "";
        _mapList.Opened -= OnMapRouteOpened;
        _mapList.Opened += OnMapRouteOpened;

        // The list under its own small capital title, the way every other set of
        // figures in this window is named.
        var listHost = new Panel { Dock = DockStyle.Right, Width = 250, BackColor = Look.Window, Padding = new Padding(12, 0, 0, 0) };
        var title = new Panel { Dock = DockStyle.Top, Height = 26, BackColor = Look.Window };
        title.Paint += (_, e) => {
            e.Graphics.Clear(Look.Window);
            e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            Look.Tracked(e.Graphics, Strings.T("map.routesTitle").ToUpperInvariant(), Look.Label, Look.Dim, 2, 9);
        };
        _mapList.Dock = DockStyle.Fill;
        listHost.Controls.Add(_mapList);
        listHost.Controls.Add(title);

        page.Controls.Add(frame);
        page.Controls.Add(listHost);
        page.Controls.Add(bar);
        return page;
    }

    private void OnMapGameChanged() => ReloadMapPage();

    /// <summary>Fills the map page from whatever the history currently holds. The
    /// game list is built from the deliveries themselves rather than from the two
    /// the app knows about, so a profile that has only ever driven one of them is
    /// not offered an empty picture of the other.</summary>
    private void ReloadMapPage() {
        if (_mapPage is not { } map) return;

        var picks = new List<(string Game, string Map)>();
        foreach (var g in _rows.Select(r => r.Hra).Where(g => g.Length > 0).Distinct().OrderBy(g => g)) {
            var maps = MapsFor(g);
            if (maps.Count < 2) picks.Add((g, ""));
            else foreach (var one in maps) picks.Add((g, one.Name));
        }

        if (!_mapGames.SequenceEqual(picks)) {
            var was = _mapGame.SelectedIndex >= 0 && _mapGame.SelectedIndex < _mapGames.Count
                ? _mapGames[_mapGame.SelectedIndex] : ((string, string)?)null;
            _mapGames = picks;
            // Shown as the game calls itself, kept as the database spells it, and named
            // after its world only where there is more than one to be in.
            _mapGame.Offer(picks.Select(p => p.Map.Length == 0 ? GameName(p.Game) : $"{GameName(p.Game)} - {p.Map}"));

            var back = was is { } had ? picks.IndexOf(had) : -1;
            _mapGame.SelectedIndex = back >= 0 ? back : picks.Count > 0 ? 0 : -1;
        }
        if (_mapGame.SelectedIndex < 0 || _mapGame.SelectedIndex >= _mapGames.Count) {
            map.Show(new List<RouteLayer>(), 0, new List<CityAnchor>());
            return;
        }

        var (game, chosen) = _mapGames[_mapGame.SelectedIndex];
        // Picking a world here is picking it everywhere, since a delivery's own card
        // draws the same ground: the page is where the choice is made, not where it
        // only applies.
        if (chosen.Length > 0 && (!_settings.MapChoice.TryGetValue(game, out var already) || already != chosen)) {
            _settings.MapChoice[game] = chosen;
            _settings.Save();
        }
        // Same reasoning as the statistics: the page names the game it is showing, so
        // a speed read off it is in that game's units. The distances beside a route
        // come from its own row and were already right.
        var shown = Units.For(_settings.Units, game);
        map.FormatSpeed = kmh => shown.FormatSpeed(kmh);
        var routes = RoutesFor(game);
        map.GameMap = GameMapFor(game, Ground(routes.Routes.Values));
        map.Show(Layers(routes), 0, routes.Cities);

        // The same drives as a list, newest first, each naming its two ends and what it
        // carried. Clicking one opens its card, the way clicking the line does.
        _mapNote = $"{routes.Routes.Count} {Strings.T("map.routes")} · {Strings.T("map.hoverOne")}";
        _mapBar?.Invalidate();

        _mapList.Show(_rows.Where(r => r.Hra == game).Select(r => new CardStack.Card {
            Title = $"{r.Odkial} → {r.Kam}",
            Under = $"{r.Datum:dd.MM.yy} · {r.Vzdialenost} · {r.Naklad}",
            Behind = r.Id,
        }));
    }

    // ---------- menu ----------

    /// <summary>Three menus, grouped by what they are for: starting the game,
    /// everything that touches the database, and preferences. Units and language
    /// used to sit as top level menus of their own, which put two rarely used
    /// settings on the same footing as the whole data section.</summary>
    private MenuStrip BuildMenu() {
        var menu = new MenuStrip {
            BackColor = Surface, ForeColor = Ink,
            Renderer = new ToolStripProfessionalRenderer(new DarkMenuColours()),
            Padding = new Padding(8, 4, 0, 4),
        };
        menu.Items.Add(BuildPlayMenu());
        menu.Items.Add(BuildDataMenu());
        menu.Items.Add(BuildSettingsMenu());
        menu.Items.Add(BuildHelpMenu());
        StyleMenuItems(menu.Items);
        return menu;
    }

    /// <summary>The colour table paints the menu's own surfaces, but each item still
    /// draws its text in the system colour, which on this background is near enough
    /// invisible. Walked recursively because submenus are items too.</summary>
    private static void StyleMenuItems(ToolStripItemCollection items) {
        foreach (ToolStripItem item in items) {
            item.ForeColor = Ink;
            item.BackColor = Surface;
            if (item is ToolStripMenuItem menuItem && menuItem.HasDropDownItems) {
                StyleMenuItems(menuItem.DropDownItems);
            }
        }
    }

    /// <summary>Everything that moves data in or out of the database, in one place:
    /// what comes in, what goes out, what protects it, and where it lives. These were
    /// spread between a menu and a row of buttons above the delivery list, where they
    /// sat next to searching and filtering and looked like the same kind of thing.</summary>
    private ToolStripMenuItem BuildDataMenu() {
        var data = new ToolStripMenuItem(Strings.T("menu.data"));

        data.DropDownItems.Add(MenuAction(Strings.T("menu.import"), ImportTrucksBook));
        data.DropDownItems.Add(MenuAction(Strings.T("menu.rebuild"), RebuildFromRecordings));
        data.DropDownItems.Add(MenuAction(Strings.T("menu.removeImported"), RemoveImported));
        data.DropDownItems.Add(new ToolStripSeparator());

        data.DropDownItems.Add(MenuAction(Strings.T("menu.exportCsv"), () => Export("csv")));
        data.DropDownItems.Add(MenuAction(Strings.T("menu.exportJson"), () => Export("json")));
        data.DropDownItems.Add(new ToolStripSeparator());

        data.DropDownItems.Add(MenuAction(Strings.T("menu.backup"), DoBackup));
        data.DropDownItems.Add(MenuAction(Strings.T("menu.restore"), DoRestore));
        data.DropDownItems.Add(new ToolStripSeparator());

        // Data lives under LocalAppData, not next to the exe, so it survives
        // rebuilds, which also makes it hard to find by hand.
        data.DropDownItems.Add(OpenFolderItem(Strings.T("menu.folder.db"), DeliveryStore.DefaultDir()));
        data.DropDownItems.Add(OpenFolderItem(Strings.T("menu.folder.backups"), Path.Combine(DeliveryStore.DefaultDir(), "backups")));
        data.DropDownItems.Add(OpenFolderItem(Strings.T("menu.folder.sessions"), Path.Combine(DeliveryStore.DefaultDir(), "sessions")));
        return data;
    }

    /// <summary>Where the window explains itself. One item for now, but the menu
    /// is the place a reader looks first, and a legend buried in a settings dialog
    /// is a legend nobody finds.</summary>
    private ToolStripMenuItem BuildHelpMenu() {
        var help = new ToolStripMenuItem(Strings.T("menu.help"));
        help.DropDownItems.Add(MenuAction(Strings.T("menu.legend"), ShowLegend));
        return help;
    }

    /// <summary>Preferences about how the same data is presented, so they belong
    /// together rather than one menu each.</summary>
    private ToolStripMenuItem BuildSettingsMenu() {
        var settings = new ToolStripMenuItem(Strings.T("menu.settings"));
        settings.DropDownItems.Add(BuildUnitsMenu());
        settings.DropDownItems.Add(BuildLanguageMenu());
        if (BuildRecordWorldMenu() is { } worlds) settings.DropDownItems.Add(worlds);
        settings.DropDownItems.Add(BuildDiscordMenu());
        settings.DropDownItems.Add(MenuAction(Strings.T("menu.signature"), SignHere));

        var liveMap = new ToolStripMenuItem(Strings.T("menu.liveMap")) { Checked = _settings.LiveMap };
        liveMap.Click += (_, _) => {
            _settings.LiveMap = !_settings.LiveMap;
            _settings.Save();
            liveMap.Checked = _settings.LiveMap;
            AfterMenuCloses(BuildLayout);
        };
        settings.DropDownItems.Add(liveMap);

        var regions = new ToolStripMenuItem(Strings.T("menu.cityRegions")) { Checked = _settings.CityRegions };
        regions.Click += (_, _) => {
            _settings.CityRegions = !_settings.CityRegions;
            _settings.Save();
            regions.Checked = _settings.CityRegions;
            AfterMenuCloses(BuildLayout);
        };
        settings.DropDownItems.Add(regions);
        return settings;
    }

    private ToolStripMenuItem BuildDiscordMenu() {
        var discord = new ToolStripMenuItem(Strings.T("menu.discord"));

        var show = new ToolStripMenuItem(Strings.T("menu.discordPresence")) { Checked = _settings.DiscordPresence };
        show.Click += (_, _) => {
            _settings.DiscordPresence = !_settings.DiscordPresence;
            _settings.Save();
            show.Checked = _settings.DiscordPresence;
            StartDiscord();
        };
        discord.DropDownItems.Add(show);

        discord.DropDownItems.Add(MenuAction(Strings.T("menu.discordAppId"), () => {
            var entered = Prompt(Strings.T("discord.appIdTitle"), Strings.T("discord.appIdPrompt"), _settings.DiscordAppId ?? "");
            if (entered == null) return;
            _settings.DiscordAppId = entered.Trim() is { Length: > 0 } id ? id : null;
            _settings.Save();
            StartDiscord();
        }));

        return discord;
    }

    /// <summary>Rebuilt from scratch on every change rather than reconfigured: the
    /// application ID is fixed for the lifetime of a connection, so switching it
    /// means a new one anyway.</summary>
    private void StartDiscord() {
        _discord?.Dispose();
        _discord = null;

        if (!_settings.DiscordPresence) return;

        // The driver's own application when there is one, Waybill's otherwise. There
        // is always one, so turning the switch on is the whole of the setup.
        var app = _settings.DiscordAppId is { Length: > 0 } own ? own : DiscordPresence.DefaultAppId;
        _discord = new DiscordPresence(app);
        _discord.Message += m => BeginInvoke(() => AddLog(m));
        // Said up front, so the log shows the feature is on and trying rather than
        // saying nothing at all until something goes wrong.
        AddLog(Strings.T("discord.waiting"));
    }

    private ToolStripMenuItem MenuAction(string label, Action action) {
        var item = new ToolStripMenuItem(label);
        item.Click += (_, _) => AfterMenuCloses(action);
        return item;
    }

    private ToolStripMenuItem BuildUnitsMenu() {
        var units = new ToolStripMenuItem(Strings.T("menu.units"));

        // "auto" is the default because each title has its own convention; the
        // explicit choices are for anyone who wants one system everywhere.
        var options = new[] { "auto", "metric", "imperial" };

        foreach (var key in options) {
            var item = new ToolStripMenuItem(Strings.T("menu.units." + key)) { Tag = key, Checked = _settings.Units == key };
            item.Click += (_, _) => {
                _settings.Units = key;
                _settings.Save();
                foreach (ToolStripMenuItem other in units.DropDownItems) {
                    other.Checked = Equals(other.Tag, key);
                }
                ReloadHistory();
                ReloadStats();
            };
            units.DropDownItems.Add(item);
        }

        return units;
    }

    /// <summary>
    /// Which world new deliveries are recorded as driven in.
    ///
    /// Not the same question as which one the map page is showing. This one says what
    /// is being played tonight, so looking through last month's vanilla history while
    /// running a map mod does not label tonight's drive wrongly. Offered only where a
    /// game has more than one world, and "not said" is a real answer: it leaves the
    /// map to be worked out from where the drive began and ended.
    /// </summary>
    private ToolStripMenuItem? BuildRecordWorldMenu() {
        var games = new[] { "Ets2", "Ats" }.Where(g => MapsFor(g).Count > 1).ToList();
        if (games.Count == 0) return null;

        var top = new ToolStripMenuItem(Strings.T("menu.recordWorld"));
        foreach (var game in games) {
            var forGame = games.Count > 1 ? new ToolStripMenuItem(GameName(game)) : top;
            var now = _settings.MapRecord.TryGetValue(game, out var world) ? world : "";

            void Choice(string name, string label) {
                var item = new ToolStripMenuItem(label) { Tag = name, Checked = now.Equals(name, StringComparison.OrdinalIgnoreCase) };
                item.Click += (_, _) => {
                    if (name.Length == 0) _settings.MapRecord.Remove(game);
                    else _settings.MapRecord[game] = name;
                    _settings.Save();
                    foreach (ToolStripMenuItem other in forGame.DropDownItems) {
                        other.Checked = Equals(other.Tag, name);
                    }
                };
                forGame.DropDownItems.Add(item);
            }

            Choice("", Strings.T("map.worldAuto"));
            foreach (var one in MapsFor(game)) Choice(one.Name, one.Name);
            if (!ReferenceEquals(forGame, top)) top.DropDownItems.Add(forGame);
        }
        return top;
    }

    private ToolStripMenuItem BuildLanguageMenu() {
        var language = new ToolStripMenuItem(Strings.T("menu.language"));
        foreach (var (code, name) in Strings.All) {
            var item = new ToolStripMenuItem(name) { Tag = code, Checked = _settings.Language == code };
            item.Click += (_, _) => {
                if (_settings.Language == code) return;
                _settings.Language = code;
                _settings.Save();
                Strings.Language = code;
                // Rebuilding is deferred so the menu drop-down is gone before the
                // controls it belongs to are disposed.
                AfterMenuCloses(BuildLayout);
            };
            language.DropDownItems.Add(item);
        }

        return language;
    }

    /// <summary>One click from "app open" to "playing with tracking on": the engine
    /// is already recording by the time the window exists, so launching the game
    /// from here is the whole of one-click play.</summary>
    private ToolStripMenuItem BuildPlayMenu() {
        var play = new ToolStripMenuItem(Strings.T("menu.play"));

        foreach (var game in new[] { SimGame.Ats, SimGame.Ets2 }) {
            var installed = GameLauncher.IsInstalled(game);
            var item = new ToolStripMenuItem(GameLauncher.DisplayName(game)) { Enabled = installed };
            if (!installed) item.ToolTipText = Strings.T("msg.gameNotFound");

            item.Click += (_, _) => AfterMenuCloses(() => LaunchGame(game));
            play.DropDownItems.Add(item);
        }

        play.DropDownItems.Add(new ToolStripSeparator());
        var pluginItem = new ToolStripMenuItem(Strings.T("menu.installPlugin"));
        pluginItem.Click += (_, _) => AfterMenuCloses(InstallPlugin);
        play.DropDownItems.Add(pluginItem);
        play.DropDownItems.Add(BuildGamePathsMenu());

        return play;
    }

    /// <summary>Lets the games be pointed at by hand. The automatic search reads
    /// Steam's own registry entries, which describe where Steam thinks things are:
    /// that misses a game installed outside Steam entirely, and it can land on the
    /// empty folder Steam leaves behind after a game is moved to another library.</summary>
    private ToolStripMenuItem BuildGamePathsMenu() {
        var paths = new ToolStripMenuItem(Strings.T("menu.gamePaths"));

        foreach (var game in new[] { SimGame.Ats, SimGame.Ets2 }) {
            // The current answer is on the label, so the menu doubles as a way to see
            // which folder is being used without opening anything.
            var current = _settings.PathFor(game) ?? GameLauncher.FindGameDirectory(game);
            var shown = current ?? Strings.T("msg.gameNotFound");
            var suffix = _settings.PathFor(game) == null ? $" ({Strings.T("msg.gamePathAutoNow")})" : "";

            var item = new ToolStripMenuItem($"{GameLauncher.DisplayName(game)}...") { ToolTipText = shown + suffix };
            item.Click += (_, _) => AfterMenuCloses(() => PickGameFolder(game));
            paths.DropDownItems.Add(item);
        }

        paths.DropDownItems.Add(new ToolStripSeparator());
        var auto = new ToolStripMenuItem(Strings.T("menu.gamePathAuto"));
        auto.Click += (_, _) => AfterMenuCloses(() => {
            foreach (var game in new[] { SimGame.Ats, SimGame.Ets2 }) {
                _settings.SetPathFor(game, null);
                GameLauncher.SetOverride(game, null);
            }
            _settings.Save();
            AddLog(Strings.T("msg.gamePathCleared"));
            BuildLayout();
        });
        paths.DropDownItems.Add(auto);

        return paths;
    }

    /// <summary>Re-derives the tracked deliveries from their recordings. Worth having
    /// on a menu rather than only behind --rebuild: it is what puts a history back
    /// after a detection fix, or after the database and the recordings have drifted
    /// apart, and neither is a moment to send someone to a terminal.</summary>
    private void RebuildFromRecordings() {
        var answer = MessageBox.Show(this, Strings.T("msg.rebuildConfirm"), Strings.T("msg.rebuildTitle"),
            MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
        if (answer != DialogResult.OK) return;

        try {
            Cursor = Cursors.WaitCursor;
            var result = Tracking.Rebuild.Run(_store);

            var report = $"{Strings.T("msg.rebuildDone")}\n\n"
                + $"{Strings.T("msg.rebuildRecordings")}: {result.Recordings}\n"
                + $"{Strings.T("msg.rebuildDeliveries")}: {result.Deliveries}\n"
                + $"{Strings.T("msg.rebuildKept")}: {result.Kept}\n"
                + $"{Strings.T("msg.backupSaved")} {result.BackupPath}";
            if (result.Skipped.Count > 0) {
                report += $"\n\n{Strings.T("msg.rebuildSkipped")}\n" + string.Join("\n", result.Skipped);
            }

            AddLog($"{Strings.T("msg.rebuildDone")}: {result.Deliveries}");
            ReloadHistory();
            ReloadStats();
            MessageBox.Show(this, report, Strings.T("msg.rebuildTitle"));
        } catch (Exception ex) {
            MessageBox.Show(this, ex.Message, Strings.T("msg.error"), MessageBoxButtons.OK, MessageBoxIcon.Error);
        } finally {
            Cursor = Cursors.Default;
        }
    }

    /// <summary>Takes the TrucksBook rows back out. Nothing can regenerate them, which
    /// is exactly why it asks first and backs up before it does anything.</summary>
    private void RemoveImported() {
        var answer = MessageBox.Show(this, Strings.T("msg.removeImportedConfirm"), Strings.T("menu.removeImported"),
            MessageBoxButtons.OKCancel, MessageBoxIcon.Warning);
        if (answer != DialogResult.OK) return;

        try {
            var backup = _store.Backup();
            var removed = _store.DeleteImportedDeliveries();
            AddLog($"{Strings.T("menu.removeImported")}: {removed}");
            ReloadHistory();
            ReloadStats();
            MessageBox.Show(this, $"{Strings.T("msg.removed")}: {removed}\n\n{Strings.T("msg.backupSaved")} {backup}",
                Strings.T("menu.removeImported"));
        } catch (Exception ex) {
            MessageBox.Show(this, ex.Message, Strings.T("msg.error"), MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void PickGameFolder(SimGame game) {
        using var dlg = new FolderBrowserDialog {
            Description = $"{GameLauncher.DisplayName(game)}: {Strings.T("msg.pickGameFolder")}",
            UseDescriptionForTitle = true,
            SelectedPath = _settings.PathFor(game) ?? GameLauncher.FindGameDirectory(game) ?? "",
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        // Checked the same way the automatic search checks a candidate, so a folder
        // accepted here behaves exactly like one that was found on its own.
        if (!GameLauncher.LooksLikeGameDirectory(game, dlg.SelectedPath)) {
            MessageBox.Show(this, $"{Strings.T("msg.notGameFolder")}\n{dlg.SelectedPath}",
                GameLauncher.DisplayName(game), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _settings.SetPathFor(game, dlg.SelectedPath);
        _settings.Save();
        GameLauncher.SetOverride(game, dlg.SelectedPath);
        AddLog($"{Strings.T("msg.gamePathSet")}: {dlg.SelectedPath}");

        // The launch entries are enabled from what was found, so they are rebuilt.
        BuildLayout();
    }

    /// <summary>
    /// Runs an action once the menu drop-down has closed and the message loop is
    /// back to normal. Opening a modal dialog straight from a menu click leaves the
    /// drop-down holding the mouse capture, which can wedge input entirely: the app
    /// stops responding and Windows logs an AppHang. Posting the work back to the
    /// message queue lets the menu tear down first.
    /// </summary>
    private void AfterMenuCloses(Action action) => BeginInvoke(action);

    private void LaunchGame(SimGame game) {
        // The plugin is what makes any of this work, so say so before the game
        // starts rather than leaving the user watching a tracker that sees nothing.
        if (!GameLauncher.IsPluginInstalled(game)) {
            var answer = MessageBox.Show(this,
                $"{GameLauncher.DisplayName(game)} {Strings.T("msg.pluginMissing")}",
                Strings.T("msg.pluginMissingTitle"), MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning);

            if (answer == DialogResult.Cancel) return;
            if (answer == DialogResult.Yes) {
                var source = GameLauncher.FindBundledPlugin() ?? AskForPluginFile();
                if (source == null) return;
                if (InstallPluginFor(game, source) is { } problem) {
                    MessageBox.Show(this, problem, Strings.T("msg.plugin"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
            }
        }

        try {
            GameLauncher.Launch(game);
            AddLog($"{Strings.T("msg.launching")} {GameLauncher.DisplayName(game)}...");
        } catch (Exception ex) {
            MessageBox.Show(this, Strings.T("msg.launchFailed") + "\n" + ex.Message, Strings.T("msg.error"), MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void InstallPlugin() {
        var choices = new[] { SimGame.Ats, SimGame.Ets2 }.Where(GameLauncher.IsInstalled).ToArray();
        if (choices.Length == 0) {
            MessageBox.Show(this, Strings.T("msg.noGameInstalled"), Strings.T("msg.plugin"));
            return;
        }

        // One picker at most, reused for every game, instead of a dialog per game.
        var source = GameLauncher.FindBundledPlugin() ?? AskForPluginFile();
        if (source == null) return;

        var report = new List<string>();
        foreach (var game in choices) report.Add(InstallPluginFor(game, source) ?? $"{GameLauncher.DisplayName(game)}: {Strings.T("msg.pluginDone")}");

        MessageBox.Show(this, string.Join("\n\n", report), Strings.T("msg.plugin"));
    }

    private string? AskForPluginFile() {
        using var dlg = new OpenFileDialog {
            Title = Strings.T("msg.pickPlugin"),
            Filter = "scs-telemetry.dll|scs-telemetry.dll|DLL|*.dll",
        };
        return dlg.ShowDialog(this) == DialogResult.OK ? dlg.FileName : null;
    }

    /// <summary>Copies the plugin into one game. Returns null on success, or a
    /// message describing what went wrong.</summary>
    private string? InstallPluginFor(SimGame game, string source) {
        var plugins = GameLauncher.PluginDirectory(game, out var problem);
        if (plugins == null) return $"{GameLauncher.DisplayName(game)}: {problem}";

        var target = Path.Combine(plugins, "scs-telemetry.dll");
        try {
            File.Copy(source, target, overwrite: true);
            AddLog($"{Strings.T("msg.pluginInstalled")}: {target}");
            return null;
        } catch (UnauthorizedAccessException) {
            return $"{GameLauncher.DisplayName(game)}: {Strings.T("msg.pluginNoWrite")}\n{plugins}";
        } catch (Exception ex) {
            return $"{GameLauncher.DisplayName(game)}: {ex.Message}";
        }
    }

    private static ToolStripMenuItem OpenFolderItem(string label, string path) {
        var item = new ToolStripMenuItem(label);
        item.Click += (_, _) => {
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        };
        return item;
    }

    /// <summary>Units for figures that aren't tied to one delivery - live progress
    /// and aggregate stats. Follows the game currently being played, falling back to
    /// the most recent delivery's game when nothing is running.</summary>
    private Units CurrentUnits() =>
        Units.For(_settings.Units, _engine?.ActiveState?.Game ?? _store.MostRecentGame());

    // ---------- the engine ----------

    /// <summary>
    /// A delivery to put on the live page as though it were happening, set by
    /// <c>--demo &lt;id&gt;</c> on the command line.
    ///
    /// For showing and photographing the one page that otherwise needs a game
    /// running and a load on the hook. Nothing is written and no telemetry is
    /// touched; the moment a game connects, the real drive takes the page back.
    /// </summary>
    public static long? DemoDelivery;

    private void StartEngine() {
        _engine = new TrackerEngine(_store);
        _engine.WorldForNewDelivery = game =>
            _settings.MapRecord.TryGetValue(game, out var world) ? world : "";
        _engine.Message += m => BeginInvoke(() => AddLog(m));
        _engine.JobStarted += j => BeginInvoke(() => AddLog($"{Strings.T("msg.jobStart")}  {j.SourceCity} -> {j.DestinationCity} ({j.Cargo})"));
        _engine.JobResumed += j => BeginInvoke(() => AddLog($"{Strings.T("msg.jobResume")}  {j.SourceCity} -> {j.DestinationCity}"));
        _engine.Noted += e => BeginInvoke(() => { AddLog(NoteLine(e)); FeedNote(e); });
        _engine.JobFinished += r => BeginInvoke(() => {
            AddLog($"{Strings.T("msg.jobEnd")}  {r.SourceCity} -> {r.DestinationCity}: {r.DistanceKm:0.0} km, {r.Validation.Status}");
            var paid = Units.For(_settings.Units, r.Game);
            // A cancelled job has no revenue and a penalty instead, so the line says
            // what the delivery actually did to the balance rather than assuming it
            // paid.
            var balance = r.Revenue ?? -(r.Penalty ?? 0);
            Happened(Noticed.Delivered, $"{r.SourceCity}  →  {r.DestinationCity}",
                     $"{paid.FormatDistance(r.DistanceKm)}  ·  {paid.FormatMoney(balance)}");
            // Before the reload rather than after it: the reload runs a quiet pass of
            // its own, and a quiet pass writes an award down without saying anything,
            // which would leave nothing left to announce.
            ReloadAwards(announce: true);
            ReloadHistory();
            ReloadStats();
        });

        if (!_engine.Start()) {
            AddLog(Strings.T("msg.noSharedMemory") + ": " + _engine.StartupError);
            AddLog(Strings.T("msg.pluginHint"));
        }

        AddLog(Strings.T("msg.recording") + ": " + _engine.SessionPath);
        AddLog(Strings.T("msg.database") + ": " + _engine.DbPath);

        // Waybill only counts what it sees. Started after the game, it misses however
        // far the current job has already come, and the delivery ends up shorter than
        // the one the game reports. A restart mid drive is fine and says nothing, so
        // this only fires when there is no unfinished job waiting to be picked up.
        var running = new[] { SimGame.Ats, SimGame.Ets2 }.Where(GameLauncher.IsRunning).ToArray();
        if (running.Length > 0 && !_engine.HasPendingResume) {
            AddLog(Strings.T("msg.startedAfterGame"));
            MessageBox.Show(this,
                $"{GameLauncher.DisplayName(running[0])}\n\n{Strings.T("msg.startedAfterGame")}",
                "Waybill", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        ReloadHistory();
        ReloadStats();

        if (DemoDelivery is { } demo && ShowDemoJob(demo)) {
            // Deliberately without Discord. Telling the world this driver is halfway
            // to Camp Verde because a screenshot was being taken is a lie told to
            // their friends rather than to a picture.
            AddLog($"{Strings.T("msg.demoMode")}  #{demo}");
            RefreshJob();
            return;
        }

        StartDiscord();
    }

    /// <summary>One thing that happened, written out with its figure in the units in
    /// use. "Fine" on its own says nothing that the driver did not already know from
    /// the game; the amount is the part worth reading.</summary>
    private string NoteLine(JobEvent e) {
        var u = CurrentUnits();
        var name = Strings.T("event." + e.Type);
        if (e.Value is not { } v) return name;

        var figure = e.Type switch {
            "fine" or "tollgate" or "ferry" or "train" or "refuel" => u.FormatMoney(v),
            // Damage steps and shares are already percentages of the whole.
            "collision" => $"{v:0.00} %",
            "rest" or "save_loaded" => $"{v:0} {Strings.T("unit.gameMinutes")}",
            _ => v.ToString("0.##"),
        };

        // Only where the detail adds something: what the fine was for, and which
        // crossing a ferry made. Elsewhere it holds the unit, which is already in
        // the figure.
        var extra = e.Type switch {
            "fine" => Label(e.Detail ?? ""),
            "ferry" or "train" => e.Detail ?? "",
            _ => "",
        };
        return extra.Length > 0 ? $"{name}: {figure}  ({extra})" : $"{name}: {figure}";
    }

    private void AddLog(string text) {
        _log.Items.Insert(0, $"{DateTime.Now:HH:mm:ss}  {text}");
        while (_log.Items.Count > 200) _log.Items.RemoveAt(_log.Items.Count - 1);
    }

    /// <summary>
    /// The coupled set, folded away behind one line. Closed it says what the set is;
    /// opened it lists every unit in hitching order with what each of them took,
    /// which is the part the game never shows: it reports one condition for the
    /// whole set, so on a triple the worst unit speaks for all five.
    /// </summary>
    private Control TrailerDropdown(DeliveryDetail d) {
        var units = d.TrailerUnits;
        var trailers = units.Where(x => x.Kind == "trailer").ToList();
        const int lineHeight = 26;
        var openHeight = 30 + units.Count * lineHeight + (trailers.Count > 1 ? lineHeight : 0);

        var box = new Panel { Dock = DockStyle.Top, Height = 30, BackColor = Surface };

        var arrow = new Label {
            Dock = DockStyle.Right, Width = 24, Text = "▸", ForeColor = Muted,
            TextAlign = ContentAlignment.MiddleCenter, Cursor = Cursors.Hand,
        };
        var summary = new Label {
            Dock = DockStyle.Fill, ForeColor = Ink, TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true, Cursor = Cursors.Hand,
            Text = TrailerSummary(d),
        };
        var caption = new Label {
            Dock = DockStyle.Left, Width = 200, Text = Strings.T("detail.trailer"), ForeColor = Muted,
            TextAlign = ContentAlignment.MiddleLeft, Cursor = Cursors.Hand,
        };

        var header = new Panel { Dock = DockStyle.Top, Height = 30, Padding = new Padding(16, 6, 16, 6), BackColor = Surface };
        header.Controls.Add(summary);
        header.Controls.Add(arrow);
        header.Controls.Add(caption);

        var body = new Panel { Dock = DockStyle.Top, Height = openHeight - 30, BackColor = Raised, Visible = false };
        var lines = new List<Control>();
        if (trailers.Count > 1) {
            lines.Add(UnitLine(Strings.T("detail.unitsSummary"),
                $"{Strings.T("detail.worst")} {Damage(trailers.Max(x => x.Damage))}   ·   "
                + $"{Strings.T("detail.average")} {Damage(trailers.Average(x => x.Damage))}", Muted, lineHeight));
        }
        for (var i = 0; i < units.Count; i++) {
            var unit = units[i];
            var name = TrailerNames.Describe(unit);
            var label = $"{i + 1}.  {Strings.T("value." + unit.Kind)}"
                      + (unit.Owned ? $"  ({Strings.T("detail.owned")})" : "");
            var line = UnitLine(label,
                $"{name}   ·   {unit.Plate}   ·   {Condition(unit.StartDamage, (unit.StartDamage ?? 0) + unit.Damage)}",
                unit.Kind == "dolly" ? Muted : Ink, lineHeight);
            // The reading is a convenience; the identifier is what the data says, and
            // it stays one hover away.
            foreach (Control part in line.Controls) _tips.SetToolTip(part, unit.Id);
            lines.Add(line);
        }
        // Docked children stack in reverse, so the summary ends up under the units.
        for (var i = lines.Count - 1; i >= 0; i--) body.Controls.Add(lines[i]);

        box.Controls.Add(body);
        box.Controls.Add(header);

        void Toggle(object? _, EventArgs __) {
            body.Visible = !body.Visible;
            arrow.Text = body.Visible ? "▾" : "▸";
            box.Height = body.Visible ? openHeight : 30;
        }
        foreach (Control c in new Control[] { header, summary, arrow, caption }) c.Click += Toggle;

        return box;
    }

    private string TrailerSummary(DeliveryDetail d) {
        var trailers = d.TrailerUnits.Count(x => x.Kind == "trailer");
        var dollies = d.TrailerUnits.Count(x => x.Kind == "dolly");
        var parts = new List<string>();
        if (d.TrailerChainType.Length > 0) parts.Add(Label(d.TrailerChainType));
        parts.Add($"{trailers}× {Strings.T("value.trailer")}");
        if (dollies > 0) parts.Add($"{dollies}× {Strings.T("value.dolly")}");
        if (d.TrailerOwned) parts.Add(Strings.T("detail.owned"));
        return string.Join("  ·  ", parts);
    }

    private Control UnitLine(string label, string value, Color colour, int height) {
        var line = new Panel { Dock = DockStyle.Top, Height = height, Padding = new Padding(28, 4, 16, 4), BackColor = Raised };
        line.Controls.Add(new Label {
            Dock = DockStyle.Fill, Text = value, ForeColor = colour,
            TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true, Font = new Font("Segoe UI", 8.5F),
        });
        line.Controls.Add(new Label {
            Dock = DockStyle.Left, Width = 172, Text = label, ForeColor = Muted,
            TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Segoe UI", 8.5F),
        });
        return line;
    }

    /// <summary>The game as people call it, not as the SDK enumerates it.</summary>
    private static string GameName(string game) => game switch {
        "Ats" => "ATS",
        "Ets2" => "ETS2",
        _ => game.ToUpperInvariant(),
    };

    /// <summary>Damage as it is worth reading: two decimals, because ordinary wear
    /// over a delivery lands in hundredths of a percent and rounding it to whole
    /// numbers turns every clean drive into a flat zero.</summary>
    private static string Damage(double share) =>
        share <= 0 ? $"0 %" : $"{share * 100:0.00} %";

    /// <summary>
    /// The condition of something before and after the delivery.
    ///
    /// A single figure has never been enough here. "2.98 %" against a truck means
    /// what this drive did to it, and against the load it means what it arrived in,
    /// which are two different questions answered in the same shape. Said as one
    /// figure arriving at another, both are on the line and neither can be mistaken
    /// for the other.
    ///
    /// Deliveries recorded before the starting condition was kept have only the
    /// difference, and say only that rather than pretending the set left the yard
    /// undamaged.
    /// </summary>
    private static string Condition(double? before, double after) =>
        before is { } b ? $"{Damage(b)}  →  {Damage(after)}" : Damage(after);

    /// <summary>
    /// Signing the sheets.
    ///
    /// Drawn once and kept, rather than asked for on every export: a signature is the
    /// same every time by definition, and being asked to draw one before each save
    /// would make it a chore instead of a document.
    /// </summary>
    private void SignHere() {
        using var pad = new SignaturePad(_settings.SignatureStrokes, Surface, Raised, Line, Ink, Muted);
        if (pad.ShowDialog(this) != DialogResult.OK) return;
        _settings.SignatureStrokes = pad.Written;
        _settings.Save();
    }

    /// <summary>A one line text prompt, because WinForms has no InputBox and the
    /// application ID has to come from somewhere. Returns null when cancelled,
    /// which is different from an empty string meaning "clear it".</summary>
    private string? Prompt(string title, string message, string value) {
        using var dialog = new Form {
            Text = title,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent,
            MinimizeBox = false,
            MaximizeBox = false,
            ClientSize = new Size(420, 190),
            BackColor = Surface,
            ForeColor = Ink,
            Font = Font,
        };

        var label = new Label { Text = message, Dock = DockStyle.Top, Height = 90, ForeColor = Muted };
        var input = new TextBox {
            Text = value, Dock = DockStyle.Top, BorderStyle = BorderStyle.None,
            BackColor = Raised, ForeColor = Ink, Font = new Font("Consolas", 10F),
        };
        // A borderless TextBox is exactly as tall as its text, so the room to breathe
        // has to come from a panel around it, as with the search box.
        var inputBox = new Panel { Dock = DockStyle.Top, Height = 30, BackColor = Raised, Padding = new Padding(10, 6, 10, 6) };
        inputBox.Controls.Add(input);

        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Width = 90, Height = 30, FlatStyle = FlatStyle.Flat, BackColor = Raised, ForeColor = Ink };
        var cancel = new Button { Text = Strings.T("button.cancel"), DialogResult = DialogResult.Cancel, Width = 90, Height = 30, FlatStyle = FlatStyle.Flat, BackColor = Raised, ForeColor = Ink };
        ok.FlatAppearance.BorderColor = Line;
        cancel.FlatAppearance.BorderColor = Line;

        var buttons = new FlowLayoutPanel {
            Dock = DockStyle.Bottom, Height = 44, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(0, 6, 0, 0),
        };
        buttons.Controls.Add(ok);
        buttons.Controls.Add(cancel);

        var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(16, 14, 16, 8) };
        body.Controls.Add(inputBox);
        body.Controls.Add(label);

        dialog.Controls.Add(body);
        dialog.Controls.Add(buttons);
        dialog.AcceptButton = ok;
        dialog.CancelButton = cancel;
        dialog.Load += (_, _) => UseDarkTitleBar(dialog);

        return dialog.ShowDialog(this) == DialogResult.OK ? input.Text : null;
    }

    // ---------- tabs ----------

    private Panel BuildHistoryPage() {
        var page = new Panel { Dock = DockStyle.Fill, Padding = new Padding(Look.PagePad, 10, Look.PagePad, Look.PagePad), BackColor = Look.Window };

        // The page word with the standing totals beside it, then the toolbar under it.
        var head = new Panel { Dock = DockStyle.Top, Height = 34, BackColor = Look.Window };
        head.Paint += (_, e) => {
            var g = e.Graphics;
            g.Clear(Look.Window);
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            Look.Text(g, Strings.T("tab.deliveries"), Look.PageHeading, Look.Ink, 0, 6);
            Look.TextRight(g, _historyTotals, Look.Small, Look.Dim, head.Width, 10);
        };
        _historyHead = head;

        var bar = new FlowLayoutPanel {
            Dock = DockStyle.Top, Height = 44, FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false, Padding = new Padding(0, 6, 0, 6), BackColor = Look.Window,
        };

        _search.PlaceholderText = Strings.T("search.placeholder");
        _search.BorderStyle = BorderStyle.None;
        _search.BackColor = Look.Control;
        _search.ForeColor = Look.Ink;
        _search.Font = Look.Small;
        _search.Dock = DockStyle.Fill;
        _search.TextChanged -= OnSearchTyped;
        _search.TextChanged += OnSearchTyped;

        // The glyph is drawn on the box rather than set as an icon: a search field is
        // a rounded control with a magnifier inset, and that is two shapes.
        var searchBox = new Panel {
            Width = 264, Height = Look.InputHeight, Margin = new Padding(0, 0, 10, 0),
            BackColor = Look.Window, Padding = new Padding(32, 8, 12, 6),
        };
        searchBox.Paint += (_, e) => {
            var g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            Look.Surface(g, new RectangleF(0, 0, searchBox.Width - 1, searchBox.Height - 1), Look.Control, Look.Border, Look.RadiusControl);
            using var pen = new Pen(Look.Dim, 1.4f);
            g.DrawEllipse(pen, 13, 10, 8, 8);
            g.DrawLine(pen, 20, 17, 23.5f, 20.5f);
        };
        searchBox.Controls.Add(_search);

        _gameFilter.Retext(GameName("Ets2"), Strings.T("filter.both"), GameName("Ats"));
        // Everything in this bar hangs off the same line: the switch used to sit two
        // pixels lower than the field beside it, which is exactly enough to see.
        _gameFilter.Margin = new Padding(0, 0, 10, 0);
        _gameFilter.BackColor = Look.Window;
        _gameFilter.Changed -= OnFilterChanged;
        _gameFilter.Changed += OnFilterChanged;

        // Three chips rather than a second switch. Oversize, late and damaged are not
        // three positions of one question: a run can be all three at once, and asking
        // for "ordinary" was never a question anybody had.
        _oversizeChip.Text = Strings.T("filter.oversize");
        _oversizeChip.Badge = (g, r, ink) => HazardStripes(g, Rectangle.Round(r), 220);
        _lateChip.Text = Strings.T("filter.late");
        _damagedChip.Text = Strings.T("filter.damaged");
        foreach (var chip in new[] { _oversizeChip, _lateChip, _damagedChip }) {
            chip.Margin = new Padding(0, 0, 8, 0);
            chip.Toggled -= ApplyFilter;
            chip.Toggled += ApplyFilter;
            using var g = CreateGraphics();
            chip.FitTo(g);
        }

        bar.Controls.Add(searchBox);
        bar.Controls.Add(_gameFilter);
        bar.Controls.Add(_oversizeChip);
        bar.Controls.Add(_lateChip);
        bar.Controls.Add(_damagedChip);

        // The three marks a row can carry, named. A dot with no key beside it is a
        // colour, and a colour on its own says nothing.
        var legend = new Panel { Width = 260, Height = Look.InputHeight, Margin = new Padding(10, 0, 0, 0), BackColor = Look.Window };
        legend.Paint += (_, e) => {
            var g = e.Graphics;
            g.Clear(Look.Window);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            var at = 0f;
            foreach (var (status, word) in new[] {
                         ("accepted", Strings.T("legend.dotClean")), ("review", Strings.T("legend.dotFine")),
                         ("rejected", Strings.T("legend.dotDamage")) }) {
                Look.Dot(g, new PointF(at + 4, legend.Height / 2f), VerdictColour(status), 7);
                Look.Text(g, word, Look.Caption, Look.Dim, at + 13, legend.Height / 2f - 7);
                at += 13 + Look.Measure(g, word, Look.Caption).Width + 16;
            }
        };
        _historyLegend = legend;

        _list.Dock = DockStyle.Fill;
        _list.EmptyText = Strings.T("list.empty");
        _list.Day = row => row.Datum.ToString("dd.MM.yy");
        _list.DaySummary = run => {
            var distance = run.Sum(r => r.DistanceKm);
            var pay = run.Sum(r => r.Zarobok);
            var u = Units.For(_settings.Units, run.Count > 0 ? run[0].Hra : "");
            return $"{run.Count} {Strings.T("list.deliveries")} · {u.FormatDistance(distance)} · {u.FormatMoney(pay)}";
        };
        _list.Outcome = row => (Label(row.Vysledok), VerdictColour(row.Stav));
        _list.Mark = row => VerdictColour(row.Stav);
        _list.Opened -= OnRowOpened;
        _list.Opened += OnRowOpened;
        _list.SortChanged -= ApplyFilter;
        _list.SortChanged += ApplyFilter;
        DescribeColumns();

        var hint = new Label {
            Dock = DockStyle.Bottom, Height = 22, ForeColor = Look.Faint, BackColor = Look.Window,
            Font = Look.Caption, Text = Strings.T("list.hint"), Padding = new Padding(26, 4, 0, 0),
        };

        page.Controls.Add(_list);
        page.Controls.Add(hint);
        page.Controls.Add(bar);
        page.Controls.Add(head);
        // The legend rides at the right end of the toolbar, which a flow panel cannot
        // do on its own, so it is placed against the page instead and kept there.
        page.Controls.Add(legend);
        legend.BringToFront();
        // The key gives way to the filters. It rides over the toolbar rather than in it,
        // so on a narrow window it was drawing itself on top of the three chips; what a
        // key is for is read once, and a chip is pressed.
        void Place() {
            var needs = bar.Controls.Cast<Control>().Sum(c => c.Width + c.Margin.Horizontal);
            legend.Visible = page.ClientSize.Width - needs > legend.Width + 24;
            legend.Location = new Point(page.ClientSize.Width - legend.Width - Look.PagePad, head.Bottom + 12);
        }
        page.Resize += (_, _) => Place();
        Place();
        return page;
    }

    /// <summary>The columns of the history, in the language it is being read in.</summary>
    private void DescribeColumns() {
        _list.Describe(
            (Strings.T("col.time"), 74, false, nameof(DeliveryRow.Datum), r => r.Datum.ToString("HH:mm")),
            (Strings.T("col.from"), 150, false, nameof(DeliveryRow.Odkial), r => r.Odkial),
            (Strings.T("col.to"), 150, false, nameof(DeliveryRow.Kam), r => r.Kam),
            (Strings.T("col.cargo"), 160, false, nameof(DeliveryRow.Naklad), r => r.Naklad),
            (Strings.T("col.distance"), 110, true, nameof(DeliveryRow.DistanceKm), r => r.Vzdialenost),
            (Strings.T("col.pay"), 110, true, nameof(DeliveryRow.Zarobok), r => r.Odmena),
            (Strings.T("col.outcome"), 110, true, nameof(DeliveryRow.Stav), _ => ""));
    }

    private void OnSearchTyped(object? sender, EventArgs e) => ApplyFilter();

    private void OnRowOpened(DeliveryRow row) => ShowDetail(row.Id);

    private void OnGridDoubleClick(object? sender, DataGridViewCellEventArgs e) {
        if (e.RowIndex < 0) return;
        if (_grid.Rows[e.RowIndex].DataBoundItem is DeliveryRow row) ShowDetail(row.Id);
    }

    /// <summary>Enter opens the selected delivery too. Walking the list with the
    /// arrow keys and then having to reach for the mouse to look at one is the kind
    /// of thing that makes a list feel like a wall rather than an index.</summary>
    private void OnGridKeyDown(object? sender, KeyEventArgs e) {
        if (e.KeyCode != Keys.Enter) return;
        if (_grid.CurrentRow?.DataBoundItem is not DeliveryRow row) return;
        // Otherwise Enter also steps the selection down a row behind the card.
        e.Handled = e.SuppressKeyPress = true;
        ShowDetail(row.Id);
    }

    /// <summary>
    /// Throws the list's first painting away and asks for another one.
    ///
    /// That first painting comes out with pale vertical lines between some of the
    /// columns, which no later painting draws: they went away a cell at a time as
    /// the list was clicked through, and all at once when the window was resized.
    /// Asking for the repaint any earlier does not work, because a paint is the
    /// last thing a window does and anything posted before it is dealt with first.
    /// So it is asked for from inside the painting itself, once, and then never
    /// again.
    /// </summary>
    private void DrawTheListAgainOnce(object? sender, PaintEventArgs e) {
        _grid.Paint -= DrawTheListAgainOnce;
        BeginInvoke(() => _grid.Invalidate());
    }

    private void OnFilterChanged(object? sender, EventArgs e) => ApplyFilter();

    /// <summary>How wide the gutter is: a verdict dot, then the oversize band.</summary>
    // Room for three marks side by side: the bolt of an electric truck, the
    // verdict, and the band of an oversize load down the edge. Each keeps its own
    // place whether or not the row has it, so the verdicts still read as one column
    // down the list rather than shuffling left and right by row.
    private const int GutterWidth = 36;
    private const int StripeWidth = 9;

    /// <summary>
    /// Paints the row's left gutter: the verdict as a dot, and the oversize load's
    /// markings as a band down the edge of it.
    ///
    /// The dot replaced a column of words. A verdict is one of four things and is
    /// read at a glance or not at all, so it does not need the width of a column,
    /// and the row already had a gutter waiting for it. The word is not lost: the
    /// gutter names it on hover and the delivery's own card explains it.
    ///
    /// The band fills its share of the cell edge to edge rather than sitting inside
    /// a margin, so consecutive oversize loads read as one marked stretch of the
    /// list instead of a column of dashes.
    ///
    /// Taken over from the grid entirely rather than drawn before it: a row header
    /// paints itself after the row does, so anything put there first was covered up
    /// by the header's own background a moment later.
    /// </summary>
    /// <summary>The mark for a truck that runs on a battery: the same lightning
    /// bolt it wears on its own cab, drawn rather than taken from a font, since the
    /// glyph for it is missing from half the fonts Windows ships with.</summary>
    private static void Bolt(Graphics g, RectangleF box) {
        var was = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var brush = new SolidBrush(Color.FromArgb(126, 196, 140));
        g.FillPolygon(brush, new[] {
            new PointF(box.Left + box.Width * 0.62f, box.Top),
            new PointF(box.Left, box.Top + box.Height * 0.58f),
            new PointF(box.Left + box.Width * 0.42f, box.Top + box.Height * 0.58f),
            new PointF(box.Left + box.Width * 0.34f, box.Bottom),
            new PointF(box.Right, box.Top + box.Height * 0.40f),
            new PointF(box.Left + box.Width * 0.56f, box.Top + box.Height * 0.40f),
        });
        g.SmoothingMode = was;
    }

    private void OnRowMarker(object? sender, DataGridViewCellPaintingEventArgs e) {
        if (e.ColumnIndex != -1 || e.RowIndex < 0 || e.RowIndex >= _grid.Rows.Count) return;
        // The surface is declared as one that may not be there, so it is taken once
        // and checked rather than reached through twice on trust.
        if (e.Graphics is not { } g) return;

        using (var clear = new SolidBrush(Canvas)) g.FillRectangle(clear, e.CellBounds);
        if (_grid.Rows[e.RowIndex].DataBoundItem is DeliveryRow row) {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var dot = new RectangleF(
                e.CellBounds.Left + 14,
                e.CellBounds.Top + (e.CellBounds.Height - 9) / 2f, 9, 9);
            using (var brush = new SolidBrush(VerdictColour(row.Stav))) g.FillEllipse(brush, dot);

            if (row.Special) {
                HazardStripes(g, new RectangleF(
                    e.CellBounds.Right - StripeWidth, e.CellBounds.Top,
                    StripeWidth, Math.Max(e.CellBounds.Height, 1)), 210);
            }

            // A battery instead of a tank, marked where the load's own markings are.
            // Drawn rather than written for the same reason as everything else in this
            // gutter: it is read at a glance or not at all.
            if (row.Elektricky) {
                Bolt(g, new RectangleF(
                    e.CellBounds.Left + 3, e.CellBounds.Top + (e.CellBounds.Height - 13) / 2f, 8, 13));
            }
        }
        e.Handled = true;
    }

    /// <summary>The verdict as a colour, in one place, so the dot in the list and the
    /// sample in the legend cannot end up meaning different things.</summary>
    private static Color VerdictColour(string status) => status switch {
        "rejected" => Look.Lost,
        "review" => Look.Accent,
        "imported" => Look.Route,
        _ => Look.Whole,
    };

    /// <summary>
    /// What the gutter's marks say, in words, for anyone who wants them.
    ///
    /// The verdict is a dot and the oversize load is a band, which is enough to scan
    /// a list by and not enough to learn from, so hovering names both and lists what
    /// was found. The full explanation stays on the card.
    ///
    /// Written onto the header cells rather than answered from an event: the grid
    /// asks for a cell's tooltip, and the row header is not a cell it asks about.
    /// </summary>
    private void SetGutterTips() {
        foreach (DataGridViewRow line in _grid.Rows) {
            if (line.DataBoundItem is not DeliveryRow row) continue;
            var said = new List<string> { Label(row.Stav) };
            if (row.Special) said.Add(Strings.T("detail.special"));
            said.AddRange(row.Flags
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(f => "•  " + Strings.T("flag." + f)));
            line.HeaderCell.ToolTipText = string.Join(Environment.NewLine, said);
        }
    }

    private void OnGridCellEndEdit(object? sender, DataGridViewCellEventArgs e) {
        if (_grid.Columns[e.ColumnIndex].DataPropertyName != nameof(DeliveryRow.Poznamky)) return;
        if (_grid.Rows[e.RowIndex].DataBoundItem is DeliveryRow row) _store.SetNotes(row.Id, row.Poznamky ?? "");
    }

    /// <summary>Colours the verdict so problem deliveries stand out without reading,
    /// and turns the stored identifiers into words. The bound value stays the
    /// identifier, which is what the filter compares and what the data actually is;
    /// only what reaches the screen is translated.</summary>
    private void OnGridCellFormatting(object? sender, DataGridViewCellFormattingEventArgs e) {
        var column = _grid.Columns[e.ColumnIndex].DataPropertyName;
        var raw = Convert.ToString(e.Value) ?? "";

        if (column == nameof(DeliveryRow.Stav)) {
            e.CellStyle!.ForeColor = raw switch {
                "rejected" => Color.Firebrick,
                "review" => Color.DarkGoldenrod,
                "imported" => Color.SteelBlue,
                _ => Color.ForestGreen,
            };
        }

        if (column is nameof(DeliveryRow.Stav) or nameof(DeliveryRow.Vysledok) or nameof(DeliveryRow.Styl)) {
            e.Value = Label(raw);
            e.FormattingApplied = true;
        }
    }

    /// <summary>A stored identifier as a word, capitalised, in the chosen language.
    /// Anything without a translation is shown as it is rather than hidden.</summary>
    private static string Label(string identifier) {
        if (identifier.Length == 0) return "";
        var translated = Strings.T("value." + identifier);
        return translated == "value." + identifier ? identifier : translated;
    }

    /// <summary>Everything the tracker measured stays read-only; only the user's own
    /// note is editable, and it is written straight back to the database on edit.</summary>
    private void OnGridBound(object? sender, DataGridViewBindingCompleteEventArgs e) {
        {
            foreach (DataGridViewColumn col in _grid.Columns) {
                col.ReadOnly = col.DataPropertyName != nameof(DeliveryRow.Poznamky);
            }

            // Fill mode spreads columns evenly, which wastes space on short values
            // like the game name and clips the timestamp. Weight them by content.
            var weights = new Dictionary<string, int> {
                [nameof(DeliveryRow.Datum)] = 105,
                [nameof(DeliveryRow.Hra)] = 62,
                [nameof(DeliveryRow.Odkial)] = 105,
                [nameof(DeliveryRow.Kam)] = 105,
                [nameof(DeliveryRow.Naklad)] = 130,
                [nameof(DeliveryRow.Tahac)] = 115,
                [nameof(DeliveryRow.Vzdialenost)] = 95,
                [nameof(DeliveryRow.Odmena)] = 90,
                [nameof(DeliveryRow.Pokuty)] = 70,
                [nameof(DeliveryRow.Kolizie)] = 70,
                [nameof(DeliveryRow.Vysledok)] = 90,
                [nameof(DeliveryRow.Styl)] = 80,
                [nameof(DeliveryRow.Poznamky)] = 160,
            };
            foreach (DataGridViewColumn col in _grid.Columns) {
                // Cleared first: the last column was left filling the leftover width by
                // the previous binding, and a width cannot be set on a column while it
                // is doing that.
                col.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
                if (weights.TryGetValue(col.DataPropertyName, out var w)) col.Width = (int)w;
                else col.Width = 90;
            }


            // Column captions come from the bound property names, which are fixed, so
            // they are relabelled here from the translation table.
            var captions = new Dictionary<string, string> {
                [nameof(DeliveryRow.Datum)] = Strings.T("col.date"),
                [nameof(DeliveryRow.Hra)] = Strings.T("col.game"),
                [nameof(DeliveryRow.Odkial)] = Strings.T("col.from"),
                [nameof(DeliveryRow.Kam)] = Strings.T("col.to"),
                [nameof(DeliveryRow.Naklad)] = Strings.T("col.cargo"),
                [nameof(DeliveryRow.Tahac)] = Strings.T("col.truck"),
                [nameof(DeliveryRow.Vzdialenost)] = Strings.T("col.distance"),
                [nameof(DeliveryRow.Odmena)] = Strings.T("col.pay"),
                [nameof(DeliveryRow.Pokuty)] = Strings.T("col.fines"),
                [nameof(DeliveryRow.Kolizie)] = Strings.T("col.collisions"),
                [nameof(DeliveryRow.Vysledok)] = Strings.T("col.outcome"),
                [nameof(DeliveryRow.Styl)] = Strings.T("col.style"),
                [nameof(DeliveryRow.Poznamky)] = Strings.T("col.notes"),
            };
            foreach (DataGridViewColumn col in _grid.Columns) {
                if (captions.TryGetValue(col.DataPropertyName, out var caption)) col.HeaderText = caption;
            }
            if (_grid.Columns[nameof(DeliveryRow.Datum)] is { } dateCol) {
                dateCol.DefaultCellStyle.Format = "dd.MM.yy HH:mm";
            }
            foreach (var numeric in new[] { nameof(DeliveryRow.Vzdialenost), nameof(DeliveryRow.Odmena), nameof(DeliveryRow.Pokuty), nameof(DeliveryRow.Kolizie) }) {
                if (_grid.Columns[numeric] is { } c) c.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
            }
            // Wide enough for the longest name either end of a delivery, with its
            // state written after it: at the default width "Salt Lake City, UT" came
            // out as "Salt Lake City, ...", which is the one thing the state was
            // added to avoid.
            foreach (var place in new[] { nameof(DeliveryRow.Odkial), nameof(DeliveryRow.Kam) }) {
                if (_grid.Columns[place] is { } c) c.Width = 132;
            }
            FillToEdge(_grid, nameof(DeliveryRow.Naklad));
            SetGutterTips();
        }
    }

    /// <summary>Shared look for both grids: no gridlines to speak of, a quiet header,
    /// and rows with enough height to breathe.</summary>
    private static void StyleGrid(DataGridView g) {
        g.BackgroundColor = Surface;
        g.BorderStyle = BorderStyle.None;
        g.EnableHeadersVisualStyles = false;
        g.GridColor = Line;
        g.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        g.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
        // And the row headers, which default to a raised 3D box: on a dark ground the
        // shadow half of that box is black, so every cell in the gutter came out in a
        // black frame with the verdict dot sitting inside it.
        g.RowHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
        g.ColumnHeadersHeight = 34;
        g.ColumnHeadersDefaultCellStyle.BackColor = Surface;
        g.ColumnHeadersDefaultCellStyle.ForeColor = Muted;
        // The sorted column's header is "selected", and left alone it takes the system
        // highlight: a solid block behind grey text that could not be read at all.
        g.ColumnHeadersDefaultCellStyle.SelectionBackColor = Surface;
        g.ColumnHeadersDefaultCellStyle.SelectionForeColor = Accent;
        g.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 8.5F, FontStyle.Bold);
        g.ColumnHeadersDefaultCellStyle.Padding = new Padding(6, 0, 6, 0);
        g.DefaultCellStyle.BackColor = Surface;
        g.DefaultCellStyle.ForeColor = Ink;
        g.DefaultCellStyle.SelectionBackColor = AccentSoft;
        g.DefaultCellStyle.SelectionForeColor = Ink;
        g.DefaultCellStyle.Padding = new Padding(6, 0, 6, 0);
        g.RowTemplate.Height = 30;
        g.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(34, 38, 44);
        // Row height carries no information, so dragging it about is only a way to
        // make the list worse by accident.
        g.AllowUserToResizeRows = false;
        // Windows outlines whichever cell the keyboard would type into, and on a dark
        // row that outline is a black box around one cell of a selected row. It moves
        // as the driver clicks, disappears when the grid loses the focus and comes
        // back when it gets it, which is exactly the "black frames that sometimes go
        // away" it looked like. The whole row is what gets selected here, so the
        // outline says nothing the highlight has not already said, and it is dropped
        // from the painting. Idempotent, so styling a grid twice is harmless.
        g.RowPrePaint += (_, e) => {
            // Windows outlines whichever cell the keyboard would type into, and on a
            // dark row that is a black box around one cell of a selected row. The whole
            // row is what gets selected here, so the outline says nothing the highlight
            // has not already said.
            e.PaintParts &= ~DataGridViewPaintParts.Focus;

            // The row lays its own ground across its whole width before its cells are
            // painted. A table leaves a pixel unpainted where two cells meet, and
            // whatever was on the screen before survives in it: switching to this page
            // from another left slivers of the other page's numbers standing in the
            // gaps between columns.
            var selected = (e.State & DataGridViewElementStates.Selected) != 0;
            var style = e.InheritedRowStyle;
            using var ground = new SolidBrush(selected ? style.SelectionBackColor : style.BackColor);
            e.Graphics.FillRectangle(ground, e.RowBounds);
        };

        // The row header is left alone here: on the history list it is the gutter an
        // oversize load is marked in, and this runs after that is set up.
    }

    /// <summary>
    /// Every ordinary button in the window: control tone, a hairline edge, one word.
    ///
    /// The same height, the same tone and the same ink as the search field, the
    /// segmented switch and the chips beside them, so a toolbar reads as one row of
    /// controls rather than as four things that happen to be near each other.
    /// </summary>
    private static Button MakeButton(string text, Action onClick) {
        var b = new Button {
            Text = text, AutoSize = true, Height = Look.InputHeight, Margin = new Padding(0, 2, 8, 2),
            Padding = new Padding(12, 0, 12, 0), Font = Look.Small,
            FlatStyle = FlatStyle.Flat, BackColor = Look.Control, ForeColor = Look.Ink, Cursor = Cursors.Hand,
            TabStop = false,
        };
        b.FlatAppearance.BorderColor = Look.Border;
        b.FlatAppearance.MouseOverBackColor = Look.ControlHover;
        b.FlatAppearance.MouseDownBackColor = Look.ControlHover;
        b.Click += (_, _) => onClick();
        return b;
    }

    /// <summary>Windows paints the title bar itself and defaults it to light, which
    /// leaves a white cap on a dark window. Available from Windows 10 20H1 on; older
    /// builds simply ignore the call.</summary>
    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    private void UseDarkTitleBar() => UseDarkTitleBar(this);

    private static void UseDarkTitleBar(Form form) {
        try {
            var on = 1;
            DwmSetWindowAttribute(form.Handle, 20, ref on, sizeof(int));
        } catch { /* an older Windows just keeps the light title bar */ }
    }

    // Scrollbars are drawn by the system, not by the control, so no amount of
    // BackColor reaches them and a dark window keeps bright white bars down its
    // side. Windows does have a dark set, reachable only through these two: an
    // undocumented uxtheme export to put the process in dark mode, and then asking
    // each scrolling control for the dark variant of the Explorer theme.
    [System.Runtime.InteropServices.DllImport("uxtheme.dll", EntryPoint = "#135", SetLastError = true)]
    private static extern int SetPreferredAppMode(int mode);

    [System.Runtime.InteropServices.DllImport("uxtheme.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int SetWindowTheme(IntPtr hwnd, string subAppName, string? subIdList);

    internal static void UseDarkAppMode() {
        try { SetPreferredAppMode(2); } catch { /* older Windows has no dark mode to ask for */ }
    }

    /// <summary>Applies the dark scrollbars to a control and everything inside it.
    /// Handles have to exist first, so this runs once the window is up.</summary>
    private static void UseDarkScrollbars(Control root) {
        try {
            if (root.IsHandleCreated) SetWindowTheme(root.Handle, "DarkMode_Explorer", null);
        } catch { /* not worth failing over a scrollbar */ }

        foreach (Control child in root.Controls) UseDarkScrollbars(child);
    }

    /// <summary>Statistics as a wall of tiles rather than a block of monospaced text.
    /// Each figure gets its own card, so the eye can land on one number instead of
    /// reading a column of them.</summary>
    /// <summary>Everything on one screen, no scrolling. A table rather than a flow:
    /// the rows share out whatever height there is, so the whole set of figures stays
    /// visible at any window size instead of the last ones falling off the bottom.</summary>
    /// <summary>Which stretch of time the statistics are about. Kept as a name
    /// rather than as two timestamps, because "this week" has to mean this week
    /// whenever the page is redrawn, not the week it was when it was chosen.</summary>
    private string _statsPeriod = "all";
    private readonly TriSwitch _statsGame =
        new(GameName("Ets2"), Strings.T("filter.both"), GameName("Ats"));
    private readonly FlowLayoutPanel _statsPeriods = new();

    private Panel BuildStatsPage() {
        var page = new Panel { Dock = DockStyle.Fill, BackColor = Look.Window,
                               Padding = new Padding(Look.PagePad, 10, Look.PagePad, Look.PagePad) };

        var head = new Panel { Dock = DockStyle.Top, Height = 34, BackColor = Look.Window };
        head.Paint += (_, e) => {
            var g = e.Graphics;
            g.Clear(Look.Window);
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            Look.Text(g, Strings.T("tab.stats"), Look.PageHeading, Look.Ink, 0, 6);
        };

        // Two questions asked above the figures rather than answered by extra
        // sections below them: which stretch of time, and which game. Sections would
        // have taken the page past the one screen it fits on, and comparing this week
        // with last week by reading two blocks of tiles is not comparing at all.
        // Not docked: it rides at the right end of the header's own line, so the page
        // name and the two questions asked about it share one row.
        var bar = new FlowLayoutPanel {
            AutoSize = true, WrapContents = false, BackColor = Look.Window,
            FlowDirection = FlowDirection.RightToLeft,
        };

        _statsPeriods.AutoSize = true;
        _statsPeriods.WrapContents = false;
        _statsPeriods.Margin = new Padding(0, 0, 16, 0);
        _statsPeriods.Controls.Clear();
        foreach (var period in new[] { "all", "month", "week" }) {
            var pick = MakeButton(Strings.T("stats.period." + period), () => {
                if (_statsPeriod == period) return;
                _statsPeriod = period;
                StylePeriods();
                ReloadStats();
            });
            pick.Tag = period;
            _statsPeriods.Controls.Add(pick);
        }

        _statsGame.Retext(GameName("Ets2"), Strings.T("filter.both"), GameName("Ats"));
        _statsGame.Margin = new Padding(0, 3, 0, 3);
        _statsGame.BackColor = Canvas;
        _statsGame.Changed -= OnStatsGameChanged;
        _statsGame.Changed += OnStatsGameChanged;

        bar.Controls.Add(_statsGame);
        bar.Controls.Add(_statsPeriods);

        _statsGrid.Dock = DockStyle.Fill;
        _statsGrid.BackColor = Canvas;
        // One column of sections, each section holding its own row of tiles. The
        // outer grid used to be four columns wide and a section was laid straight
        // into it, which meant adding a fifth figure to any section silently pushed
        // the next heading out of its cell and shifted every tile below it along by
        // one. A section now owns its own width and can hold as many as it likes.
        _statsGrid.ColumnCount = 1;
        // Eight for the four sections, and a ninth that exists only to take whatever
        // height is left: without it the last row of tiles inherits the slack and ends
        // up three times the height of the ones above it.
        _statsGrid.RowCount = 9;
        _statsGrid.Padding = new Padding(0);

        _statsGrid.ColumnStyles.Clear();
        _statsGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

        // Four sections, each a heading of its own height above a row of tiles as tall
        // as the three lines inside it. Sharing the height out instead left every tile
        // with an inch of empty floor under its figure, which on a tall window made
        // the page look like four half filled boxes.
        _statsGrid.RowStyles.Clear();
        for (var i = 0; i < 4; i++) {
            _statsGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
            _statsGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, TileHeight));
        }
        _statsGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        page.Controls.Add(_statsGrid);
        page.Controls.Add(head);
        head.Controls.Add(bar);
        void Place() => bar.Location = new Point(Math.Max(0, head.ClientSize.Width - bar.Width), 0);
        head.Resize += (_, _) => Place();
        Place();
        StylePeriods();
        return page;
    }

    private void OnStatsGameChanged(object? sender, EventArgs e) => ReloadStats();

    /// <summary>The chosen period, lit the way a chosen page in the sidebar is.</summary>
    private void StylePeriods() {
        foreach (var control in _statsPeriods.Controls) {
            if (control is not Button b || b.Tag is not string period) continue;
            var chosen = period == _statsPeriod;
            b.BackColor = chosen ? AccentSoft : Raised;
            b.ForeColor = chosen ? Accent : Ink;
        }
    }

    /// <summary>
    /// The stretch of time the page is about, worked out fresh each time it is
    /// drawn. A week runs from Monday and a month from the first, so "this week"
    /// means the week you are in rather than the last seven days: comparing a
    /// rolling window against the one before it compares two overlapping halves of
    /// the same evening's driving.
    /// </summary>
    private HistorySlice StatsSlice() {
        var game = _statsGame.Position switch { < 0 => "Ets2", > 0 => "Ats", _ => null };
        var now = DateTime.Now;
        DateTime? from = _statsPeriod switch {
            "week" => now.Date.AddDays(-(((int)now.DayOfWeek + 6) % 7)),
            "month" => new DateTime(now.Year, now.Month, 1),
            _ => null,
        };
        if (from is not { } start) return new HistorySlice(null, null, game);

        var fromMs = new DateTimeOffset(start).ToUnixTimeMilliseconds();
        var toMs = _statsPeriod == "week"
            ? new DateTimeOffset(start.AddDays(7)).ToUnixTimeMilliseconds()
            : new DateTimeOffset(start.AddMonths(1)).ToUnixTimeMilliseconds();
        return new HistorySlice(fromMs, toMs, game);
    }

    /// <summary>
    /// One figure on the statistics page: a small capital label, the figure itself, and
    /// a dim line under it saying what it is measured against.
    ///
    /// Painted rather than stacked out of three labels. A label cannot letter-space its
    /// capitals, cannot align its baseline with the figure beneath it, and brings a
    /// layout panel with it to hold the three of them apart.
    /// </summary>
    private static Control StatTile(string caption, string value, string? note = null) {
        var tile = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 12, 12), BackColor = Look.Panel };
        tile.Paint += (_, e) => {
            var g = e.Graphics;
            g.Clear(Look.Window);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            Look.Surface(g, new RectangleF(0, 0, tile.Width, tile.Height), Look.Panel, Look.Hairline);

            var room = tile.Width - 32;
            Look.Tracked(g, caption.ToUpperInvariant(), Look.Label, Look.Dim, 16, 13);
            // A figure is stepped down the scale before it is cut short. Two currencies
            // side by side, or an hour count with its unit spelled out, is a true figure
            // that happens to be long, and a clipped one answers nothing.
            var font = Look.FigureLarge;
            foreach (var step in new[] { 23f, 20f, 17.5f, 15.5f }) {
                font = step >= 23f ? Look.FigureLarge : Look.Semi(step);
                if (Look.Measure(g, value, font).Width <= room) break;
            }
            Look.Text(g, Look.Clip(g, value, font, room), font, Look.Ink, 16, 30 + (23 - font.Size) / 2);
            if (!string.IsNullOrEmpty(note)) {
                Look.Text(g, Look.Clip(g, note, Look.Caption, room), Look.Caption, Look.Dim, 16, 60);
            }
        };
        return tile;
    }

    /// <summary>
    /// Sizes a section's figures together: the largest type that fits all of them.
    ///
    /// One at a time was the obvious thing and read badly. "Cars" would sit at full
    /// size beside a shrunken "Yuma to Tucson", so a row of equal figures came out
    /// looking like a ransom note. Deciding once for the row keeps them equal and
    /// still lets a long one force the whole row down rather than be cut off.
    ///
    /// Re-measured whenever the row changes width, so it holds at any window size.
    /// </summary>
    private static void FitTogether(Control host, List<Label> labels, float largest, float smallest) {
        if (labels.Count == 0) return;
        var family = labels[0].Font.FontFamily;

        void Refit() {
            var measurable = labels.Where(l => l.Parent is { Width: > 24 } && l.Text.Length > 0).ToList();
            if (measurable.Count == 0) return;

            var chosen = smallest;
            for (var size = largest; size >= smallest; size -= 0.5F) {
                using var probe = new Font(family, size, FontStyle.Bold);
                // Against the tile's inside width rather than the label's own, which
                // is still whatever the last font made it while auto-sizing.
                var fits = measurable.All(l =>
                    TextRenderer.MeasureText(l.Text, probe).Width <= l.Parent!.Width - l.Parent.Padding.Horizontal - 4);
                if (fits) { chosen = size; break; }
            }

            foreach (var label in labels) {
                if (Math.Abs(label.Font.Size - chosen) < 0.01F) continue;
                var previous = label.Font;
                label.Font = new Font(family, chosen, FontStyle.Bold);
                previous.Dispose();
            }
        }

        host.Resize += (_, _) => Refit();
        host.HandleCreated += (_, _) => Refit();
    }

    /// <summary>One section's figures, sharing the width equally however many there
    /// are. Equal shares rather than a fixed four, so a section can grow a figure
    /// without the ones beside it being squeezed out of the page.</summary>
    /// <summary>How tall a tile is: a caption, a figure and a note, and the padding
    /// around them.</summary>
    private const float TileHeight = 112F;

    /// <summary>
    /// A strip of figures across the page.
    ///
    /// Every strip is laid out on the same number of columns whatever it holds, so a
    /// tile in a group of three is exactly as wide as a tile in a group of five and the
    /// eye reads down the page as well as across it.
    /// </summary>
    private const int StatColumns = 5;

    private static Control TileRow(Control[] tiles) {
        var row = new TableLayoutPanel {
            Dock = DockStyle.Fill, BackColor = Look.Window,
            Margin = new Padding(0), Padding = new Padding(0),
            ColumnCount = StatColumns, RowCount = 1,
        };
        row.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        for (var i = 0; i < StatColumns; i++) {
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F / StatColumns));
        }
        for (var i = 0; i < tiles.Length && i < StatColumns; i++) row.Controls.Add(tiles[i], i, 0);
        return row;
    }

    /// <summary>A group opens with its name, a dim note, and a hairline filling the
    /// rest of the line.</summary>
    private static Control StatHeading(string text, string note = "") {
        var head = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0, 6, 0, 8), BackColor = Look.Window };
        head.Paint += (_, e) => {
            var g = e.Graphics;
            g.Clear(Look.Window);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            var y = head.Height - 20;
            Look.Text(g, text, Look.Strong, Look.Ink, 0, y);
            var at = Look.Measure(g, text, Look.Strong).Width + 12;
            if (note.Length > 0) {
                Look.Text(g, note, Look.Caption, Look.Dim, at, y + 2);
                at += Look.Measure(g, note, Look.Caption).Width + 12;
            }
            using var rule = new Pen(Look.Hairline);
            g.DrawLine(rule, at, y + 8, head.Width, y + 8);
        };
        return head;
    }

    // ---------- delivery detail ----------

    /// <summary>One delivery on a card of its own. The list carries what is worth
    /// scanning down a column; everything else about a drive lives here, where it has
    /// room to be read rather than squeezed into another column.</summary>
    private void ShowDetail(long id) {
        var d = _store.Detail(id);
        if (d == null) return;
        _detailId = id;
        var u = Units.For(_settings.Units, d.Game);

        Quiet(_detailPage, () => {
            _detailPage.Controls.Clear();

            // Added bottom-up, since docked children stack in reverse.
            _detailPage.Controls.Add(DetailBody(d, u));
            _detailPage.Controls.Add(DetailHeader(d, u));
        });
        ShowPage("detail");
        // Built just now, so its scrolling parts have not been asked for the dark
        // theme yet and would come up as bright white bars, none of them has been told
        // to paint into a buffer, and none of them is on the type scale.
        UseDarkScrollbars(_detailPage);
        SmoothPainting(_detailPage);
        Retype(_detailPage);
    }

    /// <summary>A city as this driver has asked to see it: with the state or the
    /// country it is in, or as the game names it.</summary>
    private string Where(DeliveryDetail d, string city, string cityId) =>
        _settings.CityRegions ? Places.Say(d.Game, city, cityId) : city;

    private Control DetailHeader(DeliveryDetail d, Units u) {
        var head = new Panel { Dock = DockStyle.Top, Height = 108, BackColor = Surface, Padding = new Padding(24, 16, 24, 12) };



        // Quiet and small. These are ways out of the card, not the point of it, and
        // docking them filled the header's whole height with two slabs.
        Button Action(string text, int width) {
            var b = new Button {
                Text = text, Width = width, Height = Look.InputHeight, AutoSize = false,
                FlatStyle = FlatStyle.Flat, BackColor = Look.Control, ForeColor = Look.Ink,
                Font = Look.Small, Cursor = Cursors.Hand, TabStop = false,
                Margin = new Padding(8, 0, 0, 0), TextAlign = ContentAlignment.MiddleCenter,
            };
            b.FlatAppearance.BorderColor = Look.Border;
            b.FlatAppearance.MouseOverBackColor = Look.ControlHover;
            b.FlatAppearance.MouseDownBackColor = Look.ControlHover;
            return b;
        }

        var back = Action(Strings.T("detail.back"), 90);
        back.Click += (_, _) => ShowPage("deliveries");

        var timelineButton = Action(Strings.T("detail.timelineOpen") + "  ▸", 210);
        timelineButton.Click += (_, _) => ToggleTimeline(timelineButton);

        var sheetButton = Action(Strings.T("detail.saveSheet"), 110);
        sheetButton.Click += (_, _) => SaveSheet(d, u);

        // Right to left, so the first one added sits furthest right.
        var actions = new FlowLayoutPanel {
            Dock = DockStyle.Right, FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false, AutoSize = true, Padding = new Padding(0, 2, 0, 0),
        };
        actions.Controls.Add(back);
        actions.Controls.Add(sheetButton);
        actions.Controls.Add(timelineButton);

        var route = new Label {
            Dock = DockStyle.Top, Height = 36,
            Text = $"{Where(d, d.SourceCity, d.SourceCityId)}  →  {Where(d, d.DestinationCity, d.DestinationCityId)}",
            ForeColor = Ink, Font = new Font("Segoe UI", 16F, FontStyle.Bold),
        };
        var sub = new Label {
            Dock = DockStyle.Top, Height = 22, ForeColor = Muted,
            Text = $"{d.Cargo} · {u.MassTonnes(d.CargoMassKg):0.0} {u.MassUnit} · {d.Truck}"
                 + (d.Trailer.Length > 0 ? $" · {d.Trailer}" : ""),
        };
        // Date, game, hours, verdict. The game belongs up here beside the date rather
        // than buried among the cargo details: it is the first thing that frames
        // everything else on the card, including which units the figures are in.
        var when = new FlowLayoutPanel {
            Dock = DockStyle.Top, Height = 22, FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false, Margin = new Padding(0), Padding = new Padding(0),
        };
        // Anchored to the bottom of the row rather than the top, so the larger game
        // name sits on the same line as the rest instead of riding above it. Every
        // gap is the same eight pixels; the separators live inside the text, so the
        // spacing does not double up where one label ends and the next begins.
        Label Part(string text, Color colour, float size, FontStyle style) => new() {
            Text = text, ForeColor = colour, AutoSize = true, Anchor = AnchorStyles.Bottom,
            Font = new Font("Segoe UI", size, style), Margin = new Padding(0, 0, 8, 0),
        };
        when.Controls.Add(Part($"{d.StartedAt:dd.MM.yyyy}", Muted, 8.5F, FontStyle.Regular));
        when.Controls.Add(Part(GameName(d.Game), Accent, 10F, FontStyle.Bold));
        when.Controls.Add(Part(
            $"{d.StartedAt:HH:mm} → {d.FinishedAt:HH:mm}  ·  {Label(d.Outcome)}  ·  {Label(d.Status)}",
            Muted, 8.5F, FontStyle.Regular));

        head.Controls.Add(when);
        head.Controls.Add(sub);
        head.Controls.Add(route);
        head.Controls.Add(actions);

        // An oversize load says so before anything else does, in the markings it
        // actually carries rather than in another word among the figures. Added last
        // so it docks outermost and owns the whole left edge; added earlier it would
        // have been squeezed inside whatever the rest left over.
        if (Tracking.Trucks.IsElectric(d.TruckId, d.Truck)) {
            var mark = new Panel { Dock = DockStyle.Left, Width = 22, BackColor = Surface };
            mark.Paint += (_, e) => Bolt(e.Graphics, new RectangleF(6, mark.Height / 2f - 9, 11, 18));
            _tips.SetToolTip(mark, Strings.T("detail.electric"));
            head.Controls.Add(mark);
            head.Padding = new Padding(14, 16, 24, 12);
        }

        if (d.SpecialTransport) {
            var stripe = new Panel { Dock = DockStyle.Left, Width = 10, BackColor = Surface };
            stripe.Paint += (_, e) => HazardStripes(e.Graphics, new RectangleF(0, 0, stripe.Width, stripe.Height), 210);
            _tips.SetToolTip(stripe, Strings.T("detail.special"));
            head.Controls.Add(stripe);
            head.Padding = new Padding(14, 16, 24, 12);
        }
        return head;
    }

    /// <summary>
    /// The handle down the left edge of the timeline column.
    ///
    /// How much of the card goes to the figures and how much to the log is not a
    /// question with one answer. A drive being picked apart wants the log wide and
    /// the map with it; a delivery being glanced at wants the figures. Four pixels
    /// of grabbable edge settles it per delivery instead of the layout deciding
    /// once for everybody.
    ///
    /// Both ends are held: the column may not close itself by being dragged shut,
    /// which would leave the button saying the opposite of what the card is doing,
    /// and it may not eat the figures it sits beside.
    /// </summary>
    /// <summary>How wide the log column is allowed to be: wide enough to be worth
    /// opening, and never so wide that the figures beside it have nowhere to go.
    /// The lower limit wins on a card too narrow for both, since a handle that has
    /// been pushed off the edge cannot be pulled back.</summary>
    private static int HeldWidth(int wanted, Control body) =>
        Math.Clamp(wanted, 320, Math.Max(320, body.Width - 360));

    /// <summary>The same for the drawings against the log under them. What the log
    /// keeps is two entries and its heading: the point of pulling this down is to see
    /// the map, and a column that insists on holding four rows of a list nobody is
    /// reading gives away most of what a short window had to offer.</summary>
    private static int HeldHeight(int wanted, Control side) =>
        Math.Clamp(wanted, 200, Math.Max(200, side.Height - 120));

    private Control TimelineGrip(Panel side, Panel body) {
        var grip = new Panel {
            Dock = DockStyle.Right, Width = 5, BackColor = Canvas,
            Cursor = Cursors.SizeWE, Visible = false,
        };
        _detailGrip = grip;

        grip.Paint += (_, e) => {
            // A short bar rather than a full height rule: it says "hold here", where
            // a line down the whole card reads as a border and nobody pulls a border.
            using var pen = new Pen(Line, 1f);
            var mid = grip.Height / 2;
            for (var y = mid - 14; y <= mid + 14; y += 4) e.Graphics.DrawLine(pen, 1, y, 3, y);
        };

        var pulling = false;
        var from = 0;
        var started = 0;

        grip.MouseDown += (_, e) => {
            if (e.Button != MouseButtons.Left) return;
            pulling = true;
            // Held explicitly, so a pull that runs off the edge of five pixels of
            // handle keeps arriving here instead of stopping halfway.
            grip.Capture = true;
            // Screen coordinates, not the handle's own: the handle moves as the
            // column grows, so a position measured inside it chases itself.
            from = Cursor.Position.X;
            started = side.Width;
        };
        grip.MouseMove += (_, _) => {
            if (!pulling) return;
            // Leftwards is wider, which is the direction the column grows from.
            side.Width = HeldWidth(started + (from - Cursor.Position.X), body);
        };
        grip.MouseUp += (_, _) => {
            if (!pulling) return;
            pulling = false;
            _timelineWidth = side.Width;
            // The drawings inside are laid out for the width they were built at, so
            // they are drawn again now the pulling has stopped rather than on every
            // pixel of it.
            if (_cardMap is { IsDisposed: false } shown) shown.Replay();
            if (_cardProfile is { IsDisposed: false } beside) beside.Replay();
        };

        return grip;
    }

    /// <summary>
    /// The handle across the column, between the drawings and the log.
    ///
    /// The column opens with the map and the profile above and the log below, and
    /// which of them deserves the room is the same question as before, asked the
    /// other way round. A drive with two events in it wants the map; a drive being
    /// read event by event wants the list. Pulling this down gives the map the room
    /// the log is not using.
    /// </summary>
    private Control RouteGrip(Panel side, Control drawings) {
        var grip = new Panel {
            Dock = DockStyle.Top, Height = 5, BackColor = Canvas, Cursor = Cursors.SizeNS,
        };

        grip.Paint += (_, e) => {
            using var pen = new Pen(Line, 1f);
            var mid = grip.Width / 2;
            for (var x = mid - 14; x <= mid + 14; x += 4) e.Graphics.DrawLine(pen, x, 1, x, 3);
        };

        var pulling = false;
        var from = 0;
        var started = 0;

        grip.MouseDown += (_, e) => {
            if (e.Button != MouseButtons.Left) return;
            pulling = true;
            grip.Capture = true;
            from = Cursor.Position.Y;
            started = drawings.Height;
        };
        grip.MouseMove += (_, _) => {
            if (!pulling) return;
            drawings.Height = HeldHeight(started + (Cursor.Position.Y - from), side);
        };
        grip.MouseUp += (_, _) => {
            if (!pulling) return;
            pulling = false;
            _routeHeight = drawings.Height;
            if (_cardMap is { IsDisposed: false } shown) shown.Replay();
            if (_cardProfile is { IsDisposed: false } beside) beside.Replay();
        };

        return grip;
    }

    /// <summary>Slides the timeline in and out rather than showing or hiding it: the
    /// movement is what says where the panel came from, and a column that simply
    /// appears reads as the page having jumped.</summary>
    private void ToggleTimeline(Button button) {
        if (_detailSide is not { } side) return;

        var target = side.Width > 0 ? 0 : HeldWidth(_timelineWidth, side.Parent ?? side);
        button.Text = Strings.T("detail.timelineOpen") + (target > 0 ? "   ▸" : "   ◂");
        // The handle appears with the column and goes with it, so a shut column
        // leaves no stray edge to grab at the side of the card.
        if (_detailGrip is { IsDisposed: false } grip) grip.Visible = target > 0;

        _detailSlide?.Stop();
        _detailSlide?.Dispose();
        _detailSlide = new System.Windows.Forms.Timer { Interval = 15 };
        _detailSlide.Tick += (_, _) => {
            // A fixed fraction of what is left, so it eases out instead of arriving
            // at full speed, with a floor so the last pixels do not crawl.
            var left = target - side.Width;
            var step = Math.Max(6, Math.Abs(left) / 3);
            side.Width = Math.Abs(left) <= step ? target : side.Width + Math.Sign(left) * step;
            if (side.Width == target) {
                _detailSlide!.Stop();
                if (target > 0) {
                    UseDarkScrollbars(side);
                    // The card is rebuilt whenever another delivery is opened, so
                    // the drawings held here can be ones that no longer exist.
                    if (_cardMap is { IsDisposed: false } shown) shown.Replay();
                    if (_cardProfile is { IsDisposed: false } beside) beside.Replay();
                }
            }
        };
        _detailSlide.Start();
    }

    /// <summary>One fact as a line: the name of it held to the left, the value beside
    /// it. Reads down a column, which a row of cards does not.</summary>
    private static Control InfoRow(string label, string value, bool shaded = false) {
        var row = new Panel {
            Dock = DockStyle.Top, Height = 30, BackColor = shaded ? Raised : Surface,
            Padding = new Padding(16, 6, 16, 6),
        };
        row.Controls.Add(new Label {
            Dock = DockStyle.Fill, Text = value, ForeColor = Ink,
            TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true,
        });
        row.Controls.Add(new Label {
            Dock = DockStyle.Left, Width = 200, Text = label, ForeColor = Muted,
            TextAlign = ContentAlignment.MiddleLeft,
        });
        return row;
    }

    private static Control CardHeading(string text) => new Label {
        Dock = DockStyle.Top, Height = 34, Text = text.ToUpperInvariant(), BackColor = Surface,
        ForeColor = Muted, Font = new Font("Segoe UI", 8F, FontStyle.Bold),
        Padding = new Padding(16, 10, 0, 0),
    };

    /// <summary>
    /// Writes the delivery out as the document the app is named after.
    ///
    /// The paper treatment lives here and only here. On screen it fought the screen,
    /// where boxes need room the window has not got, a fixed sheet cannot hold seven
    /// trailer units, and a drawing cannot be zoomed or clicked. A file has none of
    /// those problems, and a delivery you can keep and show is what the whole
    /// project is about.
    /// </summary>
    private void SaveSheet(DeliveryDetail d, Units u) {
        // Somewhere a saved delivery can be found again without being looked for.
        // The app's own folder is where its data lives, which is the wrong answer for
        // something made to be kept and shown.
        var home = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Waybill");
        try { Directory.CreateDirectory(home); } catch { home = ""; }

        using var dialog = new SaveFileDialog {
            Title = Strings.T("sheet.saveTitle"),
            // Two sheets are two pictures or one document, and which of those is
            // wanted depends on whether it is being posted or filed. The name is
            // suggested without an ending so that the choice puts one on it.
            Filter = "PNG (*.png)|*.png|PDF (*.pdf)|*.pdf",
            DefaultExt = "png",
            AddExtension = true,
            InitialDirectory = home,
            FileName = WaybillSheet.SuggestedName(d),
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        try {
            // The whole atlas rather than this one route: the sheet draws the roads
            // already driven behind the delivery, and names the towns from it.
            var atlas = RoutesFor(d.Game);
            var route = atlas.Routes.TryGetValue(d.Id, out var points) ? points : new List<RoutePoint>();
            // The document is written in the game's own units, whatever the window is
            // set to, so the timeline figures on it are read in those as well.
            var paper = WaybillSheet.UnitsFor(d.Game);
            var written = WaybillSheet.Save(d, _store.TimelineRows(d.Id, paper, Tracking.Trucks.IsElectric(d.TruckId, d.Truck)),
                                            route, paper, dialog.FileName, 300f, atlas);
            MessageBox.Show(this,
                Strings.T("sheet.saved") + "\n" + string.Join("\n", written),
                Strings.T("detail.saveSheet"), MessageBoxButtons.OK, MessageBoxIcon.Information);
        } catch (Exception ex) {
            MessageBox.Show(this, ex.Message, Strings.T("detail.saveSheet"),
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>
    /// Where the drive went, drawn on top of everywhere else this profile has ever
    /// been.
    ///
    /// The background is the driver's own history and nothing else. There is no
    /// real map under it because the game's world is not a scaled country: the same
    /// nineteen deliveries put some pairs of cities thirteen times closer than
    /// reality and others thirty, so no projection fits, and the closest one misses
    /// by about thirty kilometres. Rather than draw a border in the wrong place,
    /// the routes already driven are the map.
    /// </summary>
    private Control RoutePanel(DeliveryDetail d, Units u) {
        var box = new Panel {
            Dock = DockStyle.Top, Height = 396, BackColor = Line,
            Padding = new Padding(0, 0, 0, 1),
        };

        var map = NewMap(u);
        map.GameMap = MapForDelivery(d);
        map.Show(Layers(RoutesFor(d.Game)), d.Id, RoutesFor(d.Game).Cities, _store.TimelineRows(d.Id, u));

        // Drawn out once, when the panel it sits in is actually on the screen.
        // Watching the line grow says the order things happened in, which a finished
        // line cannot: the same picture with a collision near the end reads quite
        // differently from one with a collision on the way out.
        //
        // Held here rather than started here, because the panel is built while it is
        // still slid shut. A replay nobody can see is a replay wasted.
        _cardMap = map;

        // The same drive from the side, under the map. Both are drawn out together
        // and at the same rate, so at any moment of the replay the head of the line
        // and the head of the profile are the same second of the drive.
        var profile = new HeightView {
            Dock = DockStyle.Bottom, Height = 96,
            FormatSpeed = kmh => u.FormatSpeed(kmh),
            EmptyText = Strings.T("height.none"),
            Hint = Strings.T("height.hint"),
        };
        profile.Show(_store.HeightsFor(d.Id));
        _cardProfile = profile;

        // Pointing at either drawing marks the same moment in the other. They agree
        // by the clock rather than by counting points, since the profile averages its
        // readings down to one a pixel and the map keeps every one of them.
        map.Hovering += profile.MarkAt;
        profile.Hovering += map.MarkAt;

        var divider = new Panel { Dock = DockStyle.Bottom, Height = 1, BackColor = Line };

        box.Controls.Add(map);
        box.Controls.Add(divider);
        box.Controls.Add(profile);
        MapButtons(box, map, () => BigMap(d, u), replay: true, about: d);
        return box;
    }

    /// <summary>Gives a control a rounded corner by cutting its own shape out of it.
    /// The only way a control that paints its whole surface can have one.</summary>
    private static void RoundOff(Control control, float radius) {
        if (control.Width <= 0 || control.Height <= 0) return;
        using var path = Look.Rounded(new RectangleF(0, 0, control.Width, control.Height), radius);
        control.Region = new Region(path);
    }

    private RouteView NewMap(Units u) => new() {
        Dock = DockStyle.Fill,
        FormatSpeed = kmh => u.FormatSpeed(kmh),
        EmptyText = Strings.T("map.none"),
        Hint = Strings.T("map.hint"),
    };

    private static IEnumerable<RouteLayer> Layers(GameRoutes routes) =>
        routes.Routes.Select(r => new RouteLayer { Id = r.Key, Points = r.Value });

    /// <summary>
    /// The three things the map can be told to do, over the top right of it.
    ///
    /// Kept as small square glyphs rather than words: they sit on the drawing, and
    /// a row of labelled buttons there would take more of the map than the map can
    /// spare. Placed by hand on every resize because the map underneath them is
    /// docked to fill, so there is no layout to anchor to.
    /// </summary>
    private void MapButtons(Control host, RouteView map, Action? expand, bool replay = false,
                            DeliveryDetail? about = null, bool layers = true, bool fit = true) {
        var bar = new Panel { BackColor = Color.Transparent, Height = 26, Width = 0 };

        Button Glyph(string text, string tip, Action click) {
            var b = new Button {
                Text = text, Width = 26, Height = 26, Left = bar.Width, Top = 0,
                FlatStyle = FlatStyle.Flat, BackColor = Surface, ForeColor = Muted,
                Font = new Font("Segoe UI", 10F), Cursor = Cursors.Hand, TabStop = false,
            };
            b.FlatAppearance.BorderColor = Line;
            b.FlatAppearance.MouseOverBackColor = Raised;
            b.Click += (_, _) => click();
            _tips.SetToolTip(b, tip);
            bar.Controls.Add(b);
            bar.Width += 30;
            return b;
        }

        if (layers) {
            var sheet = Glyph("≡", Strings.T("map.layers"), () => { });
            sheet.Click += (_, _) => LayerMenu(map, about).Show(sheet, new Point(0, sheet.Height));
        }
        // Only where one delivery is singled out: there is nothing to replay on the
        // map of everything, where no drive is more the subject than any other.
        if (replay) {
            Glyph("▶", Strings.T("map.replay"), () => {
                map.Replay();
                if (_cardProfile is { IsDisposed: false } beside) beside.Replay();
            });
        }
        if (fit) Glyph("⟲", Strings.T("map.fit"), map.Fit);
        if (expand is not null) Glyph("⤢", Strings.T("map.expand"), expand);

        void Place() => bar.Left = Math.Max(0, host.ClientSize.Width - bar.Width - 8);
        bar.Top = 8;
        host.Controls.Add(bar);
        bar.BringToFront();
        host.Resize += (_, _) => Place();
        Place();
    }

    /// <summary>What the map draws, as three switches. Anything the delivery does
    /// not have is simply not offered: a filter for something there is none of is
    /// noise on a menu that has to stay glanceable.</summary>
    /// <summary>The stock menu renderer paints a light gutter beside the icons and a
    /// bright blue highlight, both of which arrive from the system theme and land on
    /// a dark window looking like somebody else's menu. Only the few colours that
    /// show are overridden.</summary>
    /// <summary>
    /// The dark colours the menus are drawn in.
    ///
    /// Every surface a menu can paint has to be named here, because the ones left
    /// out fall back to the system's light theme. Opening a top level menu used to
    /// turn its own name into a pale block: a menu whose drop-down is open is drawn
    /// "pressed", and the pressed colours were the three that had been forgotten.
    /// </summary>
    private sealed class DarkMenuColours : ProfessionalColorTable {
        public override Color ToolStripDropDownBackground => Surface;
        public override Color ImageMarginGradientBegin => Surface;
        public override Color ImageMarginGradientMiddle => Surface;
        public override Color ImageMarginGradientEnd => Surface;
        public override Color MenuStripGradientBegin => Surface;
        public override Color MenuStripGradientEnd => Surface;
        public override Color MenuItemSelected => Raised;
        public override Color MenuItemSelectedGradientBegin => Raised;
        public override Color MenuItemSelectedGradientEnd => Raised;
        // The open one, which is what "pressed" means for a menu.
        public override Color MenuItemPressedGradientBegin => Raised;
        public override Color MenuItemPressedGradientMiddle => Raised;
        public override Color MenuItemPressedGradientEnd => Raised;
        public override Color MenuItemBorder => Line;
        public override Color MenuBorder => Line;
        public override Color SeparatorDark => Line;
        public override Color SeparatorLight => Line;
    }

    private static Image? _eyeOpen, _eyeShut;

    /// <summary>
    /// What every mark in the window means, in one place.
    ///
    /// Almost nothing here is text on the screen: the timeline marks, the hazard
    /// stripes, the colour of a route and the two shades in the progress bar all say
    /// something, and none of them says it in words. A drawing that has to be
    /// guessed at is a drawing that failed, so this is where the guessing stops.
    ///
    /// The samples are drawn by the same code the window itself uses. A legend that
    /// keeps its own copy of the artwork is a legend that will one day be wrong.
    /// </summary>
    private void ShowLegend() {
        using var window = new Form {
            Text = Strings.T("legend.title"),
            StartPosition = FormStartPosition.CenterParent,
            Size = new Size(560, 720),
            MinimumSize = new Size(420, 400),
            BackColor = Canvas, ForeColor = Ink, KeyPreview = true,
            Icon = Icon, ShowInTaskbar = false,
            MinimizeBox = false, MaximizeBox = false,
        };
        window.KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) window.Close(); };
        window.Load += (_, _) => UseDarkTitleBar(window);

        var page = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(20, 8, 20, 20), BackColor = Canvas };
        var rows = new List<Control>();

        void Heading(string text) => rows.Add(new Label {
            Dock = DockStyle.Top, Height = 34, Text = text, BackColor = Canvas,
            ForeColor = Ink, Font = new Font("Segoe UI", 10.5F, FontStyle.Bold),
            Padding = new Padding(0, 10, 0, 0),
        });

        void Note(string text) => rows.Add(new Label {
            Dock = DockStyle.Top, Height = 34, Text = text, BackColor = Canvas,
            ForeColor = Muted, Font = new Font("Segoe UI", 8.5F),
            Padding = new Padding(0, 0, 0, 6),
        });

        // One entry: a drawn sample on the left, what it means beside it.
        void Entry(Action<Graphics, Rectangle> paint, string what, string why) {
            // Tall enough for a description that runs to two lines, since one that
            // gets its second line clipped is worse than no description.
            var row = new Panel { Dock = DockStyle.Top, Height = 42, BackColor = Canvas };
            var swatch = new Panel { Dock = DockStyle.Left, Width = 46, BackColor = Canvas };
            swatch.Paint += (_, e) => paint(e.Graphics, new Rectangle(0, 0, swatch.Width, swatch.Height));
            row.Controls.Add(new Label {
                Dock = DockStyle.Fill, Text = why, ForeColor = Muted,
                TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true,
                Font = new Font("Segoe UI", 8.5F),
            });
            row.Controls.Add(new Label {
                Dock = DockStyle.Left, Width = 148, Text = what, ForeColor = Ink,
                TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Segoe UI", 9F),
            });
            row.Controls.Add(swatch);
            rows.Add(row);
        }

        void Mark(string type, string detail, string what, string why) =>
            Entry((g, r) => {
                if (EventIcon(type, detail) is { } image) {
                    g.DrawImageUnscaled(image, (r.Width - image.Width) / 2, (r.Height - image.Height) / 2);
                }
            }, what, why);

        Heading(Strings.T("legend.verdicts"));
        Note(Strings.T("legend.verdictsNote"));
        foreach (var verdict in new[] { "accepted", "review", "rejected", "imported" }) {
            Entry((g, r) => Dot(g, r, VerdictColour(verdict)),
                Strings.T("value." + verdict), Strings.T("legend." + verdict));
        }

        Heading(Strings.T("legend.marks"));
        Mark("collision", "", Strings.T("event.collision"), Strings.T("legend.collision"));
        // The identifier the game files the offence under, not the word it is shown
        // as. Handing the word over is what put a banknote against "speeding fine" in
        // every language but English.
        Mark("fine", "Speeding", Strings.T("event.fine") + " · " + Strings.T("value.Speeding"), Strings.T("legend.speeding"));
        Mark("fine", "", Strings.T("event.fine"), Strings.T("legend.fine"));
        Mark("refuel", "", Strings.T("event.refuel"), Strings.T("legend.refuel"));
        Mark("rest", "", Strings.T("event.rest"), Strings.T("legend.rest"));
        Mark("ferry", "", Strings.T("event.ferry"), Strings.T("legend.ferry"));
        Mark("tollgate", "", Strings.T("event.tollgate"), Strings.T("legend.toll"));
        Mark("save_loaded", "", Strings.T("event.save_loaded"), Strings.T("legend.saveLoaded"));

        Heading(Strings.T("legend.map"));
        Note(Strings.T("legend.mapNote"));
        Entry(Ramp, Strings.T("legend.speedRamp"), Strings.T("legend.speedRampWhy"));
        Entry((g, r) => {
            using var pen = new Pen(Color.FromArgb(150, 150, 160, 175), 2f) { DashStyle = DashStyle.Dash };
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.DrawLine(pen, 10, r.Height / 2, 36, r.Height / 2);
        }, Strings.T("legend.break"), Strings.T("legend.breakWhy"));
        Entry((g, r) => {
            using var pen = new Pen(Color.FromArgb(165, 128, 146, 166), 1.4f);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.DrawLine(pen, 10, r.Height / 2, 36, r.Height / 2);
        }, Strings.T("legend.offJob"), Strings.T("legend.offJobWhy"));
        Entry((g, r) => {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var ring = new Pen(Ink, 2f);
            using var back = new SolidBrush(Canvas);
            g.FillEllipse(back, 9, r.Height / 2 - 5, 10, 10);
            g.DrawEllipse(ring, 9, r.Height / 2 - 5, 10, 10);
            using var solid = new SolidBrush(Accent);
            g.FillEllipse(solid, 27, r.Height / 2 - 5, 11, 11);
        }, Strings.T("legend.ends"), Strings.T("legend.endsWhy"));
        Entry((g, r) => {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var dot = new SolidBrush(Color.FromArgb(190, 150, 160, 175));
            g.FillEllipse(dot, 21, r.Height / 2 - 3, 6, 6);
        }, Strings.T("legend.city"), Strings.T("legend.cityWhy"));
        Entry((g, r) => {
            if (Eye(true) is { } open) g.DrawImageUnscaled(open, 6, (r.Height - open.Height) / 2);
            if (Eye(false) is { } shut) g.DrawImageUnscaled(shut, 26, (r.Height - shut.Height) / 2);
        }, Strings.T("legend.layers"), Strings.T("legend.layersWhy"));
        // The truck on the live map: the marker for where it is, with the needle for
        // which way it points, leaning a little so the needle reads as a direction
        // rather than as decoration.
        Entry((g, r) => {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var at = new PointF(23, r.Height / 2f);
            using var glow = new SolidBrush(Color.FromArgb(70, Accent));
            using var fill = new SolidBrush(Accent);
            g.FillEllipse(glow, at.X - 9, at.Y - 9, 18, 18);
            g.FillEllipse(fill, at.X - 5.5f, at.Y - 5.5f, 11, 11);
            const float lean = -0.42f;
            var nx = MathF.Sin(lean);
            var ny = -MathF.Cos(lean);
            g.FillPolygon(fill, new[] {
                new PointF(at.X + nx * 13, at.Y + ny * 13),
                new PointF(at.X - ny * 4.6f - nx * 2.6f, at.Y + nx * 4.6f - ny * 2.6f),
                new PointF(at.X + ny * 4.6f - nx * 2.6f, at.Y - nx * 4.6f - ny * 2.6f),
            });
        }, Strings.T("legend.truck"), Strings.T("legend.truckWhy"));

        Heading(Strings.T("legend.elsewhere"));
        Entry((g, r) => {
            using var back = new SolidBrush(Surface);
            g.FillRectangle(back, 8, 0, 30, r.Height);
            using var brush = new SolidBrush(VerdictColour("accepted"));
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.FillEllipse(brush, 13, r.Height / 2 - 5, 9, 9);
            HazardStripes(g, new RectangleF(29, 0, 9, r.Height), 210);
        }, Strings.T("legend.gutter"), Strings.T("legend.gutterWhy"));
        Entry((g, r) => HazardStripes(g, new RectangleF(16, 4, 14, r.Height - 8), 210),
            Strings.T("detail.special"), Strings.T("legend.specialWhy"));
        Entry((g, r) => Bolt(g, new RectangleF(18, r.Height / 2f - 8, 10, 16)),
            Strings.T("legend.electric"), Strings.T("legend.electricWhy"));
        Entry((g, r) => {
            using var lead = new SolidBrush(Muted);
            using var done = new SolidBrush(Accent);
            using var over = new SolidBrush(Color.FromArgb(150, 112, 52));
            g.FillRectangle(lead, 8, r.Height / 2 - 4, 7, 8);
            g.FillRectangle(done, 15, r.Height / 2 - 4, 16, 8);
            g.FillRectangle(over, 31, r.Height / 2 - 4, 7, 8);
        }, Strings.T("legend.progress"), Strings.T("legend.progressWhy"));

        // The three marks in the strip at the foot of the sidebar, drawn here the same
        // size they are drawn there.
        void FeedEntry(Noticed kind, string what, string why) =>
            Entry((g, r) => {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                var middle = new PointF(23, r.Height / 2f);
                switch (kind) {
                    case Noticed.Started:
                        using (var edge = new Pen(Accent, 1.6f)) {
                            g.DrawEllipse(edge, middle.X - 4, middle.Y - 4, 8, 8);
                        }
                        break;
                    case Noticed.Delivered:
                        using (var full = new SolidBrush(Accent)) {
                            g.FillEllipse(full, middle.X - 4, middle.Y - 4, 8, 8);
                        }
                        break;
                    default:
                        using (var full = new SolidBrush(Accent)) {
                            g.FillPolygon(full, Star(middle, 6, 2.4f));
                        }
                        break;
                }
            }, what, why);

        FeedEntry(Noticed.Started, Strings.T("legend.feedStarted"), Strings.T("legend.feedStartedWhy"));
        FeedEntry(Noticed.Delivered, Strings.T("legend.feedDelivered"), Strings.T("legend.feedDeliveredWhy"));
        FeedEntry(Noticed.Award, Strings.T("legend.feedAward"), Strings.T("legend.feedAwardWhy"));

        // Docked children stack in reverse of adding, so the list goes in backwards.
        for (var i = rows.Count - 1; i >= 0; i--) page.Controls.Add(rows[i]);
        window.Controls.Add(page);
        window.Shown += (_, _) => UseDarkScrollbars(page);
        window.ShowDialog(this);
    }

    private static void Dot(Graphics g, Rectangle r, Color colour) {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var brush = new SolidBrush(colour);
        g.FillEllipse(brush, 17, r.Height / 2 - 5, 10, 10);
    }

    /// <summary>The speed ramp as a strip, drawn from the same eight colours the map
    /// draws a route with.</summary>
    private static void Ramp(Graphics g, Rectangle r) {
        var colours = RouteView.SpeedRamp;
        var w = 30f / colours.Count;
        for (var i = 0; i < colours.Count; i++) {
            using var brush = new SolidBrush(colours[i]);
            g.FillRectangle(brush, 8 + i * w, r.Height / 2 - 3, w + 0.6f, 6);
        }
    }
    private static readonly Dictionary<string, Image> EventIcons = new();

    /// <summary>How large an icon is drawn before it is shrunk to size. Drawn
    /// straight into fourteen pixels, every curve landed on a pixel boundary and it
    /// showed; drawn four times over and averaged down, the same shapes come out
    /// smooth.</summary>
    private const int IconSuper = 4;
    private const int IconSize = 20;

    /// <summary>
    /// A mark for each kind of thing that happens on a drive, so a timeline can be
    /// scanned rather than read.
    ///
    /// Drawn rather than typed: the glyphs for these live in fonts that may not be
    /// installed, and a missing one comes out as an empty box exactly where the
    /// meaning was. Filled shapes rather than outlines, because a one pixel line at
    /// this size is a suggestion and a filled shape is a shape.
    ///
    /// A fine takes its mark from the offence, since being fined for speeding and
    /// being fined for anything else are not the same thing to look at.
    /// </summary>
    private static Image? EventIcon(string type, string detail = "") {
        // Against the identifier the game files the offence under, not against the word
        // it is shown as: the word changes with the language, and the dial would have
        // quietly stopped appearing in four of the five.
        var speeding = type == "fine"
            && detail.Equals("Speeding", StringComparison.OrdinalIgnoreCase);
        var key = speeding ? "fine.speeding" : type;
        if (EventIcons.TryGetValue(key, out var made)) return made;

        var colour = type switch {
            "collision" => Color.FromArgb(226, 116, 104),
            "fine" => Color.FromArgb(232, 168, 74),
            "refuel" => Color.FromArgb(112, 172, 214),
            "ferry" or "train" => Color.FromArgb(96, 176, 168),
            "cargo_loaded" or "trailer_coupled" => Color.FromArgb(200, 210, 224),
            "save_loaded" => Color.FromArgb(180, 150, 200),
            _ => Muted,
        };
        var deep = Color.FromArgb(colour.A, colour.R * 45 / 100, colour.G * 45 / 100, colour.B * 45 / 100);

        const int Big = IconSize * IconSuper;
        var large = new Bitmap(Big, Big);
        using (var g = Graphics.FromImage(large)) {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.ScaleTransform(Big / 80f, Big / 80f);
            using var fill = new SolidBrush(colour);
            using var cut = new SolidBrush(deep);
            using var pen = new Pen(colour, 9f) {
                StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round,
            };

            if (speeding) {
                // A dial with the needle swung round to the right: too fast, plainly.
                g.DrawArc(pen, 12, 16, 56, 56, 165, 210);
                g.DrawLine(pen, 40, 44, 63, 25);
                g.FillEllipse(fill, 32, 36, 16, 16);
            } else switch (type) {
                case "collision":
                    // The burst an impact leaves, filled rather than drawn in spokes.
                    g.FillPolygon(fill, Star(40, 40, 37, 14, 10));
                    break;
                case "fine":
                    // A note handed over: the shape of money, with its face knocked
                    // out in a darker shade rather than in the panel behind it, so it
                    // reads the same on either row colour.
                    using (var note = Rounded(new RectangleF(6, 22, 68, 36), 7)) g.FillPath(fill, note);
                    g.FillEllipse(cut, 30, 30, 20, 20);
                    break;
                case "refuel":
                    using (var drop = new GraphicsPath()) {
                        drop.AddBezier(40, 8, 76, 44, 68, 72, 40, 72);
                        drop.AddBezier(40, 72, 12, 72, 4, 44, 40, 8);
                        g.FillPath(fill, drop);
                    }
                    break;
                case "rest":
                    // A crescent, cut rather than overlapped. Two ellipses in one path
                    // do not subtract: the alternate fill leaves the part of the
                    // second one that falls outside the first, which is why the moon
                    // came out as a blob with a bite and a spur. A region takes one
                    // away from the other properly.
                    using (var disc = new GraphicsPath())
                    using (var bite = new GraphicsPath()) {
                        disc.AddEllipse(11, 11, 58, 58);
                        bite.AddEllipse(27, 3, 56, 56);
                        using var crescent = new Region(disc);
                        crescent.Exclude(bite);
                        g.FillRegion(fill, crescent);
                    }
                    break;
                case "ferry":
                case "train":
                    // A hull with a funnel above it.
                    g.FillPolygon(fill, new[] {
                        new PointF(8, 44), new PointF(72, 44), new PointF(59, 70), new PointF(21, 70),
                    });
                    using (var funnel = Rounded(new RectangleF(33, 12, 15, 28), 4)) g.FillPath(fill, funnel);
                    break;
                case "tollgate":
                    // A barrier down across the road, on its post, striped.
                    using (var post = Rounded(new RectangleF(8, 22, 14, 50), 4)) g.FillPath(fill, post);
                    var bar = new[] {
                        new PointF(18, 30), new PointF(74, 44), new PointF(74, 58), new PointF(18, 44),
                    };
                    g.FillPolygon(fill, bar);
                    using (var clip = PathOf(bar)) {
                        var was = g.Clip;
                        g.SetClip(clip);
                        for (var s = 22f; s < 78f; s += 19f) {
                            g.FillPolygon(cut, new[] {
                                new PointF(s, 24), new PointF(s + 9, 24),
                                new PointF(s + 9, 64), new PointF(s, 64),
                            });
                        }
                        g.Clip = was;
                    }
                    break;
                case "save_loaded":
                    // Wound back to somewhere already passed.
                    //
                    // This was a circular arrow twice and read as neither. At twenty
                    // pixels the head has to be about as wide as the arc is thick to
                    // be seen at all, and then it swallows the curve it is supposed
                    // to finish. Two chevrons have no such fight: they are the same
                    // shape at any size, and everybody already knows what rewind
                    // looks like.
                    g.FillPolygon(fill, new[] { new PointF(38, 14), new PointF(38, 66), new PointF(6, 40) });
                    g.FillPolygon(fill, new[] { new PointF(74, 14), new PointF(74, 66), new PointF(42, 40) });
                    break;
                case "cargo_loaded":
                case "trailer_coupled":
                    // A crate, strapped.
                    using (var crate = Rounded(new RectangleF(10, 22, 60, 42), 5)) g.FillPath(fill, crate);
                    g.FillRectangle(cut, 10, 37, 60, 9);
                    break;
                default:
                    g.FillEllipse(fill, 28, 28, 24, 24);
                    break;
            }
        }

        // Averaged down, which is where the smoothness comes from.
        var small = new Bitmap(IconSize, IconSize);
        using (var g = Graphics.FromImage(small)) {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.DrawImage(large, new Rectangle(0, 0, IconSize, IconSize));
        }
        large.Dispose();

        EventIcons[key] = small;
        return small;
    }

    private static GraphicsPath PathOf(PointF[] points) {
        var path = new GraphicsPath();
        path.AddPolygon(points);
        return path;
    }

    /// <summary>A rectangle with its corners taken off, which GDI+ has no primitive
    /// for and every filled shape here wants.</summary>
    private static GraphicsPath Rounded(RectangleF r, float radius) {
        var path = new GraphicsPath();
        var d = radius * 2;
        path.AddArc(r.Left, r.Top, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    /// <summary>The points of a star, for the burst an impact leaves.</summary>
    private static PointF[] Star(float cx, float cy, float outer, float inner, int spikes) {
        var points = new PointF[spikes * 2];
        for (var i = 0; i < points.Length; i++) {
            var reach = i % 2 == 0 ? outer : inner;
            var a = Math.PI * i / spikes - Math.PI / 2;
            points[i] = new PointF(cx + (float)Math.Cos(a) * reach, cy + (float)Math.Sin(a) * reach);
        }
        return points;
    }

    /// <summary>
    /// The hazard stripes an oversize load carries on its own bumper.
    ///
    /// Quiet on purpose: a special transport is a different kind of driving rather
    /// than a warning, and a row of bright markers down a list would shout about
    /// every one of them. Drawn at an angle because that is how the real ones go and
    /// because it reads as a marking rather than as a border.
    /// </summary>
    private static void HazardStripes(Graphics g, RectangleF where, int alpha = 150) {
        var was = g.Clip;
        g.SetClip(where);
        using var dark = new SolidBrush(Color.FromArgb(alpha, 26, 28, 32));
        using var pale = new SolidBrush(Color.FromArgb(alpha, 214, 218, 226));
        g.FillRectangle(dark, where);

        const float band = 4f;
        var lean = where.Height;
        for (var x = where.Left - lean; x < where.Right + band; x += band * 2) {
            g.FillPolygon(pale, new[] {
                new PointF(x, where.Bottom),
                new PointF(x + band, where.Bottom),
                new PointF(x + band + lean, where.Top),
                new PointF(x + lean, where.Top),
            });
        }
        g.Clip = was;
    }

    /// <summary>An eye for whether a layer is being drawn: open and looking at you,
    /// or closed. Drawn rather than typed, because the glyphs for this are scattered
    /// across fonts that may or may not be installed, and a missing one comes out as
    /// an empty box exactly where the meaning was.</summary>
    private static Image Eye(bool open) {
        if (open && _eyeOpen != null) return _eyeOpen;
        if (!open && _eyeShut != null) return _eyeShut;

        var bmp = new Bitmap(16, 16);
        using (var g = Graphics.FromImage(bmp)) {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var pen = new Pen(open ? Ink : Color.FromArgb(150, 128, 138, 152), 1.4f);
            if (open) {
                using var lid = new System.Drawing.Drawing2D.GraphicsPath();
                lid.AddBezier(1.5f, 8, 5, 3, 11, 3, 14.5f, 8);
                lid.AddBezier(14.5f, 8, 11, 13, 5, 13, 1.5f, 8);
                g.DrawPath(pen, lid);
                using var pupil = new SolidBrush(Ink);
                g.FillEllipse(pupil, 5.9f, 5.9f, 4.4f, 4.4f);
            } else {
                // A closed lid, with the lashes that make it read as shut rather than
                // as an arbitrary curve.
                g.DrawBezier(pen, 1.8f, 6.4f, 5, 11.6f, 11, 11.6f, 14.2f, 6.4f);
                g.DrawLine(pen, 4.6f, 10.2f, 3.6f, 12.4f);
                g.DrawLine(pen, 8f, 11.4f, 8f, 13.6f);
                g.DrawLine(pen, 11.4f, 10.2f, 12.4f, 12.4f);
            }
        }

        if (open) _eyeOpen = bmp; else _eyeShut = bmp;
        return bmp;
    }

    private ContextMenuStrip LayerMenu(RouteView map, DeliveryDetail? about = null) {
        var menu = new ContextMenuStrip {
            BackColor = Surface, ForeColor = Ink, ShowImageMargin = true,
            Renderer = new ToolStripProfessionalRenderer(new DarkMenuColours()),
        };
        // Turning one layer off is rarely the whole intent, so the menu survives a
        // click and stays under the pointer until it is dismissed.
        menu.Closing += (_, e) => {
            if (e.CloseReason == ToolStripDropDownCloseReason.ItemClicked) e.Cancel = true;
        };
        void Item(string label, bool on, Action<bool> set) {
            var item = new ToolStripMenuItem(label) { BackColor = Surface, Image = Eye(on) };
            item.ForeColor = on ? Ink : Muted;
            var shown = on;
            item.Click += (_, _) => {
                shown = !shown;
                set(shown);
                // The eye and the weight of the text say the same thing twice, which
                // is on purpose: a row of near-identical entries is hard to read at a
                // glance if only one small mark separates them.
                item.Image = Eye(shown);
                item.ForeColor = shown ? Ink : Muted;
                map.Invalidate();
            };
            menu.Items.Add(item);
        }
        Item(Strings.T("map.layerHistory"), map.ShowHistory, v => map.ShowHistory = v);
        Item(Strings.T("map.layerCities"), map.ShowCities, v => map.ShowCities = v);
        Item(Strings.T("map.layerStops"), map.ShowStops, v => map.ShowStops = v);
        Item(Strings.T("map.layerMarks"), map.ShowMarks, v => map.ShowMarks = v);

        // Which world this delivery was driven in, on the delivery's own map, because
        // that is where being drawn on the wrong one is noticed. Only where the game
        // has more than one to be wrong about.
        if (about is not null && MapsFor(about.Game).Count > 1) {
            menu.Items.Add(new ToolStripSeparator());
            var worlds = new ToolStripMenuItem(Strings.T("map.world")) { BackColor = Surface };

            void World(string name, string label) {
                var item = new ToolStripMenuItem(label) { Tag = name, BackColor = Surface };
                item.Checked = about.MapWorld.Equals(name, StringComparison.OrdinalIgnoreCase);
                item.Click += (_, _) => {
                    about.MapWorld = name;
                    _store.SetDeliveryWorld(about.JobUid, name);
                    foreach (ToolStripMenuItem other in worlds.DropDownItems) {
                        other.Checked = Equals(other.Tag, name);
                    }
                    map.GameMap = MapForDelivery(about);
                    map.Invalidate();
                };
                worlds.DropDownItems.Add(item);
            }

            World("", Strings.T("map.worldAuto"));
            foreach (var one in MapsFor(about.Game)) World(one.Name, one.Name);
            menu.Items.Add(worlds);
            StyleMenuItems(menu.Items);
        }
        return menu;
    }

    /// <summary>The world the driver has said they are playing in, if any of that
    /// game's exports is called that.</summary>
    private MapBackdrop? PlayingIn(string game) {
        if (game.Length == 0 || !_settings.MapRecord.TryGetValue(game, out var world) || world.Length == 0) return null;
        return MapsFor(game).FirstOrDefault(m => m.Name.Equals(world, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The world a delivery is drawn on.
    ///
    /// What the driver said about this one wins, since nothing else here knows better:
    /// the game never says which map is loaded, so a person saying "this was ProMods"
    /// is the only certain answer there is. Without one, the drive is asked to explain
    /// itself, and failing that the picker's choice stands.
    /// </summary>
    private MapBackdrop? MapForDelivery(DeliveryDetail about) {
        if (about.MapWorld.Length > 0) {
            var said = MapsFor(about.Game).FirstOrDefault(
                m => m.Name.Equals(about.MapWorld, StringComparison.OrdinalIgnoreCase));
            if (said is not null) return said;
        }
        var drawn = RoutesFor(about.Game).Routes.TryGetValue(about.Id, out var line) ? line : new List<RoutePoint>();
        return drawn.Count > 0
            ? GameMapFor(about.Game, Ground(new[] { drawn }),
                         new PointF(drawn[0].X, drawn[0].Z), new PointF(drawn[^1].X, drawn[^1].Z))
            : GameMapFor(about.Game);
    }

    /// <summary>The same map with the whole screen to itself. A route panel beside a
    /// column of figures is enough to see the shape of a drive and not enough to
    /// look at a junction, and zooming inside a 300 pixel strip is a poor substitute
    /// for room.</summary>
    private void BigMap(DeliveryDetail d, Units u) {
        using var window = new Form {
            Text = $"{Where(d, d.SourceCity, d.SourceCityId)}  →  {Where(d, d.DestinationCity, d.DestinationCityId)}",
            StartPosition = FormStartPosition.CenterScreen,
            WindowState = FormWindowState.Maximized,
            BackColor = Canvas, ForeColor = Ink, KeyPreview = true,
            Icon = Icon, MinimumSize = new Size(640, 480),
        };
        var map = NewMap(u);
        map.Hint = Strings.T("map.hintBig");
        window.Controls.Add(map);
        MapButtons(window, map, null, replay: true);
        // Shown before the data, so the map measures itself against the size it will
        // actually have rather than fitting the route to a window that is about to
        // be maximised.
        window.Shown += (_, _) => {
            map.Show(Layers(RoutesFor(d.Game)), d.Id, RoutesFor(d.Game).Cities,
                     _store.TimelineRows(d.Id, u));
            map.Replay();
        };
        window.Load += (_, _) => UseDarkTitleBar(window);
        window.KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) window.Close(); };
        window.ShowDialog(this);
    }

    private Control DetailBody(DeliveryDetail d, Units u) {
        var body = new Panel { Dock = DockStyle.Fill, BackColor = Canvas, Padding = new Padding(0, 12, 0, 0), AutoScroll = true };

        // The map and the timeline slide out from the right together on request. They
        // are worth reading when something went wrong and worth nothing when nothing
        // did, and as a permanent column they took a third of the card away from the
        // figures either way.
        var side = new Panel { Dock = DockStyle.Right, Width = 0, BackColor = Canvas };
        var timeline = new Panel { Dock = DockStyle.Fill, BackColor = Surface, AutoScroll = true };

        var events = _store.TimelineRows(d.Id, u, Tracking.Trucks.IsElectric(d.TruckId, d.Truck));
        if (events.Count == 0) {
            timeline.Controls.Add(new Label {
                Dock = DockStyle.Top, Height = 30, Text = Strings.T("timeline.none"),
                ForeColor = Muted, BackColor = Surface, Padding = new Padding(16, 6, 0, 0),
            });
        }
        for (var i = events.Count - 1; i >= 0; i--) timeline.Controls.Add(EventLine(events[i], i % 2 == 1));
        timeline.Controls.Add(CardHeading(Strings.T("detail.timeline")));

        // The facts as a list rather than a wall of cards: one column, name on the
        // left, value on the right, which is how anyone actually reads a docket.
        var info = new Panel { Dock = DockStyle.Fill, BackColor = Surface, AutoScroll = true, Margin = new Padding(0, 0, 12, 0) };
        var rows = new List<Control>();
        var shade = false;
        void Row(string label, string value) {
            rows.Add(InfoRow(label, value, shade));
            shade = !shade;
        }
        // Grouped by the question being asked rather than run as one long list: how
        // far, what it paid, how it was driven, what state it ended in.
        void Group(string heading) {
            rows.Add(CardHeading(heading));
            shade = false;
        }

        Group(Strings.T("detail.groupLoad"));
        Row(Strings.T("col.cargo"), d.Cargo);
        Row(Strings.T("detail.weight"), $"{u.MassTonnes(d.CargoMassKg):0.0} {u.MassUnit}");
        Row(Strings.T("col.truck"), d.Truck);
        if (d.JobType.Length > 0) Row(Strings.T("detail.jobType"), Label(d.JobType));
        // The coupled set folds away: one line for what it is, opened for what each
        // unit of it took.
        if (d.TrailerUnits.Count > 0) rows.Add(TrailerDropdown(d));
        else if (d.Trailer.Length > 0) Row(Strings.T("detail.trailer"), d.Trailer);

        Group(Strings.T("detail.groupDistance"));
        // The three measurements read as one thing, so they belong on one line where
        // they can be compared instead of three where they have to be remembered.
        var reported = d.ReportedDistanceKm is > 0 ? $"{u.Distance(d.ReportedDistanceKm.Value):0}" : "?";
        Row(Strings.T("detail.distances"),
            $"{u.Distance(d.PlannedDistanceKm):0}  /  {u.Distance(d.DistanceKm):0.0}  /  {reported} {u.DistanceUnit}");
        // Only where there was one. On a quick job the truck is set down at the depot
        // already loaded, so a row saying "0" would be a line about nothing.
        if (d.DistanceToLoadKm > 0.05) {
            Row(Strings.T("detail.legs"),
                $"{u.Distance(d.DistanceKm - d.DistanceToLoadKm):0.0}  +  {u.Distance(d.DistanceToLoadKm):0.0} {u.DistanceUnit}");
        }
        // Both times on this card are read the same way, and neither repeats its own
        // label: the rows are already called "in game" and "at the wheel". The
        // statistics keep the hours with a decimal, since a figure covering a month
        // has no business being said to the minute.
        Row(Strings.T("detail.timeGame"), Units.Duration(d.DrivingGameMin));
        Row(Strings.T("detail.timeReal"), Units.Duration(d.RealDurationMs / 60000.0));
        Row(Strings.T("detail.rest"), $"{d.RestStops}×  ·  {Units.Duration(d.RestMinutes)}");

        Group(Strings.T("detail.groupMoney"));
        if (d.Xp > 0) Row(Strings.T("detail.xp"), $"{d.Xp} XP");
        var paid = d.Outcome == "delivered" ? d.Revenue : -d.Penalty;
        Row(Strings.T("detail.paidOffered"), $"{u.FormatMoney(paid)}  /  {u.FormatMoney(d.OfferedIncome)}");
        Row(Strings.T("detail.fines"), $"{u.FormatMoney(d.FinesTotal)}  ({d.FinesCount}×)");
        Row(Strings.T("detail.tolls"), u.FormatMoney(d.TollsPaid));
        // A battery is not a tank. The figure comes out of the same telemetry field,
        // and on an electric truck the game puts kilowatt hours in it.
        var battery = Tracking.Trucks.IsElectric(d.TruckId, d.Truck);
        Row(Strings.T("detail.fuel"), battery ? Units.FormatEnergy(d.FuelUsedL) : u.FormatVolume(d.FuelUsedL));
        if (battery && d.AvgConsumption is { } kwh) Row(Strings.T("detail.consumption"), u.FormatEnergyPer100(kwh));
        else if (!battery && u.Consumption(d.AvgConsumption) is { } c) Row(Strings.T("detail.consumption"), $"{c:0.0} {u.ConsumptionUnit}");
        Row(Strings.T("detail.refuels"), d.Refuels.ToString());

        Group(Strings.T("detail.groupDriving"));
        Row(Strings.T("col.style"), Label(d.Style));
        Row(Strings.T("detail.topSpeed"), u.FormatSpeed(d.TopSpeedKmh));
        Row(Strings.T("detail.speeding"), $"{d.SpeedingShare * 100:0.0} %  ·  {Strings.T("detail.clearlyOver")} {d.HardSpeedingShare * 100:0.0} %");
        Row(Strings.T("detail.cruise"), $"{d.CruiseShare * 100:0.0} %");
        Row(Strings.T("detail.collisions"), d.Collisions.ToString());
        Row(Strings.T("detail.ferries"), d.Ferries.ToString());

        // Damage on its own, one line per thing that can take it. Truck and trailer
        // used to share a line and the cargo was not shown at all, which left no way
        // to see that a load arrived damaged without a collision behind it.
        // Before and after, where the before is known. The truck and the trailer
        // store what this delivery added to them; the load's figure is what the game
        // reported outright on arrival, so it is already the after.
        Group(Strings.T("detail.groupDamage"));
        Row(Strings.T("col.truck"), Condition(d.TruckDamageStart, (d.TruckDamageStart ?? 0) + d.TruckDamage));
        Row(Strings.T("detail.trailer"), Condition(d.TrailerDamageStart, (d.TrailerDamageStart ?? 0) + d.TrailerDamage));
        Row(Strings.T("col.cargo"), Condition(d.CargoDamageStart, d.CargoDamage));

        // Docked children stack in reverse order of adding, so the list goes in
        // backwards to come out in the order it was built.
        for (var i = rows.Count - 1; i >= 0; i--) info.Controls.Add(rows[i]);

        var notes = new Panel { Dock = DockStyle.Bottom, Height = 96, BackColor = Surface, Padding = new Padding(16, 12, 16, 12), Margin = new Padding(0) };
        notes.BringToFront();
        var notesBox = new TextBox {
            Dock = DockStyle.Fill, Multiline = true, Text = d.Notes,
            BorderStyle = BorderStyle.None, BackColor = Raised, ForeColor = Ink,
        };
        notesBox.Leave += (_, _) => { _store.SetNotes(d.Id, notesBox.Text); ReloadHistory(); };
        notes.Controls.Add(notesBox);
        notes.Controls.Add(new Label {
            Dock = DockStyle.Top, Height = 20, Text = Strings.T("col.notes").ToUpperInvariant(),
            ForeColor = Muted, Font = new Font("Segoe UI", 7.5F, FontStyle.Bold),
        });

        // Added after the timeline so it docks outermost and takes its height off the
        // top, leaving the timeline the rest. The map stays put while the timeline
        // scrolls under it, which is what lets the two refer to each other.
        var drawings = RoutePanel(d, u);
        drawings.Height = _routeHeight;
        side.Controls.Add(timeline);
        // Between the two, so the drawings can be given more of the column and the
        // log less, or the other way about.
        side.Controls.Add(RouteGrip(side, drawings));
        side.Controls.Add(drawings);
        _detailSide = side;

        // Neither handle can be reached once the thing it moves has grown past the
        // window, and a window can shrink under it: pulled wide on a full screen and
        // then restored, the column filled the card and its handle was off the edge
        // of it. So both are held inside what there is room for whenever the card
        // changes size, not only while they are being pulled.
        body.Resize += (_, _) => {
            if (side.Width > 0) side.Width = HeldWidth(side.Width, body);
            // Measured against what was asked for rather than against what it is now.
            // A resize passes through sizes nobody chose, and clamping the current
            // height at each of them ratcheted the drawings down to the floor and
            // left them there. Asked for 396 and given room for 300, it takes 300;
            // given the room back, it takes 396 again.
            if (side.Height > 240) drawings.Height = HeldHeight(_routeHeight, side);
        };

        body.Controls.Add(info);
        body.Controls.Add(notes);
        // Before the column, so it docks inside it and lands against its left edge.
        body.Controls.Add(TimelineGrip(side, body));
        body.Controls.Add(side);
        // Added last so it docks outermost and gets the full width. The verdict is
        // the first thing anyone asks about a delivery, so it reads across the top
        // rather than as one more row buried in the list of facts.
        body.Controls.Add(VerdictBand(d, u));
        // Over the verdict, four figures about the drive: what a delivery is, before
        // any of the detail under it. Added last so it docks above everything else.
        body.Controls.Add(DetailFigures(d, u));
        return body;
    }

    /// <summary>
    /// The strip of four along the top of a card: how far, what it paid, how long it
    /// took, and what it cost the truck.
    ///
    /// The same shape as the strip on the live page, because it answers the same
    /// question about the same drive, only afterwards.
    /// </summary>
    private Control DetailFigures(DeliveryDetail d, Units u) {
        var strip = new Panel { Dock = DockStyle.Top, Height = 68, BackColor = Look.Window, Margin = new Padding(0) };
        var hours = d.DrivingGameMin / 60;
        var damage = Math.Max(d.TruckDamage, d.TrailerDamage) * 100;

        strip.Paint += (_, e) => {
            var g = e.Graphics;
            g.Clear(Look.Window);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            Look.Surface(g, new RectangleF(0, 0, strip.Width, strip.Height - 4), Look.Panel, Look.Hairline);

            var tiles = new (string Label, string Figure, string Under, Color Ink)[] {
                (Strings.T("col.distance"), u.FormatDistance(d.DistanceKm),
                 d.PlannedDistanceKm > 0 ? $"{Strings.T("detail.planned")} {u.FormatDistance(d.PlannedDistanceKm)}" : "", Look.Ink),
                (Strings.T("col.pay"), u.FormatMoney(d.Revenue > 0 ? d.Revenue : -d.Penalty),
                 d.OfferedIncome > 0 ? $"{Strings.T("detail.offered")} {u.FormatMoney(d.OfferedIncome)}" : "",
                 d.Revenue > 0 ? Look.Ink : Look.Lost),
                (Strings.T("detail.timeGame"), Units.Duration(d.DrivingGameMin),
                 d.RestStops > 0 ? $"{d.RestStops}× {Strings.T("detail.rest")}" : Strings.T("detail.noRest"), Look.Ink),
                // What the drive cost the set, with the collisions under it. Wear on its
                // own is a truck getting older; a collision is something that happened.
                (Strings.T("trucks.damage"), $"{damage:0.00} %",
                 d.Collisions > 0 ? $"{d.Collisions}× {Strings.T("live.collisions")}"
                     : damage < 0.05 ? Strings.T("live.notAScratch") : Strings.T("detail.wearOnly"),
                 damage < 0.05 ? Look.Whole : damage < 1 ? Look.Accent : Look.Lost),
            };

            var wide = (strip.Width - 2) / (float)tiles.Length;
            for (var i = 0; i < tiles.Length; i++) {
                var at = new RectangleF(1 + i * wide, 1, wide, strip.Height - 7);
                if (i > 0) {
                    using var line = new Pen(Look.Hairline);
                    g.DrawLine(line, at.X, at.Y + 10, at.X, at.Bottom - 10);
                }
                var (label, figure, under, ink) = tiles[i];
                Look.FigureTile(g, new RectangleF(at.X + 16, at.Y + 12, at.Width - 24, at.Height - 16),
                                label, figure, under, ink, figureFont: Look.FigureSmall);
            }
        };
        return strip;
    }

    /// <summary>Why this delivery got the verdict it did, in words. Every flag is
    /// stored, but stored as its identifier, and "distance_mismatch" on a row tells
    /// the driver nothing about what to do with it.</summary>
    private Control VerdictBand(DeliveryDetail d, Units u) {
        var reasons = Reasons(d, u);
        var note = d.Status switch {
            "rejected" => Strings.T("verdict.rejectedNote"),
            "review" => Strings.T("verdict.reviewNote"),
            "imported" => Strings.T("verdict.imported"),
            _ => Strings.T("verdict.accepted"),
        };

        var band = new Panel {
            Dock = DockStyle.Top, BackColor = Surface, Padding = new Padding(0, 0, 0, 10),
            Height = 34 + reasons.Count * 46 + 32,
        };

        var footer = new Label {
            Dock = DockStyle.Bottom, Height = 32, Text = note, ForeColor = Muted,
            Padding = new Padding(16, 0, 16, 0), TextAlign = ContentAlignment.MiddleLeft,
        };

        // Docked children stack in reverse, so the reasons go in backwards.
        for (var i = reasons.Count - 1; i >= 0; i--) {
            band.Controls.Add(ReasonLine(reasons[i].Title, reasons[i].Why, i % 2 == 1));
        }
        band.Controls.Add(CardHeading(Strings.T("verdict.heading")));
        band.Controls.Add(footer);
        return band;
    }

    /// <summary>One reason: what was noticed, and underneath it what that means. The
    /// figures behind a measurement disagreement are appended, because "the two
    /// distances disagree" invites the obvious next question.</summary>
    private static Control ReasonLine(string title, string why, bool shaded) {
        var line = new Panel {
            Dock = DockStyle.Top, Height = 46, BackColor = shaded ? Raised : Surface,
            Padding = new Padding(16, 5, 16, 5),
        };
        line.Controls.Add(new Label {
            Dock = DockStyle.Fill, Text = why, ForeColor = Muted,
            Font = new Font("Segoe UI", 8.5F), AutoEllipsis = true,
        });
        line.Controls.Add(new Label {
            Dock = DockStyle.Top, Height = 19, Text = title, ForeColor = Ink,
            Font = new Font("Segoe UI", 9F, FontStyle.Bold),
        });
        // A marker down the side, so the reasons read as a set at a glance rather
        // than as more prose.
        line.Controls.Add(new Panel { Dock = DockStyle.Left, Width = 3, BackColor = Accent, Margin = new Padding(0) });
        return line;
    }

    private static List<(string Title, string Why)> Reasons(DeliveryDetail d, Units u) {
        var reasons = new List<(string, string)>();

        foreach (var flag in d.Flags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) {
            var why = Strings.T("flag." + flag + ".why");
            // The measurements themselves, for the flags that are about two numbers
            // not agreeing. Without them the explanation is unanswerable.
            why += flag switch {
                "implausible_top_speed" => $"  ({u.FormatSpeed(d.TopSpeedKmh)})",
                "distance_too_short" => $"  ({u.FormatDistance(d.DistanceKm)})",
                "distance_inconsistent" => $"  ({u.FormatDistance(d.DistanceKm)} · {u.FormatDistance(d.SimSpeedDistanceKm)})",
                "distance_mismatch" when d.ReportedDistanceKm is > 0
                    => $"  ({u.FormatDistance(d.DistanceKm)} · {u.FormatDistance(d.ReportedDistanceKm.Value)})",
                _ => "",
            };
            reasons.Add((Strings.T("flag." + flag), why));
        }

        return reasons;
    }

    /// <summary>One event as a line, not a table row: the time set away on the left,
    /// then what happened, then the figure it carries.</summary>
    private static Control EventLine(TimelineRow e, bool shaded) {
        var line = new Panel {
            Dock = DockStyle.Top, Height = 30, BackColor = shaded ? Raised : Surface,
            Padding = new Padding(16, 6, 16, 6),
        };

        var detail = new Label { Dock = DockStyle.Fill, Text = e.Detail, ForeColor = Muted, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true };
        var value = new Label { Dock = DockStyle.Right, Width = 90, Text = e.Hodnota, ForeColor = Accent, TextAlign = ContentAlignment.MiddleRight, Font = new Font("Segoe UI", 9F, FontStyle.Bold) };
        var what = new Label { Dock = DockStyle.Left, Width = 98, Text = e.Udalost, ForeColor = Ink, TextAlign = ContentAlignment.MiddleLeft };
        var time = new Label { Dock = DockStyle.Left, Width = 66, Text = e.Cas, ForeColor = Muted, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Consolas", 8.5F) };
        var icon = new PictureBox {
            Dock = DockStyle.Left, Width = 26, SizeMode = PictureBoxSizeMode.CenterImage,
            Image = EventIcon(e.Type, e.Offence), BackColor = Color.Transparent,
        };

        line.Controls.Add(detail);
        line.Controls.Add(value);
        line.Controls.Add(what);
        line.Controls.Add(icon);
        line.Controls.Add(time);
        return line;
    }

    // ---------- data ----------

    private void ReloadHistory() {
        _rows = _store.RecentDeliveryRows(500, _settings.Units).ToList();
        // Written into the rows rather than at the moment they are drawn, because the
        // list is bound to them: the column shows the field, and the search box reads
        // it too, so "Yakima, WA" can also be searched for by its state.
        if (_settings.CityRegions) {
            foreach (var row in _rows) {
                row.Odkial = Places.Say(row.Hra, row.Odkial, row.OdkialId);
                row.Kam = Places.Say(row.Hra, row.Kam, row.KamId);
            }
        }
        _routes.Clear();
        ApplyFilter();
        ReloadMapPage();
        ReloadSessions();
        ReloadTrucks();
        ReloadAwards();
    }

    /// <summary>
    /// Every tracked route of one game, read once and kept.
    ///
    /// The map on a delivery's card draws the whole history underneath the drive
    /// it is showing, so opening five cards in a row would otherwise mean five
    /// reads of the same twenty thousand rows. Cleared whenever the history
    /// changes, which is the only thing that can make it stale.
    /// </summary>
    private GameRoutes RoutesFor(string game) {
        if (!_routes.TryGetValue(game, out var routes)) _routes[game] = routes = _store.RoutesForGame(game);
        return routes;
    }

    /// <summary>
    /// The maps a game has, if somebody has put one where Waybill looks.
    ///
    /// Under `map\ets2` and `map\ats` beside the database, each with the descriptor
    /// that says which square of the world its tiles cover. A folder of tiles straight
    /// in there is one map; folders inside it are one map each, which is how a world
    /// changed by a map mod lives beside the one the game shipped with. Nothing here
    /// reads the game's archives: the tiles are exported once by a tool that already
    /// knows how. No map at all is the ordinary case and draws nothing.
    /// </summary>
    private readonly Dictionary<string, List<MapBackdrop>> _gameMaps = new(StringComparer.OrdinalIgnoreCase);

    private List<MapBackdrop> MapsFor(string game) {
        if (game.Length == 0) return new List<MapBackdrop>();
        if (_gameMaps.TryGetValue(game, out var had)) return had;

        var folder = Path.Combine(DeliveryStore.DefaultDir(), "map", game.ToLowerInvariant());
        var found = new List<MapBackdrop>();
        if (MapBackdrop.Open(folder) is { } plain) found.Add(plain);
        try {
            foreach (var inside in Directory.GetDirectories(folder).OrderBy(d => d, StringComparer.OrdinalIgnoreCase)) {
                if (MapBackdrop.Open(inside) is { } one) found.Add(one);
            }
        } catch { /* a game with no map at all is the ordinary case */ }
        return _gameMaps[game] = found;
    }

    /// <summary>The map chosen for a game, or the first one it has.</summary>
    private MapBackdrop? GameMapFor(string game) {
        var maps = MapsFor(game);
        if (maps.Count == 0) return null;
        var chosen = _settings.MapChoice.TryGetValue(game, out var name) ? name : "";
        return maps.FirstOrDefault(m => m.Name.Equals(chosen, StringComparison.OrdinalIgnoreCase)) ?? maps[0];
    }

    /// <summary>
    /// The map for a game that can actually hold this drive.
    ///
    /// The chosen one wins whenever it covers the ground, which is the ordinary case.
    /// A map mod's world is larger than the one the game shipped with, so a drive that
    /// falls outside the chosen map happened in another world, and drawing it over the
    /// wrong one, or over nothing at all, says less than quietly reaching for the map
    /// that contains it.
    /// </summary>
    private MapBackdrop? GameMapFor(string game, RectangleF need, params PointF[] ends) {
        var chosen = GameMapFor(game);
        if (chosen is null) return null;
        if (Explains(chosen, need, ends)) return chosen;
        foreach (var map in MapsFor(game)) {
            if (Explains(map, need, ends)) return map;
        }
        return chosen;
    }

    /// <summary>
    /// Whether a world can account for a drive.
    ///
    /// It has to hold the ground the drive covered, and it has to have a town at each
    /// end of it. The ends are what tell two worlds apart: a map mod keeps the towns
    /// the game shipped with and adds its own, so a delivery that loaded or dropped
    /// where only one of them has a town happened in that one, whatever is chosen.
    /// Anything both can account for stays with whatever the driver chose.
    /// </summary>
    private static bool Explains(MapBackdrop map, RectangleF need, PointF[] ends) {
        if (!need.IsEmpty && !map.Bounds.Contains(need)) return false;
        foreach (var end in ends) {
            if (!map.HasTownNear(end.X, end.Y, TownReachMetres)) return false;
        }
        return true;
    }

    /// <summary>
    /// How far from a town a delivery may end and still count as being there.
    ///
    /// In the game's own metres, which are compressed: the whole of America is 161 km
    /// across in them. Depots sit inside their town at this scale, and towns are far
    /// further apart than this, so it separates them without being fussy about which
    /// depot was used.
    /// </summary>
    private const float TownReachMetres = 3000f;

    /// <summary>The ground a set of drives covers, for choosing a map by it.</summary>
    private static RectangleF Ground(IEnumerable<List<RoutePoint>> runs) {
        float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
        foreach (var run in runs) {
            foreach (var point in run) {
                minX = Math.Min(minX, point.X); maxX = Math.Max(maxX, point.X);
                minZ = Math.Min(minZ, point.Z); maxZ = Math.Max(maxZ, point.Z);
            }
        }
        return minX > maxX ? RectangleF.Empty : RectangleF.FromLTRB(minX, minZ, maxX, maxZ);
    }

    private void ApplyFilter() {
        var text = _search.Text.Trim();
        IEnumerable<DeliveryRow> filtered = _rows;
        // Each switch does nothing in the middle, which is where both of them start.
        if (_gameFilter.Position != 0) {
            var game = _gameFilter.Position < 0 ? "Ets2" : "Ats";
            filtered = filtered.Where(r => r.Hra == game);
        }
        if (_oversizeChip.On) filtered = filtered.Where(r => r.Special);
        if (_lateChip.On) filtered = filtered.Where(r => r.Meskala);
        if (_damagedChip.On) filtered = filtered.Where(r => r.Kolizie > 0);
        if (text.Length > 0) {
            filtered = filtered.Where(r =>
                r.Odkial.Contains(text, StringComparison.OrdinalIgnoreCase) ||
                r.Kam.Contains(text, StringComparison.OrdinalIgnoreCase) ||
                r.Naklad.Contains(text, StringComparison.OrdinalIgnoreCase) ||
                r.Tahac.Contains(text, StringComparison.OrdinalIgnoreCase));
        }

        // Ordered here rather than by the list, which draws what it is given. Sorting
        // by a formatted figure would sort "1 000 km" next to "999 km", so the order
        // is taken from the stored number and the column merely names which one.
        var shown = filtered.ToList();
        Comparison<DeliveryRow> by = _list.SortedBy switch {
            nameof(DeliveryRow.Odkial) => (a, b) => string.Compare(a.Odkial, b.Odkial, StringComparison.CurrentCultureIgnoreCase),
            nameof(DeliveryRow.Kam) => (a, b) => string.Compare(a.Kam, b.Kam, StringComparison.CurrentCultureIgnoreCase),
            nameof(DeliveryRow.Naklad) => (a, b) => string.Compare(a.Naklad, b.Naklad, StringComparison.CurrentCultureIgnoreCase),
            nameof(DeliveryRow.DistanceKm) => (a, b) => a.DistanceKm.CompareTo(b.DistanceKm),
            nameof(DeliveryRow.Zarobok) => (a, b) => a.Zarobok.CompareTo(b.Zarobok),
            nameof(DeliveryRow.Stav) => (a, b) => string.Compare(a.Stav, b.Stav, StringComparison.Ordinal),
            _ => (a, b) => a.Datum.CompareTo(b.Datum),
        };
        shown.Sort((a, b) => _list.Descending ? by(b, a) : by(a, b));

        // What the page says beside its own name, in the units of the game being shown:
        // with both games in the list there is no one unit, so it says nothing.
        var one = _gameFilter.Position != 0 ? (_gameFilter.Position < 0 ? "Ets2" : "Ats")
                : shown.Select(r => r.Hra).Distinct().Count() == 1 ? shown.FirstOrDefault()?.Hra ?? "" : "";
        if (one.Length > 0) {
            var u = Units.For(_settings.Units, one);
            _historyTotals = $"{shown.Count} {Strings.T("list.deliveries")}  ·  {u.FormatDistance(shown.Sum(r => r.DistanceKm))}"
                           + $"  ·  {u.FormatMoney(shown.Sum(r => r.Zarobok))}";
        } else {
            _historyTotals = $"{shown.Count} {Strings.T("list.deliveries")}";
        }
        _historyHead?.Invalidate();

        _list.Show(shown);
        RefreshFrame();
    }

    private void ReloadStats() {
        var slice = StatsSlice();
        var s = _store.GetStats(slice);
        var roam = _store.FreeroamTotals(slice);
        // The same length of time immediately before this one, so a figure can say
        // what it did as well as what it is. There is no before for all of history.
        var before = slice.Previous is { } earlier ? _store.GetStats(earlier) : null;
        // The units of the game these figures are about, not of the game last played.
        // With the switch set to one game and the units following the game, a page of
        // European deliveries was being answered in miles because the last drive
        // happened to be American.
        var u = Units.For(_settings.Units, slice.Game ?? _store.MostRecentGame());
        var gameHours = s.TotalGameMinutes / 60.0;
        var realHours = s.TotalDrivingMs / 3600000.0;
        // Distances are simulated km, so they pair with game hours - dividing by real
        // hours would report the time-compression factor as speed (~770 km/h).
        var avg = gameHours > 0.01 ? s.TimedDistanceKm / gameHours : 0;

        Quiet(_statsGrid, () => Fill(s, before, roam, u, gameHours, realHours, avg));
    }

    private void Fill(StatsSummary s, StatsSummary? before, (double DistanceKm, int Stretches) roam, Units u,
                      double gameHours, double realHours, double avg) {
        _statsGrid.Controls.Clear();

        void Section(int row, string heading, params Control[] tiles) {
            _statsGrid.Controls.Add(StatHeading(heading), 0, row);
            _statsGrid.Controls.Add(TileRow(tiles), 0, row + 1);
        }

        // How a figure moved against the same stretch of time before it. Shown as a
        // percentage and never as a verdict: driving less this week than last is not
        // worse, it is a week. A period that had nothing to compare against says so
        // rather than claiming an infinite rise.
        string? Change(double now, Func<StatsSummary, double> of) {
            if (before is null) return null;
            var then = of(before);
            // Whole phrases per language rather than a figure with words glued to it:
            // Slovak wants the period in a different case in the two sentences, and
            // gluing gets one of them wrong whichever way round it is done.
            if (then <= 0) return now > 0 ? Strings.T("stats.noneBefore." + _statsPeriod) : null;
            var move = (now - then) / then * 100;
            return $"{(move >= 0 ? "+" : "")}{move:0} % {Strings.T("stats.vs." + _statsPeriod)}";
        }

        Section(0, Strings.T("stats.headingOverall"),
            // Only the states there are any of. Spelling out "0 review · 0 rejected"
            // took the width of the tile to say nothing.
            StatTile(Strings.T("stats.deliveries"), s.TotalDeliveries.ToString(),
                Change(s.TotalDeliveries, x => x.TotalDeliveries) ?? string.Join(" · ", new[] {
                    s.Accepted > 0 ? $"{s.Accepted} {Label("accepted")}" : null,
                    s.Review > 0 ? $"{s.Review} {Label("review")}" : null,
                    s.Rejected > 0 ? $"{s.Rejected} {Label("rejected")}" : null,
                }.OfType<string>())),
            StatTile(Strings.T("stats.distance"), u.FormatDistance(s.TotalDistanceKm),
                Change(s.TotalDistanceKm, x => x.TotalDistanceKm) ?? (roam.DistanceKm > 0
                    ? $"{u.FormatDistance(s.TotalDistanceKm + roam.DistanceKm)} {Strings.T("stats.withFreeroam")}"
                    : null)),
            // Money is the one figure that cannot simply be summed: two games, two
            // currencies. Told apart by game and put back together only once there
            // is a currency to put it together in.
            StatTile(Strings.T("stats.revenue"), Units.FormatTotal(_settings.Units, s.RevenueByGame),
                Change(s.TotalRevenue, x => x.TotalRevenue)
                    ?? (s.TotalPenalties > 0
                        ? $"{Strings.T("stats.penalties")} {Units.FormatTotal(_settings.Units, s.PenaltiesByGame)}"
                        : null)),
            // Diesel in the figure and the battery under it, never added together:
            // one is a volume and the other is energy, and their sum is a number of
            // nothing. The comparison against the period before gives way to the
            // battery when there is one, rather than the two sharing a line.
            StatTile(Strings.T("stats.fuel"), u.FormatVolume(s.TotalFuelL),
                s.TotalBatteryKwh > 0.5
                    ? $"{Units.FormatEnergy(s.TotalBatteryKwh)} {Strings.T("stats.battery")}"
                    : Change(s.TotalFuelL, x => x.TotalFuelL)),
            // Driving that carried nothing. Shown beside the deliveries rather than
            // folded into them: it is real distance, but it earned nothing and was
            // never judged, so adding it to the delivery figure would flatter both.
            StatTile(Strings.T("stats.freeroam"), u.FormatDistance(roam.DistanceKm),
                roam.Stretches > 0 ? $"{roam.Stretches}×" : null));

        Section(2, Strings.T("stats.headingDriving"),
            StatTile(Strings.T("stats.time"), $"{gameHours:0.0} {Strings.T("stats.gameTime")}",
                Change(s.TotalGameMinutes, x => x.TotalGameMinutes)
                    ?? $"{realHours:0.0} {Strings.T("stats.realTime")}"),
            StatTile(Strings.T("stats.avgSpeed"), u.FormatSpeed(avg)),
            // No note under it: it used to repeat its own caption back, word for word.
            StatTile(Strings.T("stats.style"), $"{s.Clean} / {s.Spirited}"));

        Section(4, Strings.T("stats.headingIncidents"),
            StatTile(Strings.T("stats.collisions"), s.TotalCollisions.ToString(),
                Change(s.TotalCollisions, x => x.TotalCollisions)),
            StatTile(Strings.T("stats.finesTotal"), Units.FormatTotal(_settings.Units, s.FinesByGame),
                Change(s.TotalFines, x => x.TotalFines)),
            StatTile(Strings.T("stats.late"), s.LateDeliveries.ToString()));

        Section(6, Strings.T("stats.headingFavourites"),
            StatTile(Strings.T("stats.favTruck"), s.FavoriteTruck ?? "?"),
            StatTile(Strings.T("stats.favRoute"), s.FavoriteRoute ?? "?"),
            StatTile(Strings.T("stats.favCargo"), s.FavoriteCargo ?? "?"));

        SmoothPainting(_statsGrid);
    }

    // ---------- actions ----------

    private void Export(string format) {
        using var dlg = new SaveFileDialog {
            FileName = $"deliveries-{DateTime.Now:yyyyMMdd-HHmmss}.{format}",
            Filter = format == "csv" ? "CSV|*.csv" : "JSON|*.json",
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        _store.Export(dlg.FileName, format);
        MessageBox.Show(this, Strings.T("msg.exported") + "\n" + dlg.FileName, Strings.T("msg.database"));
    }

    private void ImportTrucksBook() {
        using var dlg = new OpenFileDialog {
            Title = Strings.T("msg.importPick"),
            Filter = "CSV|*.csv",
            InitialDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        var result = new TrucksBookImport(_store).Import(dlg.FileName);

        var message = $"{Strings.T("msg.imported")} {result.Imported}\n{Strings.T("msg.alreadyThere")} {result.Skipped}";
        if (result.Uncredited > 0) {
            var u = CurrentUnits();
            message += "\n\n" + string.Format(Strings.T("msg.uncredited"), result.Uncredited,
                u.FormatDistance(result.UncreditedKm, "0"));
        }
        if (result.Problems.Count > 0) {
            message += "\n\n" + Strings.T("msg.problems") + "\n" + string.Join("\n", result.Problems.Take(5));
        }

        MessageBox.Show(this, message, Strings.T("msg.importTitle"));
        ReloadHistory();
        ReloadStats();
    }

    private void DoBackup() {
        var path = _store.Backup();
        MessageBox.Show(this, Strings.T("msg.backupSaved") + "\n" + path, Strings.T("button.backup"));
    }

    private void DoRestore() {
        using var dlg = new OpenFileDialog {
            InitialDirectory = Path.Combine(DeliveryStore.DefaultDir(), "backups"),
            Filter = "Databaza|*.db",
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        var confirm = MessageBox.Show(this, Strings.T("msg.restoreConfirm"),
            Strings.T("msg.restoreTitle"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
        if (confirm != DialogResult.Yes) return;

        try {
            // The engine holds the database open, so it has to let go before the file
            // underneath it can be replaced; it is restarted straight after.
            _engine?.Dispose();
            _engine = null;
            _store.Dispose();

            var safety = DeliveryStore.RestoreFromBackup(dlg.FileName);
            MessageBox.Show(this,
                $"{Strings.T("msg.restoreDone")}\n{safety}\n\n{Strings.T("msg.restartingApp")}",
                Strings.T("msg.restoreTitle"));

            Application.Restart();
        } catch (Exception ex) {
            MessageBox.Show(this, Strings.T("msg.restoreFailed") + "\n" + ex.Message,
                Strings.T("msg.error"), MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
