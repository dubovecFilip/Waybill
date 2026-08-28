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

    /// <summary>The close view: the truck in the middle at a fixed scale, and the
    /// panel it sits in when there is room for it under the wide one.</summary>
    private RouteView? _jobClose;
    private Panel? _jobCloseFrame;

    /// <summary>The one pixel of frame around the close view while it sits over the
    /// wide map. Without it the two drawings run into each other, since both are the
    /// same dark ground with the same lines on it.</summary>
    private Panel? _jobCloseCorner;
    private Panel? _jobCloseGap;
    private Panel? _jobMapFrame;

    /// <summary>Which corner the close view sits in when it is laid over the wide
    /// map, kept so it does not hop about from one second to the next.</summary>
    private ContentAlignment _closeCorner = ContentAlignment.BottomRight;

    /// <summary>Which game's history the two maps are holding behind the drive. Taking
    /// a history apart into stretches is the most expensive thing either of them does,
    /// and it is worth doing once rather than once a second.</summary>
    private string _mapsHolding = "";
    private Panel? _jobLaunch;

    /// <summary>What the map is currently drawing, so it is only redrawn when there
    /// is more of it. The route grows once a second and the page refreshes twice, so
    /// redrawing on every refresh would be work for a line that has not moved.</summary>
    private string _jobShowing = "";
    private int _jobShown = -1;

    /// <summary>How many new points are worth a redraw. Each one re-fits the view
    /// and, on this page, works out which way to turn the drive.</summary>
    /// <summary>How many new readings it takes to draw the live map again. One, so
    /// the line keeps up with the truck: the tracker records a point a second, and at
    /// ten the map sat ten seconds behind the drive it was showing.</summary>
    private const int JobRouteStep = 1;

    private Panel BuildJobPage() {
        _jobPage.Dock = DockStyle.Fill;
        _jobPage.BackColor = Canvas;
        _jobPage.Padding = new Padding(16, 8, 16, 16);
        _jobPage.Controls.Clear();

        // Added innermost first: the map takes what the card above it leaves.
        if (_settings.LiveMap) _jobPage.Controls.Add(BuildJobMap());
        else _jobMap = null;
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

    /// <summary>
    /// The drive in progress, twice over.
    ///
    /// The wide one is the delivery, fitted to what has been driven so far, with the
    /// rest of this game's history behind it in a quieter line so a route in a corner
    /// of the map still has somewhere to sit. The close one holds the truck in the
    /// middle at a fixed scale, about thirty kilometres of road across, which answers
    /// "where am I now" rather than "where does this go".
    ///
    /// Both are pictures rather than instruments: no zooming, no panning, no compass,
    /// north up. That last one is what lets a glance here and a glance at the map in
    /// the cab agree with each other.
    /// </summary>
    private Control BuildJobMap() {
        var frame = new Panel { Dock = DockStyle.Fill, BackColor = Canvas };

        _jobMap = NewMap(CurrentUnits());
        _jobMap.Dock = DockStyle.Fill;
        _jobMap.ShowMarks = false;
        _jobMap.EmptyText = Strings.T("job.noRoute");
        _jobMap.Hint = "";
        _jobMap.Cursor = Cursors.Default;
        _jobMap.Locked = true;

        _jobClose = NewMap(CurrentUnits());
        _jobClose.ShowMarks = false;
        _jobClose.ShowCities = true;
        _jobClose.EmptyText = "";
        _jobClose.Hint = "";
        _jobClose.Cursor = Cursors.Default;
        _jobClose.WorldWidth = CloseWorldMetres;
        _jobClose.Locked = true;
        _jobClose.Visible = false;

        var wide = new Panel { Dock = DockStyle.Fill, BackColor = Line, Padding = new Padding(1) };
        wide.Controls.Add(_jobMap);

        _jobCloseCorner = new Panel { BackColor = Line, Padding = new Padding(1), Visible = false };
        _jobCloseGap = new Panel { Dock = DockStyle.Bottom, Height = 8, BackColor = Canvas, Visible = false };
        _jobCloseFrame = new Panel { Dock = DockStyle.Bottom, BackColor = Line, Padding = new Padding(1), Height = 0, Visible = false };

        frame.Controls.Add(wide);
        frame.Controls.Add(_jobCloseGap);
        frame.Controls.Add(_jobCloseFrame);
        _jobMapFrame = frame;
        frame.Resize += (_, _) => PlaceCloseUp();
        return frame;
    }

    /// <summary>How much of the world the close view holds across its width, in the
    /// game's own metres. The odometer runs about seventeen times further than the
    /// position does, measured over this driver's own deliveries, so this is about
    /// thirty kilometres of road.</summary>
    private const float CloseWorldMetres = 1800f;

    /// <summary>
    /// Where the close view goes, which follows the shape of the room it is in.
    ///
    /// Wider than it is tall, and it takes a column down the right. Taller than it is
    /// wide, and it takes a band along the bottom. Roughly square, and there is no
    /// side to give it without ruining the shape, so it goes into a corner of the wide
    /// map instead and the wide map is told to keep the drive out from under it.
    ///
    /// Either way the wide map is left as near square as the panel allows, which is
    /// the shape that suits a route however it happens to run.
    /// </summary>
    private void PlaceCloseUp() {
        if (_jobMapFrame is not { } frame || _jobMap is null || _jobClose is null) return;
        if (_jobCloseFrame is not { } band || _jobCloseGap is not { } gap || _jobCloseCorner is not { } corner) return;

        if (!_jobClose.Visible && !band.Visible && !corner.Visible) {
            _jobMap.Reserved = Padding.Empty;
            return;
        }

        var room = frame.ClientSize;
        var shape = room.Height > 0 ? room.Width / (float)room.Height : 1f;

        if (shape > 1.25f) {
            Give(band, gap, corner, DockStyle.Right, Math.Clamp(room.Width - room.Height, 200, 380));
            return;
        }
        if (shape < 0.8f) {
            Give(band, gap, corner, DockStyle.Bottom, Math.Clamp(room.Height - room.Width, 160, 300));
            return;
        }

        // Square enough that any band would spoil it: over a corner instead.
        band.Visible = false;
        gap.Visible = false;
        if (corner.Parent != _jobMap.Parent) _jobMap.Parent!.Controls.Add(corner);
        if (_jobClose.Parent != corner) corner.Controls.Add(_jobClose);
        _jobClose.Dock = DockStyle.Fill;
        corner.Visible = true;
        corner.BringToFront();

        var host = _jobMap.Parent!.ClientSize;
        var side = Math.Max(140, Math.Min(host.Width / 3, 260));
        var tall = Math.Max(100, side * 3 / 4);
        var left = _closeCorner is ContentAlignment.TopLeft or ContentAlignment.BottomLeft;
        var top = _closeCorner is ContentAlignment.TopLeft or ContentAlignment.TopRight;
        corner.Bounds = new Rectangle(
            left ? 10 : host.Width - side - 10,
            top ? 10 : host.Height - tall - 10, side, tall);
        // A band the full height of the panel, since the fit can only be told to keep
        // out of an edge rather than out of a rectangle. Wasteful of a strip above or
        // below the close view, and worth it for a drive that never hides under it.
        _jobMap.Reserved = left ? new Padding(side + 20, 0, 0, 0) : new Padding(0, 0, side + 20, 0);
    }

    /// <summary>Hands the close view a side of its own, along the edge named.</summary>
    private void Give(Panel band, Panel gap, Panel corner, DockStyle side, int thickness) {
        corner.Visible = false;
        if (_jobClose!.Parent != band) band.Controls.Add(_jobClose);
        _jobClose.Dock = DockStyle.Fill;

        band.Dock = side;
        gap.Dock = side;
        gap.Width = 8;
        gap.Height = 8;
        if (side == DockStyle.Right) band.Width = thickness; else band.Height = thickness;
        band.Visible = true;
        gap.Visible = true;
        _jobMap!.Reserved = Padding.Empty;
    }

    /// <summary>
    /// Which corner of the wide map has least of the drive in it.
    ///
    /// The close view has to sit somewhere, and the least it can cost is the corner
    /// the route was not using. Counted in quarters of the drive's own bounding box,
    /// and only moved when another corner is clearly emptier, so it does not hop from
    /// one second to the next as the line grows.
    /// </summary>
    private void ChooseCorner(List<RoutePoint> points) {
        if (points.Count < 30) return;

        double minX = double.MaxValue, maxX = double.MinValue, minZ = double.MaxValue, maxZ = double.MinValue;
        foreach (var p in points) {
            minX = Math.Min(minX, p.X); maxX = Math.Max(maxX, p.X);
            minZ = Math.Min(minZ, p.Z); maxZ = Math.Max(maxZ, p.Z);
        }
        var midX = (minX + maxX) / 2;
        var midZ = (minZ + maxZ) / 2;

        // North is negative z, so a smaller z is nearer the top of the panel.
        var counts = new Dictionary<ContentAlignment, int> {
            [ContentAlignment.TopLeft] = 0,
            [ContentAlignment.TopRight] = 0,
            [ContentAlignment.BottomLeft] = 0,
            [ContentAlignment.BottomRight] = 0,
        };
        foreach (var p in points) {
            var corner = (p.X < midX, p.Z < midZ) switch {
                (true, true) => ContentAlignment.TopLeft,
                (false, true) => ContentAlignment.TopRight,
                (true, false) => ContentAlignment.BottomLeft,
                _ => ContentAlignment.BottomRight,
            };
            counts[corner]++;
        }

        var best = counts.OrderBy(c => c.Value).First();
        if (best.Key == _closeCorner) return;
        // A tenth of the drive's points is the margin worth moving for.
        if (counts[_closeCorner] - best.Value < points.Count / 10) return;
        _closeCorner = best.Key;
        PlaceCloseUp();
    }

    /// <summary>
    /// Brings the page up to date with whatever the tracker can see, twice a second.
    /// </summary>
    /// <summary>
    /// A delivery that has already been driven, dressed up as one in progress.
    ///
    /// Cut off at five eighths of the way along, which is far enough that the figures
    /// read like a job under way and short enough that the route on the map is
    /// plainly still being drawn. Everything in it is read from the delivery: the
    /// cities, the load, the pay, the plan, and the drive as far as that point.
    /// </summary>
    private bool ShowDemoJob(long id) {
        if (_engine is null) return false;
        var d = _store.Detail(id);
        if (d is null) return false;

        var route = _store.RoutesForGame(d.Game).Routes.TryGetValue(id, out var pts)
            ? pts
            : new List<RoutePoint>();
        if (route.Count < 4) return false;

        const double Along = 0.625;
        var upTo = Math.Max(2, (int)(route.Count * Along));
        var loaded = Math.Max(0, d.DistanceKm - d.DistanceToLoadKm) * Along;

        var job = new JobInfo {
            SourceCity = d.SourceCity, SourceCompany = d.SourceCompany,
            DestinationCity = d.DestinationCity, DestinationCompany = d.DestinationCompany,
            Cargo = d.Cargo, CargoMassKg = d.CargoMassKg,
            Income = d.OfferedIncome > 0 ? d.OfferedIncome : d.Revenue,
            PlannedDistanceKm = d.PlannedDistanceKm,
            SpecialJob = d.SpecialTransport, CargoLoaded = true,
        };

        var state = new JobState {
            JobUid = $"demo-{id}",
            Game = d.Game,
            StartedAtMs = route[0].AtMs,
            Job = job,
            DistanceKm = d.DistanceToLoadKm + loaded,
            DistanceToLoadKm = d.DistanceToLoadKm,
            TripPoints = route.Take(upTo)
                .Select(p => new TripPoint { AtMs = p.AtMs, X = p.X, Y = 0, Z = p.Z, SpeedKmh = p.SpeedKmh })
                .ToList(),
        };

        // A recording keeps no facing, so the demonstration takes the direction of the
        // last two readings instead: where the truck was going, which is where it was
        // pointing on any stretch of road that is not a dock. North is the game's
        // negative z, and the game measures counterclockwise from it.
        if (state.TripPoints.Count >= 2) {
            var a = state.TripPoints[^2];
            var b = state.TripPoints[^1];
            var turns = Math.Atan2(-(b.X - a.X), -(b.Z - a.Z)) / (Math.PI * 2);
            state.Heading = turns < 0 ? turns + 1 : turns;
        }

        _engine.ShowDemo(job, state);
        // The strip along the foot fills from telemetry, which a demonstration has
        // none of, so it is given the two lines this job would have put there by the
        // time it reached the point it is being shown at.
        Happened(Noticed.Started, Strings.T("feed.coupled"));
        Happened(Noticed.Started, Strings.T("feed.loaded"));
        return true;
    }

    private void RefreshJob() {
        // Not "and a map": the map is a drawing on this page, not the page itself.
        // Switched off, the figures, the log and the presence all still have to keep
        // up, and asking for a map first stopped the whole page dead.
        if (_engine is null) return;

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
            // No delivery, but there may still be a truck: the map follows it while
            // somebody drives about with an empty hook.
            ShowJobRoute(null, _engine.Roaming?.Game ?? "");

            // Between jobs the profile says so; with the game closed it says nothing
            // at all, rather than leaving Waybill sitting there all evening.
            _discord?.Update(_engine.Connected
                ? new DiscordPresence.Activity { Details = Strings.T("discord.idle"), LargeImage = "waybill", LargeText = "Waybill" }
                : null);
            return;
        }

        if (_jobLaunch is { } running) running.Visible = false;
        _status.Text = $"{Strings.T("live.jobRunning")}   ({Strings.T("live.ticks")}: {_engine.TickCount}, {Strings.T("live.deliveriesThisRun")}: {_engine.DeliveriesThisRun})".ToUpperInvariant();
        // Named the way the history names them, with the state or the country after
        // the city when the driver has asked for that.
        _jobLine.Text = _settings.CityRegions
            ? $"{Places.Say(state.Game, job.SourceCity, job.SourceCityId)}"
              + $"  →  {Places.Say(state.Game, job.DestinationCity, job.DestinationCityId)}"
            : $"{job.SourceCity}  →  {job.DestinationCity}";
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
    /// Draws the drive so far, over everywhere this driver has already been.
    ///
    /// The history behind it is the same drawing the map page makes, in the same
    /// quieter line, so a delivery in a corner of the map is a delivery somewhere
    /// rather than a line in the dark. The drive itself is the one that is singled
    /// out, which is also what the frame is fitted to: the history is background and
    /// must never pull the view away from the delivery.
    ///
    /// With no job on, there is no route to fit and the map follows the truck instead,
    /// which is the only useful thing a map can do while somebody drives about with an
    /// empty hook.
    /// </summary>
    private void ShowJobRoute(JobState? state, string game) {
        if (_jobMap is null) return;

        // The job's own heading while there is a job, the truck's own otherwise: a
        // demonstration has the first and no telemetry, a free ride the second and no
        // job.
        var facing = state?.Heading ?? _engine?.Facing;
        _jobMap.Facing = facing;
        if (_jobClose is not null) _jobClose.Facing = facing;

        // With no delivery on, the line being drawn is the roaming one.
        var roaming = state is null ? _engine?.Roaming : null;
        var points = state?.TripPoints ?? roaming?.Points ?? new List<TripPoint>();
        var key = state is null ? (roaming is null ? "" : "roam") : state.JobUid;
        var same = key == _jobShowing;
        if (same && points.Count < _jobShown + JobRouteStep) return;
        _jobShowing = key;
        _jobShown = points.Count;

        var history = game.Length > 0 ? RoutesFor(game) : null;
        var cities = history?.Cities ?? new List<CityAnchor>();
        var freeroam = history is null
            ? new List<List<RoutePoint>>()
            : history.RunUps.Concat(RoamedIn(game)).ToList();

        // Whatever is being driven right now is the line singled out, delivery or not.
        // Deliveries carry their database id and neither of these has one yet, so they
        // take numbers no row can hold: one for a delivery, another for a roam.
        var line = points.Count >= 2
            ? new RouteLayer {
                Id = state is not null ? -1 : -2,
                Points = points.Select(p => new RoutePoint(p.AtMs, (float)p.X, (float)p.Z, (float)p.SpeedKmh)).ToList(),
            }
            : null;

        var at = points.Count > 0
            ? new PointF((float)points[^1].X, (float)points[^1].Z)
            : (PointF?)null;

        // The history goes in when the game it belongs to changes, and not again. It
        // does not move while somebody drives, and taking every route of it apart once
        // a second is the most expensive thing this page can do; after that only the
        // line being driven is handed over, which leaves the picture underneath alone.
        _jobMap.GameMap = GameMapFor(game);
        if (_jobClose is not null) _jobClose.GameMap = _jobMap.GameMap;

        if (_mapsHolding != game) {
            var behind = history is null ? new List<RouteLayer>() : Layers(history).ToList();
            _jobMap.Show(behind, 0, cities, null, freeroam);
            _jobClose?.Show(behind, 0, cities, null, freeroam);
            _mapsHolding = game;
        }
        // A roam is singled out the same way a delivery is, so the history behind it
        // can be left alone, but it is not one and must not look like one.
        _jobMap.FocusSpare = state is null;
        if (line is not null) _jobMap.ShowLive(line);

        // The wide map fits the delivery, or follows the truck when there is none to
        // fit. With a delivery on, the close view is what follows.
        _jobMap.Follow = state is null ? at : null;
        _jobMap.WorldWidth = CloseWorldMetres;

        ShowCloseUp(line, at, state is not null);
    }

    /// <summary>The close view, which exists only while a delivery is on: without one
    /// the wide map is already following the truck, and a second map saying the same
    /// thing would be a waste of the panel.</summary>
    private void ShowCloseUp(RouteLayer? line, PointF? at, bool onAJob) {
        if (_jobClose is null || _jobCloseFrame is null) return;

        if (!onAJob || line is null || at is null) {
            _jobClose.Visible = false;
            _jobCloseFrame.Visible = false;
            if (_jobCloseCorner is { } hide) hide.Visible = false;
            if (_jobCloseGap is { } gap) gap.Visible = false;
            _jobMap!.Reserved = Padding.Empty;
            return;
        }

        _jobClose.Visible = true;
        _jobClose.Follow = at;
        _jobClose.ShowLive(line);
        ChooseCorner(line.Points);
        PlaceCloseUp();
    }
}
