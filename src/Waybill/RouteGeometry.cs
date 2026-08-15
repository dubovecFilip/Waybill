using Waybill.Storage;

namespace Waybill;

/// <summary>
/// The two pieces of route maths that more than one thing needs: where a
/// recording stops being a drive, and how to throw away the points that cannot be
/// seen.
///
/// Shared rather than copied because both answers are thresholds measured against
/// real history, and a second copy would drift away from the first the moment one
/// of them was tuned. The map on screen and the sheet that gets exported have to
/// break a line in exactly the same places, or the picture in the file is not the
/// picture that was looked at.
/// </summary>
public static class RouteGeometry {
    /// <summary>Two recorded positions further apart than this were not driven
    /// between. Measured on real history the ordinary gap is 19 m and the 95th
    /// percentile 32 m, while every teleport and reload was over 1 700 m, so there
    /// is a wide empty band to put the line in.</summary>
    public const float BreakMetres = 250f;

    /// <summary>
    /// Breaks a recording into stretches that were actually driven.
    ///
    /// The first point of every job is where the driver stood when the offer was
    /// taken, and the second is where the truck is: on a quick job that is another
    /// city, and joining them draws a hundred kilometre line across the map that
    /// was never driven.
    ///
    /// The same thing happens mid drive, and for more reasons than the obvious
    /// one. A ferry or a train is the expected case, but loading an earlier save
    /// moves the truck too. None of these were driven along, so none of them is
    /// drawn as a line that was.
    /// </summary>
    public static List<List<RoutePoint>> Split(List<RoutePoint> pts) {
        var runs = new List<List<RoutePoint>>();
        var run = new List<RoutePoint>();
        for (var i = 0; i < pts.Count; i++) {
            if (i > 0) {
                var dx = pts[i].X - pts[i - 1].X;
                var dz = pts[i].Z - pts[i - 1].Z;
                if (dx * dx + dz * dz > BreakMetres * BreakMetres) {
                    runs.Add(run);
                    run = new List<RoutePoint>();
                }
            }
            run.Add(pts[i]);
        }
        runs.Add(run);
        // A stretch of one point was never driven along, and left in it would drag
        // the view out to wherever the driver happened to be standing.
        runs.RemoveAll(r => r.Count < 2);
        return runs;
    }

    /// <summary>
    /// Ramer-Douglas-Peucker, with the tolerance in the units being drawn into
    /// because that is the only place it means anything: on a view of the whole map
    /// one pixel is about 135 metres and the recorded points are 19 metres apart,
    /// so seventeen of every twenty land somewhere already painted.
    /// </summary>
    public static PointF[] Reduce(PointF[] pts, float tolerance) {
        if (pts.Length < 3) return pts;
        var keep = new List<PointF> { pts[0] };
        Walk(pts, 0, pts.Length - 1, tolerance, keep);
        keep.Add(pts[^1]);
        return keep.ToArray();
    }

    private static void Walk(PointF[] pts, int first, int last, float tolerance, List<PointF> keep) {
        if (last <= first + 1) return;

        float dx = pts[last].X - pts[first].X, dy = pts[last].Y - pts[first].Y;
        var span = MathF.Sqrt(dx * dx + dy * dy);

        var worst = 0f;
        var at = -1;
        for (var i = first + 1; i < last; i++) {
            var d = span < 1e-4f
                ? MathF.Sqrt(MathF.Pow(pts[i].X - pts[first].X, 2) + MathF.Pow(pts[i].Y - pts[first].Y, 2))
                : MathF.Abs(dy * pts[i].X - dx * pts[i].Y + pts[last].X * pts[first].Y - pts[last].Y * pts[first].X) / span;
            if (d > worst) { worst = d; at = i; }
        }
        if (worst <= tolerance || at < 0) return;

        Walk(pts, first, at, tolerance, keep);
        keep.Add(pts[at]);
        Walk(pts, at, last, tolerance, keep);
    }

    /// <summary>The box every drawn run falls inside, in world units.</summary>
    public static RectangleF Bounds(IEnumerable<List<RoutePoint>> runs) {
        float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
        var any = false;
        foreach (var run in runs)
            foreach (var p in run) {
                any = true;
                minX = Math.Min(minX, p.X); maxX = Math.Max(maxX, p.X);
                minZ = Math.Min(minZ, p.Z); maxZ = Math.Max(maxZ, p.Z);
            }
        return any ? RectangleF.FromLTRB(minX, minZ, maxX, maxZ) : RectangleF.Empty;
    }
}
