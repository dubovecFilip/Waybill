using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace Waybill;

/// <summary>
/// A choice between two things, or between not choosing at all.
///
/// Three positions in a row, the middle one meaning both. That shape says
/// something a dropdown cannot: the two ends are opposites and the middle is the
/// whole. A list of "all / ATS / ETS2" reads as three unrelated options and hides
/// two of them until it is opened, where this shows the whole choice at once and
/// takes one click to change.
///
/// It is painted rather than assembled from buttons: a radio group cannot be made
/// to look like this on a dark ground without fighting the system theme for every
/// pixel, and three of anything laid out by hand drift apart the moment the text
/// is translated.
/// </summary>
public sealed class TriSwitch : Control {
    // The same palette every other control in this window is drawn from, so a switch
    // sits in a toolbar beside a search field and three chips as one family rather
    // than as four things that happen to be near each other.
    private static readonly Color Raised = Look.Control;
    private static readonly Color Edge = Look.Border;
    private static readonly Color Ink = Look.Ink;
    private static readonly Color Muted = Look.Muted;
    private static readonly Color Accent = Look.Accent;

    private const int Pad = 12;
    private const int BadgeWidth = 10;

    private readonly string[] _captions;
    private readonly int[] _widths = new int[3];
    private int _position;
    private int _hot = -1;

    /// <summary>Which end is chosen: -1 for the left, 0 for both, 1 for the
    /// right.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Position {
        get => _position;
        set {
            var clamped = Math.Clamp(value, -1, 1);
            if (clamped == _position) return;
            _position = clamped;
            Invalidate();
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Something drawn in the right segment ahead of its text: the hazard
    /// stripes an oversize load is marked with, so the switch carries the mark it
    /// filters by rather than only naming it.</summary>
    public Action<Graphics, Rectangle>? RightBadge;

    public event EventHandler? Changed;

    public TriSwitch(string left, string both, string right) {
        _captions = new[] { left, both, right };
        DoubleBuffered = true;
        SetStyle(ControlStyles.ResizeRedraw | ControlStyles.OptimizedDoubleBuffer, true);
        Height = Look.InputHeight;
        Font = Look.Small;
        Cursor = Cursors.Hand;
        BackColor = Look.Window;
    }

    /// <summary>Sized to its own text, since a fixed width either clips a language
    /// or leaves a hole in the bar. Called once the control has a surface to measure
    /// against, which is the earliest a font's real width is known.</summary>
    public void FitText() {
        using var g = CreateGraphics();
        var total = 0;
        for (var i = 0; i < 3; i++) {
            var text = (int)Math.Ceiling(g.MeasureString(_captions[i], Font).Width);
            var badge = i == 2 && RightBadge is not null ? BadgeWidth + 6 : 0;
            _widths[i] = Math.Max(text + badge + Pad * 2, 30);
            total += _widths[i];
        }
        Width = total + 2;
    }

    /// <summary>New words for the same switch, since the language can change while
    /// the window is open and the control outlives the layout it sits in. The chosen
    /// position is kept: it was a choice about the data, not about the words.</summary>
    public void Retext(string left, string both, string right) {
        _captions[0] = left;
        _captions[1] = both;
        _captions[2] = right;
        FitText();
        Invalidate();
    }

    protected override void OnHandleCreated(EventArgs e) {
        base.OnHandleCreated(e);
        if (_widths[0] == 0) FitText();
    }

    private int SegmentAt(int x) {
        var edge = 1;
        for (var i = 0; i < 3; i++) {
            edge += _widths[i];
            if (x < edge) return i;
        }
        return 2;
    }

    protected override void OnMouseMove(MouseEventArgs e) {
        base.OnMouseMove(e);
        var over = SegmentAt(e.X);
        if (over == _hot) return;
        _hot = over;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e) {
        base.OnMouseLeave(e);
        _hot = -1;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e) {
        base.OnMouseDown(e);
        Position = SegmentAt(e.X) - 1;
    }

    protected override void OnPaint(PaintEventArgs e) {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        using (var back = new SolidBrush(BackColor)) g.FillRectangle(back, ClientRectangle);

        var whole = new Rectangle(0, 0, Width - 1, Height - 1);
        using (var track = new SolidBrush(Raised))
        using (var border = new Pen(Edge))
        using (var path = Rounded(whole, Look.RadiusControl)) {
            g.FillPath(track, path);
            g.DrawPath(border, path);
        }

        var x = 1;
        for (var i = 0; i < 3; i++) {
            var seat = new Rectangle(x, 3, _widths[i], Height - 7);
            var chosen = i == _position + 1;
            if (chosen) {
                using var fill = new SolidBrush(Look.Tint(Look.Accent, 14));
                using var edge = new Pen(Look.TintEdge(Look.Accent, 34));
                using var path = Rounded(seat, Look.RadiusChip);
                g.FillPath(fill, path);
                g.DrawPath(edge, path);
            } else if (i == _hot) {
                using var fill = new SolidBrush(Look.ControlHover);
                using var path = Rounded(seat, Look.RadiusChip);
                g.FillPath(fill, path);
            }

            var badge = i == 2 ? RightBadge : null;
            var text = seat;
            if (badge is not null) {
                // The mark sits against the segment's left padding and the text is
                // pushed past it, so the two never overlap however the words grow.
                badge(g, new Rectangle(seat.Left + Pad - 2, seat.Top + 3, BadgeWidth, seat.Height - 6));
                text = new Rectangle(seat.Left + BadgeWidth + 4, seat.Top, seat.Width - BadgeWidth - 4, seat.Height);
            }

            using var pen = new SolidBrush(chosen ? Accent : i == _hot ? Ink : Muted);
            using var centre = new StringFormat {
                Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center,
            };
            g.DrawString(_captions[i], Font, pen, text, centre);
            x += _widths[i];
        }
    }

    private static GraphicsPath Rounded(Rectangle r, int radius) {
        var path = new GraphicsPath();
        var d = radius * 2;
        path.AddArc(r.Left, r.Top, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
