using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Waybill.Storage;

namespace Waybill;

/// <summary>
/// The history, drawn as a list rather than assembled as a grid.
///
/// A DataGridView brings its own borders, its own selection colours, its own idea of
/// what a cell looks like under the pointer, and every one of them had to be argued
/// with; two of the bugs this project has already fixed were the grid drawing
/// something nobody asked for. What is actually wanted is simple enough to draw: a
/// sticky header, rows of a fixed height on the same grid as that header, runs broken
/// by a date, figures right-aligned, and one pill at the end of each row.
///
/// Read only by design. Nothing here edits a delivery; the card behind a double click
/// is where a delivery is looked at.
/// </summary>
public sealed class DeliveryList : Control {
    /// <summary>One column of the list: what it is called, how wide, and how a row
    /// answers it.</summary>
    private sealed class Column {
        public string Label = "";
        public float Width;
        public bool Figure;
        public string Sort = "";
        public Func<DeliveryRow, string> Read = _ => "";
    }

    private readonly List<Column> _columns = new();
    private List<DeliveryRow> _rows = new();

    /// <summary>The rows with their group headings folded in, which is what the list
    /// actually draws: an entry is either a heading or a delivery.</summary>
    private readonly List<(DeliveryRow? Row, string Title, string Summary)> _entries = new();

    private int _top;
    private int _hover = -1;
    private int _hoverColumn = -1;

    public const int RowHeight = 42;
    public const int HeadHeight = 30;
    private const int GroupHeight = 30;
    private const int Gutter = 26;

    /// <summary>Which column the list is ordered by, and which way.</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public string SortedBy { get; private set; } = nameof(DeliveryRow.Datum);

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool Descending { get; private set; } = true;

    public event Action<DeliveryRow>? Opened;
    public event Action? SortChanged;

    /// <summary>What a row's date and its group summary say. Set by the page, since the
    /// units and the wording belong to it rather than to a list control.</summary>
    public Func<DeliveryRow, string> Day = r => r.Datum.ToString("dd.MM.yy");
    public Func<IReadOnlyList<DeliveryRow>, string> DaySummary = _ => "";
    public Func<DeliveryRow, (string Word, Color Hue)> Outcome = _ => ("", Look.Muted);
    public Func<DeliveryRow, Color> Mark = _ => Look.Whole;
    public string EmptyText = "";

    public DeliveryList() {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
               | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = Look.Window;
        Cursor = Cursors.Hand;
    }

    /// <summary>Names the columns. Called once by the page, and again when the language
    /// changes, since the labels are its words.</summary>
    public void Describe(params (string Label, float Width, bool Figure, string Sort, Func<DeliveryRow, string> Read)[] columns) {
        _columns.Clear();
        foreach (var (label, width, figure, sort, read) in columns) {
            _columns.Add(new Column { Label = label, Width = width, Figure = figure, Sort = sort, Read = read });
        }
        Invalidate();
    }

    public void Show(IEnumerable<DeliveryRow> rows) {
        _rows = rows.ToList();
        Regroup();
        _top = Math.Min(_top, Math.Max(0, Height(_entries.Count) - ClientSize.Height));
        Invalidate();
    }

    /// <summary>Breaks the list into runs by day. Only when it is ordered by date: a
    /// list sorted by distance has no days in it, only rows, and a heading over them
    /// would be a lie about the order.</summary>
    private void Regroup() {
        _entries.Clear();
        if (SortedBy != nameof(DeliveryRow.Datum)) {
            foreach (var row in _rows) _entries.Add((row, "", ""));
            return;
        }
        DateTime? day = null;
        for (var i = 0; i < _rows.Count; i++) {
            var row = _rows[i];
            if (day is null || row.Datum.Date != day) {
                day = row.Datum.Date;
                var run = _rows.Where(r => r.Datum.Date == day).ToList();
                _entries.Add((null, Day(row), DaySummary(run)));
            }
            _entries.Add((row, "", ""));
        }
    }

    private int Height(int entries) {
        var tall = 0;
        for (var i = 0; i < entries && i < _entries.Count; i++) tall += _entries[i].Row is null ? GroupHeight : RowHeight;
        return tall;
    }

    /// <summary>What the scrollbar keeps for itself, so no row is drawn under it.</summary>
    private int Bar => Height(_entries.Count) > ClientSize.Height - HeadHeight ? 14 : 4;

    private float Scale => _columns.Sum(c => c.Width) is var total && total > 0
        ? (ClientSize.Width - Gutter - Bar - 8) / total : 1f;

    protected override void OnMouseWheel(MouseEventArgs e) {
        base.OnMouseWheel(e);
        // A little further than the last row, so the end of the list is not pressed
        // against the bottom edge of the panel.
        var most = Math.Max(0, Height(_entries.Count) - (ClientSize.Height - HeadHeight) + 8);
        _top = Math.Clamp(_top - e.Delta, 0, most);
        Invalidate();
    }

    protected override void OnMouseMove(MouseEventArgs e) {
        base.OnMouseMove(e);
        var was = _hover;
        var wasColumn = _hoverColumn;
        _hover = EntryAt(e.Y);
        _hoverColumn = e.Y < HeadHeight ? ColumnAt(e.X) : -1;
        if (was != _hover || wasColumn != _hoverColumn) Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e) {
        base.OnMouseLeave(e);
        _hover = -1;
        _hoverColumn = -1;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e) {
        base.OnMouseDown(e);
        Focus();
        if (e.Y >= HeadHeight) return;

        var hit = ColumnAt(e.X);
        if (hit < 0 || _columns[hit].Sort.Length == 0) return;
        if (SortedBy == _columns[hit].Sort) Descending = !Descending;
        else {
            SortedBy = _columns[hit].Sort;
            Descending = true;
        }
        SortChanged?.Invoke();
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e) {
        base.OnMouseDoubleClick(e);
        var index = EntryAt(e.Y);
        if (index >= 0 && _entries[index].Row is { } row) Opened?.Invoke(row);
    }

    private int ColumnAt(int x) {
        var at = (float)Gutter;
        var scale = Scale;
        for (var i = 0; i < _columns.Count; i++) {
            var wide = _columns[i].Width * scale;
            if (x >= at && x < at + wide) return i;
            at += wide;
        }
        return -1;
    }

    private int EntryAt(int y) {
        if (y < HeadHeight) return -1;
        var at = HeadHeight - _top;
        for (var i = 0; i < _entries.Count; i++) {
            var tall = _entries[i].Row is null ? GroupHeight : RowHeight;
            if (y >= at && y < at + tall) return i;
            at += tall;
        }
        return -1;
    }

    protected override void OnPaint(PaintEventArgs e) {
        var g = e.Graphics;
        g.Clear(Look.Window);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        if (_entries.Count == 0) {
            Look.Text(g, EmptyText, Look.Body, Look.Faint, Gutter, HeadHeight + 24);
            PaintHead(g);
            return;
        }

        var scale = Scale;
        var y = (float)HeadHeight - _top;

        foreach (var (row, title, summary) in _entries) {
            var tall = row is null ? GroupHeight : RowHeight;
            if (y + tall > HeadHeight && y < ClientSize.Height) {
                if (row is null) Look.GroupHeading(g, new RectangleF(Gutter, y, ClientSize.Width - Gutter - Bar - 6, tall), title, summary);
                else PaintRow(g, row, y, scale);
            }
            y += tall;
        }

        PaintHead(g);
        PaintScrollbar(g);
    }

    /// <summary>The header the rows are measured against: chrome tone, small capitals,
    /// and the same column positions the rows use, so nothing can drift.</summary>
    private void PaintHead(Graphics g) {
        using var back = new SolidBrush(Look.Chrome);
        g.FillRectangle(back, 0, 0, ClientSize.Width, HeadHeight);
        using var rule = new Pen(Look.Hairline);
        g.DrawLine(rule, 0, HeadHeight - 1, ClientSize.Width, HeadHeight - 1);

        var at = (float)Gutter;
        var scale = Scale;
        for (var i = 0; i < _columns.Count; i++) {
            var column = _columns[i];
            var wide = column.Width * scale;
            var sorted = column.Sort.Length > 0 && column.Sort == SortedBy;
            var ink = sorted ? Look.Accent : i == _hoverColumn && column.Sort.Length > 0 ? Look.Secondary : Look.Dim;
            var label = column.Label.ToUpperInvariant();

            if (column.Figure) {
                var wideLabel = Look.TrackedWidth(g, label, Look.Label);
                var mark = sorted ? 9 : 0;
                Look.Tracked(g, label, Look.Label, ink, at + wide - wideLabel - 12 - mark, 9);
                if (sorted) Arrow(g, new PointF(at + wide - 12, 15), ink);
            } else {
                var wideLabel = Look.Tracked(g, label, Look.Label, ink, at, 9);
                if (sorted) Arrow(g, new PointF(at + wideLabel + 6, 15), ink);
            }
            at += wide;
        }
    }

    private void Arrow(Graphics g, PointF at, Color ink) {
        using var brush = new SolidBrush(ink);
        var up = !Descending;
        g.FillPolygon(brush, new[] {
            new PointF(at.X - 3.5f, at.Y + (up ? 2 : -2)),
            new PointF(at.X + 3.5f, at.Y + (up ? 2 : -2)),
            new PointF(at.X, at.Y + (up ? -3 : 3)),
        });
    }

    private void PaintRow(Graphics g, DeliveryRow row, float y, float scale) {
        var index = _entries.FindIndex(entry => ReferenceEquals(entry.Row, row));
        if (index == _hover) {
            using var hover = new SolidBrush(Look.RowHover);
            g.FillRectangle(hover, 0, y, ClientSize.Width, RowHeight);
        }
        using (var rule = new Pen(Look.Hairline)) {
            g.DrawLine(rule, Gutter, y + RowHeight - 1, ClientSize.Width - Bar - 6, y + RowHeight - 1);
        }

        // The mark in the gutter says the same thing the pill at the other end says,
        // so nothing on the row is carried by colour alone.
        Look.Dot(g, new PointF(Gutter / 2f + 3, y + RowHeight / 2f), Mark(row), 7);
        if (row.Special) {
            using var stripes = new Pen(Look.Accent, 1.4f);
            for (var i = 0; i < 3; i++) {
                var x = Gutter / 2f - 3 + i * 3;
                g.DrawLine(stripes, x, y + RowHeight / 2f + 9, x + 4, y + RowHeight / 2f + 3);
            }
        }

        var at = (float)Gutter;
        var middle = y + (RowHeight - Look.Body.Height) / 2f;
        for (var i = 0; i < _columns.Count; i++) {
            var column = _columns[i];
            var wide = column.Width * scale;
            var text = column.Read(row);

            if (column.Sort == nameof(DeliveryRow.Stav)) {
                var (word, hue) = Outcome(row);
                if (word.Length > 0) {
                    var size = Look.PillSize(g, word);
                    Look.Pill(g, new PointF(at + wide - size.Width - 12, y + (RowHeight - size.Height) / 2), word, hue);
                }
            } else if (column.Figure) {
                Look.TextRight(g, text, Look.Body, Look.Secondary, at + wide - 12, middle);
            } else {
                var ink = i == 0 ? Look.Muted : Look.Secondary;
                Look.Text(g, Look.Clip(g, text, Look.Body, wide - 14), Look.Body, ink, at, middle);
            }
            at += wide;
        }
    }

    /// <summary>A hairline of a scrollbar down the right, drawn only while there is
    /// more list than window.</summary>
    private void PaintScrollbar(Graphics g) {
        var whole = Height(_entries.Count);
        var room = ClientSize.Height - HeadHeight;
        if (whole <= room) return;

        var track = new RectangleF(ClientSize.Width - 6, HeadHeight + 2, 4, room - 4);
        Look.FillRounded(g, track, 2, Look.Hairline);
        var tall = Math.Max(30f, track.Height * room / whole);
        var span = track.Height - tall;
        var at = track.Y + (whole - room <= 0 ? 0 : span * _top / (whole - room));
        Look.FillRounded(g, new RectangleF(track.X, at, track.Width, tall), 2, Look.Control);
    }
}
