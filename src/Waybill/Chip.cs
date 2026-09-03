using System;
using System.Drawing;
using System.Windows.Forms;

namespace Waybill;

/// <summary>
/// A filter chip: one word in a box, on or off.
///
/// The same box as the search field and the game switch beside it, down to the corner
/// radius, the fill and the padding: three shapes in one toolbar have to be one family
/// or the bar reads as three things that happen to be near each other. Off it is the
/// control tone with muted ink; on it is a wash of the accent with the accent's own ink
/// and border, which is exactly how the chosen end of the switch is drawn.
/// </summary>
public sealed class Chip : Control {
    private bool _on;
    private bool _hover;

    /// <summary>An optional mark drawn before the word: the hazard stripes on the
    /// oversize chip, a dot on the others. Given the ink to draw in.</summary>
    public Action<Graphics, RectangleF, Color>? Badge;

    public event Action? Toggled;

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool On {
        get => _on;
        set {
            if (_on == value) return;
            _on = value;
            Invalidate();
        }
    }

    public Chip() {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
               | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Look.Window;
        Cursor = Cursors.Hand;
        Height = Look.InputHeight;
    }

    protected override void OnMouseEnter(EventArgs e) {
        base.OnMouseEnter(e);
        _hover = true;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e) {
        base.OnMouseLeave(e);
        _hover = false;
        Invalidate();
    }

    protected override void OnClick(EventArgs e) {
        base.OnClick(e);
        On = !On;
        Toggled?.Invoke();
    }

    /// <summary>Wide enough for its own word, which is not the same width in every
    /// language: a chip sized to the English one clips the German one. The padding is
    /// the switch's own, so a chip beside it is the same shape around its word.</summary>
    public void FitTo(Graphics g) {
        var wide = Look.Measure(g, Text, Look.Small).Width;
        Width = (int)Math.Ceiling(wide) + (Badge is null ? 24 : 44);
    }

    protected override void OnPaint(PaintEventArgs e) {
        var g = e.Graphics;
        g.Clear(Look.Window);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        var box = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
        var fill = On ? Look.Tint(Look.Accent, 14) : _hover ? Look.ControlHover : Look.Control;
        var edge = On ? Look.TintEdge(Look.Accent, 34) : Look.Border;
        var ink = On ? Look.Accent : _hover ? Look.Ink : Look.Muted;

        Look.FillRounded(g, box, Look.RadiusControl, fill);
        Look.DrawRounded(g, box, Look.RadiusControl, edge);

        var text = Look.Measure(g, Text, Look.Small);
        var left = Badge is null ? (Width - text.Width) / 2 : 34;
        // The mark keeps its own corner of the chip, rounded like everything else, so
        // the word beside it never runs into it however the language grows.
        if (Badge is not null) {
            var mark = new RectangleF(10, (Height - 11) / 2f, 16, 11);
            using var path = Look.Rounded(mark, 2.5f);
            var was = g.Clip;
            g.SetClip(path, System.Drawing.Drawing2D.CombineMode.Replace);
            Badge(g, mark, ink);
            g.Clip = was;
        }
        Look.Text(g, Text, Look.Small, ink, left, (Height - text.Height) / 2);
    }
}
