using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace Waybill;

/// <summary>
/// A choice from a short list, drawn like everything else in this window.
///
/// A ComboBox cannot be made to look like this: its button, its border and its list
/// are drawn by the system, so on a dark window it arrives as a white arrow in a white
/// box with a white popup, whatever colours the control itself is given. This is a
/// control tone box with a chevron, and the list behind it is the same dark menu the
/// rest of the window uses.
/// </summary>
public sealed class Picker : Control {
    private readonly List<string> _items = new();
    private int _index = -1;
    private bool _hover;
    private bool _open;

    public event Action? Changed;

    /// <summary>The dark menu the window draws every other list with. Handed in by the
    /// form, since the colour table lives there.</summary>
    public Func<ContextMenuStrip>? MenuMaker;

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public int SelectedIndex {
        get => _index;
        set {
            var clamped = _items.Count == 0 ? -1 : Math.Clamp(value, 0, _items.Count - 1);
            if (clamped == _index) return;
            _index = clamped;
            Invalidate();
            Changed?.Invoke();
        }
    }

    public string Selected => _index >= 0 && _index < _items.Count ? _items[_index] : "";

    public Picker() {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
               | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = Look.Window;
        Cursor = Cursors.Hand;
        Height = Look.InputHeight;
        Width = 150;
    }

    /// <summary>Replaces the list. The chosen entry is kept by name where it is still
    /// there, since a rebuilt list is usually the same list with one more in it.</summary>
    public void Offer(IEnumerable<string> items) {
        var was = Selected;
        _items.Clear();
        _items.AddRange(items);
        _index = _items.IndexOf(was);
        if (_index < 0) _index = _items.Count > 0 ? 0 : -1;
        FitText();
        Invalidate();
    }

    /// <summary>Wide enough for the longest entry it offers, so choosing a longer one
    /// does not make the box change size under the pointer.</summary>
    public void FitText() {
        using var g = CreateGraphics();
        var wide = 0f;
        foreach (var item in _items) wide = Math.Max(wide, Look.Measure(g, item, Look.Small).Width);
        Width = (int)Math.Ceiling(wide) + 46;
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

    protected override void OnMouseDown(MouseEventArgs e) {
        base.OnMouseDown(e);
        if (_items.Count == 0) return;

        var menu = MenuMaker?.Invoke() ?? new ContextMenuStrip();
        for (var i = 0; i < _items.Count; i++) {
            var at = i;
            var item = new ToolStripMenuItem(_items[i]) { Checked = at == _index };
            item.Click += (_, _) => SelectedIndex = at;
            menu.Items.Add(item);
        }
        _open = true;
        menu.Closed += (_, _) => { _open = false; Invalidate(); };
        menu.Show(this, new Point(0, Height + 2));
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e) {
        var g = e.Graphics;
        g.Clear(Look.Window);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        var box = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
        Look.FillRounded(g, box, Look.RadiusControl, _hover || _open ? Look.ControlHover : Look.Control);
        Look.DrawRounded(g, box, Look.RadiusControl, _open ? Look.TintEdge(Look.Accent, 40) : Look.Border);

        var text = Look.Measure(g, Selected, Look.Small);
        Look.Text(g, Selected, Look.Small, _open ? Look.Accent : Look.Ink, 12, (Height - text.Height) / 2);

        // The chevron, drawn rather than set as a glyph so it is the same weight as
        // every other line in the window.
        using var pen = new Pen(Look.Dim, 1.4f);
        var x = Width - 20;
        var y = Height / 2f - 1;
        g.DrawLines(pen, new[] { new PointF(x - 4, y - 1), new PointF(x, y + 3), new PointF(x + 4, y - 1) });
    }
}
