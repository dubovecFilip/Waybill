using System.ComponentModel;
using System.Drawing.Drawing2D;
using Waybill.Storage;

namespace Waybill;

/// <summary>
/// The shape of a drive seen from the side: how the ground rose and fell under it,
/// coloured by how fast the truck was going at the time.
///
/// It answers the question the map cannot. A route drawn from above says the drive
/// went that way; it does not say why forty minutes of it were spent at fifty. Put
/// the height beside the speed and the mountain says it for you.
///
/// Two things are deliberately absent, both for the same reason the map reports no
/// distances. There is no height in metres: measured across this history the game's
/// vertical is not a scaled version of the real one, and the drop at Winslow sits
/// at the same height as the drop at Tucson while the real places are eight hundred
/// metres apart. And there is no gradient, which would need a real horizontal scale
/// as well as a vertical one, and would be twice as wrong.
///
/// What is left is honest and is the useful part anyway: the order of the climbs,
/// their size relative to each other within this drive, and the speed on each.
/// </summary>
public class HeightView : Control {
    private static readonly Color Backdrop = Color.FromArgb(22, 25, 29);
    private static readonly Color Ink = Color.FromArgb(228, 233, 240);
    private static readonly Color Muted = Color.FromArgb(138, 148, 163);
    private static readonly Color Edge = Color.FromArgb(48, 54, 62);

    private const float Pad = 10f;

    /// <summary>The route's eight speed colours, each already mixed down against the
    /// backdrop so the ground can be filled opaquely. Worked out once: the ramp
    /// never changes and neither does what is behind it.</summary>
    private static readonly Color[] Ground = RouteView.SpeedRamp
        .Select(c => Color.FromArgb(
            Backdrop.R + (c.R - Backdrop.R) * 47 / 100,
            Backdrop.G + (c.G - Backdrop.G) * 47 / 100,
            Backdrop.B + (c.B - Backdrop.B) * 47 / 100))
        .ToArray();

    private List<HeightPoint> _points = new();
    private float _low, _high;
    private long _from, _span;
    private int _hover = -1;

    private double _sweep = 1;
    private System.Windows.Forms.Timer? _replay;
    private int _replayMs;
    private int _replayFrom;

    /// <summary>Formats a speed for the readout, in whichever units the window is
    /// showing. Injected for the same reason the map takes one.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Func<float, string> FormatSpeed { get; set; } = kmh => $"{kmh:0} km/h";

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string EmptyText { get; set; } = "";

    /// <summary>Said quietly in the corner, because a profile with no numbers on it
    /// invites someone to read the numbers that are not there.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Hint { get; set; } = "";

    public HeightView() {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
               | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = Backdrop;
        TabStop = false;
    }

    public void Show(List<HeightPoint> points) {
        // One reading a second over a long haul is far more than a strip a few
        // hundred pixels wide can show. Averaged down to roughly one reading per
        // pixel, which is both all the strip can resolve and what stops the colour
        // turning into a barcode: at four readings a pixel the speed flickers
        // between bands inside a single column and the shape disappears under it.
        _points = Thin(points, Math.Max(Width, 200));
        _hover = -1;
        StopReplay();

        if (_points.Count > 1) {
            _low = _points.Min(p => p.Y);
            _high = _points.Max(p => p.Y);
            _from = _points[0].AtMs;
            _span = Math.Max(1, _points[^1].AtMs - _from);
        }
        Invalidate();
    }

    /// <summary>
    /// Averages the readings down into as many buckets as there is room for, rather
    /// than keeping every nth and throwing the rest away.
    ///
    /// Averaging matters more than it sounds here. Sampling picks whatever second it
    /// happens to land on, so one moment of braking colours a whole pixel column as
    /// though the truck crawled the lot; the mean over the stretch a column stands
    /// for says what actually happened along it.
    /// </summary>
    private static List<HeightPoint> Thin(List<HeightPoint> points, int most) {
        if (points.Count <= most) return points;

        var kept = new List<HeightPoint>(most);
        var step = (double)points.Count / most;
        for (var b = 0; b < most; b++) {
            var from = (int)(b * step);
            var to = Math.Min(points.Count, (int)((b + 1) * step));
            if (to <= from) continue;

            double y = 0, speed = 0;
            for (var i = from; i < to; i++) { y += points[i].Y; speed += points[i].SpeedKmh; }
            var n = to - from;
            kept.Add(new HeightPoint(points[(from + to) / 2].AtMs, (float)(y / n), (float)(speed / n)));
        }
        return kept;
    }

    /// <summary>Draws the profile out from the start at the same rate, so it can run
    /// beside the route replay and the two show the same moment.</summary>
    public void Replay(int milliseconds = 2400) {
        if (_points.Count < 2 || milliseconds <= 0) return;
        StopReplay();
        _sweep = 0;
        _replayMs = milliseconds;
        _replayFrom = Environment.TickCount;
        _replay = new System.Windows.Forms.Timer { Interval = 16 };
        _replay.Tick += (_, _) => {
            var gone = (Environment.TickCount - _replayFrom) / (double)_replayMs;
            _sweep = gone >= 1 ? 1 : 1 - Math.Pow(1 - gone, 1.6);
            if (_sweep >= 1) StopReplay();
            Invalidate();
        };
        _replay.Start();
        Invalidate();
    }

    public void StopReplay() {
        _replay?.Stop();
        _replay?.Dispose();
        _replay = null;
        _sweep = 1;
    }

    private RectangleF Plot => new(Pad, Pad, Math.Max(1, Width - Pad * 2), Math.Max(1, Height - Pad * 2 - 12));

    private PointF At(int i) {
        var plot = Plot;
        var p = _points[i];
        var x = plot.Left + plot.Width * (p.AtMs - _from) / _span;
        // A flat drive would divide by nothing, so it is drawn along the middle.
        var range = _high - _low;
        var y = range > 0.01f
            ? plot.Bottom - plot.Height * (p.Y - _low) / range
            : plot.Top + plot.Height / 2;
        return new PointF(x, y);
    }

    protected override void OnMouseMove(MouseEventArgs e) {
        base.OnMouseMove(e);
        if (_points.Count < 2) return;

        var plot = Plot;
        var along = (e.X - plot.Left) / plot.Width;
        var was = _hover;
        _hover = along is < 0 or > 1
            ? -1
            : Math.Clamp((int)Math.Round(along * (_points.Count - 1)), 0, _points.Count - 1);
        if (_hover != was) Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e) {
        base.OnMouseLeave(e);
        if (_hover < 0) return;
        _hover = -1;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e) {
        var g = e.Graphics;
        g.Clear(Backdrop);

        if (_points.Count < 2) {
            if (EmptyText.Length > 0) {
                using var brush = new SolidBrush(Muted);
                using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                g.DrawString(EmptyText, Font, brush, new RectangleF(8, 8, Width - 16, Height - 16), format);
            }
            return;
        }

        g.SmoothingMode = SmoothingMode.AntiAlias;
        var plot = Plot;
        var reached = _sweep >= 1 ? _points.Count : Math.Max(2, (int)Math.Ceiling(_sweep * _points.Count));

        // The ground, filled: one band per speed colour, each closed down to the
        // floor. Filling rather than stroking is what makes it read as terrain
        // instead of as another line chart.
        //
        // Drawn without antialiasing, in colours already mixed against the
        // background rather than laid over it with an alpha, and each piece run half
        // a pixel into the next.
        //
        // All three are the same lesson learned the hard way. Smoothed edges do not
        // tile, so every seam let a hairline of background through; overlapping them
        // to close the seam painted the overlap twice, which with a see-through
        // colour comes out darker. Either way a few hundred seams down the strip
        // read as stripes painted on the hill rather than as one hillside. An opaque
        // colour can be overlapped as much as it likes and nothing shows.
        g.SmoothingMode = SmoothingMode.None;
        for (var i = 1; i < reached; i++) {
            var a = At(i - 1);
            var b = At(i);
            using var fill = new SolidBrush(Ground[RouteView.SpeedBand(_points[i].SpeedKmh)]);
            g.FillPolygon(fill, new[] {
                a, new PointF(b.X + 0.5f, b.Y), new PointF(b.X + 0.5f, plot.Bottom), new PointF(a.X, plot.Bottom),
            });
        }
        g.SmoothingMode = SmoothingMode.AntiAlias;

        // The skyline over the top, so the shape stays legible where the fill is dark.
        using (var pen = new Pen(Color.FromArgb(235, 214, 224, 238), 1.6f) { LineJoin = LineJoin.Round }) {
            var line = new PointF[reached];
            for (var i = 0; i < reached; i++) line[i] = At(i);
            if (line.Length > 1) g.DrawLines(pen, line);
        }

        using (var floor = new Pen(Edge)) g.DrawLine(floor, plot.Left, plot.Bottom, plot.Right, plot.Bottom);

        if (Hint.Length > 0 && _hover < 0) {
            using var font = new Font("Segoe UI", 7.5F);
            using var brush = new SolidBrush(Color.FromArgb(120, 138, 148, 163));
            var size = g.MeasureString(Hint, font);
            g.DrawString(Hint, font, brush, 8, Height - size.Height - 2);
        }

        if (_hover >= 0 && _hover < reached) Readout(g);
    }

    private void Readout(Graphics g) {
        var p = _points[_hover];
        var at = At(_hover);
        var into = TimeSpan.FromMilliseconds(p.AtMs - _from);
        var when = into.TotalHours >= 1
            ? $"{(int)into.TotalHours}:{into.Minutes:00}:{into.Seconds:00}"
            : $"{into.Minutes}:{into.Seconds:00}";
        // No height, on purpose: the number under the pointer would be the game's own
        // vertical, and anyone reading it would read it as metres.
        var label = $"{when}   {FormatSpeed(p.SpeedKmh)}";

        using var line = new Pen(Color.FromArgb(90, 200, 210, 224));
        g.DrawLine(line, at.X, Plot.Top, at.X, Plot.Bottom);
        using var dot = new SolidBrush(Ink);
        g.FillEllipse(dot, at.X - 2.5f, at.Y - 2.5f, 5, 5);

        using var font = new Font("Segoe UI", 8F);
        var size = g.MeasureString(label, font);
        var box = new RectangleF(
            Math.Clamp(at.X + 8, 2, Math.Max(2, Width - size.Width - 6)),
            Math.Max(2, Plot.Top), size.Width + 8, size.Height + 4);
        using var back = new SolidBrush(Color.FromArgb(220, 30, 34, 39));
        using var rim = new Pen(Edge);
        g.FillRectangle(back, box);
        g.DrawRectangle(rim, box.X, box.Y, box.Width, box.Height);
        using var text = new SolidBrush(Ink);
        g.DrawString(label, font, text, box.X + 4, box.Y + 2);
    }

    protected override void Dispose(bool disposing) {
        if (disposing) StopReplay();
        base.Dispose(disposing);
    }
}
