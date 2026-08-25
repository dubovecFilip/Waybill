using System.Data;
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
internal class DarkMenuColours : ProfessionalColorTable {
    private static readonly Color Bg = Color.FromArgb(30, 34, 39);
    private static readonly Color Raised = Color.FromArgb(38, 43, 50);
    private static readonly Color Edge = Color.FromArgb(48, 54, 62);
    private static readonly Color Highlight = Color.FromArgb(52, 45, 33);

    public override Color MenuStripGradientBegin => Bg;
    public override Color MenuStripGradientEnd => Bg;
    public override Color ToolStripDropDownBackground => Bg;
    public override Color ImageMarginGradientBegin => Bg;
    public override Color ImageMarginGradientMiddle => Bg;
    public override Color ImageMarginGradientEnd => Bg;
    public override Color MenuItemSelected => Highlight;
    public override Color MenuItemSelectedGradientBegin => Highlight;
    public override Color MenuItemSelectedGradientEnd => Highlight;
    public override Color MenuItemPressedGradientBegin => Raised;
    public override Color MenuItemPressedGradientMiddle => Raised;
    public override Color MenuItemPressedGradientEnd => Raised;
    public override Color MenuItemBorder => Edge;
    public override Color MenuBorder => Edge;
    public override Color SeparatorDark => Edge;
    public override Color SeparatorLight => Edge;
}

public partial class MainForm : Form {
    // One dark palette for the whole window, so nothing has to invent a colour
    // inline. There is no light variant on purpose: two of them means every control
    // has to be checked twice and one of them is always the neglected one.
    private static readonly Color Canvas = Color.FromArgb(22, 25, 29);
    private static readonly Color Surface = Color.FromArgb(30, 34, 39);
    private static readonly Color Raised = Color.FromArgb(38, 43, 50);
    private static readonly Color Line = Color.FromArgb(48, 54, 62);
    private static readonly Color Ink = Color.FromArgb(228, 233, 240);
    private static readonly Color Muted = Color.FromArgb(138, 148, 163);
    // Amber rather than blue: it is the colour of a truck's indicators and warning
    // boards, and it stays legible on a dark ground where blue goes muddy.
    private static readonly Color Accent = Color.FromArgb(232, 168, 74);
    private static readonly Color AccentSoft = Color.FromArgb(52, 45, 33);

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
    /// <summary>The timeline column, which lives at width zero until asked for.</summary>
    private Panel? _detailSide;
    private RouteView? _cardMap;
    private HeightView? _cardProfile;
    private System.Windows.Forms.Timer? _detailSlide;

    private List<DeliveryRow> _rows = new();
    private readonly Dictionary<string, GameRoutes> _routes = new();
    /// <summary>For the map's glyph buttons, which have no room to say in words what
    /// they do.</summary>
    private readonly ToolTip _tips = new();
    private RouteView? _mapPage;
    private readonly ComboBox _mapGame = new();
    /// <summary>The games behind the entries in <see cref="_mapGame"/>, as the
    /// database spells them. The box shows them the way the game does.</summary>
    private List<string> _mapGames = new();

    public MainForm() {
        Text = "Waybill";
        Width = 1100;
        Height = 720;
        MinimumSize = new Size(900, 560);
        Font = new Font("Segoe UI", 9F);
        StartPosition = FormStartPosition.CenterScreen;

        // The window icon comes from the same .ico the exe is built with, so the
        // taskbar, alt-tab and the title bar all match.
        var iconPath = Path.Combine(AppContext.BaseDirectory, "waybill.ico");
        if (File.Exists(iconPath)) {
            try { Icon = new Icon(iconPath); } catch { /* a missing icon is not worth failing over */ }
        }

        Strings.Language = _settings.Language;

        // Before anything asks where the games are, including the menu built below.
        foreach (var game in new[] { SimGame.Ats, SimGame.Ets2 }) {
            GameLauncher.SetOverride(game, _settings.PathFor(game));
        }

        BuildLayout();

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
    private void BuildLayout() {
        Controls.Clear();
        BackColor = Canvas;

        var content = BuildContent();
        var sidebar = BuildSidebar();
        var menu = BuildMenu();

        // Docked children stack in reverse order of adding, so the filling one goes
        // in first and the outermost edges last.
        Controls.Add(content);
        Controls.Add(sidebar);
        Controls.Add(menu);
        MainMenuStrip = menu;

        ReloadHistory();
        ReloadStats();
        ShowPage(_page);
        UseDarkScrollbars(this);
    }

    // ---------- sidebar ----------

    /// <summary>Deliveries and statistics used to be tabs. As a sidebar they read as
    /// two places in the app rather than two folders inside one, and the labels get
    /// room to be words instead of cramped tab strips.</summary>
    private Panel BuildSidebar() {
        var bar = new Panel { Dock = DockStyle.Left, Width = 176, BackColor = Surface, Padding = new Padding(12, 16, 12, 12) };
        var edge = new Panel { Dock = DockStyle.Right, Width = 1, BackColor = Line };

        // Added bottom-up so the first entry ends up on top.
        bar.Controls.Add(NavButton("stats", Strings.T("tab.stats")));
        bar.Controls.Add(NavButton("map", Strings.T("tab.map")));
        bar.Controls.Add(NavButton("deliveries", Strings.T("tab.deliveries")));
        bar.Controls.Add(NavButton("live", Strings.T("tab.live")));
        bar.Controls.Add(edge);
        return bar;
    }

    private Button NavButton(string page, string label) {
        var selected = _page == page;
        var b = new Button {
            Text = "   " + label,
            Dock = DockStyle.Top,
            Height = 38,
            Margin = new Padding(0, 0, 0, 6),
            TextAlign = ContentAlignment.MiddleLeft,
            FlatStyle = FlatStyle.Flat,
            BackColor = selected ? AccentSoft : Surface,
            ForeColor = selected ? Accent : Ink,
            Font = new Font("Segoe UI", 10F, selected ? FontStyle.Bold : FontStyle.Regular),
            Cursor = Cursors.Hand,
            Tag = page,
        };
        b.FlatAppearance.BorderSize = 0;
        b.FlatAppearance.MouseOverBackColor = selected ? AccentSoft : Canvas;
        b.Click += (_, _) => ShowPage(page);
        return b;
    }

    private void ShowPage(string page) {
        _page = page;

        foreach (Control c in _content.Controls) {
            c.Visible = (string?)c.Tag == page;
        }

        // The buttons carry their own selected styling, so they are restyled rather
        // than rebuilt, which would lose the click that is still being handled.
        foreach (var b in Controls.OfType<Panel>().SelectMany(p => p.Controls.OfType<Button>())) {
            if (b.Tag is not string tag) continue;
            var selected = tag == page;
            b.BackColor = selected ? AccentSoft : Surface;
            b.ForeColor = selected ? Accent : Ink;
            b.Font = new Font("Segoe UI", 10F, selected ? FontStyle.Bold : FontStyle.Regular);
            b.FlatAppearance.MouseOverBackColor = selected ? AccentSoft : Canvas;
        }
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
        var map = BuildMapPage();
        map.Tag = "map";
        var stats = BuildStatsPage();
        stats.Tag = "stats";

        _content.Controls.Add(live);
        _content.Controls.Add(deliveries);
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

        _mapGame.DropDownStyle = ComboBoxStyle.DropDownList;
        _mapGame.FlatStyle = FlatStyle.Flat;
        _mapGame.BackColor = Raised;
        _mapGame.ForeColor = Ink;
        _mapGame.Width = 150;
        _mapGame.SelectedIndexChanged -= OnMapGameChanged;
        _mapGame.SelectedIndexChanged += OnMapGameChanged;

        var bar = new Panel { Dock = DockStyle.Top, Height = 40, BackColor = Canvas };
        bar.Controls.Add(_mapGame);

        var frame = new Panel { Dock = DockStyle.Fill, BackColor = Line, Padding = new Padding(1) };
        frame.Controls.Add(map);
        MapButtons(frame, map, null);

        page.Controls.Add(frame);
        page.Controls.Add(bar);
        return page;
    }

    private void OnMapGameChanged(object? sender, EventArgs e) => ReloadMapPage();

    /// <summary>Fills the map page from whatever the history currently holds. The
    /// game list is built from the deliveries themselves rather than from the two
    /// the app knows about, so a profile that has only ever driven one of them is
    /// not offered an empty picture of the other.</summary>
    private void ReloadMapPage() {
        if (_mapPage is not { } map) return;

        var games = _rows.Select(r => r.Hra).Where(g => g.Length > 0).Distinct().OrderBy(g => g).ToList();
        if (!_mapGames.SequenceEqual(games)) {
            var was = _mapGame.SelectedIndex >= 0 && _mapGame.SelectedIndex < _mapGames.Count
                ? _mapGames[_mapGame.SelectedIndex] : null;
            _mapGames = games;
            _mapGame.Items.Clear();
            // Shown as the game calls itself, kept as the database spells it.
            foreach (var g in games) _mapGame.Items.Add(GameName(g));
            _mapGame.SelectedIndex = was is not null && games.IndexOf(was) >= 0 ? games.IndexOf(was)
                : games.Count > 0 ? 0 : -1;
        }
        if (_mapGame.SelectedIndex < 0 || _mapGame.SelectedIndex >= _mapGames.Count) {
            map.Show(new List<RouteLayer>(), 0, new List<CityAnchor>());
            return;
        }

        var game = _mapGames[_mapGame.SelectedIndex];
        var routes = RoutesFor(game);
        map.Show(Layers(routes), 0, routes.Cities, null, routes.RunUps.Concat(_store.FreeroamRoutes(game)));
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
        settings.DropDownItems.Add(BuildDiscordMenu());
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

    private void StartEngine() {
        _engine = new TrackerEngine(_store);
        _engine.Message += m => BeginInvoke(() => AddLog(m));
        _engine.JobStarted += j => BeginInvoke(() => AddLog($"{Strings.T("msg.jobStart")}  {j.SourceCity} -> {j.DestinationCity} ({j.Cargo})"));
        _engine.JobResumed += j => BeginInvoke(() => AddLog($"{Strings.T("msg.jobResume")}  {j.SourceCity} -> {j.DestinationCity}"));
        _engine.Noted += e => BeginInvoke(() => AddLog(NoteLine(e)));
        _engine.JobFinished += r => BeginInvoke(() => {
            AddLog($"{Strings.T("msg.jobEnd")}  {r.SourceCity} -> {r.DestinationCity}: {r.DistanceKm:0.0} km, {r.Validation.Status}");
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
            var line = UnitLine(label, $"{name}   ·   {unit.Plate}   ·   {Damage(unit.Damage)}",
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
        parts.Add($"{trailers}x {Strings.T("value.trailer")}");
        if (dollies > 0) parts.Add($"{dollies}x {Strings.T("value.dolly")}");
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
        var page = new Panel { Dock = DockStyle.Fill, Padding = new Padding(16), BackColor = Canvas };

        // Flow layout rather than fixed coordinates, so buttons size to their own
        // text and nothing gets clipped when a label changes.
        var bar = new FlowLayoutPanel {
            Dock = DockStyle.Top,
            Height = 36,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(0, 3, 0, 3),
        };

        // A TextBox has no padding of its own, so its text sits hard against the
        // border on both sides. Wrapping it in a panel that carries the background and
        // the padding gives the text room to sit in without touching anything.
        _search.PlaceholderText = Strings.T("search.placeholder");
        _search.BorderStyle = BorderStyle.None;
        _search.BackColor = Raised;
        _search.ForeColor = Ink;
        _search.Dock = DockStyle.Fill;
        _search.TextChanged += (_, _) => ApplyFilter();

        var searchBox = new Panel {
            Width = 260, Height = 28, Margin = new Padding(0, 3, 8, 3),
            BackColor = Raised, Padding = new Padding(10, 6, 10, 4),
        };
        searchBox.Controls.Add(_search);

        // Two switches rather than a list of states. Filtering by verdict was
        // filtering by something that is already a dot on every row and that almost
        // never has more than one value worth asking for; which game and which kind
        // of load are the two questions the history is actually read with, and both
        // have a natural middle meaning both.
        _cargoFilter.RightBadge = (g, r) => HazardStripes(g, r, 210);
        // The controls outlive the layout, which is rebuilt when the language
        // changes, so the words are set here rather than only where they are made.
        _gameFilter.Retext(GameName("Ets2"), Strings.T("filter.both"), GameName("Ats"));
        _cargoFilter.Retext(Strings.T("filter.ordinary"), Strings.T("filter.both"), Strings.T("filter.oversize"));
        foreach (var filter in new[] { _gameFilter, _cargoFilter }) {
            filter.Margin = new Padding(0, 3, 10, 3);
            filter.BackColor = Canvas;
            filter.Changed -= OnFilterChanged;
            filter.Changed += OnFilterChanged;
        }

        // Only what changes the view of the list. Exporting, backing up and restoring
        // used to sit here too, which put "show me fewer rows" and "replace the whole
        // database" one button apart; they live under Data now.
        bar.Controls.Add(searchBox);
        bar.Controls.Add(_gameFilter);
        bar.Controls.Add(_cargoFilter);
        bar.Controls.Add(MakeButton(Strings.T("button.refresh"), () => { ReloadHistory(); ReloadStats(); }));

        _grid.Dock = DockStyle.Fill;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.MultiSelect = false;
        // Fixed widths and a horizontal scrollbar rather than columns that redistribute
        // themselves every time the window is resized: a column should stay where the
        // eye last found it, and narrowing the window should not squeeze fourteen of
        // them into unreadable slivers.
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
        _grid.ScrollBars = ScrollBars.Both;
        // The gutter down the left of the list, carrying the two things a row says
        // without words: its verdict, and whether the load was oversize. The row
        // header is already there and already sits left of the date, so both live in
        // it rather than in columns of their own squeezed in among the figures.
        _grid.RowHeadersVisible = true;
        _grid.RowHeadersWidth = GutterWidth;
        _grid.RowHeadersWidthSizeMode = DataGridViewRowHeadersWidthSizeMode.DisableResizing;
        _grid.RowHeadersDefaultCellStyle.BackColor = Canvas;
        _grid.RowHeadersDefaultCellStyle.SelectionBackColor = Canvas;
        _grid.EnableHeadersVisualStyles = false;
        _grid.CellPainting -= OnRowMarker;
        _grid.CellPainting += OnRowMarker;
        _grid.EditMode = DataGridViewEditMode.EditOnEnter;
        StyleGrid(_grid);

        // Detach before attaching: these controls are reused when the layout is
        // rebuilt for a language change, and handlers added a second time would fire
        // a second time.
        _grid.DataBindingComplete -= OnGridBound;
        _grid.DataBindingComplete += OnGridBound;
        _grid.CellEndEdit -= OnGridCellEndEdit;
        _grid.CellEndEdit += OnGridCellEndEdit;
        _grid.CellFormatting -= OnGridCellFormatting;
        _grid.CellFormatting += OnGridCellFormatting;

        // Opening a delivery is a double click: a single one is how a row gets
        // selected while walking the list, and it would fling the card open on every
        // press of an arrow key.
        _grid.CellDoubleClick -= OnGridDoubleClick;
        _grid.CellDoubleClick += OnGridDoubleClick;
        _grid.KeyDown -= OnGridKeyDown;
        _grid.KeyDown += OnGridKeyDown;

        var hint = new Label {
            Dock = DockStyle.Bottom, Height = 24, Text = Strings.T("list.openHint"),
            ForeColor = Muted, Font = new Font("Segoe UI", 8.5F),
            Padding = new Padding(2, 6, 0, 0), BackColor = Canvas,
        };

        page.Controls.Add(_grid);
        page.Controls.Add(hint);
        page.Controls.Add(bar);
        return page;
    }

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
    private const int GutterWidth = 28;
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
    private void OnRowMarker(object? sender, DataGridViewCellPaintingEventArgs e) {
        if (e.ColumnIndex != -1 || e.RowIndex < 0 || e.RowIndex >= _grid.Rows.Count) return;
        // The surface is declared as one that may not be there, so it is taken once
        // and checked rather than reached through twice on trust.
        if (e.Graphics is not { } g) return;

        using (var clear = new SolidBrush(Canvas)) g.FillRectangle(clear, e.CellBounds);
        if (_grid.Rows[e.RowIndex].DataBoundItem is DeliveryRow row) {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var dot = new RectangleF(
                e.CellBounds.Left + (GutterWidth - StripeWidth - 9) / 2f,
                e.CellBounds.Top + (e.CellBounds.Height - 9) / 2f, 9, 9);
            using (var brush = new SolidBrush(VerdictColour(row.Stav))) g.FillEllipse(brush, dot);

            if (row.Special) {
                HazardStripes(g, new RectangleF(
                    e.CellBounds.Right - StripeWidth, e.CellBounds.Top,
                    StripeWidth, Math.Max(e.CellBounds.Height, 1)), 210);
            }
        }
        e.Handled = true;
    }

    /// <summary>The verdict as a colour, in one place, so the dot in the list and the
    /// sample in the legend cannot end up meaning different things.</summary>
    private static Color VerdictColour(string status) => status switch {
        "rejected" => Color.FromArgb(226, 116, 104),
        "review" => Color.FromArgb(226, 168, 74),
        "imported" => Color.FromArgb(112, 172, 214),
        _ => Color.FromArgb(96, 176, 128),
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
        // The row header is left alone here: on the history list it is the gutter an
        // oversize load is marked in, and this runs after that is set up.
    }

    private static Button MakeButton(string text, Action onClick) {
        var b = new Button {
            Text = text, AutoSize = true, Height = 28, Margin = new Padding(0, 3, 6, 3),
            Padding = new Padding(10, 0, 10, 0),
            FlatStyle = FlatStyle.Flat, BackColor = Raised, ForeColor = Ink, Cursor = Cursors.Hand,
        };
        b.FlatAppearance.BorderColor = Line;
        b.FlatAppearance.MouseOverBackColor = Line;
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
        var page = new Panel { Dock = DockStyle.Fill, Padding = new Padding(16), BackColor = Canvas };

        // Two questions asked above the figures rather than answered by extra
        // sections below them: which stretch of time, and which game. Sections would
        // have taken the page past the one screen it fits on, and comparing this week
        // with last week by reading two blocks of tiles is not comparing at all.
        var bar = new FlowLayoutPanel {
            Dock = DockStyle.Top, Height = 40, WrapContents = false, BackColor = Canvas,
            Padding = new Padding(0, 0, 0, 8),
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

        bar.Controls.Add(_statsPeriods);
        bar.Controls.Add(_statsGame);

        _statsGrid.Dock = DockStyle.Fill;
        _statsGrid.BackColor = Canvas;
        // One column of sections, each section holding its own row of tiles. The
        // outer grid used to be four columns wide and a section was laid straight
        // into it, which meant adding a fifth figure to any section silently pushed
        // the next heading out of its cell and shifted every tile below it along by
        // one. A section now owns its own width and can hold as many as it likes.
        _statsGrid.ColumnCount = 1;
        _statsGrid.RowCount = 8;
        _statsGrid.Padding = new Padding(0);

        _statsGrid.ColumnStyles.Clear();
        _statsGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

        // Four sections, each a heading of its own height above a row of tiles that
        // takes an equal quarter of what is left.
        _statsGrid.RowStyles.Clear();
        for (var i = 0; i < 4; i++) {
            _statsGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
            _statsGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 25F));
        }

        page.Controls.Add(_statsGrid);
        page.Controls.Add(bar);
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

    /// <summary>One figure: the number large enough to read at a glance, the caption
    /// under it out of the way.</summary>
    private static Control StatTile(string caption, string value, string? note = null) {
        var card = new Panel {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 12, 12),
            BackColor = Surface,
            Padding = new Padding(16, 10, 16, 10),
        };

        // Three rows that take what they need rather than what they were given: the
        // caption and the note as tall as one line of their own type, the figure
        // everything left over. Fixed heights meant a tile in a shorter section
        // wasted space and one in a crowded window clipped.
        var inner = new TableLayoutPanel {
            Dock = DockStyle.Fill, BackColor = Surface, Margin = new Padding(0), Padding = new Padding(0),
            ColumnCount = 1, RowCount = 4,
        };
        inner.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        // Each line takes exactly its own height and the slack goes to the bottom, so
        // the three of them stay together at the top of the tile. Giving the figure
        // the slack instead pushed the note down to the floor, away from what it was
        // a note about.
        inner.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        inner.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        inner.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        inner.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        // One line each, shortened with an ellipsis rather than wrapped. A wrapped
        // note grew past the height a tile has and lost its last line off the bottom
        // with nothing to show for it; shortened, it says how much is missing and the
        // tooltip has the rest.
        var captionLabel = new Label {
            Dock = DockStyle.Fill, AutoSize = false, Height = 17, Text = caption.ToUpperInvariant(),
            ForeColor = Muted, Font = new Font("Segoe UI", 7.5F, FontStyle.Bold),
            Margin = new Padding(0), AutoEllipsis = true,
        };
        var valueLabel = new Label {
            Dock = DockStyle.Fill, AutoSize = true, Text = value, ForeColor = Ink,
            Font = new Font("Segoe UI", 17F, FontStyle.Bold),
            Margin = new Padding(0, 3, 0, 3), AutoEllipsis = true,
        };
        var noteLabel = new Label {
            Dock = DockStyle.Fill, AutoSize = false, Height = 18, Text = note ?? "",
            ForeColor = Muted, Font = new Font("Segoe UI", 8F),
            Margin = new Padding(0), AutoEllipsis = true,
        };


        inner.Controls.Add(captionLabel, 0, 0);
        inner.Controls.Add(valueLabel, 0, 1);
        inner.Controls.Add(noteLabel, 0, 2);
        card.Controls.Add(inner);

        // Nothing is allowed to disappear silently: whatever ends up shortened still
        // says the whole of itself when pointed at.
        var tips = new ToolTip();
        tips.SetToolTip(captionLabel, caption);
        tips.SetToolTip(valueLabel, value);
        if (!string.IsNullOrEmpty(note)) tips.SetToolTip(noteLabel, note);
        // Handed to the row, which sizes every figure in a section together.
        card.Tag = valueLabel;
        return card;
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
    private static Control TileRow(Control[] tiles) {
        var row = new TableLayoutPanel {
            Dock = DockStyle.Fill, BackColor = Canvas,
            Margin = new Padding(0), Padding = new Padding(0),
            ColumnCount = Math.Max(tiles.Length, 1), RowCount = 1,
        };
        row.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        for (var i = 0; i < tiles.Length; i++) {
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F / tiles.Length));
            row.Controls.Add(tiles[i], i, 0);
        }
        FitTogether(row, tiles.Select(t => t.Tag as Label).OfType<Label>().ToList(), 17F, 9F);
        return row;
    }

    private static Control StatHeading(string text) => new Label {
        Dock = DockStyle.Fill, Text = text, TextAlign = ContentAlignment.BottomLeft,
        Margin = new Padding(0, 4, 0, 6), BackColor = Canvas,
        ForeColor = Ink, Font = new Font("Segoe UI", 10.5F, FontStyle.Bold),
    };

    // ---------- delivery detail ----------

    /// <summary>One delivery on a card of its own. The list carries what is worth
    /// scanning down a column; everything else about a drive lives here, where it has
    /// room to be read rather than squeezed into another column.</summary>
    private void ShowDetail(long id) {
        var d = _store.Detail(id);
        if (d == null) return;
        var u = Units.For(_settings.Units, d.Game);

        _detailPage.SuspendLayout();
        _detailPage.Controls.Clear();

        // Added bottom-up, since docked children stack in reverse.
        _detailPage.Controls.Add(DetailBody(d, u));
        _detailPage.Controls.Add(DetailHeader(d, u));

        _detailPage.ResumeLayout();
        ShowPage("detail");
        // Built just now, so its scrolling parts have not been asked for the dark
        // theme yet and would come up as bright white bars.
        UseDarkScrollbars(_detailPage);
    }

    private Control DetailHeader(DeliveryDetail d, Units u) {
        var head = new Panel { Dock = DockStyle.Top, Height = 108, BackColor = Surface, Padding = new Padding(24, 16, 24, 12) };



        // Quiet and small. These are ways out of the card, not the point of it, and
        // docking them filled the header's whole height with two slabs.
        Button Action(string text, int width) {
            var b = new Button {
                Text = text, Width = width, Height = 26, AutoSize = false,
                FlatStyle = FlatStyle.Flat, BackColor = Surface, ForeColor = Muted,
                Font = new Font("Segoe UI", 8.5F), Cursor = Cursors.Hand,
                Margin = new Padding(8, 0, 0, 0), TextAlign = ContentAlignment.MiddleCenter,
            };
            b.FlatAppearance.BorderColor = Line;
            b.FlatAppearance.MouseOverBackColor = Raised;
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
            Dock = DockStyle.Top, Height = 36, Text = $"{d.SourceCity}  →  {d.DestinationCity}",
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
        if (d.SpecialTransport) {
            var stripe = new Panel { Dock = DockStyle.Left, Width = 10, BackColor = Surface };
            stripe.Paint += (_, e) => HazardStripes(e.Graphics, new RectangleF(0, 0, stripe.Width, stripe.Height), 210);
            _tips.SetToolTip(stripe, Strings.T("detail.special"));
            head.Controls.Add(stripe);
            head.Padding = new Padding(14, 16, 24, 12);
        }
        return head;
    }

    /// <summary>Slides the timeline in and out rather than showing or hiding it: the
    /// movement is what says where the panel came from, and a column that simply
    /// appears reads as the page having jumped.</summary>
    private void ToggleTimeline(Button button) {
        if (_detailSide is not { } side) return;

        var target = side.Width > 0 ? 0 : 480;
        button.Text = Strings.T("detail.timelineOpen") + (target > 0 ? "   ▸" : "   ◂");

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
        using var dialog = new SaveFileDialog {
            Title = Strings.T("sheet.saveTitle"),
            Filter = "PNG|*.png",
            FileName = WaybillSheet.SuggestedName(d),
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        try {
            var route = RoutesFor(d.Game).Routes.TryGetValue(d.Id, out var points) ? points : new List<RoutePoint>();
            var written = WaybillSheet.Save(d, _store.TimelineRows(d.Id, u), route, u, dialog.FileName, 300f);
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
        map.Show(Layers(RoutesFor(d.Game)), d.Id, RoutesFor(d.Game).Cities,
                 _store.TimelineRows(d.Id, u), RoutesFor(d.Game).RunUps.Concat(_store.FreeroamRoutes(d.Game)));

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
        MapButtons(box, map, () => BigMap(d, u), replay: true);
        return box;
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
    private void MapButtons(Control host, RouteView map, Action? expand, bool replay = false) {
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

        var layers = Glyph("≡", Strings.T("map.layers"), () => { });
        layers.Click += (_, _) => LayerMenu(map).Show(layers, new Point(0, layers.Height));
        // Only where one delivery is singled out: there is nothing to replay on the
        // map of everything, where no drive is more the subject than any other.
        if (replay) {
            Glyph("▶", Strings.T("map.replay"), () => {
                map.Replay();
                if (_cardProfile is { IsDisposed: false } beside) beside.Replay();
            });
        }
        Glyph("⟲", Strings.T("map.fit"), map.Fit);
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
    private sealed class DarkMenuColours : ProfessionalColorTable {
        public override Color ToolStripDropDownBackground => Surface;
        public override Color ImageMarginGradientBegin => Surface;
        public override Color ImageMarginGradientMiddle => Surface;
        public override Color ImageMarginGradientEnd => Surface;
        public override Color MenuItemSelected => Raised;
        public override Color MenuItemSelectedGradientBegin => Raised;
        public override Color MenuItemSelectedGradientEnd => Raised;
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
        Mark("fine", Strings.T("value.Speeding"), Strings.T("event.fine") + " · " + Strings.T("value.Speeding"), Strings.T("legend.speeding"));
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
        Entry((g, r) => {
            using var lead = new SolidBrush(Muted);
            using var done = new SolidBrush(Accent);
            using var over = new SolidBrush(Color.FromArgb(150, 112, 52));
            g.FillRectangle(lead, 8, r.Height / 2 - 4, 7, 8);
            g.FillRectangle(done, 15, r.Height / 2 - 4, 16, 8);
            g.FillRectangle(over, 31, r.Height / 2 - 4, 7, 8);
        }, Strings.T("legend.progress"), Strings.T("legend.progressWhy"));

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
        var speeding = type == "fine"
            && detail.Equals(Strings.T("value.Speeding"), StringComparison.OrdinalIgnoreCase);
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

        const float band = 5f;
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

    private ContextMenuStrip LayerMenu(RouteView map) {
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
        Item(Strings.T("map.layerFreeroam"), map.ShowFreeroam, v => map.ShowFreeroam = v);
        Item(Strings.T("map.layerCities"), map.ShowCities, v => map.ShowCities = v);
        Item(Strings.T("map.layerMarks"), map.ShowMarks, v => map.ShowMarks = v);
        return menu;
    }

    /// <summary>The same map with the whole screen to itself. A route panel beside a
    /// column of figures is enough to see the shape of a drive and not enough to
    /// look at a junction, and zooming inside a 300 pixel strip is a poor substitute
    /// for room.</summary>
    private void BigMap(DeliveryDetail d, Units u) {
        using var window = new Form {
            Text = $"{d.SourceCity}  →  {d.DestinationCity}",
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
                     _store.TimelineRows(d.Id, u), RoutesFor(d.Game).RunUps.Concat(_store.FreeroamRoutes(d.Game)));
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

        var events = _store.TimelineRows(d.Id, u);
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
        Row(Strings.T("detail.timeGame"), $"{d.DrivingGameMin / 60:0.0} {Strings.T("stats.gameTime")}");
        Row(Strings.T("detail.timeReal"), $"{d.RealDurationMs / 60000.0:0} min");
        Row(Strings.T("detail.rest"), $"{d.RestStops}x  ·  {Units.Duration(d.RestMinutes)}");

        Group(Strings.T("detail.groupMoney"));
        var paid = d.Outcome == "delivered" ? d.Revenue : -d.Penalty;
        Row(Strings.T("detail.paidOffered"), $"{u.FormatMoney(paid)}  /  {u.FormatMoney(d.OfferedIncome)}");
        Row(Strings.T("detail.fines"), $"{u.FormatMoney(d.FinesTotal)}  ({d.FinesCount}x)");
        Row(Strings.T("detail.tolls"), u.FormatMoney(d.TollsPaid));
        Row(Strings.T("detail.fuel"), u.FormatVolume(d.FuelUsedL));
        if (u.Consumption(d.AvgConsumption) is { } c) Row(Strings.T("detail.consumption"), $"{c:0.0} {u.ConsumptionUnit}");
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
        Group(Strings.T("detail.groupDamage"));
        Row(Strings.T("col.truck"), Damage(d.TruckDamage));
        Row(Strings.T("detail.trailer"), Damage(d.TrailerDamage));
        Row(Strings.T("col.cargo"), Damage(d.CargoDamage));

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
        side.Controls.Add(timeline);
        side.Controls.Add(RoutePanel(d, u));
        _detailSide = side;

        body.Controls.Add(info);
        body.Controls.Add(notes);
        body.Controls.Add(side);
        // Added last so it docks outermost and gets the full width. The verdict is
        // the first thing anyone asks about a delivery, so it reads across the top
        // rather than as one more row buried in the list of facts.
        body.Controls.Add(VerdictBand(d, u));
        return body;
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
            Image = EventIcon(e.Type, e.Detail), BackColor = Color.Transparent,
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
        _routes.Clear();
        ApplyFilter();
        ReloadMapPage();
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

    private void ApplyFilter() {
        var text = _search.Text.Trim();
        IEnumerable<DeliveryRow> filtered = _rows;
        // Each switch does nothing in the middle, which is where both of them start.
        if (_gameFilter.Position != 0) {
            var game = _gameFilter.Position < 0 ? "Ets2" : "Ats";
            filtered = filtered.Where(r => r.Hra == game);
        }
        if (_cargoFilter.Position != 0) {
            var oversize = _cargoFilter.Position > 0;
            filtered = filtered.Where(r => r.Special == oversize);
        }
        if (text.Length > 0) {
            filtered = filtered.Where(r =>
                r.Odkial.Contains(text, StringComparison.OrdinalIgnoreCase) ||
                r.Kam.Contains(text, StringComparison.OrdinalIgnoreCase) ||
                r.Naklad.Contains(text, StringComparison.OrdinalIgnoreCase) ||
                r.Tahac.Contains(text, StringComparison.OrdinalIgnoreCase));
        }

        // Rebinding throws the sort away, and the list is rebound whenever anything
        // sends the user back to it: opening a delivery and pressing Back left them
        // looking at a list ordered by date again, having asked for it by distance a
        // moment earlier. The order they chose is theirs until they change it.
        var sortedBy = _grid.SortedColumn?.DataPropertyName;
        var sortedWay = _grid.SortOrder == SortOrder.Descending
            ? System.ComponentModel.ListSortDirection.Descending
            : System.ComponentModel.ListSortDirection.Ascending;

        var bound = new SortableBindingList<DeliveryRow>(filtered.ToList(), new Dictionary<string, string> {
            [nameof(DeliveryRow.Vzdialenost)] = nameof(DeliveryRow.DistanceKm),
            [nameof(DeliveryRow.Odmena)] = nameof(DeliveryRow.Zarobok),
        });
        _grid.DataSource = bound;
        // Two kinds of hidden column. The raw metric values stay bound so sorting by
        // distance or pay compares numbers rather than formatted text. The rest are
        // simply not what a list is for: they are on the delivery's own card, where
        // they can be read instead of squeezed into another narrow column.
        foreach (var hidden in new[] {
            nameof(DeliveryRow.Id), nameof(DeliveryRow.DistanceKm), nameof(DeliveryRow.Zarobok),
            nameof(DeliveryRow.Hra), nameof(DeliveryRow.Tahac), nameof(DeliveryRow.Pokuty),
            nameof(DeliveryRow.Kolizie), nameof(DeliveryRow.Styl), nameof(DeliveryRow.Poznamky),
            nameof(DeliveryRow.Flags), nameof(DeliveryRow.Special), nameof(DeliveryRow.Stav),
        }) {
            if (_grid.Columns[hidden] is { } col) col.Visible = false;
        }

        // Put the order back. After the columns are hidden, or sorting by one that
        // has just been taken off the screen throws.
        if (sortedBy is not null && _grid.Columns[sortedBy] is { Visible: true } column) {
            _grid.Sort(column, sortedWay);
        }
    }

    private void ReloadStats() {
        var slice = StatsSlice();
        var s = _store.GetStats(slice);
        var roam = _store.FreeroamTotals(slice);
        // The same length of time immediately before this one, so a figure can say
        // what it did as well as what it is. There is no before for all of history.
        var before = slice.Previous is { } earlier ? _store.GetStats(earlier) : null;
        var u = CurrentUnits();
        var gameHours = s.TotalGameMinutes / 60.0;
        var realHours = s.TotalDrivingMs / 3600000.0;
        // Distances are simulated km, so they pair with game hours - dividing by real
        // hours would report the time-compression factor as speed (~770 km/h).
        var avg = gameHours > 0.01 ? s.TimedDistanceKm / gameHours : 0;

        _statsGrid.SuspendLayout();
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
            StatTile(Strings.T("stats.fuel"), u.FormatVolume(s.TotalFuelL),
                Change(s.TotalFuelL, x => x.TotalFuelL)),
            // Driving that carried nothing. Shown beside the deliveries rather than
            // folded into them: it is real distance, but it earned nothing and was
            // never judged, so adding it to the delivery figure would flatter both.
            StatTile(Strings.T("stats.freeroam"), u.FormatDistance(roam.DistanceKm),
                roam.Stretches > 0 ? $"{roam.Stretches}x" : null));

        Section(2, Strings.T("stats.headingDriving"),
            StatTile(Strings.T("stats.time"), $"{gameHours:0.0} {Strings.T("stats.gameTime")}",
                Change(s.TotalGameMinutes, x => x.TotalGameMinutes)
                    ?? $"{realHours:0.0} {Strings.T("stats.realTime")}"),
            StatTile(Strings.T("stats.avgSpeed"), u.FormatSpeed(avg)),
            StatTile(Strings.T("stats.style"), $"{s.Clean} / {s.Spirited}",
                $"{Strings.T("stats.styleClean")} / {Strings.T("stats.styleSpirited")}"));

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

        _statsGrid.ResumeLayout();
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
