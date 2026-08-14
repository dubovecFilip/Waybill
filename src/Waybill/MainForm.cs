using System.Data;
using System.Diagnostics;
using System.Windows.Forms;
using Waybill.Storage;
using Waybill.Tracking;

namespace Waybill;

/// <summary>
/// The whole UI: a live panel on top (what the engine is doing right now) and
/// tabs below for delivery history, statistics and the event timeline. The
/// engine runs in this same process, so starting the app is all the user does.
/// </summary>
public class MainForm : Form {
    private readonly DeliveryStore _store = new();
    private readonly Settings _settings = Settings.Load();
    private TrackerEngine? _engine;

    private readonly Label _status = new();
    private readonly Label _jobLine = new();
    private readonly Label _jobDetail = new();
    private readonly ProgressBar _progress = new();
    private readonly ListBox _log = new();

    private readonly DataGridView _grid = new();
    private readonly TextBox _search = new();
    private readonly ComboBox _statusFilter = new();
    private readonly Label _statsLabel = new();
    private readonly DataGridView _timeline = new();

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

        BuildLayout();

        Load += (_, _) => StartEngine();
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

        var tabs = BuildTabs();
        var live = BuildLivePanel();
        var menu = BuildMenu();

        Controls.Add(tabs);
        Controls.Add(live);
        Controls.Add(menu);
        MainMenuStrip = menu;

        ReloadHistory();
        ReloadStats();
    }

    // ---------- menu ----------

    private MenuStrip BuildMenu() {
        var menu = new MenuStrip();
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

        menu.Items.Add(BuildPlayMenu());
        menu.Items.Add(units);

        // Data lives under LocalAppData, not next to the exe, so it survives
        // rebuilds, which also makes it hard to find by hand.
        var data = new ToolStripMenuItem(Strings.T("menu.data"));
        var import = new ToolStripMenuItem(Strings.T("menu.import"));
        import.Click += (_, _) => AfterMenuCloses(ImportTrucksBook);
        data.DropDownItems.Add(import);
        data.DropDownItems.Add(new ToolStripSeparator());
        data.DropDownItems.Add(OpenFolderItem(Strings.T("menu.folder.db"), DeliveryStore.DefaultDir()));
        data.DropDownItems.Add(OpenFolderItem(Strings.T("menu.folder.backups"), Path.Combine(DeliveryStore.DefaultDir(), "backups")));
        data.DropDownItems.Add(OpenFolderItem(Strings.T("menu.folder.sessions"), Path.Combine(DeliveryStore.DefaultDir(), "sessions")));
        menu.Items.Add(data);
        menu.Items.Add(language);

        return menu;
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

        return play;
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

    private Control BuildLivePanel() {
        var panel = new Panel { Dock = DockStyle.Top, Height = 168, Padding = new Padding(12, 10, 12, 6) };

        _status.Text = Strings.T("live.starting");
        _status.Dock = DockStyle.Top;
        _status.Height = 22;
        _status.Font = new Font(Font, FontStyle.Bold);

        _jobLine.Dock = DockStyle.Top;
        _jobLine.Height = 24;
        _jobLine.Font = new Font("Segoe UI", 11F, FontStyle.Bold);

        _jobDetail.Dock = DockStyle.Top;
        _jobDetail.Height = 20;
        _jobDetail.ForeColor = SystemColors.GrayText;

        _progress.Dock = DockStyle.Top;
        _progress.Height = 20;
        _progress.Maximum = 1000; // finer resolution than whole percent

        _log.Dock = DockStyle.Fill;
        _log.BorderStyle = BorderStyle.None;
        _log.BackColor = SystemColors.Control;
        _log.ForeColor = SystemColors.GrayText;
        _log.IntegralHeight = false;

        // Docked children stack in reverse order of adding, so add bottom-up.
        panel.Controls.Add(_log);
        panel.Controls.Add(_progress);
        panel.Controls.Add(_jobDetail);
        panel.Controls.Add(_jobLine);
        panel.Controls.Add(_status);
        return panel;
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
            _status.Text = _engine.Connected
                ? $"{Strings.T("live.waitingJob")}   ({Strings.T("live.ticks")}: {_engine.TickCount})"
                : Strings.T("live.waitingGame");
            _jobLine.Text = "";
            _jobDetail.Text = "";
            _progress.Value = 0;
            return;
        }

        var state = _engine.ActiveState;
        var driven = state?.DistanceKm ?? 0;
        var planned = job.PlannedDistanceKm;
        var u = CurrentUnits();

        _status.Text = $"{Strings.T("live.jobRunning")}   ({Strings.T("live.ticks")}: {_engine.TickCount}, {Strings.T("live.deliveriesThisRun")}: {_engine.DeliveriesThisRun})";
        _jobLine.Text = $"{job.SourceCity} -> {job.DestinationCity}";
        _jobDetail.Text = $"{job.Cargo}, {u.MassTonnes(job.CargoMassKg):0.0} {u.MassUnit}   |   "
                        + $"{u.Distance(driven):0.0} / {u.Distance(planned):0} {u.DistanceUnit}   |   "
                        + $"{Strings.T("live.reward")} {u.FormatMoney(job.Income)}";

        // Planned distance is the game's own route length, in the same simulated km
        // the odometer counts, so this genuinely tracks progress toward the drop-off.
        var ratio = planned > 0 ? Math.Clamp(driven / planned, 0, 1) : 0;
        _progress.Value = (int)(ratio * _progress.Maximum);
    }

    // ---------- tabs ----------

    private Control BuildTabs() {
        var tabs = new TabControl { Dock = DockStyle.Fill };
        tabs.TabPages.Add(BuildHistoryTab());
        tabs.TabPages.Add(BuildStatsTab());
        return tabs;
    }

    private TabPage BuildHistoryTab() {
        var page = new TabPage(Strings.T("tab.deliveries")) { Padding = new Padding(8) };

        // Flow layout rather than fixed coordinates, so buttons size to their own
        // text and nothing gets clipped when a label changes.
        var bar = new FlowLayoutPanel {
            Dock = DockStyle.Top,
            Height = 36,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(0, 3, 0, 3),
        };

        _search.Width = 240;
        _search.Margin = new Padding(0, 3, 8, 3);
        _search.PlaceholderText = Strings.T("search.placeholder");
        _search.TextChanged += (_, _) => ApplyFilter();

        _statusFilter.Width = 120;
        _statusFilter.Margin = new Padding(0, 3, 16, 3);
        _statusFilter.DropDownStyle = ComboBoxStyle.DropDownList;
        // The controls are reused when the layout is rebuilt for a language change,
        // so the old entries have to go first. Left in place they pile up and, worse,
        // the still-selected entry is in the previous language while the filter
        // compares against the new one, which quietly empties the list.
        _statusFilter.Items.Clear();
        _statusFilter.Items.AddRange(new object[] { Strings.T("filter.all"), "accepted", "review", "rejected", "imported" });
        _statusFilter.SelectedIndex = 0;
        _statusFilter.SelectedIndexChanged -= OnFilterChanged;
        _statusFilter.SelectedIndexChanged += OnFilterChanged;

        bar.Controls.Add(_search);
        bar.Controls.Add(_statusFilter);
        bar.Controls.Add(MakeButton(Strings.T("button.refresh"), () => { ReloadHistory(); ReloadStats(); }));
        bar.Controls.Add(MakeButton(Strings.T("button.exportCsv"), () => Export("csv")));
        bar.Controls.Add(MakeButton(Strings.T("button.exportJson"), () => Export("json")));
        bar.Controls.Add(MakeButton(Strings.T("button.backup"), DoBackup));
        bar.Controls.Add(MakeButton(Strings.T("button.restore"), DoRestore));

        _grid.Dock = DockStyle.Fill;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.MultiSelect = false;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.RowHeadersVisible = false;
        _grid.EditMode = DataGridViewEditMode.EditOnEnter;

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
        _timeline.DataBindingComplete -= OnTimelineBound;
        _timeline.DataBindingComplete += OnTimelineBound;

        var splitLabel = new Label { Dock = DockStyle.Bottom, Height = 20, Text = Strings.T("timeline.label"), ForeColor = SystemColors.GrayText };

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

    /// <summary>Colours the verdict so problem deliveries stand out without reading.</summary>
    private void OnGridCellFormatting(object? sender, DataGridViewCellFormattingEventArgs e) {
        if (_grid.Columns[e.ColumnIndex].DataPropertyName != nameof(DeliveryRow.Stav)) return;
        e.CellStyle!.ForeColor = Convert.ToString(e.Value) switch {
            "rejected" => Color.Firebrick,
            "review" => Color.DarkGoldenrod,
            "imported" => Color.SteelBlue,
            _ => Color.ForestGreen,
        };
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
                [nameof(DeliveryRow.Hra)] = 45,
                [nameof(DeliveryRow.Odkial)] = 105,
                [nameof(DeliveryRow.Kam)] = 105,
                [nameof(DeliveryRow.Naklad)] = 130,
                [nameof(DeliveryRow.Tahac)] = 115,
                [nameof(DeliveryRow.Vzdialenost)] = 75,
                [nameof(DeliveryRow.Odmena)] = 75,
                [nameof(DeliveryRow.Pokuty)] = 50,
                [nameof(DeliveryRow.Kolizie)] = 50,
                [nameof(DeliveryRow.Stav)] = 80,
                [nameof(DeliveryRow.Poznamky)] = 120,
            };
            foreach (DataGridViewColumn col in _grid.Columns) {
                if (weights.TryGetValue(col.DataPropertyName, out var w)) col.FillWeight = w;
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

    private static Button MakeButton(string text, Action onClick) {
        var b = new Button { Text = text, AutoSize = true, Height = 26, Margin = new Padding(0, 3, 6, 3), Padding = new Padding(8, 0, 8, 0) };
        b.Click += (_, _) => onClick();
        return b;
    }

    private TabPage BuildStatsTab() {
        var page = new TabPage(Strings.T("tab.stats")) { Padding = new Padding(16) };
        _statsLabel.Dock = DockStyle.Fill;
        _statsLabel.Font = new Font("Consolas", 11F);
        page.Controls.Add(_statsLabel);
        return page;
    }

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

        _statsLabel.Text = string.Join(Environment.NewLine, new[] {
            $"{Strings.T("stats.deliveries"),-18} {s.TotalDeliveries}   (accepted {s.Accepted}, review {s.Review}, rejected {s.Rejected})",
            $"{Strings.T("stats.distance"),-18} {u.FormatDistance(s.TotalDistanceKm)}",
            $"{Strings.T("stats.revenue"),-18} {u.FormatMoney(s.TotalRevenue)}",
            $"{Strings.T("stats.fuel"),-18} {u.FormatVolume(s.TotalFuelL)}",
            $"{Strings.T("stats.time"),-18} {realHours:0.0} {Strings.T("stats.realTime")} ({gameHours:0.0} {Strings.T("stats.gameTime")})",
            $"{Strings.T("stats.avgSpeed"),-18} {u.FormatSpeed(avg)}",
            "",
            $"{Strings.T("stats.collisions"),-18} {s.TotalCollisions}",
            $"{Strings.T("stats.late"),-18} {s.LateDeliveries}",
            $"{Strings.T("stats.finesTotal"),-18} {u.FormatMoney(s.TotalFines)}",
            "",
            $"{Strings.T("stats.favTruck"),-18} {s.FavoriteTruck ?? "?"}",
            $"{Strings.T("stats.favRoute"),-18} {s.FavoriteRoute ?? "?"}",
            $"{Strings.T("stats.favCargo"),-18} {s.FavoriteCargo ?? "?"}",
        });
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
