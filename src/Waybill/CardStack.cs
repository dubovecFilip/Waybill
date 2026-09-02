using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace Waybill;

/// <summary>
/// A stack of panels, one per thing, drawn rather than bound.
///
/// Two pages are the same shape: a truck and a sitting at the wheel are both one line
/// of identity on the left, one thing worth seeing as a proportion in the middle, and
/// a handful of figures pushed to the right. Written twice they drifted apart; written
/// once they cannot.
///
/// The newest, or the most used, sits in the raised tone, which is the only difference
/// between one card and the next.
/// </summary>
public sealed class CardStack : Control {
    /// <summary>A figure at the right end of a card: what it is, what it says, and in
    /// what ink.</summary>
    public sealed class Figure {
        public string Label = "";
        public string Value = "";
        public Color Ink = Look.Ink;
    }

    /// <summary>A block of the bar across the middle of a card.</summary>
    public sealed class Block {
        public float Part;
        public Color Hue = Look.Accent;
    }

    public sealed class Card {
        public string Title = "";
        public string Tag = "";
        public string Under = "";
        public string Middle = "";
        public float? Share;
        public string ShareLabel = "";
        public List<Block> Bar = new();
        public List<Figure> Figures = new();
        public object? Behind;
    }

    private List<Card> _cards = new();
    private int _top;
    private int _hover = -1;

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public int CardHeight { get; set; } = 74;
    public string EmptyText = "";
    public event Action<object?>? Opened;

    public CardStack() {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
               | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = Look.Window;
    }

    public void Show(IEnumerable<Card> cards) {
        _cards = cards.ToList();
        _top = 0;
        Invalidate();
    }

    private int Whole => _cards.Count * (CardHeight + 10);

    protected override void OnMouseWheel(MouseEventArgs e) {
        base.OnMouseWheel(e);
        _top = Math.Clamp(_top - e.Delta, 0, Math.Max(0, Whole - ClientSize.Height));
        Invalidate();
    }

    protected override void OnMouseMove(MouseEventArgs e) {
        base.OnMouseMove(e);
        var was = _hover;
        _hover = (e.Y + _top) / (CardHeight + 10);
        if (_hover >= _cards.Count) _hover = -1;
        if (was != _hover) Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e) {
        base.OnMouseLeave(e);
        _hover = -1;
        Invalidate();
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e) {
        base.OnMouseDoubleClick(e);
        var index = (e.Y + _top) / (CardHeight + 10);
        if (index >= 0 && index < _cards.Count) Opened?.Invoke(_cards[index].Behind);
    }

    protected override void OnPaint(PaintEventArgs e) {
        var g = e.Graphics;
        g.Clear(Look.Window);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        if (_cards.Count == 0) {
            Look.Text(g, EmptyText, Look.Body, Look.Faint, 2, 8);
            return;
        }

        for (var i = 0; i < _cards.Count; i++) {
            var y = i * (CardHeight + 10) - _top;
            if (y + CardHeight < 0 || y > ClientSize.Height) continue;
            PaintCard(g, _cards[i], new RectangleF(0, y, ClientSize.Width - 2, CardHeight), i == 0, i == _hover);
        }

        if (Whole <= ClientSize.Height) return;
        var track = new RectangleF(ClientSize.Width - 5, 2, 4, ClientSize.Height - 4);
        Look.FillRounded(g, track, 2, Look.Hairline);
        var tall = Math.Max(30f, track.Height * ClientSize.Height / Whole);
        var at = track.Y + (track.Height - tall) * _top / Math.Max(1, Whole - ClientSize.Height);
        Look.FillRounded(g, new RectangleF(track.X, at, track.Width, tall), 2, Look.Control);
    }

    private void PaintCard(Graphics g, Card card, RectangleF box, bool first, bool hover) {
        var fill = first ? Look.Raised : hover ? Look.RowHover : Look.Panel;
        Look.Surface(g, box, fill, Look.Hairline);

        // Left: what this card is, and one dim line of context under it. A card with
        // nothing at its right end gives the whole width to the name, which is what a
        // narrow list of routes beside a map is.
        var x = box.X + 16;
        var busy = card.Figures.Count > 0 || card.Bar.Count > 0 || card.Share is not null;
        var room = busy ? box.Width * 0.3f : box.Width - 30;
        var title = Look.Semi(14);
        var top = box.Height > 60 ? 14 : 6;

        Look.Text(g, Look.Clip(g, card.Title, title, room), title, Look.Ink, x, box.Y + top);
        var wide = Look.Measure(g, card.Title, title).Width;
        if (card.Tag.Length > 0) {
            Look.Pill(g, new PointF(x + wide + 10, box.Y + top - 1), card.Tag, Look.Accent);
        }
        if (card.Under.Length > 0) {
            Look.Text(g, Look.Clip(g, card.Under, Look.Caption, room), Look.Caption, Look.Dim, x, box.Y + top + 22);
        }

        // Right: the figures, each under its own label, right-aligned so a column of
        // cards reads down as well as across.
        var right = box.Right - 18;
        foreach (var figure in Enumerable.Reverse(card.Figures)) {
            var wideFigure = Math.Max(Look.TrackedWidth(g, figure.Label.ToUpperInvariant(), Look.Label),
                                      Look.Measure(g, figure.Value, Look.Strong).Width);
            var at = right - wideFigure;
            Look.Tracked(g, figure.Label.ToUpperInvariant(), Look.Label, Look.Dim, at, box.Y + 16);
            Look.TextRight(g, figure.Value, Look.Strong, figure.Ink, right, box.Y + 34);
            right = at - 28;
        }

        // Middle: a proportion, or a line of names with a bar of blocks under it.
        var middleLeft = box.X + box.Width * 0.34f;
        var middleRight = right - 20;
        if (middleRight - middleLeft < 60) return;

        if (card.Middle.Length > 0) {
            Look.Text(g, Look.Clip(g, card.Middle, Look.Body, middleRight - middleLeft), Look.Body,
                      Look.Secondary, middleLeft, box.Y + 16);
        }
        if (card.Bar.Count > 0) {
            var bar = new RectangleF(middleLeft, box.Y + 42, middleRight - middleLeft, 5);
            var at = bar.X;
            foreach (var block in card.Bar) {
                var wideBlock = Math.Max(0, block.Part) * bar.Width;
                if (wideBlock <= 0) continue;
                Look.FillRounded(g, new RectangleF(at, bar.Y, Math.Max(2, wideBlock - 2), bar.Height), 2.5f, block.Hue);
                at += wideBlock;
            }
        } else if (card.Share is { } share) {
            var track = new RectangleF(middleLeft, box.Y + 40, middleRight - middleLeft, 5);
            Look.Track(g, track, share);
            if (card.ShareLabel.Length > 0) {
                Look.Text(g, card.ShareLabel, Look.Caption, Look.Dim, middleLeft, box.Y + 18);
            }
        }
    }
}
