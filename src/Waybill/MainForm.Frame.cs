using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace Waybill;

/// <summary>
/// The frame the pages sit in: the bar across the top and the column down the left.
///
/// Both are drawn rather than assembled out of controls. A menu strip, a row of
/// buttons and a label each bring their own idea of padding, focus rectangles and
/// system colours, and fighting three of those is more work than painting the eight
/// shapes this frame actually is.
/// </summary>
public partial class MainForm {
    private Panel? _titleBar;
    private Label? _statusChip;

    /// <summary>What the chip along the top says, and in which hue.</summary>
    private string _chipText = "";
    private Color _chipHue = Look.Faint;

    // ---------- the bar across the top ----------

    private Panel BuildTitleBar() {
        var bar = new Panel { Dock = DockStyle.Top, Height = Look.TitleBarHeight, BackColor = Look.Chrome };
        _titleBar = bar;

        var menu = BuildMenu();
        menu.Dock = DockStyle.None;
        menu.BackColor = Look.Chrome;
        menu.Padding = new Padding(0);
        menu.Location = new Point(150, (Look.TitleBarHeight - 26) / 2);
        menu.AutoSize = true;
        menu.Font = Look.Body;
        foreach (ToolStripItem item in menu.Items) {
            item.ForeColor = Look.Muted;
            item.Padding = new Padding(6, 3, 6, 3);
        }
        bar.Controls.Add(menu);
        MainMenuStrip = menu;

        var chip = new Label {
            AutoSize = false, Height = 26, Width = 200, ForeColor = Look.Muted,
            BackColor = Look.Chrome, Font = Look.Small, TextAlign = ContentAlignment.MiddleRight,
        };
        chip.Paint += (_, e) => PaintChip(e.Graphics, chip);
        bar.Controls.Add(chip);
        _statusChip = chip;

        bar.Paint += (_, e) => PaintTitleBar(e.Graphics, bar);
        bar.Resize += (_, _) => FitChip();
        FitChip();
        return bar;
    }

    /// <summary>The icon, the name and the version. The menu words draw themselves.</summary>
    private void PaintTitleBar(Graphics g, Panel bar) {
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        using var hairline = new Pen(Look.Hairline);
        g.DrawLine(hairline, 0, bar.Height - 1, bar.Width, bar.Height - 1);

        if (Icon is { } mark) {
            using var picture = mark.ToBitmap();
            g.DrawImage(picture, new Rectangle(16, (bar.Height - 22) / 2, 22, 22));
        }
        Look.Text(g, "Waybill", Look.Semi(14), Look.Ink, 46, (bar.Height - 18) / 2f);
        Look.Text(g, AppVersion.TrimStart('v'), Look.Caption, Look.Faint, 108, (bar.Height - 14) / 2f + 1);
    }

    /// <summary>
    /// The label is cut to the pill rather than the pill drawn inside a label that is
    /// wider than it. Drawn the other way round, the shape ran off both ends of its own
    /// control and arrived with two flat sides.
    /// </summary>
    private void FitChip() {
        if (_statusChip is not { } chip || _titleBar is not { } bar) return;
        using var g = chip.CreateGraphics();
        var wide = (int)Math.Ceiling(Look.Measure(g, _chipText, Look.Small).Width) + 44;
        // Never past the menu: a long game name on a narrow window gives up its own
        // words before it gives up the shape.
        chip.Width = Math.Max(60, Math.Min(wide, bar.Width - 240));
        chip.Location = new Point(bar.Width - chip.Width - 16, (bar.Height - chip.Height) / 2);
        chip.Invalidate();
    }

    /// <summary>
    /// The chip at the right end: a dot with a soft ring around it and a line of text,
    /// tinted when a game is attached and faint when there is none.
    /// </summary>
    private void PaintChip(Graphics g, Label chip) {
        g.Clear(Look.Chrome);
        if (_chipText.Length == 0) return;

        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        var text = Look.Measure(g, _chipText, Look.Small);
        var box = new RectangleF(0.5f, 0.5f, chip.Width - 1, chip.Height - 1);
        Look.FillRounded(g, box, box.Height / 2, Look.Tint(_chipHue, 10));
        Look.DrawRounded(g, box, box.Height / 2, Look.TintEdge(_chipHue, 22));

        var middle = box.Y + box.Height / 2;
        using var soft = new SolidBrush(Look.Tint(_chipHue, 30));
        g.FillEllipse(soft, box.X + 11, middle - 6.5f, 13, 13);
        Look.Dot(g, new PointF(box.X + 17.5f, middle), _chipHue);
        Look.Text(g, Look.Clip(g, _chipText, Look.Small, box.Width - 42), Look.Small, _chipHue,
                  box.X + 30, middle - text.Height / 2);
    }

    /// <summary>Says which game is attached, or that none is. Called from the live page
    /// refresh, which is the only thing that knows.</summary>
    private void SayAttached(string game, bool recording) {
        var was = _chipText;
        _chipText = game.Length == 0
            ? Strings.T("live.waitingGame")
            : recording ? $"{game} · {Strings.T("chip.recording")}" : game;
        _chipHue = game.Length == 0 ? Look.Faint : Look.Whole;
        // The shape follows the words: "Waiting for the game" and "ATS · recording" are
        // not the same width, and a pill sized for one of them cuts the other.
        if (_chipText != was) FitChip();
    }

    // ---------- the column down the left ----------

    private sealed class NavRow {
        public string Page = "";
        public string Label = "";
        public string Glyph = "";
        public Func<string> Count = () => "";
    }

    private readonly List<NavRow> _navRows = new();

    /// <summary>
    /// The pages, in two groups: what is happening now, and what has already happened.
    ///
    /// The split is the whole point of the column. Driving is two pages a driver looks
    /// at with the game running; the book is five they look at afterwards, and reading
    /// them as one list of seven made every one of them feel equally likely.
    /// </summary>
    private Panel BuildSidebarColumn() {
        var bar = new Panel { Dock = DockStyle.Left, Width = Look.SidebarWidth, BackColor = Look.Chrome };

        _navRows.Clear();
        _navRows.Add(new NavRow { Page = "live", Label = Strings.T("tab.live"), Glyph = "wheel",
                                  Count = () => _engine?.ActiveJob is not null ? Strings.T("nav.live") : "" });
        _navRows.Add(new NavRow { Page = "map", Label = Strings.T("tab.map"), Glyph = "map" });
        _navRows.Add(new NavRow { Page = "deliveries", Label = Strings.T("tab.deliveries"), Glyph = "list",
                                  Count = () => _rows.Count.ToString() });
        _navRows.Add(new NavRow { Page = "sessions", Label = Strings.T("tab.sessions"), Glyph = "clock",
                                  Count = () => _navSessions > 0 ? _navSessions.ToString() : "" });
        _navRows.Add(new NavRow { Page = "trucks", Label = Strings.T("tab.trucks"), Glyph = "truck",
                                  Count = () => _navTrucks > 0 ? _navTrucks.ToString() : "" });
        _navRows.Add(new NavRow { Page = "awards", Label = Strings.T("tab.awards"), Glyph = "star",
                                  Count = () => _profile.Unique > 0 ? _profile.Unique.ToString() : "" });
        _navRows.Add(new NavRow { Page = "stats", Label = Strings.T("tab.stats"), Glyph = "chart" });

        var level = BuildLevelPanel();
        bar.Controls.Add(level);

        var nav = new Panel { Dock = DockStyle.Fill, BackColor = Look.Chrome };
        nav.Paint += (_, e) => PaintNav(e.Graphics, nav);
        nav.MouseMove += (_, e) => {
            var over = RowAt(nav, e.Y);
            if (over == _navHover) return;
            _navHover = over;
            nav.Invalidate();
        };
        nav.MouseLeave += (_, _) => { _navHover = -1; nav.Invalidate(); };
        nav.MouseClick += (_, e) => {
            var hit = RowAt(nav, e.Y);
            if (hit >= 0) ShowPage(_navRows[hit].Page);
        };
        nav.Cursor = Cursors.Hand;
        bar.Controls.Add(nav);
        _nav = nav;

        var edge = new Panel { Dock = DockStyle.Right, Width = 1, BackColor = Look.Hairline };
        bar.Controls.Add(edge);
        return bar;
    }

    private Panel? _nav;
    private int _navHover = -1;

    /// <summary>What the counts down the column say. Filled by the pages themselves as
    /// they load, since the column has no business reading the database.</summary>
    private int _navSessions;
    private int _navTrucks;

    /// <summary>Where each row sits, with the two group headings taking a line of their
    /// own above the first row of their group.</summary>
    private IEnumerable<(int Index, RectangleF Box, string Heading)> NavLayout(Control host) {
        var y = 12f;
        for (var i = 0; i < _navRows.Count; i++) {
            var heading = i == 0 ? Strings.T("nav.driving") : i == 2 ? Strings.T("nav.book") : "";
            if (heading.Length > 0) y += i == 0 ? 8 : 20;
            yield return (i, new RectangleF(0, y, host.Width, Look.SidebarRow), heading);
            y += Look.SidebarRow;
        }
    }

    private int RowAt(Control host, int y) {
        foreach (var (index, box, _) in NavLayout(host)) {
            if (y >= box.Top && y < box.Bottom) return index;
        }
        return -1;
    }

    private void PaintNav(Graphics g, Panel nav) {
        g.Clear(Look.Chrome);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        foreach (var (index, box, heading) in NavLayout(nav)) {
            var row = _navRows[index];
            var picked = row.Page == _page;

            if (heading.Length > 0) {
                Look.Tracked(g, heading.ToUpperInvariant(), Look.Label, Look.Faint, 16, box.Y - 16);
            }

            if (picked) {
                using var wash = new SolidBrush(Look.Tint(Look.Accent, 8));
                g.FillRectangle(wash, box);
                using var mark = new SolidBrush(Look.Accent);
                g.FillRectangle(mark, box.X, box.Y, 2, box.Height);
            } else if (index == _navHover) {
                using var hover = new SolidBrush(Look.RowHover);
                g.FillRectangle(hover, box);
            }

            var ink = picked ? Look.Accent : Look.Secondary;
            NavGlyph(g, row.Glyph, new PointF(20, box.Y + box.Height / 2), ink);
            Look.Text(g, row.Label, picked ? Look.Semi(13.5f) : Look.Plain(13.5f), ink, 44, box.Y + 10);

            var count = row.Count();
            if (count.Length > 0) {
                Look.TextRight(g, count, Look.Caption, picked ? Look.Accent : Look.Faint, nav.Width - 16, box.Y + 12);
            }
        }
    }

    /// <summary>
    /// Sixteen pixel line drawings, one per page.
    ///
    /// Drawn rather than set in a font: an icon font is another file to ship and another
    /// thing to be missing on somebody's machine, and these are seven shapes made of
    /// lines and circles.
    /// </summary>
    private static void NavGlyph(Graphics g, string which, PointF at, Color ink) {
        using var pen = new Pen(ink, 1.4f) { LineJoin = System.Drawing.Drawing2D.LineJoin.Round };
        var x = at.X - 8;
        var y = at.Y - 8;

        switch (which) {
            case "wheel":
                g.DrawEllipse(pen, x + 1, y + 1, 14, 14);
                g.DrawEllipse(pen, x + 5.5f, y + 5.5f, 5, 5);
                g.DrawLine(pen, x + 8, y + 1, x + 8, y + 5.5f);
                g.DrawLine(pen, x + 1.6f, y + 11, x + 5.6f, y + 9);
                g.DrawLine(pen, x + 14.4f, y + 11, x + 10.4f, y + 9);
                break;
            case "map":
                g.DrawLines(pen, new[] { new PointF(x + 1, y + 4), new PointF(x + 5.5f, y + 2),
                                         new PointF(x + 10.5f, y + 5), new PointF(x + 15, y + 3),
                                         new PointF(x + 15, y + 13), new PointF(x + 10.5f, y + 15),
                                         new PointF(x + 5.5f, y + 12), new PointF(x + 1, y + 14) });
                g.DrawLine(pen, x + 5.5f, y + 2, x + 5.5f, y + 12);
                g.DrawLine(pen, x + 10.5f, y + 5, x + 10.5f, y + 15);
                break;
            case "list":
                for (var i = 0; i < 3; i++) g.DrawLine(pen, x + 2, y + 4 + i * 4, x + 14, y + 4 + i * 4);
                break;
            case "clock":
                g.DrawEllipse(pen, x + 1, y + 1, 14, 14);
                g.DrawLine(pen, x + 8, y + 4.5f, x + 8, y + 8);
                g.DrawLine(pen, x + 8, y + 8, x + 11, y + 10);
                break;
            case "truck":
                g.DrawRectangle(pen, x + 1, y + 4, 8, 6);
                g.DrawLines(pen, new[] { new PointF(x + 9, y + 6), new PointF(x + 12.5f, y + 6),
                                         new PointF(x + 15, y + 8.5f), new PointF(x + 15, y + 10),
                                         new PointF(x + 9, y + 10) });
                g.DrawEllipse(pen, x + 3, y + 10, 3.4f, 3.4f);
                g.DrawEllipse(pen, x + 10, y + 10, 3.4f, 3.4f);
                break;
            case "star":
                var points = new PointF[10];
                for (var i = 0; i < 10; i++) {
                    var radius = i % 2 == 0 ? 7.4f : 3.2f;
                    var angle = -Math.PI / 2 + i * Math.PI / 5;
                    points[i] = new PointF(at.X + (float)(Math.Cos(angle) * radius),
                                           at.Y + (float)(Math.Sin(angle) * radius));
                }
                g.DrawPolygon(pen, points);
                break;
            case "chart":
                g.DrawLine(pen, x + 2, y + 14, x + 14, y + 14);
                using (var bars = new SolidBrush(ink)) {
                    g.FillRectangle(bars, x + 3, y + 8, 2.6f, 5);
                    g.FillRectangle(bars, x + 7, y + 4, 2.6f, 9);
                    g.FillRectangle(bars, x + 11, y + 6, 2.6f, 7);
                }
                break;
        }
    }

    // ---------- the panel at the foot of the column ----------

    private Panel? _levelPanel;

    private Panel BuildLevelPanel() {
        var host = new Panel { Dock = DockStyle.Bottom, Height = 88, BackColor = Look.Chrome, Padding = new Padding(12) };
        var panel = new Panel { Dock = DockStyle.Fill, BackColor = Look.Chrome };
        panel.Paint += (_, e) => PaintLevel(e.Graphics, panel);
        host.Controls.Add(panel);
        _levelPanel = panel;
        return host;
    }

    private void PaintLevel(Graphics g, Panel panel) {
        g.Clear(Look.Chrome);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        var box = new RectangleF(0, 0, panel.Width, panel.Height);
        Look.Surface(g, box, Look.Panel, Look.Hairline);

        var needs = Math.Max(1, _profile.LevelTo - _profile.LevelFrom);
        var into = Math.Clamp(_profile.Xp - _profile.LevelFrom, 0, needs);

        Look.Text(g, $"{Strings.T("award.level")} {_profile.Level}", Look.Semi(13), Look.Accent, 12, 9);
        Look.TextRight(g, $"{into:N0} / {needs:N0} XP", Look.Caption, Look.Dim, panel.Width - 12, 11);

        Look.Track(g, new RectangleF(12, 30, panel.Width - 24, 4), into / (float)needs);

        Look.Text(g, $"{_profile.Unique} / {_profile.Possible} {Strings.T("nav.awardsFound")}",
                  Look.Caption, Look.Faint, 12, panel.Height - 20);
    }

    /// <summary>
    /// Puts every control on the type scale.
    ///
    /// The pages were written against point sizes, which are right at 96 dpi and wrong
    /// on every other screen, and against three weights of Segoe UI chosen a page at a
    /// time. Rather than edit two hundred call sites, the tree is walked once when the
    /// layout is built and each font is mapped to the nearest step of the scale. New
    /// work uses Look directly; this is what carries the pages that came before it.
    /// </summary>
    private static void Retype(Control root) {
        foreach (Control child in root.Controls) {
            if (child is RouteView or HeightView) continue;
            if (child.Font is { } was && !ReferenceEquals(was, Look.Body)) {
                var bold = was.Bold;
                var points = was.Unit == GraphicsUnit.Point ? was.SizeInPoints : was.Size * 0.75f;
                child.Font = points switch {
                    >= 15f => Look.CardTitle,
                    >= 12f => bold ? Look.PageHeading : Look.Semi(16),
                    >= 10.5f => bold ? Look.Semi(14) : Look.BodyLarge,
                    >= 9.5f => bold ? Look.Strong : Look.Body,
                    >= 8.8f => bold ? Look.StrongSmall : Look.Body,
                    >= 8f => bold ? Look.CaptionSemi : Look.Small,
                    _ => Look.Caption,
                };
            }
            Retype(child);
        }
    }

    /// <summary>Redraws the frame's own figures. Called whenever the history behind
    /// them changes, since none of them is a control that could notice on its own.</summary>
    private void RefreshFrame() {
        _nav?.Invalidate();
        _levelPanel?.Invalidate();
        _statusChip?.Invalidate();
    }
}
