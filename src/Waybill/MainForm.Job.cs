using Waybill.Integrations;
using Waybill.Storage;
using Waybill.Tracking;

namespace Waybill;

/// <summary>
/// The page the window opens on: the drive in progress, said twice.
///
/// It used to be two pages. One was a glance, the other was the tracker narrating
/// itself, and both were about the same drive at the same moment, so whichever you
/// were on you were on the wrong one half the time. They are one page now: the
/// figures on the left, the log beside them, and the drive drawing itself
/// underneath as it goes.
///
/// The last few deliveries and the quick settings that used to sit under the
/// figures are gone rather than moved. Both already had a home, in the list and in
/// the menus, and neither was about the drive in progress.
/// </summary>
public partial class MainForm {
    private readonly Panel _jobPage = new();
    private RouteView? _jobMap;
    private Panel? _jobLaunch;

    /// <summary>What the map is currently drawing, so it is only redrawn when there
    /// is more of it. The route grows once a second and the page refreshes twice, so
    /// redrawing on every refresh would be work for a line that has not moved.</summary>
    private string _jobShowing = "";
    private int _jobShown = -1;

    /// <summary>How many new points are worth a redraw. Each one re-fits the view
    /// and, on this page, works out which way to turn the drive.</summary>
    private const int JobRouteStep = 10;

    private Panel BuildJobPage() {
        _jobPage.Dock = DockStyle.Fill;
        _jobPage.BackColor = Canvas;
        _jobPage.Padding = new Padding(16, 8, 16, 16);
        _jobPage.Controls.Clear();

        // Added innermost first: the map takes what the card above it leaves.
        _jobPage.Controls.Add(BuildJobMap());
        _jobPage.Controls.Add(new Panel { Dock = DockStyle.Top, Height = 12, BackColor = Canvas });
        _jobPage.Controls.Add(BuildJobCard());
        _jobPage.Controls.Add(new Label {
            Dock = DockStyle.Top, Height = 36, Text = Strings.T("job.onTheRoad"), BackColor = Canvas,
            ForeColor = Ink, Font = new Font("Segoe UI", 10.5F, FontStyle.Bold),
            Padding = new Padding(0, 8, 0, 0),
        });
        return _jobPage;
    }

    /// <summary>The figures on the left, the log on the right. The log is the whole
    /// of it, technical lines included: it is there for the question "is the tracker
    /// seeing the game at all", and the lines that answer that are exactly the ones
    /// a tidier log would drop.</summary>
    private Control BuildJobCard() {
        var frame = new Panel { Dock = DockStyle.Top, Height = 262, BackColor = Line, Padding = new Padding(1) };
        var card = new Panel { Dock = DockStyle.Fill, BackColor = Surface };

        var logBox = new Panel { Dock = DockStyle.Right, Width = 430, BackColor = Surface, Padding = new Padding(16, 14, 16, 14) };
        _log.Dock = DockStyle.Fill;
        _log.BorderStyle = BorderStyle.None;
        _log.BackColor = Surface;
        _log.ForeColor = Muted;
        _log.Font = new Font("Consolas", 8.5F);
        _log.IntegralHeight = false;
        // Nothing follows from picking a log line, and the system highlight it draws
        // is a bright blue bar that belongs to no part of this window.
        _log.SelectionMode = SelectionMode.None;
        _status.Dock = DockStyle.Top;
        _status.Height = 22;
        _status.ForeColor = Muted;
        _status.Font = new Font("Segoe UI", 8F, FontStyle.Bold);
        _status.Text = Strings.T("live.starting");
        logBox.Controls.Add(_log);
        logBox.Controls.Add(_status);

        var edge = new Panel { Dock = DockStyle.Right, Width = 1, BackColor = Line };
        var text = new Panel { Dock = DockStyle.Fill, BackColor = Surface, Padding = new Padding(20, 16, 20, 16) };

        _jobLine.Dock = DockStyle.Top;
        _jobLine.Height = 40;
        _jobLine.Font = new Font("Segoe UI", 15F, FontStyle.Bold);
        _jobLine.ForeColor = Muted;
        _jobLine.AutoEllipsis = true;
        _jobLine.Text = Strings.T("live.waitingGame");

        _jobDetail.Dock = DockStyle.Top;
        _jobDetail.Height = 26;
        _jobDetail.ForeColor = Muted;
        _jobDetail.AutoEllipsis = true;
        _jobDetail.Text = "";

        _progressText.Dock = DockStyle.Top;
        _progressText.Height = 26;
        _progressText.ForeColor = Ink;
        _progressText.TextAlign = ContentAlignment.MiddleLeft;
        _progressText.Padding = new Padding(0, 6, 0, 0);
        _progressText.AutoEllipsis = true;

        _progressTrack.Dock = DockStyle.Top;
        _progressTrack.Height = 8;
        _progressTrack.BackColor = Raised;
        _progressLead.Dock = DockStyle.Left;
        _progressLead.Width = 0;
        _progressLead.BackColor = Muted;
        _progressFill.Dock = DockStyle.Left;
        _progressFill.Width = 0;
        _progressFill.BackColor = Accent;
        // Whatever was driven past the plan, in a dimmer amber. Third segment rather
        // than a bar that stops at full: a drive that overshot by a tenth is a fact
        // about the drive, and a bar pinned at 100 % hides it.
        _progressOver.Dock = DockStyle.Left;
        _progressOver.Width = 0;
        _progressOver.BackColor = Color.FromArgb(150, 112, 52);
        // Docked left, so the last one added sits leftmost: the run-up, then the
        // loaded stretch, then the overshoot, which is the order they happened in.
        _progressTrack.Controls.Add(_progressOver);
        _progressTrack.Controls.Add(_progressFill);
        _progressTrack.Controls.Add(_progressLead);

        // Only up while there is no game to talk about. Starting the game is the one
        // thing worth offering on a page that otherwise has nothing to say.
        var launch = new FlowLayoutPanel {
            Dock = DockStyle.Bottom, Height = 44, BackColor = Surface,
            WrapContents = false, Padding = new Padding(0, 10, 0, 0),
        };
        foreach (var game in new[] { SimGame.Ets2, SimGame.Ats }) {
            var installed = GameLauncher.IsInstalled(game);
            var start = MakeButton("▷    " + GameLauncher.DisplayName(game), () => LaunchGame(game));
            start.Enabled = installed;
            start.Margin = new Padding(0, 3, 10, 3);
            if (!installed) _tips.SetToolTip(start, Strings.T("msg.gameNotFound"));
            launch.Controls.Add(start);
        }
        _jobLaunch = launch;

        text.Controls.Add(launch);
        text.Controls.Add(_progressText);
        text.Controls.Add(_progressTrack);
        text.Controls.Add(new Panel { Dock = DockStyle.Top, Height = 10, BackColor = Surface });
        text.Controls.Add(_jobDetail);
        text.Controls.Add(_jobLine);

        card.Controls.Add(text);
        card.Controls.Add(edge);
        card.Controls.Add(logBox);
        frame.Controls.Add(card);
        return frame;
    }

    private Control BuildJobMap() {
        var frame = new Panel { Dock = DockStyle.Fill, BackColor = Line, Padding = new Padding(1) };
        _jobMap = NewMap(CurrentUnits());
        _jobMap.Dock = DockStyle.Fill;
        _jobMap.ShowMarks = false;
        _jobMap.EmptyText = Strings.T("job.noRoute");
        _jobMap.Hint = "";
        // Turned to lie along the panel, which is much wider than it is tall. A drive
        // running north to south would otherwise draw itself as a thread down the
        // middle with nine tenths of the panel empty either side.
        _jobMap.Straighten = true;
        // A picture, not an instrument. The map page is one click away for anyone who
        // wants to zoom something.
        _jobMap.Cursor = Cursors.Default;
        frame.Controls.Add(_jobMap);
        return frame;
    }

    /// <summary>
    /// Brings the page up to date with whatever the tracker can see, twice a second.
    /// </summary>
    private void RefreshJob() {
        if (_engine is null || _jobMap is null) return;

        var job = _engine.ActiveJob;
        var state = _engine.ActiveState;
        var u = CurrentUnits();

        if (job is null || state is null) {
            _status.Text = (_engine.Connected
                ? $"{Strings.T("live.waitingJob")}   ({Strings.T("live.ticks")}: {_engine.TickCount})"
                : Strings.T("live.waitingGame")).ToUpperInvariant();
            _jobLine.Text = _engine.Connected ? Strings.T("live.noJob") : Strings.T("live.waitingGame");
            _jobLine.ForeColor = Muted;
            _jobDetail.Text = "";
            _progressText.Text = "";
            _progressTrack.Visible = false;
            if (_jobLaunch is { } waiting) waiting.Visible = true;
            ShowJobRoute(null, "");

            // Between jobs the profile says so; with the game closed it says nothing
            // at all, rather than leaving Waybill sitting there all evening.
            _discord?.Update(_engine.Connected
                ? new DiscordPresence.Activity { Details = Strings.T("discord.idle"), LargeImage = "waybill", LargeText = "Waybill" }
                : null);
            return;
        }

        if (_jobLaunch is { } running) running.Visible = false;
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
        var driven = state.DistanceKm;
        var planned = job.PlannedDistanceKm;
        var toLoad = state.DistanceToLoadKm;
        var loaded = Math.Max(0, driven - toLoad);
        // Not clamped. Going past the plan is ordinary: a detour, a closed road, a
        // wrong turn. Saying 100 % for the last forty kilometres of it would be the
        // one stretch where the figure stopped meaning anything.
        var ratio = planned > 0 ? loaded / planned : 0;

        // The track holds the whole job, and once the drive runs past the plan it
        // holds the drive instead, so the bar rescales rather than filling up and
        // stopping. The plan then sits where it fell: at 112 % it is nine tenths of
        // the way along and the overshoot is what is left.
        var whole = toLoad + Math.Max(planned, loaded);
        _progressTrack.Visible = true;
        var track = _progressTrack.ClientSize.Width;
        _progressLead.Width = whole > 0 ? (int)(track * toLoad / whole) : 0;
        _progressFill.Width = whole > 0 ? (int)(track * Math.Min(loaded, planned) / whole) : 0;
        _progressOver.Width = whole > 0 ? (int)(track * Math.Max(0, loaded - planned) / whole) : 0;

        _progressText.Text = $"{u.Distance(loaded):0.0} / {u.Distance(planned):0} {u.DistanceUnit}   ·   {ratio * 100:0} %"
            + (toLoad > 0.05 ? $"   (+{u.Distance(toLoad):0.0} {Strings.T("live.toLoad")})" : "");

        ShowJobRoute(state, state.Game);

        // The same three numbers, in one line each, for Discord. The start time is
        // sent raw so Discord runs the elapsed counter itself, which keeps ticking
        // between the updates it only accepts every 15 seconds.
        var game = state.Game;
        _discord?.Update(new DiscordPresence.Activity {
            Details = $"{job.SourceCity} → {job.DestinationCity}",
            State = planned > 0
                ? $"{job.Cargo} · {u.Distance(loaded):0} / {u.Distance(planned):0} {u.DistanceUnit} ({ratio * 100:0} %)"
                : job.Cargo,
            StartUnix = state.StartedAtMs / 1000,
            LargeImage = game.ToLowerInvariant() is "ats" or "ets2" ? game.ToLowerInvariant() : "waybill",
            LargeText = game == "Ats" ? GameLauncher.DisplayName(SimGame.Ats)
                      : game == "Ets2" ? GameLauncher.DisplayName(SimGame.Ets2) : "Waybill",
            SmallImage = "waybill",
            SmallText = "Waybill",
        });
    }

    /// <summary>
    /// Draws the drive so far, with the cities this profile knows behind it.
    ///
    /// Only the drive itself: the rest of the history is a thousand times more line
    /// than one panel can show and would have to be taken apart again on every
    /// redraw. Cities cost nothing and are what make a line mean somewhere.
    /// </summary>
    private void ShowJobRoute(JobState? state, string game) {
        if (_jobMap is null) return;

        var points = state?.TripPoints ?? new List<TripPoint>();
        var key = state is null ? "" : state.JobUid;
        var same = key == _jobShowing;
        if (same && points.Count < _jobShown + JobRouteStep) return;
        _jobShowing = key;
        _jobShown = points.Count;

        if (points.Count < 2) {
            _jobMap.Show(Array.Empty<RouteLayer>(), 0, new List<CityAnchor>());
            return;
        }

        var live = new RouteLayer {
            // Deliveries carry their database id and this drive has none yet, so it
            // takes one no row can hold rather than borrowing somebody else's.
            Id = -1,
            Points = points.Select(p => new RoutePoint(p.AtMs, (float)p.X, (float)p.Z, (float)p.SpeedKmh)).ToList(),
        };
        var cities = game.Length > 0 ? RoutesFor(game).Cities : new List<CityAnchor>();
        _jobMap.Show(new List<RouteLayer> { live }, live.Id, cities);
    }
}
