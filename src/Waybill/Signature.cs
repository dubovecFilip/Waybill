using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Linq;

namespace Waybill;

/// <summary>
/// The driver's own hand, for the foot of the sheet.
///
/// A waybill has a line for a signature and every other line on it filled in. Left
/// blank it reads as a document nobody signed, and a name printed onto it would be
/// the one thing a document must never do: the app cannot sign for its driver. So
/// the driver draws it once, and from then on the sheet carries what they drew.
///
/// Kept as the strokes rather than as a picture. A picture would have to choose a
/// size, a colour and a background before knowing anything about the paper it ends
/// up on; strokes are scaled into whatever room the sheet has and drawn in the same
/// ink as everything else, and they cost a couple of hundred bytes in a settings
/// file that is meant to stay readable.
/// </summary>
public static class Signature {
    /// <summary>
    /// The strokes, as a string a settings file can hold on one line.
    ///
    /// Strokes separated by ";", points by " ", the two coordinates by ",". Both
    /// coordinates are fractions of the width they were drawn in, never of the
    /// height: measured against each separately, a signature drawn in a wide box and
    /// shown in a narrow one comes back as somebody else's. Three decimals is finer
    /// than a pen nib on A4.
    /// </summary>
    public static string Write(IEnumerable<IReadOnlyList<PointF>> strokes) =>
        string.Join(";", strokes
            .Where(s => s.Count > 1)
            .Select(s => string.Join(" ", s.Select(p =>
                p.X.ToString("0.###", CultureInfo.InvariantCulture) + "," +
                p.Y.ToString("0.###", CultureInfo.InvariantCulture)))));

    /// <summary>The strokes back out again. Anything malformed is dropped rather than
    /// thrown: a hand-edited settings file must not stop a sheet being saved.</summary>
    public static List<List<PointF>> Read(string? written) {
        var strokes = new List<List<PointF>>();
        if (string.IsNullOrWhiteSpace(written)) return strokes;

        foreach (var part in written.Split(';', StringSplitOptions.RemoveEmptyEntries)) {
            var points = new List<PointF>();
            foreach (var pair in part.Split(' ', StringSplitOptions.RemoveEmptyEntries)) {
                var xy = pair.Split(',');
                if (xy.Length != 2) continue;
                if (float.TryParse(xy[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x)
                    && float.TryParse(xy[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y)) {
                    points.Add(new PointF(x, y));
                }
            }
            if (points.Count > 1) strokes.Add(points);
        }
        return strokes;
    }

    /// <summary>
    /// Draws it inside a box, at the largest size that fits.
    ///
    /// Fitted, never stretched, and sat on the bottom of the box the way a name sits
    /// on a line.
    /// </summary>
    public static void Draw(Graphics g, string? written, RectangleF box, Color ink, float thickness) {
        var strokes = Read(written);
        if (strokes.Count == 0 || box.Width <= 0 || box.Height <= 0) return;

        // How tall the writing is in the units it was written in, where the width is
        // one by definition.
        var tall = strokes.SelectMany(s => s).Max(p => p.Y);
        var scale = tall > 0.001f ? Math.Min(box.Width, box.Height / tall) : box.Width;
        var left = box.Left + (box.Width - scale) / 2;
        var top = box.Bottom - tall * scale;

        using var pen = new Pen(ink, thickness) {
            StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round,
        };
        var was = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        foreach (var stroke in strokes) {
            var pts = stroke.Select(p => new PointF(left + p.X * scale, top + p.Y * scale)).ToArray();
            if (pts.Length > 1) g.DrawLines(pen, pts);
        }
        g.SmoothingMode = was;
    }
}
