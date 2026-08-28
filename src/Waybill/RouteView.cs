using System.ComponentModel;
using System.Drawing.Drawing2D;
using Waybill.Storage;

namespace Waybill;

/// <summary>
/// Draws drives in the game's world space. One route can be singled out and drawn
/// in full, with the rest behind it; with nothing singled out every route is drawn
/// alike and the whole history becomes the picture.
///
/// There is no real map underneath and there deliberately is not one. The game's
/// world is not a scaled United States: measured across nineteen deliveries, some
/// pairs of cities sit thirteen times closer than reality and others thirty, so
/// no projection lines real geography up with it. Fitting one anyway put state
/// borders about thirty kilometres out, which is far enough to draw the truck in
/// the wrong state. What is drawn here is instead entirely the driver's own data,
/// where every position is exactly where the game put it.
///
/// For the same reason nothing here reports a distance. The length of a line on
/// this control is not kilometres and never will be; the odometer answers that.
/// </summary>
public class RouteView : Control {
    private static readonly Color Backdrop = Color.FromArgb(22, 25, 29);
    private static readonly Color Ink = Color.FromArgb(228, 233, 240);
    private static readonly Color Muted = Color.FromArgb(138, 148, 163);
    private static readonly Color Accent = Color.FromArgb(232, 168, 74);
    private static readonly Color Surface = Color.FromArgb(30, 34, 39);
    private static readonly Color Edge = Color.FromArgb(48, 54, 62);

    /// <summary>Slow to fast. Eight steps rather than a continuous gradient so the
    /// line can be drawn as a handful of polylines instead of a thousand separate
    /// segments, which is the difference between one millisecond and thirty.</summary>
    /// <summary>Readable from outside so the legend can show the same eight
    /// colours the route is drawn with rather than a copy that will one day be
    /// wrong.</summary>
    public static IReadOnlyList<Color> SpeedRamp => Ramp;

    private static readonly Color[] Ramp = {
        Color.FromArgb( 74,  84,  99),
        Color.FromArgb( 68, 104, 124),
        Color.FromArgb( 66, 126, 140),
        Color.FromArgb( 80, 148, 142),
        Color.FromArgb(114, 166, 132),
        Color.FromArgb(162, 178, 120),
        Color.FromArgb(206, 186, 112),
        Color.FromArgb(240, 198, 128),
    };

    private const float Pad = 18f;

    /// <summary>One route, split into the stretches that were actually driven.
    /// <see cref="RunStart"/> says where each stretch begins in the whole drive,
    /// which is what lets the drawing stop partway through it.</summary>
    private class Drawn {
        public long Id;
        public List<RoutePoint> All = new();
        public List<List<RoutePoint>> Runs = new();
        public List<int> RunStart = new();
        /// <summary>What each stretch covers, so one that is nowhere near the panel
        /// can be skipped without walking its points.</summary>
        public List<RectangleF> RunBounds = new();

        public void Index() {
            RunStart = new List<int>(Runs.Count);
            RunBounds = new List<RectangleF>(Runs.Count);
            var at = 0;
            foreach (var run in Runs) {
                RunStart.Add(at);
                at += run.Count;
                RunBounds.Add(Around(run));
            }
        }

        public static RectangleF Around(List<RoutePoint> run) {
            float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
            foreach (var p in run) {
                minX = Math.Min(minX, p.X); maxX = Math.Max(maxX, p.X);
                minZ = Math.Min(minZ, p.Z); maxZ = Math.Max(maxZ, p.Z);
            }
            return run.Count == 0
                ? RectangleF.Empty
                : RectangleF.FromLTRB(minX, minZ, maxX, maxZ);
        }
    }

    private List<Drawn> _drawn = new();
    /// <summary>Stretches belonging to no delivery, already split into runs.</summary>
    private Drawn? _focus;
    private List<CityAnchor> _cities = new();
    private List<(TimelineRow Row, RoutePoint At, int Index)> _marks = new();

    private float _fitScale = 1f;
    private float _zoom = 1f;
    private PointF _centre;
    private bool _fitted;

    private Bitmap? _under;
    private (int W, int H, float Scale, float CX, float CY, long Lit, bool History, string Map) _underKey;

    private Point _dragFrom;
    private bool _dragging;
    private bool _dragged;
    private int _hoverPoint = -1;
    private int _hoverMark = -1;
    private long _lit;

    /// <summary>How much of the singled out route is drawn, from nothing to all of
    /// it. Only a replay ever moves it, and it is left at the end when one finishes,
    /// so every other use of this control is unaffected.</summary>
    private double _sweep = 1;
    private System.Windows.Forms.Timer? _replay;
    private int _replayMs;
    private int _replayFrom;

    // Set from code and never from a designer, which is what the attribute says:
    // this control is built by hand like the rest of the window.

    /// <summary>Formats a speed for the hover readout. Injected because the window
    /// owns the unit system and this control has no business knowing whether the
    /// driver reads miles or kilometres.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Func<float, string> FormatSpeed { get; set; } = kmh => $"{kmh:0} km/h";

    /// <summary>Says what a route is, for the readout when one is pointed at with
    /// nothing singled out. The control knows an identifier and nothing else.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Func<long, string> DescribeRoute { get; set; } = _ => "";

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string EmptyText { get; set; } = "";

    /// <summary>Says how to work the thing, quietly, in a corner. Wheel zooming and
    /// drag panning are not visible affordances, and a map that looks fixed does not
    /// get zoomed.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Hint { get; set; } = "";

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool ShowCities { get; set; } = true;

    /// <summary>
    /// Which way the truck is pointing, nought to one counterclockwise from north,
    /// or null when nobody knows.
    ///
    /// Only the head of a route being drawn live has one. It is drawn as a needle
    /// rather than left to the shape of the line, because the line says where the
    /// truck has been and this says where it is facing, and on a dock approach or at
    /// a standstill those are not the same answer.
    /// </summary>
    [System.ComponentModel.DesignerSerializationVisibility(
        System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public double? Facing { get; set; }

    /// <summary>Whether the deliveries are drawn. A hidden route is also not there to
    /// be pointed at or opened: leaving it hit-testable meant the map named and
    /// offered a line that was not on the screen.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool ShowHistory {
        get => _showHistory;
        set {
            if (_showHistory == value) return;
            _showHistory = value;
            if (!value) _lit = 0;
            Discard();
            Invalidate();
        }
    }
    private bool _showHistory = true;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool ShowMarks { get; set; } = true;

    /// <summary>
    /// The game's own map, drawn under everything else.
    ///
    /// Null draws nothing, which is where this started: a route on a dark ground, with
    /// the rest of the driving behind it for the only scale there was. With a map under
    /// it, that scale comes from the roads themselves.
    /// </summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public MapBackdrop? GameMap { get; set; }

    /// <summary>
    /// Whether the line being singled out is a drive with nothing on the hook.
    ///
    /// It is singled out for the same reason a delivery is, so the picture behind it
    /// can be kept while it grows, but it is not one: it is drawn in the quiet shade
    /// the rest of the driving between jobs wears, and it has no pickup to ring.
    /// </summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool FocusSpare { get; set; }

    /// <summary>
    /// Whether the pointer can do anything to this drawing at all.
    ///
    /// The maps on the live page are pictures rather than instruments: they frame
    /// themselves, and a wheel turned over one, or a drag across it, would only put
    /// the drive somewhere the next second's refit takes it away from again. The map
    /// page is one click away for anybody who wants to go looking.
    /// </summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Locked { get; set; }

    /// <summary>
    /// A point the drawing is held on rather than fitting what it holds.
    ///
    /// Set to the truck's own position, this is a map that follows the truck instead
    /// of a map of the drive: what it shows is wherever the truck is, at whatever
    /// scale <see cref="WorldWidth"/> asks for. Left null, the drawing is fitted to
    /// what it holds, which is what every map here did before.
    /// </summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public PointF? Follow { get; set; }

    /// <summary>
    /// How much of the world the panel shows across its width, in the game's own
    /// metres, when it is following something rather than fitting it.
    ///
    /// The world is not the distances the game reports: measured over this driver's
    /// deliveries, the odometer runs about seventeen times further than the position
    /// does, so a panel showing 1800 metres of world is showing about thirty
    /// kilometres of road.
    /// </summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public float WorldWidth { get; set; } = 1800f;

    /// <summary>
    /// Room along the edges that the drawing must keep out of, because something else
    /// is sitting on top of it there.
    ///
    /// Used where the close view is laid over a corner of the wide one: the route is
    /// fitted into what is left, so the drive never disappears under it.
    /// </summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Padding Reserved { get; set; }

    /// <summary>Raised when a route is clicked with none singled out, which is the
    /// history map's way of opening a delivery.</summary>
    public event Action<long>? RouteChosen;

    /// <summary>
    /// Which moment of the drive is under the pointer, as the time it happened, or
    /// zero for none.
    ///
    /// The time rather than an index, because whatever is listening keeps its own
    /// list of points: the height profile averages its readings down to one a pixel,
    /// so index four hundred is not the same place in both. The clock is the one
    /// thing both lists agree about.
    /// </summary>
    public event Action<long>? Hovering;

    /// <summary>Marks the moment another view of this drive is pointing at. Sets no
    /// hover of its own and raises nothing, or the two views would chase each
    /// other.</summary>
    public void MarkAt(long atMs) {
        var was = _companion;
        _companion = atMs <= 0 || _focus is not { } f ? -1 : NearestInTime(f.All, atMs);
        if (_companion != was) Invalidate();
    }

    /// <summary>The reading closest to a moment, by binary search: a long haul holds
    /// twenty thousand of them and this runs on every mouse move in the other
    /// view.</summary>
    private static int NearestInTime(List<RoutePoint> points, long atMs) {
        if (points.Count == 0) return -1;
        int low = 0, high = points.Count - 1;
        while (low < high) {
            var mid = (low + high) / 2;
            if (points[mid].AtMs < atMs) low = mid + 1; else high = mid;
        }
        if (low > 0 && Math.Abs(points[low - 1].AtMs - atMs) < Math.Abs(points[low].AtMs - atMs)) low--;
        return low;
    }

    private int _companion = -1;

    public RouteView() {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
               | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        BackColor = Backdrop;
        TabStop = false;
        Cursor = Cursors.Hand;
    }

    /// <summary><paramref name="focus"/> is the delivery to draw in full, or 0 to
    /// draw every route alike. <paramref name="marks"/> only mean anything against
    /// a focused route, since they are placed by matching their time to it.</summary>
    public void Show(IEnumerable<RouteLayer> routes, long focus, List<CityAnchor> cities,
                     List<TimelineRow>? marks = null) {
        _drawn = routes.Select(r => new Drawn { Id = r.Id, All = r.Points, Runs = Split(r.Points) })
                       .Where(d => d.Runs.Count > 0).ToList();
        foreach (var d in _drawn) d.Index();
        _focus = _drawn.FirstOrDefault(d => d.Id == focus);
        _cities = cities;
        _marks = PlaceMarks(marks);
        _lit = 0;
        _hoverPoint = _hoverMark = -1;
        _fitted = false;
        StopReplay();
        Discard();
        Invalidate();
    }

    /// <summary>
    /// Replaces only the route being singled out, leaving everything behind it alone.
    ///
    /// The history does not change while a delivery is being driven, and taking it
    /// apart and drawing it again every second is the most expensive thing this
    /// control can be asked to do. Nothing is discarded here: the picture underneath
    /// holds everything except the singled out route, so it stays good until the frame
    /// itself moves, and it knows when that happens.
    /// </summary>
    public void ShowLive(RouteLayer live) {
        var drawn = new Drawn { Id = live.Id, All = live.Points, Runs = Split(live.Points) };
        if (drawn.Runs.Count == 0) return;
        drawn.Index();

        // Both of the numbers no row can hold go, not just this one: the roam that
        // ran up to a delivery must not stay behind as a line that never changes
        // again, and a delivery must not linger once the roaming after it begins.
        _drawn.RemoveAll(d => d.Id < 0);
        _drawn.Add(drawn);
        _focus = drawn;
        _sweep = 1;
        _fitted = false;
        Invalidate();
    }

    /// <summary>
    /// Draws the singled out route again from its beginning, at a steady rate, as
    /// though the drive were being watched from above.
    ///
    /// It is playing back time rather than distance: the points are one second of
    /// driving apart, so the line grows quickly on an open road and dawdles through
    /// a city, which is what the drive actually did. Pins arrive as the line reaches
    /// them, so a collision is seen happening rather than found afterwards.
    ///
    /// Only the route on top is animated. Everything behind it is a cached picture
    /// that is not redrawn, which is what keeps this cheap enough to run at sixty
    /// frames a second on a route of twenty thousand points.
    /// </summary>
    public void Replay(int milliseconds = 2400) {
        if (_focus is null || _focus.All.Count < 2 || milliseconds <= 0) return;
        StopReplay();
        _sweep = 0;
        _replayMs = milliseconds;
        _replayFrom = Environment.TickCount;
        _replay = new System.Windows.Forms.Timer { Interval = 16 };
        _replay.Tick += (_, _) => {
            var gone = (Environment.TickCount - _replayFrom) / (double)_replayMs;
            // Easing out at the end, so the line arrives rather than stops dead.
            _sweep = gone >= 1 ? 1 : 1 - Math.Pow(1 - gone, 1.6);
            if (_sweep >= 1) StopReplay();
            Invalidate();
        };
        _replay.Start();
        Invalidate();
    }

    /// <summary>Ends a replay wherever it is and shows the whole route. Called by
    /// anything the driver does to the map: a replay is worth watching until you
    /// want to look at something, and then it is in the way.</summary>
    public void StopReplay() {
        _replay?.Stop();
        _replay?.Dispose();
        _replay = null;
        _sweep = 1;
    }

    /// <summary>How many of the route's points have been reached, as an index into
    /// the whole drive.</summary>
    private int Reached(Drawn route) =>
        _sweep >= 1 ? route.All.Count : (int)Math.Ceiling(_sweep * route.All.Count);

    /// <summary>
    /// Ties each event to the position the truck was in when it happened, by time.
    ///
    /// Events and route points are recorded by the same clock a second apart, so
    /// the nearest point is the right one. An event further than a minute from any
    /// recorded position is dropped rather than pinned to a guess: that means the
    /// route stopped being recorded around it, and a pin in the wrong place says
    /// something false about where the driver was.
    /// </summary>
    private List<(TimelineRow, RoutePoint, int)> PlaceMarks(List<TimelineRow>? marks) {
        var placed = new List<(TimelineRow, RoutePoint, int)>();
        if (marks is null || _focus is null) return placed;

        foreach (var mark in marks) {

            var best = long.MaxValue;
            RoutePoint at = default;
            var index = -1;
            for (var i = 0; i < _focus.All.Count; i++) {
                var p = _focus.All[i];
                var off = Math.Abs(p.AtMs - mark.AtMs);
                if (off >= best) continue;
                best = off;
                at = p;
                index = i;
            }
            if (best <= 60_000) placed.Add((mark, at, index));
        }
        return placed;
    }

    private static List<List<RoutePoint>> Split(List<RoutePoint> pts) => RouteGeometry.Split(pts);

    /// <summary>
    /// The angle that lays a drive along the longer side of the control.
    ///
    /// The drive's own long axis is the first principal component of its points,
    /// which for a route is the direction it broadly went; turning by the negative
    /// of that puts it flat, and a further quarter turn stands it up when the
    /// control is taller than it is wide.
    ///
    /// Two guards, both against the same thing. A drive only a minute old is a
    /// squiggle in a car park with no long axis at all, so nothing is turned until
    /// there is enough of it; and once turned, the angle is left alone unless the
    /// new one differs by a good margin, or a live route would swing about under the
    /// driver every time it grew.
    /// </summary>
    public void Fit() {
        _zoom = 1f;
        _fitted = true;

        // The focused route sets the frame when there is one. On the history map
        // there is not, so everything does.
        var scope = _focus is { } f ? new List<Drawn> { f } : _drawn;

        // Following something needs nothing measured: the scale is asked for and the
        // centre is the thing being followed. Everything below is for a drawing that
        // has to be made to fit instead.
        if (Follow is { } held) {
            _fitScale = Math.Max(Math.Max((Width - Pad * 2), 1) / Math.Max(WorldWidth, 1f), Least());
            _centre = Inside(Shifted(held, _fitScale), _fitScale);
            _snapNext = false;
            Discard();
            Invalidate();
            return;
        }

        if (scope.Count == 0) { _fitScale = 1f; _centre = PointF.Empty; return; }

        var points = scope.SelectMany(d => d.Runs).SelectMany(r => r).ToList();

        float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
        foreach (var p in points) {
            minX = Math.Min(minX, p.X); maxX = Math.Max(maxX, p.X);
            minZ = Math.Min(minZ, p.Z); maxZ = Math.Max(maxZ, p.Z);
        }
        var mid = new PointF((minX + maxX) / 2, (minZ + maxZ) / 2);

        var w = Math.Max(maxX - minX, 1f);
        var h = Math.Max(maxZ - minZ, 1f);
        // One scale for both axes. Stretching the route to fill the control would
        // make a straight motorway look like a curve, which is a lie about the only
        // thing this control does claim to show.
        var room = Room();
        var wanted = Math.Min(room.Width / w, room.Height / h);
        if (wanted <= 0 || float.IsInfinity(wanted)) wanted = 1f;
        // Never closer than the close view is. A drive an inch long, or one that has
        // barely started, would otherwise be blown up until a car park filled the
        // panel and every wobble in it looked like a detour.
        wanted = Math.Min(wanted, Math.Max(room.Width, 1) / Math.Max(WorldWidth, 1f));
        // ...and never so far out that the panel looks past the edge of the map. A
        // drive is read against roads and towns; the emptiness beyond where the map
        // was exported says nothing about anything.
        wanted = Math.Max(wanted, Least());

        // The frame already drawn is kept only while it is still the right frame: the
        // drive fits inside it with the safe band to spare, it is the size the drive
        // wants, and the drive is still in the middle of it. Anything else and the map
        // is fitted again, which is what keeps a growing drive centred rather than
        // creeping into a corner until it touches the edge.
        //
        // The tolerance is what stops it twitching: a second of driving moves the
        // middle of a long route by a fraction of a pixel, and nothing is redrawn for
        // that.
        var want = Inside(Shifted(mid, wanted), wanted);
        var offBy = Math.Abs(wanted - _fitScale) / Math.Max(wanted, 0.0001f);
        var drift = Math.Max(Math.Abs(want.X - _centre.X), Math.Abs(want.Y - _centre.Y)) * PerMetre;
        var settled = offBy < 0.02f && drift < Math.Min(Width, Height) * 0.02f;
        if (!_snapNext && _fitScale > 0 && settled && Holds(minX, maxX, minZ, maxZ)) {
            _snapNext = false;
            Discard();
            Invalidate();
            return;
        }

        _snapNext = false;
        _fitScale = wanted;
        _centre = want;
        Discard();
        Invalidate();
    }

    /// <summary>The point of the world that is drawn in the middle of the room left
    /// over, which is where a fitted drive is centred.</summary>
    private PointF Middle() {
        var room = Room();
        return ToWorld(room.X + room.Width / 2, room.Y + room.Height / 2);
    }

    /// <summary>What the panel can see, in the world's own metres, with a margin so a
    /// stretch that only just reaches the edge is still drawn.</summary>
    private RectangleF Seen() {
        var topLeft = ToWorld(-40, -40);
        var bottomRight = ToWorld(Width + 40, Height + 40);
        return RectangleF.FromLTRB(topLeft.X, topLeft.Y, bottomRight.X, bottomRight.Y);
    }

    /// <summary>What is left of the panel once the padding and anything sitting on
    /// top of it are taken off.</summary>
    private RectangleF Room() => new(
        Pad + Reserved.Left,
        Pad + Reserved.Top,
        Math.Max(Width - Pad * 2 - Reserved.Horizontal, 1),
        Math.Max(Height - Pad * 2 - Reserved.Vertical, 1));

    /// <summary>
    /// The centre to hold, so that what should be in the middle of the room left over
    /// ends up there rather than in the middle of the panel.
    ///
    /// Everything on the way to the screen is measured from the middle of the panel,
    /// so reserving a corner means moving the centre by half of what was reserved.
    /// </summary>
    private PointF Shifted(PointF middle) => Shifted(middle, PerMetre);

    private PointF Shifted(PointF middle, float perMetre) {
        var room = Room();
        var offX = room.X + room.Width / 2 - Width / 2f;
        var offY = room.Y + room.Height / 2 - Height / 2f;
        var scale = Math.Max(perMetre, 0.000001f);
        return new PointF(middle.X - offX / scale, middle.Y - offY / scale);
    }

    /// <summary>
    /// The furthest out the panel may be taken, in pixels per metre.
    ///
    /// With a map underneath, that is the scale at which the map still covers the
    /// panel: past it the drive would be read against a hole rather than against
    /// roads. Without one there is nothing to fall off the edge of, and the answer
    /// is that there is no limit.
    /// </summary>
    private float Least() {
        if (GameMap is not { } map) return 0f;
        var bounds = map.Bounds;
        if (bounds.Width <= 0 || bounds.Height <= 0) return 0f;
        return Math.Max(Width / bounds.Width, Height / bounds.Height);
    }

    /// <summary>The nearest centre to the one asked for that keeps the panel inside
    /// the map. Panning stops at the coast rather than sliding off into nothing.</summary>
    private PointF Inside(PointF centre, float perMetre) {
        if (GameMap is not { } map || perMetre <= 0) return centre;
        var bounds = map.Bounds;
        if (bounds.Width <= 0 || bounds.Height <= 0) return centre;

        var halfW = Width / 2f / perMetre;
        var halfH = Height / 2f / perMetre;
        return new PointF(Between(centre.X, bounds.Left + halfW, bounds.Right - halfW),
                          Between(centre.Y, bounds.Top + halfH, bounds.Bottom - halfH));
    }

    /// <summary>Clamped, and when the panel is wider than what it may see, held in the
    /// middle of it rather than thrown to one side by a backwards clamp.</summary>
    private static float Between(float value, float low, float high) =>
        low > high ? (low + high) / 2 : Math.Clamp(value, low, high);

    /// <summary>
    /// How much of the panel is held back from the edge.
    ///
    /// The frame is only kept while the drive sits inside this much less than the
    /// panel, so the truck is redrawn into the middle while it still has a good inch
    /// of picture in front of it rather than at the moment it crosses the edge. A live
    /// drive grows by a second of driving at a time, and on a long route a second is
    /// worth a pixel or two, so this is a great many seconds of warning.
    /// </summary>
    private const float SafeBand = 64f;

    /// <summary>Whether the whole drawing still sits inside the frame on screen, with
    /// the safe band to spare.</summary>
    private bool Holds(float minX, float maxX, float minZ, float maxZ) {
        var room = Room();
        var reachX = (room.Width / 2f - SafeBand) / PerMetre;
        var reachZ = (room.Height / 2f - SafeBand) / PerMetre;
        if (reachX <= 0 || reachZ <= 0) return false;

        // Measured from the middle of the room rather than of the panel, which is
        // where the drawing was centred.
        var middle = Middle();
        return minX >= middle.X - reachX && maxX <= middle.X + reachX
            && minZ >= middle.Y - reachZ && maxZ <= middle.Y + reachZ;
    }

    /// <summary>Set when the next fit has to arrive at once rather than be held: the
    /// panel changed size under it, and a frame chosen for the old size is simply
    /// wrong rather than nearly right.</summary>
    private bool _snapNext = true;

    /// <summary>Pixels per world metre. Named for what it is rather than "scale",
    /// which on a Control already means resizing one.</summary>
    private float PerMetre => _fitScale * _zoom;

    private PointF ToScreen(float x, float z) =>
        new((x - _centre.X) * PerMetre + Width / 2f, (z - _centre.Y) * PerMetre + Height / 2f);

    private PointF ToWorld(float sx, float sy) =>
        new((sx - Width / 2f) / PerMetre + _centre.X, (sy - Height / 2f) / PerMetre + _centre.Y);

    private void Discard() { _under?.Dispose(); _under = null; }

    protected override void OnResize(EventArgs e) {
        base.OnResize(e);
        _fitted = false;
        _snapNext = true;
        Discard();
    }

    /// <summary>Focus follows the mouse so the wheel zooms without a click first,
    /// but never while a text box is being edited: the notes field saves when it
    /// loses focus, and merely passing over the map is not a reason to write to
    /// the database.</summary>
    protected override void OnMouseEnter(EventArgs e) {
        base.OnMouseEnter(e);
        if (FindForm() is { } form && form.ActiveControl is not TextBoxBase) Focus();
    }

    protected override void OnMouseWheel(MouseEventArgs e) {
        base.OnMouseWheel(e);
        if (Locked || _drawn.Count == 0) return;
        StopReplay();

        // Zoom about the pointer: whatever is under it stays under it, which is what
        // makes zooming feel like moving closer rather than being thrown somewhere.
        var before = ToWorld(e.X, e.Y);
        var step = e.Delta > 0 ? 1.2f : 1 / 1.2f;
        _zoom = Math.Clamp(_zoom * step, 1f, 60f);
        var after = ToWorld(e.X, e.Y);
        _centre = Inside(new PointF(_centre.X + (before.X - after.X), _centre.Y + (before.Y - after.Y)), PerMetre);

        Discard();
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e) {
        base.OnMouseDown(e);
        if (Locked) return;
        StopReplay();
        Focus();
        _dragging = true;
        _dragged = false;
        _dragFrom = e.Location;
        Cursor = Cursors.SizeAll;
    }

    protected override void OnMouseMove(MouseEventArgs e) {
        base.OnMouseMove(e);
        if (_dragging) {
            if (Math.Abs(e.X - _dragFrom.X) > 2 || Math.Abs(e.Y - _dragFrom.Y) > 2) _dragged = true;
            _centre = Inside(new PointF(
                _centre.X - (e.X - _dragFrom.X) / PerMetre,
                _centre.Y - (e.Y - _dragFrom.Y) / PerMetre), PerMetre);
            _dragFrom = e.Location;
            Discard();
            Invalidate();
            return;
        }

        var wasPoint = _hoverPoint;
        var wasMark = _hoverMark;
        var wasLit = _lit;

        if (_focus is { } f) {
            _hoverMark = NearestMark(e.Location);
            _hoverPoint = _hoverMark >= 0 ? -1 : NearestPoint(f, e.Location);
        } else {
            _lit = NearestRoute(e.Location);
        }

        if (wasPoint != _hoverPoint || wasMark != _hoverMark) { Invalidate(); SayHovering(); }
        // The lit route lives in the cached layer, so changing it has to throw it.
        if (wasLit != _lit) { Discard(); Invalidate(); }
    }

    /// <summary>Tells whoever is listening which moment is under the pointer: the
    /// event's if one is, otherwise the position's, otherwise none.</summary>
    private void SayHovering() {
        if (Hovering is null) return;
        var at = _hoverMark >= 0 && _hoverMark < _marks.Count ? _marks[_hoverMark].At.AtMs
               : _hoverPoint >= 0 && _focus is { } f && _hoverPoint < f.All.Count ? f.All[_hoverPoint].AtMs
               : 0;
        Hovering(at);
    }

    protected override void OnMouseUp(MouseEventArgs e) {
        base.OnMouseUp(e);
        _dragging = false;
        Cursor = Cursors.Hand;
        // A click that did not drag, on the history map, opens what was clicked.
        if (!_dragged && _focus is null && _lit != 0) RouteChosen?.Invoke(_lit);
    }

    protected override void OnMouseLeave(EventArgs e) {
        base.OnMouseLeave(e);
        if (_hoverPoint >= 0 || _hoverMark >= 0) { _hoverPoint = _hoverMark = -1; Invalidate(); SayHovering(); }
        if (_lit != 0) { _lit = 0; Discard(); Invalidate(); }
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e) {
        base.OnMouseDoubleClick(e);
        if (Locked) return;
        // Only where a click means nothing else. On the history map the first click
        // has already opened a delivery.
        if (_focus is not null) Fit();
    }

    private int NearestPoint(Drawn route, Point at) {
        var best = -1;
        var bestDist = 14f * 14f;
        for (var i = 0; i < route.All.Count; i++) {
            var s = ToScreen(route.All[i].X, route.All[i].Z);
            var d = (s.X - at.X) * (s.X - at.X) + (s.Y - at.Y) * (s.Y - at.Y);
            if (d < bestDist) { bestDist = d; best = i; }
        }
        return best;
    }

    private int NearestMark(Point at) {
        if (!ShowMarks) return -1;
        var best = -1;
        var bestDist = 11f * 11f;
        for (var i = 0; i < _marks.Count; i++) {
            var s = ToScreen(_marks[i].At.X, _marks[i].At.Z);
            var d = (s.X - at.X) * (s.X - at.X) + (s.Y - at.Y) * (s.Y - at.Y);
            if (d < bestDist) { bestDist = d; best = i; }
        }
        return best;
    }

    /// <summary>Which route the pointer is over, on the history map. Every fourth
    /// point is enough: they are 19 m apart and the tolerance is a dozen pixels,
    /// so three out of four are asking the same question twice.</summary>
    private long NearestRoute(Point at) {
        if (!ShowHistory) return 0;
        long best = 0;
        var bestDist = 12f * 12f;
        foreach (var d in _drawn)
            for (var i = 0; i < d.All.Count; i += 4) {
                var s = ToScreen(d.All[i].X, d.All[i].Z);
                var dist = (s.X - at.X) * (s.X - at.X) + (s.Y - at.Y) * (s.Y - at.Y);
                if (dist < bestDist) { bestDist = dist; best = d.Id; }
            }
        return best;
    }

    /// <summary>Which of the eight colours a speed is drawn in. Public so the
    /// height profile bands the same speeds the same way and the two drawings of
    /// one drive agree with each other.</summary>
    public static int SpeedBand(float kmh) => Band(kmh);

    private static int Band(float kmh) => Math.Clamp((int)(kmh / 15f), 0, Ramp.Length - 1);

    protected override void OnPaint(PaintEventArgs e) {
        var g = e.Graphics;
        g.Clear(Backdrop);

        // Nothing to draw and nothing to follow. Following something is enough on its
        // own: a driver on their first evening has no history behind them and still
        // has a truck, which is the whole of what the map is for at that point.
        if (_drawn.Count == 0 && Follow is null) {
            if (EmptyText.Length > 0) {
                using var brush = new SolidBrush(Muted);
                using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                g.DrawString(EmptyText, Font, brush, new RectangleF(8, 8, Width - 16, Height - 16), format);
            }
            return;
        }
        if (!_fitted) Fit();

        g.SmoothingMode = SmoothingMode.AntiAlias;

        DrawUnderlay(g);
        if (_focus is { } f) {
            DrawRoute(g, f);
            DrawEnds(g, f);
        } else if (Follow is { } truck) {
            // Following without a delivery to single out: the truck is the whole of
            // what this map is about, so it is drawn where the map is held.
            DrawTruck(g, ToScreen(truck.X, truck.Y));
        }
        // Names go over the route rather than under it. A label is what makes the
        // line mean somewhere, so the line is not allowed to eat the first letters
        // of it, which it did wherever a drive passed through a city it named.
        if (ShowCities) DrawCities(g);
        if (ShowMarks) DrawMarks(g);
        DrawCompanion(g);
        DrawReadout(g);

        if (Hint.Length > 0 && _hoverPoint < 0 && _hoverMark < 0 && _lit == 0) {
            using var font = new Font("Segoe UI", 7.5F);
            using var brush = new SolidBrush(Color.FromArgb(120, 138, 148, 163));
            var size = g.MeasureString(Hint, font);
            g.DrawString(Hint, font, brush, 8, Height - size.Height - 5);
        }
    }

    /// <summary>
    /// Every route that is not the one being read, painted once into a bitmap and
    /// kept.
    ///
    /// It only has to be redone when the view actually moves. Measured on real
    /// routes cloned up to a five hundred delivery history, drawing them costs
    /// 13 ms while copying the finished bitmap costs 1.3 ms, and pointing at the
    /// route repaints constantly.
    /// </summary>
    private void DrawUnderlay(Graphics g) {
        // The switches belong in the key: turning a layer off changes what the cached
        // bitmap should hold, and without them the old one was kept and the toggle
        // did nothing until the view happened to move.
        var key = (Width, Height, PerMetre, _centre.X, _centre.Y, _lit, ShowHistory,
                   GameMap is null ? "" : GameMap.Game);
        if (_under is null || _underKey != key) {
            Discard();
            _underKey = key;
            _under = new Bitmap(Width, Height);
            using var ug = Graphics.FromImage(_under);
            ug.SmoothingMode = SmoothingMode.AntiAlias;

            // With the game's own map underneath, one drive is read against roads and
            // towns. Without it the only scale on offer is the other drives, faintly,
            // which is the whole reason they were ever drawn behind a route.
            var alone = GameMap is not null && _focus is not null;

            // Behind a route being read they are context and stay out of the way; with
            // nothing singled out they are the whole picture, and a background drawn
            // at background strength would leave the page looking empty. Over a map
            // they answer the roads as well, which are about as bright as the old
            // quiet line was.
            using var quiet = _focus is not null
                ? new Pen(Color.FromArgb(64, 104, 116, 132), 1.1f)
                : GameMap is not null
                    ? new Pen(Color.FromArgb(220, 122, 176, 228), 1.6f) { LineJoin = LineJoin.Round }
                    : new Pen(Color.FromArgb(165, 128, 146, 166), 1.4f) { LineJoin = LineJoin.Round };
            using var loud = new Pen(Color.FromArgb(235, 232, 168, 74), 2f) { LineJoin = LineJoin.Round };

            // What the panel can see, in the world's own metres. A drawing held close
            // to the truck has almost the whole history off the edges of it, and
            // projecting a stretch only to throw every point away is the one cost
            // worth avoiding here.
            var seen = Seen();

            // The map first, since everything else is drawn on top of it. It goes into
            // the same cached picture as the history, so a live drive costs nothing
            // between refits.
            GameMap?.Draw(ug, seen, PerMetre, ToScreen);

            foreach (var d in _drawn) {
                if (d == _focus) continue;
                if (alone) continue;
                if (!ShowHistory && d.Id != _lit) continue;
                var pen = d.Id == _lit && _lit != 0 ? loud : quiet;
                for (var i = 0; i < d.Runs.Count; i++) {
                    if (i < d.RunBounds.Count && !seen.IntersectsWith(d.RunBounds[i])) continue;
                    var run = d.Runs[i];
                    // Simplified against the screen, not the world: points closer
                    // together than a pixel cannot be told apart, and on a whole-map
                    // view that is about 97 % of them.
                    var pts = Reduce(Project(run), 0.7f);
                    if (pts.Length > 1) ug.DrawLines(pen, pts);
                }
            }
        }
        g.DrawImageUnscaled(_under, 0, 0);
    }

    private PointF[] Project(List<RoutePoint> run) {
        var pts = new PointF[run.Count];
        for (var i = 0; i < run.Count; i++) pts[i] = ToScreen(run[i].X, run[i].Z);
        return pts;
    }

    private void DrawRoute(Graphics g, Drawn route) {
        var reached = Reached(route);

        // Driving that carried nothing is drawn the way it is drawn everywhere else:
        // one quiet line, no colour of its own. It has no cargo, so it has no speed
        // worth colouring and no pickup worth ringing.
        if (FocusSpare) {
            using var spare = new Pen(Color.FromArgb(150, 132, 136, 142), 1.4f) {
                StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round,
            };
            foreach (var run in route.Runs) {
                var pts = Reduce(Project(run), 0.7f);
                if (pts.Length > 1) g.DrawLines(spare, pts);
            }
            return;
        }

        // The stretches that were not driven, drawn first so the route sits on top.
        // A break only appears once the drive has come out the other side of it.
        using (var skip = new Pen(Color.FromArgb(90, 150, 160, 175), 1f) { DashStyle = DashStyle.Dash }) {
            for (var i = 1; i < route.Runs.Count; i++) {
                if (route.RunStart[i] >= reached) break;
                var a = ToScreen(route.Runs[i - 1][^1].X, route.Runs[i - 1][^1].Z);
                var b = ToScreen(route.Runs[i][0].X, route.Runs[i][0].Z);
                g.DrawLine(skip, a, b);
            }
        }

        var pens = new Pen[Ramp.Length];
        for (var i = 0; i < Ramp.Length; i++)
            pens[i] = new Pen(Ramp[i], 2.4f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };

        try {
            for (var r = 0; r < route.Runs.Count; r++) {
                var run = route.Runs[r];
                var upTo = Math.Min(run.Count, reached - route.RunStart[r]);
                if (upTo < 2) continue;

                var band = Band(run[0].SpeedKmh);
                var stretch = new List<PointF> { ToScreen(run[0].X, run[0].Z) };
                for (var i = 1; i < upTo; i++) {
                    stretch.Add(ToScreen(run[i].X, run[i].Z));
                    var next = Band(run[i].SpeedKmh);
                    if (next == band) continue;
                    // The point where the speed changes belongs to both stretches, or
                    // the line would show a gap at every change of pace.
                    if (stretch.Count > 1) g.DrawLines(pens[band], stretch.ToArray());
                    stretch = new List<PointF> { stretch[^1] };
                    band = next;
                }
                if (stretch.Count > 1) g.DrawLines(pens[band], stretch.ToArray());
            }
        } finally {
            foreach (var p in pens) p.Dispose();
        }
    }

    /// <summary>The marker for where the truck is now, with the needle for which way
    /// it points. Shared by the end of a live route and by a map that is following the
    /// truck with no route to end.</summary>
    private void DrawTruck(Graphics g, PointF at) {
        using var fill = new SolidBrush(Accent);
        using var glow = new SolidBrush(Color.FromArgb(70, Accent));
        g.FillEllipse(glow, at.X - 9, at.Y - 9, 18, 18);
        g.FillEllipse(fill, at.X - 5.5f, at.Y - 5.5f, 11, 11);
        Needle(g, fill, at);
    }

    private void DrawEnds(Graphics g, Drawn route) {
        // A roam has no pickup and no drop: the only thing worth marking on it is
        // where the truck is now.
        if (FocusSpare) {
            DrawTruck(g, ToScreen(route.Runs[^1][^1].X, route.Runs[^1][^1].Z));
            return;
        }

        var reached = Reached(route);
        var start = ToScreen(route.Runs[0][0].X, route.Runs[0][0].Z);

        using var ring = new Pen(Ink, 2f);
        using var back = new SolidBrush(Backdrop);
        g.FillEllipse(back, start.X - 5, start.Y - 5, 10, 10);
        g.DrawEllipse(ring, start.X - 5, start.Y - 5, 10, 10);

        using var fill = new SolidBrush(Accent);
        if (reached >= route.All.Count) {
            var end = ToScreen(route.Runs[^1][^1].X, route.Runs[^1][^1].Z);
            g.FillEllipse(fill, end.X - 5.5f, end.Y - 5.5f, 11, 11);
            Needle(g, fill, end);
            return;
        }

        // Mid replay the far end has not been reached yet, so the marker rides the
        // head of the line instead of sitting on a place the truck has not got to.
        if (reached < 1) return;
        var head = route.All[Math.Min(reached, route.All.Count) - 1];
        var at = ToScreen(head.X, head.Z);
        using var glow = new SolidBrush(Color.FromArgb(70, Accent));
        g.FillEllipse(glow, at.X - 9, at.Y - 9, 18, 18);
        g.FillEllipse(fill, at.X - 4.5f, at.Y - 4.5f, 9, 9);
        Needle(g, fill, at);
    }

    /// <summary>
    /// Which way the truck is pointing, at the marker for where it is.
    ///
    /// Drawn on both markers, since a drive being watched live has reached the end of
    /// its own line at every moment: the end of the line is where the truck is. The
    /// game measures counterclockwise from north and the drawing carries whatever
    /// turn was taken to fit the panel, so the needle is set against both at once.
    /// North unturned is straight up the screen, which is why the y term is negative.
    /// </summary>
    private void Needle(Graphics g, Brush fill, PointF at) {
        if (Facing is not { } facing) return;
        var a = -(float)(facing * Math.PI * 2);
        var fx = MathF.Sin(a);
        var fy = -MathF.Cos(a);
        g.FillPolygon(fill, new[] {
            new PointF(at.X + fx * 14, at.Y + fy * 14),
            new PointF(at.X - fy * 5f - fx * 3, at.Y + fx * 5f - fy * 3),
            new PointF(at.X + fy * 5f - fx * 3, at.Y - fx * 5f - fy * 3),
        });
    }

    /// <summary>Colour and shape for an event, by its stored identifier. Shape as
    /// well as colour, so the pins stay apart from each other where several land on
    /// the same stretch of road, and for anyone who reads colour poorly.</summary>
    private static (Color Colour, int Sides) Pin(string type) => type switch {
        "collision" => (Color.FromArgb(226, 116, 104), 3),
        "fine" => (Color.FromArgb(232, 168, 74), 0),
        "refuel" => (Color.FromArgb(112, 172, 214), 4),
        "ferry" or "train" => (Color.FromArgb(96, 176, 168), 4),
        "rest" => (Color.FromArgb(150, 160, 175), 0),
        "tollgate" => (Color.FromArgb(150, 160, 175), 4),
        "save_loaded" => (Color.FromArgb(180, 150, 200), 3),
        "cargo_loaded" => (Color.FromArgb(200, 210, 224), 4),
        _ => (Color.FromArgb(150, 160, 175), 0),
    };

    private void DrawMarks(Graphics g) {
        var reached = _focus is { } f ? Reached(f) : int.MaxValue;
        for (var i = 0; i < _marks.Count; i++) {
            var (row, point, index) = _marks[i];
            // A pin arrives when the line reaches it, not before.
            if (index >= reached) continue;
            var at = ToScreen(point.X, point.Z);
            var (colour, sides) = Pin(row.Type);
            var r = i == _hoverMark ? 6.5f : 4.5f;

            using var fill = new SolidBrush(colour);
            using var rim = new Pen(Backdrop, 1.6f);
            if (sides == 0) {
                g.FillEllipse(fill, at.X - r, at.Y - r, r * 2, r * 2);
                g.DrawEllipse(rim, at.X - r, at.Y - r, r * 2, r * 2);
            } else {
                var shape = sides == 3
                    ? new[] { new PointF(at.X, at.Y - r * 1.2f), new PointF(at.X + r, at.Y + r * 0.8f), new PointF(at.X - r, at.Y + r * 0.8f) }
                    : new[] { new PointF(at.X, at.Y - r), new PointF(at.X + r, at.Y), new PointF(at.X, at.Y + r), new PointF(at.X - r, at.Y) };
                g.FillPolygon(fill, shape);
                g.DrawPolygon(rim, shape);
            }
        }
    }

    /// <summary>
    /// The cities this history knows, each where the game actually put it.
    ///
    /// They are learned rather than looked up: every job names the city it loaded
    /// in and the one it unloaded in, and the trailer coupling says where the load
    /// was. That means a dot is really the middle of the depots used there, which
    /// for a city seen once is one depot. Close enough to label a corner of the map
    /// with, and honest, which a downloaded list from an older version of the game
    /// would not be.
    /// </summary>
    private void DrawCities(Graphics g) {
        using var dot = new SolidBrush(Color.FromArgb(190, 150, 160, 175));
        using var text = new SolidBrush(Color.FromArgb(200, 190, 200, 214));
        using var halo = new SolidBrush(Color.FromArgb(190, 22, 25, 29));
        using var font = new Font("Segoe UI", 8F);

        var placed = new List<RectangleF>();
        var view = new RectangleF(-40, -20, Width + 80, Height + 40);

        foreach (var city in _cities) {
            var at = ToScreen(city.X, city.Z);
            if (!view.Contains(at)) continue;

            g.FillEllipse(dot, at.X - 2.5f, at.Y - 2.5f, 5, 5);

            var size = g.MeasureString(city.Name, font);
            // Far enough out to clear the marker drawn at either end of the route,
            // which lands on a city dot whenever a drive starts or finishes there,
            // and swallowed the first letters of the name when it did.
            //
            // On the other side where there is no room on this one. A route turned to
            // fill the panel puts cities hard against both edges, and a name running
            // off the right came out as "Salt Lak".
            var right = at.X + 10 + size.Width <= Width - 2;
            var label = new RectangleF(
                right ? at.X + 10 : at.X - 10 - size.Width,
                at.Y - size.Height / 2, size.Width, size.Height);
            if (label.Left < 2) continue;
            // Cities seen in most places are drawn first, so where two labels collide
            // the one the driver knows better is the one that survives.
            if (placed.Any(p => p.IntersectsWith(label))) continue;
            placed.Add(RectangleF.Inflate(label, 3, 1));

            g.FillRectangle(halo, RectangleF.Inflate(label, 2, 0));
            g.DrawString(city.Name, font, text, label.Location);
        }
    }

    /// <summary>Where the profile beside this map is being pointed at. Drawn in the
    /// accent with a halo, which is the same mark the replay's head wears, because it
    /// means the same thing: this is the moment being looked at.</summary>
    private void DrawCompanion(Graphics g) {
        if (_companion < 0 || _focus is not { } f || _companion >= f.All.Count) return;
        var p = f.All[_companion];
        var at = ToScreen(p.X, p.Z);
        using var glow = new SolidBrush(Color.FromArgb(70, Accent));
        using var fill = new SolidBrush(Accent);
        using var rim = new Pen(Backdrop, 1.4f);
        g.FillEllipse(glow, at.X - 9, at.Y - 9, 18, 18);
        g.FillEllipse(fill, at.X - 4, at.Y - 4, 8, 8);
        g.DrawEllipse(rim, at.X - 4, at.Y - 4, 8, 8);
    }

    private void DrawReadout(Graphics g) {
        string label;
        PointF at;

        if (_hoverMark >= 0 && _hoverMark < _marks.Count) {
            var (row, point, _) = _marks[_hoverMark];
            at = ToScreen(point.X, point.Z);
            label = row.Udalost;
            if (row.Hodnota.Length > 0) label += $"   {row.Hodnota}";
            if (row.Detail.Length > 0) label += $"   {row.Detail}";
        } else if (_hoverPoint >= 0 && _focus is { } f && _hoverPoint < f.All.Count) {
            var p = f.All[_hoverPoint];
            at = ToScreen(p.X, p.Z);
            var into = TimeSpan.FromMilliseconds(p.AtMs - f.All[0].AtMs);
            var when = into.TotalHours >= 1
                ? $"{(int)into.TotalHours}:{into.Minutes:00}:{into.Seconds:00}"
                : $"{into.Minutes}:{into.Seconds:00}";
            label = $"{when}   {FormatSpeed(p.SpeedKmh)}";
            using var ring = new Pen(Ink, 1.6f);
            g.DrawEllipse(ring, at.X - 4, at.Y - 4, 8, 8);
        } else if (_lit != 0) {
            label = DescribeRoute(_lit);
            if (label.Length == 0) return;
            at = PointToClient(MousePosition);
        } else {
            return;
        }

        using var font = new Font("Segoe UI", 8F);
        var size = g.MeasureString(label, font);
        var box = new RectangleF(at.X + 10, at.Y - size.Height - 8, size.Width + 12, size.Height + 6);
        // Kept inside the control, or the readout falls off the edge exactly when the
        // pointer is somewhere interesting.
        if (box.Right > Width - 4) box.X = at.X - box.Width - 10;
        if (box.X < 4) box.X = 4;
        if (box.Top < 4) box.Y = at.Y + 10;

        using var back = new SolidBrush(Surface);
        using var edge = new Pen(Edge);
        using var ink = new SolidBrush(Ink);
        g.FillRectangle(back, box);
        g.DrawRectangle(edge, box.X, box.Y, box.Width, box.Height);
        g.DrawString(label, font, ink, box.X + 6, box.Y + 3);
    }

    private static PointF[] Reduce(PointF[] pts, float tolerance) => RouteGeometry.Reduce(pts, tolerance);

    protected override void Dispose(bool disposing) {
        if (disposing) {
            StopReplay();
            Discard();
        }
        base.Dispose(disposing);
    }
}
