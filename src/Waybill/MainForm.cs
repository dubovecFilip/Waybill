using System.Data;
using System.Diagnostics;
using System.Windows.Forms;
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

    private readonly Label _status = new();
    private readonly Label _jobLine = new();
    private readonly Label _jobDetail = new();
    private Panel? _progressRow;
    private readonly Panel _progressTrack = new();
    private readonly Panel _progressFill = new();
    private readonly Label _progressText = new();
    private readonly ListBox _log = new();

    private readonly DataGridView _grid = new();
    private readonly TextBox _search = new();
    private readonly ComboBox _statusFilter = new();
    private readonly TableLayoutPanel _statsGrid = new();
    private readonly DataGridView _timeline = new();

    /// <summary>Which sidebar page is showing. Kept across a language change, which
    /// rebuilds every control from scratch.</summary>
    private string _page = "deliveries";
    private readonly Panel _content = new();

    private List<DeliveryRow> _rows = new();

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
        FormClosing += (_, _) => _engine?.Dispose();

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
        var deliveries = BuildHistoryPage();
        deliveries.Tag = "deliveries";
        var stats = BuildStatsPage();
        stats.Tag = "stats";

        _content.Controls.Add(live);
        _content.Controls.Add(deliveries);
        _content.Controls.Add(stats);
        return _content;
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
        return settings;
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
        _progressTrack.Controls.Add(_progressFill);

        progressRow.Controls.Add(_progressTrack);
        progressRow.Controls.Add(_progressText);

        var logBox = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 10, 0, 8) };
        _log.Dock = DockStyle.Fill;
        _log.BorderStyle = BorderStyle.None;
        _log.BackColor = Surface;
        _log.ForeColor = Muted;
        _log.Font = new Font("Consolas", 8.5F);
        _log.IntegralHeight = false;
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
            if (_progressRow != null) _progressRow.Visible = false;
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
        var ratio = planned > 0 ? Math.Clamp(driven / planned, 0, 1) : 0;
        if (_progressRow != null) _progressRow.Visible = true;
        _progressFill.Width = (int)(_progressTrack.ClientSize.Width * ratio);
        _progressText.Text = $"{u.Distance(driven):0.0} / {u.Distance(planned):0} {u.DistanceUnit}   ·   {ratio * 100:0} %";
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
        _grid.SelectionChanged -= OnGridSelectionChanged;
        _grid.SelectionChanged += OnGridSelectionChanged;
        _grid.DataBindingComplete -= OnGridBound;
        _grid.DataBindingComplete += OnGridBound;
        _grid.CellEndEdit -= OnGridCellEndEdit;
        _grid.CellEndEdit += OnGridCellEndEdit;
        _grid.CellFormatting -= OnGridCellFormatting;
        _grid.CellFormatting += OnGridCellFormatting;

        _timeline.Dock = DockStyle.Bottom;
        _timeline.Height = 150;
        _timeline.ReadOnly = true;
        _timeline.AllowUserToAddRows = false;
        _timeline.RowHeadersVisible = false;
        _timeline.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        StyleGrid(_timeline);
        _timeline.DataBindingComplete -= OnTimelineBound;
        _timeline.DataBindingComplete += OnTimelineBound;

        var splitLabel = new Label {
            Dock = DockStyle.Bottom, Height = 26, Text = Strings.T("timeline.label"),
            ForeColor = Muted, Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
            Padding = new Padding(0, 8, 0, 0), BackColor = Canvas,
        };

        page.Controls.Add(_grid);
        page.Controls.Add(splitLabel);
        page.Controls.Add(_timeline);
        page.Controls.Add(bar);
        return page;
    }

    private void OnFilterChanged(object? sender, EventArgs e) => ApplyFilter();
    private void OnGridSelectionChanged(object? sender, EventArgs e) => ReloadTimeline();

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

    private void OnTimelineBound(object? sender, DataGridViewBindingCompleteEventArgs e) {
        var captions = new Dictionary<string, string> {
            [nameof(TimelineRow.Cas)] = Strings.T("col.time"),
            [nameof(TimelineRow.Udalost)] = Strings.T("col.event"),
            [nameof(TimelineRow.Hodnota)] = Strings.T("col.value"),
            [nameof(TimelineRow.Detail)] = Strings.T("col.detail"),
        };
        foreach (DataGridViewColumn col in _timeline.Columns) {
            if (captions.TryGetValue(col.DataPropertyName, out var caption)) col.HeaderText = caption;
        }
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

    private void UseDarkTitleBar() {
        try {
            var on = 1;
            DwmSetWindowAttribute(Handle, 20, ref on, sizeof(int));
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

    // ---------- data ----------

    private void ReloadHistory() {
        _rows = _store.RecentDeliveryRows(500, _settings.Units).ToList();
        ApplyFilter();
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

        _grid.DataSource = new SortableBindingList<DeliveryRow>(filtered.ToList());
        // Raw metric values back the formatted columns; hide them but keep them
        // bound so sorting by distance/pay sorts numerically rather than by text.
        foreach (var hidden in new[] { nameof(DeliveryRow.Id), nameof(DeliveryRow.DistanceKm), nameof(DeliveryRow.Zarobok) }) {
            if (_grid.Columns[hidden] is { } col) col.Visible = false;
        }
    }

    private void ReloadTimeline() {
        if (_grid.CurrentRow?.DataBoundItem is not DeliveryRow row) {
            _timeline.DataSource = null;
            return;
        }
        _timeline.DataSource = new SortableBindingList<TimelineRow>(_store.TimelineRows(row.Id).ToList());
    }

    private void ReloadStats() {
        var s = _store.GetStats();
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
            StatTile(Strings.T("stats.distance"), u.FormatDistance(s.TotalDistanceKm)),
            StatTile(Strings.T("stats.revenue"), u.FormatMoney(s.TotalRevenue),
                s.TotalPenalties > 0 ? $"{Strings.T("stats.penalties")} {u.FormatMoney(s.TotalPenalties)}" : null),
            StatTile(Strings.T("stats.fuel"), u.FormatVolume(s.TotalFuelL)));

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
