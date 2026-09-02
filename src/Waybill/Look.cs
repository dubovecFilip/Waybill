using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace Waybill;

/// <summary>
/// Everything the window is drawn with: the surfaces, the ink, the type, the measures
/// and the handful of shapes every page is built from.
///
/// One place rather than thirty, because a palette spread through a window is a palette
/// that drifts. Nothing here knows what a delivery is; it knows what a figure looks
/// like, what a row is worth in pixels, and which grey means "this is a label".
///
/// The register is an instrument panel: flat surfaces separated by tone rather than
/// shadow, hairlines rather than borders, one amber accent, and a small capital label
/// over every figure. No light variant, no second accent, no gradient behind content.
/// </summary>
public static class Look {
    // ---------- surfaces ----------

    /// <summary>The page area behind the panels.</summary>
    public static readonly Color Window = Hex(0x0F1114);
    /// <summary>The title bar, the sidebar, a page header.</summary>
    public static readonly Color Chrome = Hex(0x12151A);
    /// <summary>A card, a figure tile, a list row.</summary>
    public static readonly Color Panel = Hex(0x14181D);
    /// <summary>The live hero, the top row of a list: one step up from a panel.</summary>
    public static readonly Color Raised = Hex(0x191D23);
    /// <summary>Sunk into a panel: the log, an input.</summary>
    public static readonly Color Well = Hex(0x101317);
    /// <summary>A division inside a panel.</summary>
    public static readonly Color Hairline = Hex(0x1E222A);
    /// <summary>The edge of a panel.</summary>
    public static readonly Color Border = Hex(0x23282F);
    /// <summary>An input, a segmented button, a chip.</summary>
    public static readonly Color Control = Hex(0x2A303A);
    /// <summary>The same, under the pointer.</summary>
    public static readonly Color ControlHover = Hex(0x39414D);
    /// <summary>A row under the pointer: one step of tone and nothing else.</summary>
    public static readonly Color RowHover = Hex(0x181C22);

    // ---------- ink ----------

    /// <summary>Headings and figures.</summary>
    public static readonly Color Ink = Hex(0xE8EBEF);
    /// <summary>Ordinary cell text.</summary>
    public static readonly Color Secondary = Hex(0xB7BFC9);
    /// <summary>Field names and captions.</summary>
    public static readonly Color Muted = Hex(0x8B94A0);
    /// <summary>Labels, units, the line under a figure.</summary>
    public static readonly Color Dim = Hex(0x6B7480);
    /// <summary>The version, a legend, anything inactive.</summary>
    public static readonly Color Faint = Hex(0x5C6470);

    // ---------- signals ----------

    /// <summary>Live, selected, primary, progress. At most three marks on a page.</summary>
    public static readonly Color Accent = Hex(0xE9A23B);
    /// <summary>The far end of a progress fill.</summary>
    public static readonly Color AccentDeep = Hex(0xB9711C);
    /// <summary>A figure that is undamaged, or in time.</summary>
    public static readonly Color Whole = Hex(0x5FBF8B);
    /// <summary>Money or cargo lost.</summary>
    public static readonly Color Lost = Hex(0xE2685F);
    /// <summary>Route ink, town roads, map marks.</summary>
    public static readonly Color Route = Hex(0x5EC2C8);
    /// <summary>An unpicked route on the map.</summary>
    public static readonly Color Slate = Hex(0x2E3A43);

    /// <summary>
    /// A signal laid over a panel as a wash rather than as paint.
    ///
    /// Seven to fourteen percent of the hue, which is enough to name a panel and not
    /// enough to shout. The border of the same wash sits at a quarter to a third, so
    /// the edge reads before the fill does.
    /// </summary>
    public static Color Tint(Color hue, int percent = 12) => Color.FromArgb(percent * 255 / 100, hue);

    public static Color TintEdge(Color hue, int percent = 28) => Color.FromArgb(percent * 255 / 100, hue);

    // ---------- type ----------

    /// <summary>
    /// The face, in order of preference.
    ///
    /// IBM Plex Sans where somebody has installed it, Segoe UI everywhere else, which
    /// is the fallback the specification names and the face Windows already draws every
    /// other window in. Both have tabular figures, which is the property that matters:
    /// a column of numbers has to line up and a changing figure must not change width.
    /// </summary>
    public static readonly string Face = Pick("IBM Plex Sans", "Segoe UI");

    /// <summary>The same face at 600, which Windows ships as a family of its own.</summary>
    public static readonly string FaceSemi = Pick("IBM Plex Sans SemiBold", "Segoe UI Semibold", Face);

    /// <summary>The log and file paths, and nothing else in the window.</summary>
    public static readonly string Mono = Pick("IBM Plex Mono", "Cascadia Mono", "Consolas");

    // Sizes are the specification's pixels, so the font is built in pixels rather than
    // points: a point size would be right at 96 dpi and wrong on every other screen.
    public static readonly Font CardTitle = Semi(26);
    public static readonly Font PageHeading = Semi(17);
    public static readonly Font FigureLarge = Semi(23);
    public static readonly Font Figure = Semi(19);
    public static readonly Font FigureSmall = Semi(15.5f);
    public static readonly Font Strong = Semi(13.5f);
    public static readonly Font StrongSmall = Semi(12.5f);
    public static readonly Font Body = Plain(13);
    public static readonly Font BodyLarge = Plain(14);
    public static readonly Font Small = Plain(12.5f);
    public static readonly Font Caption = Plain(11.5f);
    public static readonly Font CaptionSemi = Semi(11.5f);
    /// <summary>The name over every figure. Drawn in capitals with tracking, which is
    /// what <see cref="Tracked"/> is for.</summary>
    public static readonly Font Label = Semi(10.5f);
    public static readonly Font Mono11 = new(Mono, 11, FontStyle.Regular, GraphicsUnit.Pixel);
    public static readonly Font Mono12 = new(Mono, 12, FontStyle.Regular, GraphicsUnit.Pixel);

    public static Font Plain(float px) => new(Face, px, FontStyle.Regular, GraphicsUnit.Pixel);
    public static Font Semi(float px) => new(FaceSemi, px, FontStyle.Regular, GraphicsUnit.Pixel);

    // ---------- measure ----------

    /// <summary>A two pixel grid: everything in the window is one of these apart.</summary>
    public const int TitleBarHeight = 44;
    public const int SidebarWidth = 212;
    public const int SidebarRow = 38;
    public const int ListRow = 42;
    public const int InputHeight = 32;
    public const int PagePad = 18;
    public const int PanelPad = 14;
    public const int PanelGap = 14;
    public const int TrackHeight = 6;

    public const int RadiusChip = 5;
    public const int RadiusControl = 7;
    public const int RadiusPanel = 9;

    // ---------- drawing helpers ----------

    private static Color Hex(int rgb) => Color.FromArgb((rgb >> 16) & 255, (rgb >> 8) & 255, rgb & 255);

    private static string Pick(params string[] wanted) {
        foreach (var name in wanted) {
            if (Installed(name)) return name;
        }
        return wanted[^1];
    }

    /// <summary>Read on the first question rather than at type load: the fields above
    /// are initialised in the order they are declared, and asking a field further down
    /// the file is asking for null.</summary>
    private static HashSet<string>? _faces;

    private static HashSet<string> Families() {
        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try {
            using var installed = new InstalledFontCollection();
            foreach (var family in installed.Families) found.Add(family.Name);
        } catch { /* a window with no font list is not a window worth crashing */ }
        return found;
    }

    private static bool Installed(string name) => (_faces ??= Families()).Contains(name);

    /// <summary>A rounded rectangle, since GDI+ has no such shape of its own.</summary>
    public static GraphicsPath Rounded(RectangleF box, float radius) {
        var path = new GraphicsPath();
        var r = Math.Min(radius, Math.Min(box.Width, box.Height) / 2);
        if (r <= 0.5f) {
            path.AddRectangle(box);
            return path;
        }
        var d = r * 2;
        path.AddArc(box.Left, box.Top, d, d, 180, 90);
        path.AddArc(box.Right - d, box.Top, d, d, 270, 90);
        path.AddArc(box.Right - d, box.Bottom - d, d, d, 0, 90);
        path.AddArc(box.Left, box.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    public static void FillRounded(Graphics g, RectangleF box, float radius, Color fill) {
        using var path = Rounded(box, radius);
        using var brush = new SolidBrush(fill);
        g.FillPath(brush, path);
    }

    public static void DrawRounded(Graphics g, RectangleF box, float radius, Color edge, float width = 1f) {
        using var path = Rounded(box, radius);
        using var pen = new Pen(edge, width);
        g.DrawPath(pen, path);
    }

    /// <summary>
    /// A panel: a flat surface with an edge, and nothing else. No shadow, no gradient,
    /// no lift.
    ///
    /// The edge is drawn half a pixel inside the box it was given. A one pixel line
    /// centred on the boundary puts half of itself outside the control, and the half
    /// outside is the half that gets clipped: panels came out missing their right and
    /// bottom edges.
    /// </summary>
    public static void Surface(Graphics g, RectangleF box, Color fill, Color? edge = null, float radius = RadiusPanel) {
        FillRounded(g, box, radius, fill);
        if (edge is { } line) {
            DrawRounded(g, RectangleF.Inflate(box, -0.5f, -0.5f), radius, line);
        }
    }

    /// <summary>
    /// Text with the letters pushed apart, which GDI+ will not do on its own.
    ///
    /// Only for the small capital labels, which are short: every glyph is measured and
    /// placed by hand, so this is not something to run over a paragraph.
    /// </summary>
    public static float Tracked(Graphics g, string text, Font font, Color ink, float x, float y, float tracking = 1.1f) {
        using var brush = new SolidBrush(ink);
        var at = x;
        foreach (var letter in text) {
            // A space measures as nothing on its own under typographic measurement, so
            // it is given a width rather than asked for one; "THE BOOK" ran together
            // into one word without this.
            if (letter == ' ') {
                at += font.Size * 0.34f + tracking;
                continue;
            }
            var one = letter.ToString();
            g.DrawString(one, font, brush, at - 1f, y, StringFormat.GenericTypographic);
            at += g.MeasureString(one, font, PointF.Empty, StringFormat.GenericTypographic).Width - 2f + tracking;
        }
        return at - x;
    }

    /// <summary>How wide <see cref="Tracked"/> will draw, for laying out beside it.</summary>
    public static float TrackedWidth(Graphics g, string text, Font font, float tracking = 1.1f) {
        var wide = 0f;
        foreach (var letter in text) {
            wide += letter == ' '
                ? font.Size * 0.34f + tracking
                : g.MeasureString(letter.ToString(), font, PointF.Empty, StringFormat.GenericTypographic).Width - 2f + tracking;
        }
        return wide;
    }

    /// <summary>Plain text, measured and drawn the same way everywhere: no trailing
    /// space in the measurement, no grid fitting, so a figure sits where it was put.
    /// </summary>
    public static void Text(Graphics g, string text, Font font, Color ink, float x, float y) {
        if (string.IsNullOrEmpty(text)) return;
        using var brush = new SolidBrush(ink);
        g.DrawString(text, font, brush, x - 1f, y, StringFormat.GenericTypographic);
    }

    public static SizeF Measure(Graphics g, string text, Font font) =>
        string.IsNullOrEmpty(text) ? SizeF.Empty
            : g.MeasureString(text, font, PointF.Empty, StringFormat.GenericTypographic);

    /// <summary>Right aligned, which is where every figure in a column belongs.</summary>
    public static void TextRight(Graphics g, string text, Font font, Color ink, float right, float y) {
        if (string.IsNullOrEmpty(text)) return;
        Text(g, text, font, ink, right - Measure(g, text, font).Width, y);
    }

    /// <summary>Clipped with an ellipsis rather than wrapped, because a row is a fixed
    /// height and a wrapped cell would push the grid apart.</summary>
    public static string Clip(Graphics g, string text, Font font, float room) {
        if (string.IsNullOrEmpty(text) || Measure(g, text, font).Width <= room) return text;
        var cut = text;
        while (cut.Length > 1 && Measure(g, cut + "…", font).Width > room) cut = cut[..^1];
        return cut + "…";
    }

    // ---------- the drawn parts ----------

    /// <summary>
    /// A figure tile: a small capital label, a large tabular figure, one dim line, and
    /// optionally a three pixel track when the figure is a share.
    ///
    /// Never a figure on its own. A number with nothing over it is a number nobody can
    /// read twice the same way.
    /// </summary>
    public static void FigureTile(Graphics g, RectangleF box, string label, string figure, string under,
                                  Color? figureInk = null, float? share = null, Color? shareInk = null,
                                  Font? figureFont = null) {
        var ink = figureInk ?? Ink;
        Tracked(g, label.ToUpperInvariant(), Label, Dim, box.X, box.Y);
        Text(g, figure, figureFont ?? Figure, ink, box.X, box.Y + 16);
        if (under.Length > 0) Text(g, under, Caption, Dim, box.X, box.Y + 16 + (figureFont ?? Figure).Height + 3);

        if (share is not { } part) return;
        var track = new RectangleF(box.X, box.Bottom - 3, box.Width, 3);
        FillRounded(g, track, 1.5f, Hairline);
        var run = Math.Clamp(part, 0f, 1f) * track.Width;
        if (run > 0) FillRounded(g, new RectangleF(track.X, track.Y, run, 3), 1.5f, shareInk ?? Accent);
    }

    /// <summary>
    /// A status pill: one word on a wash of its own hue, fully rounded.
    ///
    /// The dot that repeats it at the other end of a row is drawn by the row, because a
    /// signal must never live in the colour alone: the pill carries the word.
    /// </summary>
    public static void Pill(Graphics g, PointF at, string word, Color hue) {
        var size = Measure(g, word, CaptionSemi);
        var box = new RectangleF(at.X, at.Y, size.Width + 18, size.Height + 6);
        FillRounded(g, box, box.Height / 2, Tint(hue, 13));
        DrawRounded(g, box, box.Height / 2, TintEdge(hue, 26));
        Text(g, word, CaptionSemi, hue, box.X + 9, box.Y + 3);
    }

    public static SizeF PillSize(Graphics g, string word) {
        var size = Measure(g, word, CaptionSemi);
        return new SizeF(size.Width + 18, size.Height + 6);
    }

    /// <summary>A small round mark, which is the other half of a signal that must not
    /// be carried by colour alone.</summary>
    public static void Dot(Graphics g, PointF at, Color hue, float size = 7f) {
        using var brush = new SolidBrush(hue);
        g.FillEllipse(brush, at.X - size / 2, at.Y - size / 2, size, size);
    }

    public static void Ring(Graphics g, PointF at, Color hue, float size = 9f, float width = 1.6f) {
        using var pen = new Pen(hue, width);
        g.DrawEllipse(pen, at.X - size / 2, at.Y - size / 2, size, size);
    }

    /// <summary>
    /// A progress track: a fill running from deep amber to amber, and when it stands for
    /// a truck on a route, a disc riding the head of it with a ring of the panel colour
    /// around it.
    /// </summary>
    public static void Track(Graphics g, RectangleF box, float part, bool truck = false, Color? ring = null,
                             float? over = null) {
        FillRounded(g, box, box.Height / 2, Border);
        var run = Math.Clamp(part, 0f, 1f) * box.Width;
        if (run > 1) {
            var fill = new RectangleF(box.X, box.Y, run, box.Height);
            using var path = Rounded(fill, box.Height / 2);
            using var brush = new LinearGradientBrush(
                new RectangleF(box.X, box.Y, Math.Max(box.Width, 1), box.Height), AccentDeep, Accent, 0f);
            g.FillPath(brush, path);
        }
        if (over is { } past && past > 0) {
            var from = box.X + run;
            var wide = Math.Min(box.Right - from, past * box.Width);
            if (wide > 1) FillRounded(g, new RectangleF(from, box.Y, wide, box.Height), box.Height / 2, AccentDeep);
        }
        if (!truck) return;

        var head = new PointF(box.X + run, box.Y + box.Height / 2);
        using var around = new SolidBrush(ring ?? Raised);
        g.FillEllipse(around, head.X - 13, head.Y - 13, 26, 26);
        using var disc = new SolidBrush(Accent);
        g.FillEllipse(disc, head.X - 10, head.Y - 10, 20, 20);
    }

    /// <summary>
    /// A group heading: a date, a dim summary beside it, and a hairline filling the rest
    /// of the line. Drawn on the window's own tone so rows slide beneath it cleanly.
    /// </summary>
    public static void GroupHeading(Graphics g, RectangleF box, string title, string summary) {
        using var back = new SolidBrush(Window);
        g.FillRectangle(back, box);

        var y = box.Y + (box.Height - CaptionSemi.Height) / 2;
        Text(g, title, CaptionSemi, Secondary, box.X, y);
        var at = box.X + Measure(g, title, CaptionSemi).Width + 12;
        if (summary.Length > 0) {
            Text(g, summary, Caption, Dim, at, y + 0.5f);
            at += Measure(g, summary, Caption).Width + 12;
        }
        using var rule = new Pen(Hairline);
        var middle = box.Y + box.Height / 2;
        if (at < box.Right) g.DrawLine(rule, at, middle, box.Right, middle);
    }

    /// <summary>
    /// A verdict banner: a round mark, a coloured headline with a muted sentence under
    /// it, and figures pushed to the right behind a hairline.
    ///
    /// Green when nothing went wrong, amber when something did. Never red on a finished
    /// delivery: the drive happened, whatever the flags say about it.
    /// </summary>
    public static void Banner(Graphics g, RectangleF box, Color hue, string headline, string sentence) {
        Surface(g, box, Tint(hue, 9), TintEdge(hue, 25));
        var mark = new RectangleF(box.X + 14, box.Y + (box.Height - 30) / 2, 30, 30);
        FillRounded(g, mark, 15, Tint(hue, 22));
        Ring(g, new PointF(mark.X + 15, mark.Y + 15), hue, 13, 1.8f);

        Text(g, headline, Semi(14), hue, mark.Right + 12, box.Y + 12);
        Text(g, sentence, Body, Muted, mark.Right + 12, box.Y + 30);
    }

    /// <summary>An event rail: rings joined by a hairline, each with a line of text and
    /// a dimmer second line of time and detail.</summary>
    public static void RailStep(Graphics g, PointF at, Color hue, bool last, float toNext) {
        Ring(g, at, hue, 9, 1.6f);
        if (last) return;
        using var line = new Pen(Hairline);
        g.DrawLine(line, at.X, at.Y + 6, at.X, at.Y + toNext - 6);
    }
}
