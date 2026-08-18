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

    /// <summary>One route, split into the stretches that were actually driven.</summary>
    private class Drawn {
        public long Id;
        public List<RoutePoint> All = new();
        public List<List<RoutePoint>> Runs = new();
    }

    private List<Drawn> _drawn = new();
    /// <summary>Stretches belonging to no delivery, already split into runs.</summary>
    private List<List<RoutePoint>> _secondary = new();
    private Drawn? _focus;
    private List<CityAnchor> _cities = new();
    private List<(TimelineRow Row, RoutePoint At)> _marks = new();

    private float _fitScale = 1f;
    private float _zoom = 1f;
    private PointF _centre;
    private bool _fitted;

    private Bitmap? _under;
    private (int W, int H, float Scale, float CX, float CY, long Lit, bool History, bool Freeroam) _underKey;

    private Point _dragFrom;
    private bool _dragging;
    private bool _dragged;
    private int _hoverPoint = -1;
    private int _hoverMark = -1;
    private long _lit;

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
    public bool ShowFreeroam { get; set; } = true;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool ShowMarks { get; set; } = true;

    /// <summary>Raised when a route is clicked with none singled out, which is the
    /// history map's way of opening a delivery.</summary>
    public event Action<long>? RouteChosen;

    public RouteView() {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
               | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        BackColor = Backdrop;
        TabStop = false;
        Cursor = Cursors.Hand;
    }

    /// <summary><paramref name="focus"/> is the delivery to draw in full, or 0 to
    /// draw every route alike. <paramref name="marks"/> only mean anything against
    /// a focused route, since they are placed by matching their time to it.
    ///
    /// <paramref name="secondary"/> is driving that belongs to no delivery:
    /// between jobs, or out to a trailer. Drawn because those roads are as much a
    /// part of where this driver has been, and drawn quietly because there is nothing
    /// behind them to open. They are never hit-tested for the same reason.</summary>
    public void Show(IEnumerable<RouteLayer> routes, long focus, List<CityAnchor> cities,
                     List<TimelineRow>? marks = null, IEnumerable<List<RoutePoint>>? secondary = null) {
        _drawn = routes.Select(r => new Drawn { Id = r.Id, All = r.Points, Runs = Split(r.Points) })
                       .Where(d => d.Runs.Count > 0).ToList();
        _secondary = (secondary ?? Enumerable.Empty<List<RoutePoint>>())
                     .SelectMany(Split).Where(r => r.Count > 1).ToList();
        _focus = _drawn.FirstOrDefault(d => d.Id == focus);
        _cities = cities;
        _marks = PlaceMarks(marks);
        _lit = 0;
        _hoverPoint = _hoverMark = -1;
        _fitted = false;
        Discard();
        Invalidate();
    }

    /// <summary>
    /// Ties each event to the position the truck was in when it happened, by time.
    ///
    /// Events and route points are recorded by the same clock a second apart, so
    /// the nearest point is the right one. An event further than a minute from any
    /// recorded position is dropped rather than pinned to a guess: that means the
    /// route stopped being recorded around it, and a pin in the wrong place says
    /// something false about where the driver was.
    /// </summary>
    private List<(TimelineRow, RoutePoint)> PlaceMarks(List<TimelineRow>? marks) {
        var placed = new List<(TimelineRow, RoutePoint)>();
        if (marks is null || _focus is null) return placed;

        foreach (var mark in marks) {

            var best = long.MaxValue;
            RoutePoint at = default;
            foreach (var p in _focus.All) {
                var off = Math.Abs(p.AtMs - mark.AtMs);
                if (off >= best) continue;
                best = off;
                at = p;
            }
            if (best <= 60_000) placed.Add((mark, at));
        }
        return placed;
    }

    private static List<List<RoutePoint>> Split(List<RoutePoint> pts) => RouteGeometry.Split(pts);

    public void Fit() {
        _zoom = 1f;
        _fitted = true;

        // The focused route sets the frame when there is one. On the history map
        // there is not, so everything does.
        var scope = _focus is { } f ? new List<Drawn> { f } : _drawn;
        // Freeroam counts towards the frame only when nothing is singled out: on the
        // whole-history map it is part of where this driver has been, while on a
        // delivery's card it must not pull the view away from the delivery.
        var alsoSecondary = _focus is null && ShowFreeroam ? _secondary : new List<List<RoutePoint>>();
        if (scope.Count == 0 && alsoSecondary.Count == 0) { _fitScale = 1f; _centre = PointF.Empty; return; }

        float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
        foreach (var run in scope.SelectMany(d => d.Runs).Concat(alsoSecondary))
            foreach (var p in run) {
                minX = Math.Min(minX, p.X); maxX = Math.Max(maxX, p.X);
                minZ = Math.Min(minZ, p.Z); maxZ = Math.Max(maxZ, p.Z);
            }
        _centre = new PointF((minX + maxX) / 2, (minZ + maxZ) / 2);

        var w = Math.Max(maxX - minX, 1f);
        var h = Math.Max(maxZ - minZ, 1f);
        // One scale for both axes. Stretching the route to fill the control would
        // make a straight motorway look like a curve, which is a lie about the only
        // thing this control does claim to show.
        _fitScale = Math.Min((Width - Pad * 2) / w, (Height - Pad * 2) / h);
        if (_fitScale <= 0 || float.IsInfinity(_fitScale)) _fitScale = 1f;
        Discard();
        Invalidate();
    }

    /// <summary>Pixels per world metre. Named for what it is rather than "scale",
    /// which on a Control already means resizing one.</summary>
    private float PerMetre => _fitScale * _zoom;

    private PointF ToScreen(float x, float z) => new(
        (x - _centre.X) * PerMetre + Width / 2f,
        (z - _centre.Y) * PerMetre + Height / 2f);

    private PointF ToWorld(float sx, float sy) => new(
        (sx - Width / 2f) / PerMetre + _centre.X,
        (sy - Height / 2f) / PerMetre + _centre.Y);

    private void Discard() { _under?.Dispose(); _under = null; }

    protected override void OnResize(EventArgs e) {
        base.OnResize(e);
        _fitted = false;
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
        if (_drawn.Count == 0) return;

        // Zoom about the pointer: whatever is under it stays under it, which is what
        // makes zooming feel like moving closer rather than being thrown somewhere.
        var before = ToWorld(e.X, e.Y);
        var step = e.Delta > 0 ? 1.2f : 1 / 1.2f;
        _zoom = Math.Clamp(_zoom * step, 1f, 60f);
        var after = ToWorld(e.X, e.Y);
        _centre = new PointF(_centre.X + (before.X - after.X), _centre.Y + (before.Y - after.Y));

        Discard();
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e) {
        base.OnMouseDown(e);
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
            _centre = new PointF(
                _centre.X - (e.X - _dragFrom.X) / PerMetre,
                _centre.Y - (e.Y - _dragFrom.Y) / PerMetre);
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

        if (wasPoint != _hoverPoint || wasMark != _hoverMark) Invalidate();
        // The lit route lives in the cached layer, so changing it has to throw it.
        if (wasLit != _lit) { Discard(); Invalidate(); }
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
        if (_hoverPoint >= 0 || _hoverMark >= 0) { _hoverPoint = _hoverMark = -1; Invalidate(); }
        if (_lit != 0) { _lit = 0; Discard(); Invalidate(); }
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e) {
        base.OnMouseDoubleClick(e);
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

    private static int Band(float kmh) => Math.Clamp((int)(kmh / 15f), 0, Ramp.Length - 1);

    protected override void OnPaint(PaintEventArgs e) {
        var g = e.Graphics;
        g.Clear(Backdrop);

        if (_drawn.Count == 0 && _secondary.Count == 0) {
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
        }
        // Names go over the route rather than under it. A label is what makes the
        // line mean somewhere, so the line is not allowed to eat the first letters
        // of it, which it did wherever a drive passed through a city it named.
        if (ShowCities) DrawCities(g);
        if (ShowMarks) DrawMarks(g);
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
        var key = (Width, Height, PerMetre, _centre.X, _centre.Y, _lit, ShowHistory, ShowFreeroam);
        if (_under is null || _underKey != key) {
            Discard();
            _underKey = key;
            _under = new Bitmap(Width, Height);
            using var ug = Graphics.FromImage(_under);
            ug.SmoothingMode = SmoothingMode.AntiAlias;

            // Behind a route being read they are context and stay out of the way; with
            // nothing singled out they are the whole picture, and a background drawn
            // at background strength would leave the page looking empty.
            using var quiet = _focus is null
                ? new Pen(Color.FromArgb(165, 128, 146, 166), 1.4f) { LineJoin = LineJoin.Round }
                : new Pen(Color.FromArgb(64, 104, 116, 132), 1.1f);
            using var loud = new Pen(Color.FromArgb(235, 232, 168, 74), 2f) { LineJoin = LineJoin.Round };

            // Driving that carried nothing, under everything else and without any
            // hue of its own: the deliveries are the network, this is wandering.
            if (ShowFreeroam) {
                using var spare = new Pen(Color.FromArgb(_focus is null ? 105 : 46, 132, 136, 142), 0.9f);
                foreach (var run in _secondary) {
                    var pts = Reduce(Project(run), 0.7f);
                    if (pts.Length > 1) ug.DrawLines(spare, pts);
                }
            }

            foreach (var d in _drawn) {
                if (d == _focus) continue;
                if (!ShowHistory && d.Id != _lit) continue;
                var pen = d.Id == _lit && _lit != 0 ? loud : quiet;
                foreach (var run in d.Runs) {
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
        // The stretches that were not driven, drawn first so the route sits on top.
        using (var skip = new Pen(Color.FromArgb(90, 150, 160, 175), 1f) { DashStyle = DashStyle.Dash }) {
            for (var i = 1; i < route.Runs.Count; i++) {
                var a = ToScreen(route.Runs[i - 1][^1].X, route.Runs[i - 1][^1].Z);
                var b = ToScreen(route.Runs[i][0].X, route.Runs[i][0].Z);
                g.DrawLine(skip, a, b);
            }
        }

        var pens = new Pen[Ramp.Length];
        for (var i = 0; i < Ramp.Length; i++)
            pens[i] = new Pen(Ramp[i], 2.4f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };

        try {
            foreach (var run in route.Runs) {
                var band = Band(run[0].SpeedKmh);
                var stretch = new List<PointF> { ToScreen(run[0].X, run[0].Z) };
                for (var i = 1; i < run.Count; i++) {
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

    private void DrawEnds(Graphics g, Drawn route) {
        var start = ToScreen(route.Runs[0][0].X, route.Runs[0][0].Z);
        var end = ToScreen(route.Runs[^1][^1].X, route.Runs[^1][^1].Z);

        using var ring = new Pen(Ink, 2f);
        using var back = new SolidBrush(Backdrop);
        g.FillEllipse(back, start.X - 5, start.Y - 5, 10, 10);
        g.DrawEllipse(ring, start.X - 5, start.Y - 5, 10, 10);

        using var fill = new SolidBrush(Accent);
        g.FillEllipse(fill, end.X - 5.5f, end.Y - 5.5f, 11, 11);
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
        for (var i = 0; i < _marks.Count; i++) {
            var (row, point) = _marks[i];
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
            var label = new RectangleF(at.X + 10, at.Y - size.Height / 2, size.Width, size.Height);
            // Cities seen in most places are drawn first, so where two labels collide
            // the one the driver knows better is the one that survives.
            if (placed.Any(p => p.IntersectsWith(label))) continue;
            placed.Add(RectangleF.Inflate(label, 3, 1));

            g.FillRectangle(halo, RectangleF.Inflate(label, 2, 0));
            g.DrawString(city.Name, font, text, label.Location);
        }
    }

    private void DrawReadout(Graphics g) {
        string label;
        PointF at;

        if (_hoverMark >= 0 && _hoverMark < _marks.Count) {
            var (row, point) = _marks[_hoverMark];
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
        if (disposing) Discard();
        base.Dispose(disposing);
    }
}
