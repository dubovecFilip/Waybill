using System.Data;
using System.Diagnostics;
using System.Windows.Forms;
using TelemetryReader.Storage;
using TelemetryReader.Tracking;

namespace TelemetryReader;

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
        Text = "TrucksBook Overcomer";
        Width = 1100;
        Height = 720;
        MinimumSize = new Size(900, 560);
        Font = new Font("Segoe UI", 9F);
        StartPosition = FormStartPosition.CenterScreen;

        var tabs = BuildTabs();
        var live = BuildLivePanel();
        var menu = BuildMenu();

        Controls.Add(tabs);
        Controls.Add(live);
        Controls.Add(menu);
        MainMenuStrip = menu;

        Load += (_, _) => StartEngine();
        FormClosing += (_, _) => _engine?.Dispose();

        var timer = new System.Windows.Forms.Timer { Interval = 500 };
        timer.Tick += (_, _) => RefreshLive();
        timer.Start();
    }

    // ---------- menu ----------

    private MenuStrip BuildMenu() {
        var menu = new MenuStrip();
        var units = new ToolStripMenuItem("Jednotky");

        // "auto" is the default because each title has its own convention; the
        // explicit choices are for anyone who wants one system everywhere.
        var options = new (string Key, string Label)[] {
            ("auto", "Podla hry (ATS imperial, ETS2 metricke)"),
            ("metric", "Metricke (km, l, t)"),
            ("imperial", "Imperialne (mi, gal, short t)"),
        };

        foreach (var (key, label) in options) {
            var item = new ToolStripMenuItem(label) { Tag = key, Checked = _settings.Units == key };
            item.Click += (s, _) => {
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

        menu.Items.Add(units);

        // Data lives under LocalAppData, not next to the exe, so it survives
        // rebuilds - which also makes it hard to find by hand.
        var data = new ToolStripMenuItem("Data");
        data.DropDownItems.Add(OpenFolderItem("Priecinok s databazou", DeliveryStore.DefaultDir()));
        data.DropDownItems.Add(OpenFolderItem("Priecinok so zalohami", Path.Combine(DeliveryStore.DefaultDir(), "backups")));
        data.DropDownItems.Add(OpenFolderItem("Priecinok s nahravkami", Path.Combine(DeliveryStore.DefaultDir(), "sessions")));
        menu.Items.Add(data);

        return menu;
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

        _status.Text = "Spustam...";
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
        _engine.JobStarted += j => BeginInvoke(() => AddLog($"ZACIATOK  {j.SourceCity} -> {j.DestinationCity} ({j.Cargo})"));
        _engine.JobResumed += j => BeginInvoke(() => AddLog($"POKRACUJEM  {j.SourceCity} -> {j.DestinationCity}"));
        _engine.JobFinished += r => BeginInvoke(() => {
            AddLog($"KONIEC  {r.SourceCity} -> {r.DestinationCity}: {r.DistanceKm:0.0} km, {r.Validation.Status}");
            ReloadHistory();
            ReloadStats();
        });

        if (!_engine.Start()) {
            AddLog("Nepodarilo sa pripojit na zdielanu pamat: " + _engine.StartupError);
            AddLog("Je hra spustena a je plugin v bin\\win_x64\\plugins ?");
        }

        AddLog("Nahravka: " + _engine.SessionPath);
        AddLog("Databaza: " + _engine.DbPath);

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
                ? $"Pripojene na hru - cakam na zakazku   (tiky: {_engine.TickCount})"
                : "Cakam na hru...";
            _jobLine.Text = "";
            _jobDetail.Text = "";
            _progress.Value = 0;
            return;
        }

        var state = _engine.ActiveState;
        var driven = state?.DistanceKm ?? 0;
        var planned = job.PlannedDistanceKm;
        var u = CurrentUnits();

        _status.Text = $"Zakazka prebieha   (tiky: {_engine.TickCount}, zasielok tento beh: {_engine.DeliveriesThisRun})";
        _jobLine.Text = $"{job.SourceCity} -> {job.DestinationCity}";
        _jobDetail.Text = $"{job.Cargo}, {u.MassTonnes(job.CargoMassKg):0.0} {u.MassUnit}   |   "
                        + $"{u.Distance(driven):0.0} / {u.Distance(planned):0} {u.DistanceUnit}   |   "
                        + $"odmena {u.FormatMoney(job.Income)}";

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
        var page = new TabPage("Zasielky") { Padding = new Padding(8) };

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
        _search.PlaceholderText = "hladat mesto / naklad / tahac...";
        _search.TextChanged += (_, _) => ApplyFilter();

        _statusFilter.Width = 120;
        _statusFilter.Margin = new Padding(0, 3, 16, 3);
        _statusFilter.DropDownStyle = ComboBoxStyle.DropDownList;
        _statusFilter.Items.AddRange(new object[] { "vsetky", "accepted", "review", "rejected" });
        _statusFilter.SelectedIndex = 0;
        _statusFilter.SelectedIndexChanged += (_, _) => ApplyFilter();

        bar.Controls.Add(_search);
        bar.Controls.Add(_statusFilter);
        bar.Controls.Add(MakeButton("Obnovit", () => { ReloadHistory(); ReloadStats(); }));
        bar.Controls.Add(MakeButton("Export CSV", () => Export("csv")));
        bar.Controls.Add(MakeButton("Export JSON", () => Export("json")));
        bar.Controls.Add(MakeButton("Zalohovat", DoBackup));
        bar.Controls.Add(MakeButton("Obnovit zo zalohy", DoRestore));

        _grid.Dock = DockStyle.Fill;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.MultiSelect = false;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.RowHeadersVisible = false;
        _grid.EditMode = DataGridViewEditMode.EditOnEnter;
        _grid.SelectionChanged += (_, _) => ReloadTimeline();

        // Everything the tracker measured stays read-only; only the user's own note
        // is editable, and it is written straight back to the database on edit.
        _grid.DataBindingComplete += (_, _) => {
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

            if (_grid.Columns[nameof(DeliveryRow.Datum)] is { } dateCol) {
                dateCol.DefaultCellStyle.Format = "dd.MM.yy HH:mm";
            }
            foreach (var numeric in new[] { nameof(DeliveryRow.Vzdialenost), nameof(DeliveryRow.Odmena), nameof(DeliveryRow.Pokuty), nameof(DeliveryRow.Kolizie) }) {
                if (_grid.Columns[numeric] is { } c) c.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
            }
        };
        _grid.CellEndEdit += (_, e) => {
            if (_grid.Columns[e.ColumnIndex].DataPropertyName != nameof(DeliveryRow.Poznamky)) return;
            if (_grid.Rows[e.RowIndex].DataBoundItem is DeliveryRow row) _store.SetNotes(row.Id, row.Poznamky ?? "");
        };

        // Colour the verdict so problem deliveries stand out without reading.
        _grid.CellFormatting += (_, e) => {
            if (_grid.Columns[e.ColumnIndex].DataPropertyName != nameof(DeliveryRow.Stav)) return;
            e.CellStyle.ForeColor = Convert.ToString(e.Value) switch {
                "rejected" => Color.Firebrick,
                "review" => Color.DarkGoldenrod,
                _ => Color.ForestGreen,
            };
        };

        _timeline.Dock = DockStyle.Bottom;
        _timeline.Height = 150;
        _timeline.ReadOnly = true;
        _timeline.AllowUserToAddRows = false;
        _timeline.RowHeadersVisible = false;
        _timeline.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;

        var splitLabel = new Label { Dock = DockStyle.Bottom, Height = 20, Text = "Casova os vybranej zasielky:", ForeColor = SystemColors.GrayText };

        page.Controls.Add(_grid);
        page.Controls.Add(splitLabel);
        page.Controls.Add(_timeline);
        page.Controls.Add(bar);
        return page;
    }

    private static Button MakeButton(string text, Action onClick) {
        var b = new Button { Text = text, AutoSize = true, Height = 26, Margin = new Padding(0, 3, 6, 3), Padding = new Padding(8, 0, 8, 0) };
        b.Click += (_, _) => onClick();
        return b;
    }

    private TabPage BuildStatsTab() {
        var page = new TabPage("Statistiky") { Padding = new Padding(16) };
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
        var status = _statusFilter.SelectedItem as string ?? "vsetky";

        IEnumerable<DeliveryRow> filtered = _rows;
        if (status != "vsetky") filtered = filtered.Where(r => r.Stav == status);
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
        var avg = gameHours > 0.01 ? s.TotalDistanceKm / gameHours : 0;

        _statsLabel.Text = string.Join(Environment.NewLine, new[] {
            $"zasielok spolu     {s.TotalDeliveries}   (accepted {s.Accepted}, review {s.Review}, rejected {s.Rejected})",
            $"vzdialenost        {u.FormatDistance(s.TotalDistanceKm)}",
            $"zarobok            {u.FormatMoney(s.TotalRevenue)}",
            $"palivo             {u.FormatVolume(s.TotalFuelL)}",
            $"cas za volantom    {realHours:0.0} h realneho ({gameHours:0.0} h herneho)",
            $"priemerna rychlost {u.FormatSpeed(avg)}",
            "",
            $"kolizie            {s.TotalCollisions}",
            $"meskania           {s.LateDeliveries}",
            $"pokuty spolu       {u.FormatMoney(s.TotalFines)}",
            "",
            $"oblubeny tahac     {s.FavoriteTruck ?? "-"}",
            $"oblubena trasa     {s.FavoriteRoute ?? "-"}",
            $"oblubeny naklad    {s.FavoriteCargo ?? "-"}",
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
        MessageBox.Show(this, "Exportovane do:\n" + dlg.FileName, "Export");
    }

    private void DoBackup() {
        var path = _store.Backup();
        MessageBox.Show(this, "Zaloha ulozena:\n" + path, "Zaloha");
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
