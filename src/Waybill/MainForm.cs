using System.Data;
using System.Diagnostics;
using System.Windows.Forms;
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

public class MainForm : Form {
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
    private Panel? _progressRow;
    private readonly Panel _progressTrack = new();
    private readonly Panel _progressFill = new();
    private readonly Panel _progressLead = new();
    private readonly Label _progressText = new();
    private readonly ListBox _log = new();

    private readonly DataGridView _grid = new();
    private readonly TextBox _search = new();
    private readonly ComboBox _statusFilter = new();
    private readonly TableLayoutPanel _statsGrid = new();

    /// <summary>Which sidebar page is showing. Kept across a language change, which
    /// rebuilds every control from scratch.</summary>
    private string _page = "deliveries";
    private readonly Panel _content = new();
    private readonly Panel _detailPage = new();
    /// <summary>The timeline column, which lives at width zero until asked for.</summary>
    private Panel? _detailSide;
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
        Shown += (_, _) => UseDarkScrollbars(this);
        FormClosing += (_, _) => {
            _engine?.Dispose();
            _discord?.Dispose();
        };

        var timer = new System.Windows.Forms.Timer { Interval = 500 };
        timer.Tick += (_, _) => RefreshLive();
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

        var live = BuildLivePage();
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
        map.Show(Layers(routes), 0, routes.Cities, null, _store.FreeroamRoutes(game));
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
            if (_settings.DiscordPresence && string.IsNullOrWhiteSpace(_settings.DiscordAppId)) {
                AddLog(Strings.T("discord.needsAppId"));
            }
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
        if (string.IsNullOrWhiteSpace(_settings.DiscordAppId)) {
            AddLog(Strings.T("discord.needsAppId"));
            return;
        }

        _discord = new DiscordPresence(_settings.DiscordAppId!);
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

    // ---------- live panel ----------

    /// <summary>What the engine is doing right now, as a page of its own rather than a
    /// strip above everything else. It only matters while driving, and as a permanent
    /// header it took height from the delivery list for the sake of two empty lines
    /// whenever no game was running.</summary>
    private Control BuildLivePage() {
        var page = new Panel { Dock = DockStyle.Fill, Padding = new Padding(16), BackColor = Canvas };
        var panel = new Panel { Dock = DockStyle.Fill };
        panel.BackColor = Surface;
        panel.Padding = new Padding(24, 20, 24, 16);

        _status.Text = Strings.T("live.starting");
        _status.Dock = DockStyle.Top;
        _status.Height = 20;
        _status.ForeColor = Muted;
        _status.Font = new Font("Segoe UI", 8.5F, FontStyle.Bold);

        // The route is the one thing worth reading from across the room.
        _jobLine.Dock = DockStyle.Top;
        _jobLine.Height = 32;
        _jobLine.ForeColor = Ink;
        _jobLine.Font = new Font("Segoe UI", 15F, FontStyle.Bold);

        _jobDetail.Dock = DockStyle.Top;
        _jobDetail.Height = 22;
        _jobDetail.ForeColor = Muted;

        // A drawn bar rather than a ProgressBar: the stock one is a thick block with
        // a fixed colour and no room for the figure that belongs beside it.
        // Hidden until there is a job: an empty track sitting there permanently reads
        // as a broken widget rather than as nothing to report.
        var progressRow = new Panel { Dock = DockStyle.Top, Height = 22, Padding = new Padding(0, 6, 0, 0), Visible = false };
        _progressRow = progressRow;
        _progressText.Dock = DockStyle.Right;
        _progressText.Width = 130;
        _progressText.TextAlign = ContentAlignment.MiddleRight;
        _progressText.ForeColor = Muted;
        _progressText.Font = new Font("Segoe UI", 8.5F);

        _progressTrack.Dock = DockStyle.Fill;
        _progressTrack.Height = 8;
        _progressTrack.BackColor = Line;
        _progressTrack.Padding = new Padding(0);
        _progressTrack.Margin = new Padding(0);

        _progressFill.Dock = DockStyle.Left;
        _progressFill.Width = 0;
        _progressFill.BackColor = Accent;
        // The run out to the trailer, in the quieter colour, ahead of the loaded
        // stretch. Docked left and added last so it sits leftmost, which is where it
        // happened: the bar then reads as the whole job from the moment it was taken,
        // with the commute told apart from the consignment moving.
        _progressLead.Dock = DockStyle.Left;
        _progressLead.Width = 0;
        _progressLead.BackColor = Muted;
        _progressTrack.Controls.Add(_progressFill);
        _progressTrack.Controls.Add(_progressLead);

        progressRow.Controls.Add(_progressTrack);
        progressRow.Controls.Add(_progressText);

        var logBox = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 10, 0, 8) };
        _log.Dock = DockStyle.Fill;
        _log.BorderStyle = BorderStyle.None;
        _log.BackColor = Surface;
        _log.ForeColor = Muted;
        _log.Font = new Font("Consolas", 8.5F);
        _log.IntegralHeight = false;
        // Nothing follows from picking a log line, and the system highlight it draws
        // is a bright blue bar that belongs to no part of this window.
        _log.SelectionMode = SelectionMode.None;
        logBox.Controls.Add(_log);

        // Docked children stack in reverse order of adding, so add bottom-up.
        panel.Controls.Add(logBox);
        panel.Controls.Add(progressRow);
        panel.Controls.Add(_jobDetail);
        panel.Controls.Add(_jobLine);
        panel.Controls.Add(_status);

        page.Controls.Add(panel);
        return page;
    }

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

    private void RefreshLive() {
        if (_engine == null) return;

        var job = _engine.ActiveJob;
        if (job == null) {
            _status.Text = (_engine.Connected
                ? $"{Strings.T("live.waitingJob")}   ({Strings.T("live.ticks")}: {_engine.TickCount})"
                : Strings.T("live.waitingGame")).ToUpperInvariant();
            _jobLine.Text = Strings.T("live.noJob");
            _jobLine.ForeColor = Muted;
            _jobDetail.Text = "";
            _progressText.Text = "";
            _progressFill.Width = 0;
            _progressLead.Width = 0;
            if (_progressRow != null) _progressRow.Visible = false;
            // Between jobs the profile says so; with the game closed it says nothing
            // at all, rather than leaving Waybill sitting there all evening.
            _discord?.Update(_engine.Connected
                ? new DiscordPresence.Activity { Details = Strings.T("discord.idle"), LargeImage = "waybill", LargeText = "Waybill" }
                : null);
            return;
        }

        var state = _engine.ActiveState;
        var driven = state?.DistanceKm ?? 0;
        var planned = job.PlannedDistanceKm;
        var u = CurrentUnits();

        _status.Text = $"{Strings.T("live.jobRunning")}   ({Strings.T("live.ticks")}: {_engine.TickCount}, {Strings.T("live.deliveriesThisRun")}: {_engine.DeliveriesThisRun})".ToUpperInvariant();
        _jobLine.Text = $"{job.SourceCity}  →  {job.DestinationCity}";
        _jobLine.ForeColor = Ink;
        _jobDetail.Text = $"{job.Cargo} · {u.MassTonnes(job.CargoMassKg):0.0} {u.MassUnit}"
                        + $"   ·   {Strings.T("live.reward")} {u.FormatMoney(job.Income)}";

        // Planned distance is the game's own route length, in the same simulated km
        // the odometer counts, so this genuinely tracks progress toward the drop-off.
        // Progress is the loaded leg against the plan, because that is what the plan
        // describes: measured across this history the planned figure agrees with the
        // loaded distance to about a percent, while the total ran as much as twelve
        // percent over it on a contract that started far from its trailer.
        var toLoad = state?.DistanceToLoadKm ?? 0;
        var loaded = Math.Max(0, driven - toLoad);
        var ratio = planned > 0 ? Math.Clamp(loaded / planned, 0, 1) : 0;
        // The track spans everything this job will cover, so the run-up takes its own
        // share of the width instead of pushing the loaded stretch off the scale.
        var whole = planned + toLoad;
        var leadShare = whole > 0 ? Math.Clamp(toLoad / whole, 0, 1) : 0;
        if (_progressRow != null) _progressRow.Visible = true;
        var track = _progressTrack.ClientSize.Width;
        _progressLead.Width = (int)(track * leadShare);
        _progressFill.Width = (int)(track * (1 - leadShare) * ratio);
        _progressText.Text = $"{u.Distance(loaded):0.0} / {u.Distance(planned):0} {u.DistanceUnit}   ·   {ratio * 100:0} %"
            + (toLoad > 0.05 ? $"   (+{u.Distance(toLoad):0.0} {Strings.T("live.toLoad")})" : "");

        // The same three numbers the page shows, in one line each for Discord. The
        // start time is sent raw so Discord runs the elapsed counter itself, which
        // keeps ticking between the updates it only accepts every 15 seconds.
        var game = state?.Game ?? "";
        _discord?.Update(new DiscordPresence.Activity {
            Details = $"{job.SourceCity} → {job.DestinationCity}",
            State = planned > 0
                ? $"{job.Cargo} · {u.Distance(loaded):0} / {u.Distance(planned):0} {u.DistanceUnit} ({ratio * 100:0} %)"
                : job.Cargo,
            StartUnix = state != null ? state.StartedAtMs / 1000 : null,
            LargeImage = game.ToLowerInvariant() is "ats" or "ets2" ? game.ToLowerInvariant() : "waybill",
            LargeText = game == "Ats" ? GameLauncher.DisplayName(SimGame.Ats)
                      : game == "Ets2" ? GameLauncher.DisplayName(SimGame.Ets2) : "Waybill",
            SmallImage = "waybill",
            SmallText = "Waybill",
        });
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
            var name = unit.Name.Length > 0 ? unit.Name : unit.Id;
            var label = $"{i + 1}.  {Strings.T("value." + unit.Kind)}"
                      + (unit.Owned ? $"  ({Strings.T("detail.owned")})" : "");
            lines.Add(UnitLine(label, $"{name}   ·   {unit.Plate}   ·   {Damage(unit.Damage)}",
                unit.Kind == "dolly" ? Muted : Ink, lineHeight));
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

    private static Control UnitLine(string label, string value, Color colour, int height) {
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

        _statusFilter.Width = 130;
        _statusFilter.Margin = new Padding(0, 3, 16, 3);
        _statusFilter.DropDownStyle = ComboBoxStyle.DropDownList;
        _statusFilter.FlatStyle = FlatStyle.Flat;
        _statusFilter.BackColor = Raised;
        _statusFilter.ForeColor = Ink;
        // The controls are reused when the layout is rebuilt for a language change,
        // so the old entries have to go first. Left in place they pile up and, worse,
        // the still-selected entry is in the previous language while the filter
        // compares against the new one, which quietly empties the list.
        _statusFilter.Items.Clear();
        _statusFilter.Items.AddRange(new object[] { Strings.T("filter.all"), "accepted", "review", "rejected", "imported" });
        _statusFilter.SelectedIndex = 0;
        _statusFilter.SelectedIndexChanged -= OnFilterChanged;
        _statusFilter.SelectedIndexChanged += OnFilterChanged;

        // Only what changes the view of the list. Exporting, backing up and restoring
        // used to sit here too, which put "show me fewer rows" and "replace the whole
        // database" one button apart; they live under Data now.
        bar.Controls.Add(searchBox);
        bar.Controls.Add(_statusFilter);
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
        _grid.RowHeadersVisible = false;
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
        _grid.CellToolTipTextNeeded -= OnGridToolTip;
        _grid.CellToolTipTextNeeded += OnGridToolTip;

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

    private void OnFilterChanged(object? sender, EventArgs e) => ApplyFilter();

    /// <summary>Hovering the verdict names what was found, so a row saying "review"
    /// does not have to be opened just to learn whether it matters. The full
    /// explanation stays on the card.</summary>
    private void OnGridToolTip(object? sender, DataGridViewCellToolTipTextNeededEventArgs e) {
        if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
        if (_grid.Columns[e.ColumnIndex].DataPropertyName != nameof(DeliveryRow.Stav)) return;
        if (_grid.Rows[e.RowIndex].DataBoundItem is not DeliveryRow row || row.Flags.Length == 0) return;

        e.ToolTipText = string.Join(Environment.NewLine, row.Flags
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(f => "•  " + Strings.T("flag." + f)));
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
                [nameof(DeliveryRow.Stav)] = 90,
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
                [nameof(DeliveryRow.Stav)] = Strings.T("col.status"),
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
        g.RowHeadersVisible = false;
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
    private Panel BuildStatsPage() {
        var page = new Panel { Dock = DockStyle.Fill, Padding = new Padding(16), BackColor = Canvas };
        _statsGrid.Dock = DockStyle.Fill;
        _statsGrid.BackColor = Canvas;
        _statsGrid.ColumnCount = 4;
        _statsGrid.RowCount = 8;
        _statsGrid.Padding = new Padding(0);

        _statsGrid.ColumnStyles.Clear();
        for (var i = 0; i < 4; i++) _statsGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));

        // Four sections, each a heading of its own height above a row of tiles that
        // takes an equal quarter of what is left.
        _statsGrid.RowStyles.Clear();
        for (var i = 0; i < 4; i++) {
            _statsGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
            _statsGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 25F));
        }

        page.Controls.Add(_statsGrid);
        return page;
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

        var captionLabel = new Label {
            Dock = DockStyle.Top, Height = 18, Text = caption.ToUpperInvariant(),
            ForeColor = Muted, Font = new Font("Segoe UI", 7.5F, FontStyle.Bold),
        };
        // A truck name is far longer than a number and would be cut off at the size a
        // figure wants, so the type steps down rather than the text disappearing.
        var size = value.Length > 22 ? 11F : value.Length > 13 ? 13.5F : 17F;
        var valueLabel = new Label {
            Dock = DockStyle.Top, Height = 34, Text = value,
            ForeColor = Ink, Font = new Font("Segoe UI", size, FontStyle.Bold),
            AutoEllipsis = true,
        };
        var noteLabel = new Label {
            Dock = DockStyle.Top, Height = 18, Text = note ?? "",
            ForeColor = Muted, Font = new Font("Segoe UI", 8F), AutoEllipsis = true,
        };

        card.Controls.Add(noteLabel);
        card.Controls.Add(valueLabel);
        card.Controls.Add(captionLabel);
        return card;
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
                if (target > 0) UseDarkScrollbars(side);
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
            var written = WaybillSheet.Save(d, _store.TimelineRows(d.Id), route, u, dialog.FileName, 300f);
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
            Dock = DockStyle.Top, Height = 300, BackColor = Line,
            Padding = new Padding(0, 0, 0, 1),
        };

        var map = NewMap(u);
        map.Show(Layers(RoutesFor(d.Game)), d.Id, RoutesFor(d.Game).Cities,
                 _store.TimelineRows(d.Id), _store.FreeroamRoutes(d.Game));

        box.Controls.Add(map);
        MapButtons(box, map, () => BigMap(d, u));
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
    private void MapButtons(Control host, RouteView map, Action? expand) {
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
    private ContextMenuStrip LayerMenu(RouteView map) {
        var menu = new ContextMenuStrip {
            BackColor = Surface, ForeColor = Ink, ShowImageMargin = false,
            Renderer = new ToolStripProfessionalRenderer(),
        };
        void Item(string label, bool on, Action<bool> set) {
            var item = new ToolStripMenuItem(label) { Checked = on, CheckOnClick = true, BackColor = Surface, ForeColor = Ink };
            item.Click += (_, _) => { set(item.Checked); map.Invalidate(); };
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
        MapButtons(window, map, null);
        // Shown before the data, so the map measures itself against the size it will
        // actually have rather than fitting the route to a window that is about to
        // be maximised.
        window.Shown += (_, _) => map.Show(Layers(RoutesFor(d.Game)), d.Id, RoutesFor(d.Game).Cities,
                                           _store.TimelineRows(d.Id), _store.FreeroamRoutes(d.Game));
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

        var events = _store.TimelineRows(d.Id);
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
        Row(Strings.T("detail.rest"), $"{d.RestStops}x  ·  {d.RestMinutes:0} {Strings.T("unit.gameMinutes")}");

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
        var what = new Label { Dock = DockStyle.Left, Width = 110, Text = e.Udalost, ForeColor = Ink, TextAlign = ContentAlignment.MiddleLeft };
        var time = new Label { Dock = DockStyle.Left, Width = 66, Text = e.Cas, ForeColor = Muted, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Consolas", 8.5F) };

        line.Controls.Add(detail);
        line.Controls.Add(value);
        line.Controls.Add(what);
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
        var status = _statusFilter.SelectedItem as string ?? Strings.T("filter.all");

        IEnumerable<DeliveryRow> filtered = _rows;
        if (status != Strings.T("filter.all")) filtered = filtered.Where(r => r.Stav == status);
        if (text.Length > 0) {
            filtered = filtered.Where(r =>
                r.Odkial.Contains(text, StringComparison.OrdinalIgnoreCase) ||
                r.Kam.Contains(text, StringComparison.OrdinalIgnoreCase) ||
                r.Naklad.Contains(text, StringComparison.OrdinalIgnoreCase) ||
                r.Tahac.Contains(text, StringComparison.OrdinalIgnoreCase));
        }

        _grid.DataSource = new SortableBindingList<DeliveryRow>(filtered.ToList(), new Dictionary<string, string> {
            [nameof(DeliveryRow.Vzdialenost)] = nameof(DeliveryRow.DistanceKm),
            [nameof(DeliveryRow.Odmena)] = nameof(DeliveryRow.Zarobok),
        });
        // Two kinds of hidden column. The raw metric values stay bound so sorting by
        // distance or pay compares numbers rather than formatted text. The rest are
        // simply not what a list is for: they are on the delivery's own card, where
        // they can be read instead of squeezed into another narrow column.
        foreach (var hidden in new[] {
            nameof(DeliveryRow.Id), nameof(DeliveryRow.DistanceKm), nameof(DeliveryRow.Zarobok),
            nameof(DeliveryRow.Hra), nameof(DeliveryRow.Tahac), nameof(DeliveryRow.Pokuty),
            nameof(DeliveryRow.Kolizie), nameof(DeliveryRow.Styl), nameof(DeliveryRow.Poznamky),
            nameof(DeliveryRow.Flags),
        }) {
            if (_grid.Columns[hidden] is { } col) col.Visible = false;
        }
    }

    private void ReloadStats() {
        var s = _store.GetStats();
        var roam = _store.FreeroamTotals();
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
            _statsGrid.SetColumnSpan(_statsGrid.GetControlFromPosition(0, row)!, 4);
            for (var i = 0; i < tiles.Length; i++) _statsGrid.Controls.Add(tiles[i], i, row + 1);
        }

        Section(0, Strings.T("stats.headingOverall"),
            StatTile(Strings.T("stats.deliveries"), s.TotalDeliveries.ToString(),
                $"{s.Accepted} accepted · {s.Review} review · {s.Rejected} rejected"),
            StatTile(Strings.T("stats.distance"), u.FormatDistance(s.TotalDistanceKm),
                roam.DistanceKm > 0
                    ? $"{u.FormatDistance(s.TotalDistanceKm + roam.DistanceKm)} {Strings.T("stats.withFreeroam")}"
                    : null),
            StatTile(Strings.T("stats.revenue"), u.FormatMoney(s.TotalRevenue),
                s.TotalPenalties > 0 ? $"{Strings.T("stats.penalties")} {u.FormatMoney(s.TotalPenalties)}" : null),
            StatTile(Strings.T("stats.fuel"), u.FormatVolume(s.TotalFuelL)),
            // Driving that carried nothing. Shown beside the deliveries rather than
            // folded into them: it is real distance, but it earned nothing and was
            // never judged, so adding it to the delivery figure would flatter both.
            StatTile(Strings.T("stats.freeroam"), u.FormatDistance(roam.DistanceKm),
                roam.Stretches > 0 ? $"{roam.Stretches}x" : null));

        Section(2, Strings.T("stats.headingDriving"),
            StatTile(Strings.T("stats.time"), $"{gameHours:0.0} {Strings.T("stats.gameTime")}",
                $"{realHours:0.0} {Strings.T("stats.realTime")}"),
            StatTile(Strings.T("stats.avgSpeed"), u.FormatSpeed(avg)),
            StatTile(Strings.T("stats.style"), $"{s.Clean} / {s.Spirited}",
                $"{Strings.T("stats.styleClean")} / {Strings.T("stats.styleSpirited")}"));

        Section(4, Strings.T("stats.headingIncidents"),
            StatTile(Strings.T("stats.collisions"), s.TotalCollisions.ToString()),
            StatTile(Strings.T("stats.finesTotal"), u.FormatMoney(s.TotalFines)),
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

        var confirm = MessageBox.Show(this,
            "Nahradi sa aktualna databaza zalohou.\nSucasna databaza sa najprv odlozi bokom.\n\nPokracovat?",
            "Obnova zo zalohy", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
        if (confirm != DialogResult.Yes) return;

        try {
            // The engine holds the database open, so it has to let go before the file
            // underneath it can be replaced; it is restarted straight after.
            _engine?.Dispose();
            _engine = null;
            _store.Dispose();

            var safety = DeliveryStore.RestoreFromBackup(dlg.FileName);
            MessageBox.Show(this, $"Databaza obnovena.\nPovodna odlozena sem:\n{safety}\n\nAplikacia sa restartuje.", "Obnova");

            Application.Restart();
        } catch (Exception ex) {
            MessageBox.Show(this, "Obnova zlyhala, databaza ostala nezmenena:\n" + ex.Message, "Chyba", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
