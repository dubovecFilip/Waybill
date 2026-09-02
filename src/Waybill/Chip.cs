using System;
using System.Drawing;
using System.Windows.Forms;

namespace Waybill;

/// <summary>
/// A filter chip: a rounded outline with one word in it, on or off.
///
/// An outline rather than a filled button, because a chip that is off has to be
/// visibly a thing that could be on. Off it is a border in the panel's own edge tone
/// with muted ink; on it is a wash of the accent with the accent's own ink and border,
/// which is the same tint every other signal in this window is drawn with.
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
    /// language: a chip sized to the English one clips the German one.</summary>
    public void FitTo(Graphics g) {
        var wide = Look.Measure(g, Text, Look.Small).Width;
        Width = (int)Math.Ceiling(wide) + (Badge is null ? 30 : 48);
    }

    protected override void OnPaint(PaintEventArgs e) {
        var g = e.Graphics;
        g.Clear(Look.Window);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        var box = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
        var fill = On ? Look.Tint(Look.Accent, 12) : _hover ? Look.Control : Look.Window;
        var edge = On ? Look.TintEdge(Look.Accent, 40) : _hover ? Look.ControlHover : Look.Border;
        var ink = On ? Look.Accent : Look.Muted;

        Look.FillRounded(g, box, box.Height / 2, fill);
        Look.DrawRounded(g, box, box.Height / 2, edge);

        var text = Look.Measure(g, Text, Look.Small);
        var left = Badge is null ? (Width - text.Width) / 2 : 36;
        // The mark keeps its own corner of the chip, rounded like everything else, so
        // the word beside it never runs into it however the language grows.
        if (Badge is not null) {
            var mark = new RectangleF(12, (Height - 11) / 2f, 16, 11);
            using var path = Look.Rounded(mark, 2.5f);
            var was = g.Clip;
            g.SetClip(path, System.Drawing.Drawing2D.CombineMode.Replace);
            Badge(g, mark, ink);
            g.Clip = was;
        }
        Look.Text(g, Text, Look.Small, ink, left, (Height - text.Height) / 2);
    }
}
