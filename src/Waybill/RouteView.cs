using System.ComponentModel;
using System.Drawing.Drawing2D;
using Waybill.Storage;

namespace Waybill;

/// <summary>
/// Draws a drive in the game's world space: the route itself, every other route
/// ever driven as the background, and the cities the history has learned.
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
    private static readonly Color Line = Color.FromArgb(48, 54, 62);

    /// <summary>Slow to fast. Eight steps rather than a continuous gradient so the
    /// line can be drawn as a handful of polylines instead of a thousand separate
    /// segments, which is the difference between one millisecond and thirty.</summary>
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

    /// <summary>Two recorded positions further apart than this were not driven
    /// between. Measured on real history the ordinary gap is 19 m and the 95th
    /// percentile 32 m, while every teleport and ferry was over 1 700 m, so there
    /// is a wide empty band to put the line in.</summary>
    private const float BreakMetres = 250f;

    private const float Pad = 18f;

    private List<RoutePoint> _route = new();
    private List<List<RoutePoint>> _runs = new();
    private List<List<RoutePoint>> _background = new();
    private List<CityAnchor> _cities = new();

    private float _fitScale = 1f;
    private float _zoom = 1f;
    private PointF _centre;
    private bool _fitted;

    private Bitmap? _under;
    private (int W, int H, float Scale, float CX, float CY) _underKey;

    private Point _dragFrom;
    private bool _dragging;
    private int _hover = -1;

    // Set from code and never from a designer, which is what the attribute says:
    // this control is built by hand like the rest of the window.

    /// <summary>Formats a speed for the hover readout. Injected because the window
    /// owns the unit system and this control has no business knowing whether the
    /// driver reads miles or kilometres.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Func<float, string> FormatSpeed { get; set; } = kmh => $"{kmh:0} km/h";

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string EmptyText { get; set; } = "";

    /// <summary>Says how to work the thing, quietly, in a corner. Wheel zooming and
    /// drag panning are not visible affordances, and a map that looks fixed does not
    /// get zoomed.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Hint { get; set; } = "";

    public RouteView() {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
               | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        BackColor = Backdrop;
        TabStop = false;
        Cursor = Cursors.Hand;
    }

    public void Show(List<RoutePoint> route, IEnumerable<List<RoutePoint>> background, List<CityAnchor> cities) {
        _route = route;
        _runs = Split(route);
        _background = background.SelectMany(Split).ToList();
        _cities = cities;
        _fitted = false;
        Discard();
        Invalidate();
    }

    /// <summary>
    /// Breaks the recording into stretches that were actually driven.
    ///
    /// Two of these matter. The first point of every job is where the driver stood
    /// when the offer was taken, and the second is where the truck is: on a quick
    /// job that is another city, and joining them draws a hundred kilometre line
    /// across the map that was never driven. Ferries and trains do the same thing
    /// in the middle of a drive. Neither is a straight line on a map, so neither
    /// gets drawn as one.
    /// </summary>
    private static List<List<RoutePoint>> Split(List<RoutePoint> pts) {
        var runs = new List<List<RoutePoint>>();
        var run = new List<RoutePoint>();
        for (var i = 0; i < pts.Count; i++) {
            if (i > 0) {
                var dx = pts[i].X - pts[i - 1].X;
                var dz = pts[i].Z - pts[i - 1].Z;
                if (dx * dx + dz * dz > BreakMetres * BreakMetres) {
                    runs.Add(run);
                    run = new List<RoutePoint>();
                }
            }
            run.Add(pts[i]);
        }
        runs.Add(run);
        // A stretch of one point was never driven along, and left in it would drag
        // the view out to wherever the driver happened to be standing.
        runs.RemoveAll(r => r.Count < 2);
        return runs;
    }

    private void Fit() {
        _zoom = 1f;
        _fitted = true;
        if (_runs.Count == 0) { _fitScale = 1f; _centre = PointF.Empty; return; }

        float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
        foreach (var run in _runs)
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
        if (_runs.Count == 0) return;

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
        _dragFrom = e.Location;
        Cursor = Cursors.SizeAll;
    }

    protected override void OnMouseMove(MouseEventArgs e) {
        base.OnMouseMove(e);
        if (_dragging) {
            _centre = new PointF(
                _centre.X - (e.X - _dragFrom.X) / PerMetre,
                _centre.Y - (e.Y - _dragFrom.Y) / PerMetre);
            _dragFrom = e.Location;
            Discard();
            Invalidate();
            return;
        }

        var was = _hover;
        _hover = Nearest(e.Location);
        if (was != _hover) Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e) {
        base.OnMouseUp(e);
        _dragging = false;
        Cursor = Cursors.Hand;
    }

    protected override void OnMouseLeave(EventArgs e) {
        base.OnMouseLeave(e);
        if (_hover >= 0) { _hover = -1; Invalidate(); }
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e) {
        base.OnMouseDoubleClick(e);
        Fit();
        Discard();
        Invalidate();
    }

    /// <summary>Index into the route of the closest recorded position, or -1 when
    /// the pointer is not near the line at all.</summary>
    private int Nearest(Point at) {
        var best = -1;
        var bestDist = 14f * 14f;
        for (var i = 0; i < _route.Count; i++) {
            var s = ToScreen(_route[i].X, _route[i].Z);
            var d = (s.X - at.X) * (s.X - at.X) + (s.Y - at.Y) * (s.Y - at.Y);
            if (d < bestDist) { bestDist = d; best = i; }
        }
        return best;
    }

    private static int Band(float kmh) => Math.Clamp((int)(kmh / 15f), 0, Ramp.Length - 1);

    protected override void OnPaint(PaintEventArgs e) {
        var g = e.Graphics;
        g.Clear(Backdrop);

        if (_route.Count == 0 || _runs.Count == 0) {
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
        DrawRoute(g);
        // Names go over the route rather than under it. A label is what makes the
        // line mean somewhere, so the line is not allowed to eat the first letters
        // of it, which it did wherever a drive passed through a city it named.
        DrawCities(g);
        DrawEnds(g);
        DrawHover(g);

        if (Hint.Length > 0 && _hover < 0) {
            using var font = new Font("Segoe UI", 7.5F);
            using var brush = new SolidBrush(Color.FromArgb(120, 138, 148, 163));
            var size = g.MeasureString(Hint, font);
            g.DrawString(Hint, font, brush, 8, Height - size.Height - 5);
        }
    }

    /// <summary>
    /// Every other drive, and the cities, painted once into a bitmap and kept.
    ///
    /// It only has to be redone when the view actually moves. Measured on real
    /// routes cloned up to a five hundred delivery history, drawing them costs
    /// 13 ms while copying the finished bitmap costs 1.3 ms, and hovering the
    /// route repaints constantly.
    /// </summary>
    private void DrawUnderlay(Graphics g) {
        var key = (Width, Height, PerMetre, _centre.X, _centre.Y);
        if (_under is null || _underKey != key) {
            Discard();
            _underKey = key;
            _under = new Bitmap(Width, Height);
            using var ug = Graphics.FromImage(_under);
            ug.SmoothingMode = SmoothingMode.AntiAlias;

            using var pen = new Pen(Color.FromArgb(64, 104, 116, 132), 1.1f);
            foreach (var run in _background) {
                // Simplified against the screen, not the world: points closer together
                // than a pixel cannot be told apart, and on a whole-map view that is
                // about 97 % of them.
                var pts = Reduce(Project(run), 0.7f);
                if (pts.Length > 1) ug.DrawLines(pen, pts);
            }
        }
        g.DrawImageUnscaled(_under, 0, 0);
    }

    private PointF[] Project(List<RoutePoint> run) {
        var pts = new PointF[run.Count];
        for (var i = 0; i < run.Count; i++) pts[i] = ToScreen(run[i].X, run[i].Z);
        return pts;
    }

    private void DrawRoute(Graphics g) {
        // The stretches that were not driven, drawn first so the route sits on top.
        using (var skip = new Pen(Color.FromArgb(90, 150, 160, 175), 1f) { DashStyle = DashStyle.Dash }) {
            for (var i = 1; i < _runs.Count; i++) {
                var a = ToScreen(_runs[i - 1][^1].X, _runs[i - 1][^1].Z);
                var b = ToScreen(_runs[i][0].X, _runs[i][0].Z);
                g.DrawLine(skip, a, b);
            }
        }

        var pens = new Pen[Ramp.Length];
        for (var i = 0; i < Ramp.Length; i++)
            pens[i] = new Pen(Ramp[i], 2.4f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };

        try {
            foreach (var run in _runs) {
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

    private void DrawEnds(Graphics g) {
        var start = ToScreen(_runs[0][0].X, _runs[0][0].Z);
        var end = ToScreen(_runs[^1][^1].X, _runs[^1][^1].Z);

        using var ring = new Pen(Ink, 2f);
        using var back = new SolidBrush(Backdrop);
        g.FillEllipse(back, start.X - 5, start.Y - 5, 10, 10);
        g.DrawEllipse(ring, start.X - 5, start.Y - 5, 10, 10);

        using var fill = new SolidBrush(Accent);
        g.FillEllipse(fill, end.X - 5.5f, end.Y - 5.5f, 11, 11);
    }

    /// <summary>
    /// The cities this history knows, each where the game actually put it.
    ///
    /// They are learned rather than looked up: every job names the city it loaded
    /// in and the one it unloaded in. That means a dot is really the middle of the
    /// depots used there, which for a city visited once is one depot. Close enough
    /// to label a corner of the map with, and honest, which a downloaded list from
    /// an older version of the game would not be.
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
            // Cities seen most often are drawn first, so where two labels collide the
            // one the driver knows better is the one that survives.
            if (placed.Any(p => p.IntersectsWith(label))) continue;
            placed.Add(RectangleF.Inflate(label, 3, 1));

            g.FillRectangle(halo, RectangleF.Inflate(label, 2, 0));
            g.DrawString(city.Name, font, text, label.Location);
        }
    }

    private void DrawHover(Graphics g) {
        if (_hover < 0 || _hover >= _route.Count) return;

        var p = _route[_hover];
        var at = ToScreen(p.X, p.Z);
        using (var ring = new Pen(Ink, 1.6f)) g.DrawEllipse(ring, at.X - 4, at.Y - 4, 8, 8);

        var into = TimeSpan.FromMilliseconds(p.AtMs - _route[0].AtMs);
        var when = into.TotalHours >= 1 ? $"{(int)into.TotalHours}:{into.Minutes:00}:{into.Seconds:00}" : $"{into.Minutes}:{into.Seconds:00}";
        var label = $"{when}   {FormatSpeed(p.SpeedKmh)}";

        using var font = new Font("Segoe UI", 8F);
        var size = g.MeasureString(label, font);
        var box = new RectangleF(at.X + 10, at.Y - size.Height - 8, size.Width + 12, size.Height + 6);
        // Kept inside the control, or the readout falls off the edge exactly when the
        // pointer is somewhere interesting.
        if (box.Right > Width - 4) box.X = at.X - box.Width - 10;
        if (box.Top < 4) box.Y = at.Y + 10;

        using var back = new SolidBrush(Surface);
        using var edge = new Pen(Line);
        using var ink = new SolidBrush(Ink);
        g.FillRectangle(back, box);
        g.DrawRectangle(edge, box.X, box.Y, box.Width, box.Height);
        g.DrawString(label, font, ink, box.X + 6, box.Y + 3);
    }

    /// <summary>
    /// Ramer-Douglas-Peucker, with the tolerance in screen pixels because that is
    /// the only place it means anything: on a view of the whole map one pixel is
    /// about 135 metres and the recorded points are 19 metres apart, so seventeen
    /// of every twenty land on a pixel that is already painted.
    /// </summary>
    private static PointF[] Reduce(PointF[] pts, float tolerance) {
        if (pts.Length < 3) return pts;
        var keep = new List<PointF> { pts[0] };
        Walk(pts, 0, pts.Length - 1, tolerance, keep);
        keep.Add(pts[^1]);
        return keep.ToArray();
    }

    private static void Walk(PointF[] pts, int first, int last, float tolerance, List<PointF> keep) {
        if (last <= first + 1) return;

        float dx = pts[last].X - pts[first].X, dy = pts[last].Y - pts[first].Y;
        var span = MathF.Sqrt(dx * dx + dy * dy);

        var worst = 0f;
        var at = -1;
        for (var i = first + 1; i < last; i++) {
            var d = span < 1e-4f
                ? MathF.Sqrt(MathF.Pow(pts[i].X - pts[first].X, 2) + MathF.Pow(pts[i].Y - pts[first].Y, 2))
                : MathF.Abs(dy * pts[i].X - dx * pts[i].Y + pts[last].X * pts[first].Y - pts[last].Y * pts[first].X) / span;
            if (d > worst) { worst = d; at = i; }
        }
        if (worst <= tolerance || at < 0) return;

        Walk(pts, first, at, tolerance, keep);
        keep.Add(pts[at]);
        Walk(pts, at, last, tolerance, keep);
    }

    protected override void Dispose(bool disposing) {
        if (disposing) Discard();
        base.Dispose(disposing);
    }
}
