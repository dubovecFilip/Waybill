using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using Waybill.Storage;

namespace Waybill;

/// <summary>
/// Paints one delivery as the document the application is named after: A4 upright,
/// the form printed and the figures written in.
///
/// This is deliberately the only place the paper idea lives. On screen the same
/// treatment fought the screen: boxes need room the window does not have, a fixed
/// sheet cannot hold seven trailer units or fifteen remarks, and a drawing cannot
/// be zoomed or clicked. A file has none of those problems. It is a fixed size by
/// definition, it can run onto a second sheet the way paper always has, and nobody
/// expects to click it.
///
/// Everything is measured in millimetres. The bitmap is told its own resolution and
/// the drawing surface works in millimetres on top of that, so the same code
/// produces a sheet for the screen and a sheet for a printer by changing one
/// number.
/// </summary>
public static class WaybillSheet {
    private const float PageW = 210f, PageH = 297f;
    private const float Margin = 12f;
    private const float ContentW = PageW - Margin * 2;
    private const float Bottom = PageH - Margin;

    private static readonly Color Paper = Color.FromArgb(237, 230, 212);
    private static readonly Color PaperLo = Color.FromArgb(223, 214, 190);
    private static readonly Color Print = Color.FromArgb(81, 75, 65);
    private static readonly Color PrintLo = Color.FromArgb(138, 130, 113);
    private static readonly Color Rule = Color.FromArgb(195, 185, 159);
    private static readonly Color Ink = Color.FromArgb(35, 64, 107);
    private static readonly Color InkLo = Color.FromArgb(74, 99, 137);

    private static readonly Color StampOk = Color.FromArgb(47, 107, 79);
    private static readonly Color StampWarn = Color.FromArgb(181, 100, 42);
    private static readonly Color StampNo = Color.FromArgb(168, 67, 44);

    /// <summary>What a block of the sheet knows about itself: how tall it is, how to
    /// draw it, and what to reprint at the top if it lands on a later sheet.</summary>
    private sealed class Piece {
        public float Height;
        public Action<Graphics, float> Draw = (_, _) => { };
        public Action<Graphics, float>? Reprint;
        public float ReprintHeight;
        /// <summary>Space between blocks rather than a block. Never worth a sheet of
        /// its own.</summary>
        public bool Filler;
    }

    /// <summary>Renders the delivery, one bitmap per sheet. 150 dpi is enough to
    /// read on a screen and to post; 300 is what a printer wants.</summary>
    public static Bitmap[] Render(DeliveryDetail d, List<TimelineRow> events, List<RoutePoint> route, Units u, float dpi = 150f) {
        var pieces = Build(d, events, route, u);
        var endorsement = Endorsement(d);

        // A gap left hanging at the end is not content. Left in, it could be the one
        // thing that would not fit, which started a sheet holding nothing but that
        // gap and then let the endorsement land on it alone: the very page the
        // reflow below exists to prevent, arrived at by the back door.
        while (pieces.Count > 0 && pieces[^1].Filler) pieces.RemoveAt(pieces.Count - 1);

        // Flow the blocks down the page, starting a new one when the next will not
        // fit. Most deliveries never reach a second sheet; a triple with a long list
        // of remarks does, which is exactly what paper has always done about it.
        List<List<(Piece Piece, float Y)>> Flow(float limit) {
            var flowed = new List<List<(Piece, float)>>();
            var page = new List<(Piece, float)>();
            // Where a sheet's content starts, on this sheet and on every one after
            // it: the masthead, plus the hazard band when the load carries one.
            var top = Margin + MastheadHeight + (d.SpecialTransport ? SpecialBandHeight : 0);
            var y = top;

            foreach (var piece in pieces) {
                if (y + piece.Height > limit && page.Count > 0) {
                    flowed.Add(page);
                    page = new List<(Piece, float)>();
                    y = top;
                    // A table broken across sheets reprints its heading, or the rows
                    // on the second one are a list of figures with nothing saying
                    // what they are figures of.
                    if (piece.Reprint is not null) {
                        page.Add((new Piece { Height = piece.ReprintHeight, Draw = piece.Reprint }, y));
                        y += piece.ReprintHeight;
                    }
                }
                page.Add((piece, y));
                y += piece.Height;
            }
            flowed.Add(page);
            return flowed;
        }

        // The stamp and the signature belong at the foot of the last sheet, and they
        // must never be the only thing on it: a page holding nothing but an
        // endorsement is what a form is supposed to avoid. If they do not fit under
        // the last block, the whole thing is laid out again against a lower limit so
        // that some content comes with them.
        var pages = Flow(Bottom);
        var used = pages[^1].Count > 0
            ? pages[^1][^1].Y + pages[^1][^1].Piece.Height
            : Bottom;
        if (used + endorsement.Height > Bottom) pages = Flow(Bottom - endorsement.Height);
        pages[^1].Add((endorsement, Bottom - endorsement.Height));

        var sheets = new Bitmap[pages.Count];
        for (var i = 0; i < pages.Count; i++) sheets[i] = Paint(d, pages[i], i + 1, pages.Count, dpi);
        return sheets;
    }

    /// <summary>Renders and writes the sheets. One page keeps the chosen name; more
    /// than one gets a numbered suffix, so a two sheet delivery cannot silently
    /// overwrite itself down to its last page.</summary>
    public static string[] Save(DeliveryDetail d, List<TimelineRow> events, List<RoutePoint> route, Units u, string path, float dpi = 150f) {
        var sheets = Render(d, events, route, u, dpi);
        var written = new string[sheets.Length];
        try {
            var dir = Path.GetDirectoryName(path) ?? ".";
            var stem = Path.GetFileNameWithoutExtension(path);
            for (var i = 0; i < sheets.Length; i++) {
                written[i] = sheets.Length == 1 ? path : Path.Combine(dir, $"{stem}-{i + 1}.png");
                sheets[i].Save(written[i], ImageFormat.Png);
            }
        } finally {
            foreach (var s in sheets) s.Dispose();
        }
        return written;
    }

    /// <summary>A name that sorts by date and says what it is without being opened.</summary>
    public static string SuggestedName(DeliveryDetail d) {
        static string Slug(string s) {
            var clean = new string(s.Where(c => char.IsLetterOrDigit(c) || c == ' ').ToArray());
            return clean.Trim().Replace(' ', '-').ToLowerInvariant();
        }
        return $"waybill-{d.StartedAt:yyyyMMdd-HHmm}-{Slug(d.SourceCity)}-{Slug(d.DestinationCity)}.png";
    }

    // ---------- fonts ----------

    /// <summary>The printed form, the driver's pen, and the typewriter that filled in
    /// the reference numbers. Picked from what Windows actually has rather than
    /// named outright, so a missing face falls back to something of the same
    /// character instead of to the default sans.</summary>
    private static readonly string FormFace = Pick("Arial Narrow", "Liberation Sans Narrow", "Franklin Gothic Medium", "Segoe UI");
    private static readonly string HandFace = Pick("Segoe Print", "Segoe Script", "Bradley Hand ITC", "Comic Sans MS", "Segoe UI");
    private static readonly string TypeFace = Pick("Consolas", "Courier New", "Segoe UI");

    private static string Pick(params string[] names) {
        var have = FontFamily.Families.Select(f => f.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return names.FirstOrDefault(have.Contains) ?? names[^1];
    }

    private static Font F(string face, float mm, FontStyle style = FontStyle.Regular)
        => new(face, mm, style, GraphicsUnit.Millimeter);

    // ---------- the sheet ----------

    private const float MastheadHeight = 20f;
    /// <summary>The extra the hazard band and its legend take on an oversize load.</summary>
    private const float SpecialBandHeight = 7f;

    // How many lines the form is printed with. Chosen so a sheet is full without
    // being crowded: everything above plus these plus the endorsement comes to about
    // 259 mm of the 273 the page has. A coupled set longer than five units, or a
    // drive with more than six remarks, runs onto a second sheet.
    private const int PrintedEquipmentLines = 5;
    private const int PrintedRemarkLines = 6;
    private const float RoutePanelHeight = 76f;

    private static Bitmap Paint(DeliveryDetail d, List<(Piece Piece, float Y)> page, int number, int of, float dpi) {
        var bmp = new Bitmap((int)MathF.Round(PageW / 25.4f * dpi), (int)MathF.Round(PageH / 25.4f * dpi));
        bmp.SetResolution(dpi, dpi);

        using var g = Graphics.FromImage(bmp);
        g.PageUnit = GraphicsUnit.Millimeter;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;

        using (var stock = new LinearGradientBrush(new RectangleF(0, 0, PageW, PageH), Paper, PaperLo, 92f))
            g.FillRectangle(stock, 0, 0, PageW, PageH);
        // A faint laid pattern, the width of a paper fibre. Enough that the ground
        // is not a flat fill, not enough to notice as a pattern.
        using (var fibre = new Pen(Color.FromArgb(9, 120, 108, 80), 0.12f))
            for (var y = 0f; y < PageH; y += 0.9f) g.DrawLine(fibre, 0, y, PageW, y);

        Masthead(g, d, number, of);
        foreach (var (piece, y) in page) piece.Draw(g, y);
        return bmp;
    }

    private static void Masthead(Graphics g, DeliveryDetail d, int number, int of) {
        using var heavy = new SolidBrush(Print);
        using var light = new SolidBrush(PrintLo);
        using var marque = F(FormFace, 8.4f, FontStyle.Bold);
        using var legal = F(FormFace, 2.5f, FontStyle.Bold);
        using var stamped = F(TypeFace, 2.6f);

        // Letter-spaced by hand: a masthead is set wide, and GDI+ has no tracking.
        var x = Margin;
        foreach (var ch in Strings.T("sheet.title").ToUpperInvariant()) {
            g.DrawString(ch.ToString(), marque, heavy, x - 0.7f, Margin - 1.2f);
            x += g.MeasureString(ch.ToString(), marque).Width - 1.4f + 1.9f;
        }
        Spread(g, Strings.T("sheet.legal").ToUpperInvariant(), legal, light, Margin, Margin + 9.4f, 0.55f);

        var right = new[] {
            $"{Strings.T("sheet.no")} {Uid(d)}",
            $"{Strings.T("sheet.sheet")} {number} / {of}   {GameName(d.Game)}",
            $"{d.StartedAt:dd.MM.yyyy}",
        };
        for (var i = 0; i < right.Length; i++) {
            var w = g.MeasureString(right[i], stamped).Width;
            g.DrawString(right[i], stamped, light, Margin + ContentW - w, Margin + i * 3.4f);
        }

        using var thick = new Pen(Print, 0.7f);
        using var thin = new Pen(Print, 0.25f);
        g.DrawLine(thick, Margin, Margin + 14.4f, Margin + ContentW, Margin + 14.4f);
        g.DrawLine(thin, Margin, Margin + 15.6f, Margin + ContentW, Margin + 15.6f);

        // An oversize load is marked on the document the way it is marked on the
        // truck: a band of hazard stripes, struck across the rule rather than said
        // in another word.
        if (d.SpecialTransport) {
            var band = new RectangleF(Margin, Margin + 16.6f, ContentW, 2.4f);
            var state = g.Save();
            g.SetClip(band);
            using (var dark = new SolidBrush(Print))
            using (var pale = new SolidBrush(Color.FromArgb(60, 255, 255, 255))) {
                g.FillRectangle(dark, band);
                for (var bx = band.Left - band.Height; bx < band.Right + 2.4f; bx += 4.8f) {
                    g.FillPolygon(pale, new[] {
                        new PointF(bx, band.Bottom), new PointF(bx + 2.4f, band.Bottom),
                        new PointF(bx + 2.4f + band.Height, band.Top), new PointF(bx + band.Height, band.Top),
                    });
                }
            }
            g.Restore(state);
            using var banner = F(FormFace, 2.2f, FontStyle.Bold);
            Spread(g, Strings.T("detail.special").ToUpperInvariant(), banner, light, Margin, Margin + 19.6f, 0.55f);
        }
    }

    /// <summary>Draws text with the letters pushed apart, which is how the small
    /// upper case legends on a form are set and something GDI+ will not do.
    ///
    /// Measured typographically rather than normally: the ordinary measurement pads
    /// each character with about a sixth of an em on either side, which for a single
    /// letter at a time is most of its width. Left in, the padding ate the tracking
    /// and ran the words together.</summary>
    private static void Spread(Graphics g, string text, Font font, Brush brush, float x, float y, float track) {
        var tight = StringFormat.GenericTypographic;
        foreach (var ch in text) {
            var s = ch.ToString();
            if (ch != ' ') g.DrawString(s, font, brush, x, y, tight);
            x += (ch == ' ' ? font.SizeInPoints * 0.22f : g.MeasureString(s, font, PointF.Empty, tight).Width) + track;
        }
    }

    private static string Uid(DeliveryDetail d) {
        // The stable identity, in the groups a reference number is read in.
        var seed = $"{d.Game}|{d.StartedAt:O}|{d.SourceCity}|{d.DestinationCity}|{d.Cargo}";
        var hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(seed));
        var hex = Convert.ToHexString(hash, 0, 6);
        return $"{hex[..4]} {hex[4..8]} {hex[8..]}";
    }

    private static string GameName(string game) => game.Equals("Ats", StringComparison.OrdinalIgnoreCase) ? "ATS"
        : game.Equals("Ets2", StringComparison.OrdinalIgnoreCase) ? "ETS2" : game;

    // ---------- the blocks ----------

    private static List<Piece> Build(DeliveryDetail d, List<TimelineRow> events, List<RoutePoint> route, Units u) {
        var pieces = new List<Piece>();

        var reported = d.ReportedDistanceKm is > 0 ? $"{u.Distance(d.ReportedDistanceKm.Value):0}" : "?";
        var paid = d.Outcome == "delivered" ? d.Revenue : -d.Penalty;

        pieces.Add(Boxes(new[] {
            // Joined rather than glued together with a comma. A special transport
            // names no company at either end, and the comma was printed anyway, so
            // the box read ", Stockton" as though something had gone missing.
            (Strings.T("sheet.shipper"), Where(d.SourceCompany, d.SourceCity), true),
            (Strings.T("sheet.consignee"), Where(d.DestinationCompany, d.DestinationCity), true),
            (Strings.T("detail.jobType"), d.JobType.Length > 0 ? Label(d.JobType) : "—", false),
            (Strings.T("sheet.commodity"), d.Cargo, true),
            (Strings.T("sheet.weight"), $"{u.MassTonnes(d.CargoMassKg):0.0} {u.MassUnit}", false),
            (Strings.T("sheet.unit"), d.Truck, false),
        }, 3, 15f));

        pieces.Add(Gap(3.5f));

        pieces.Add(Boxes(new[] {
            (Strings.T("detail.distances"), $"{u.Distance(d.PlannedDistanceKm):0} / {u.Distance(d.DistanceKm):0.0} / {reported} {u.DistanceUnit}", false),
            (Strings.T("sheet.linehaul"), u.FormatMoney(paid), false),
            (Strings.T("detail.fuel"), u.FormatVolume(d.FuelUsedL), false),
            (Strings.T("detail.fines"), $"{u.FormatMoney(d.FinesTotal)} ({d.FinesCount})", false),
        }, 4, 13f));

        pieces.Add(Gap(3.5f));

        pieces.Add(RouteBox(route, RoutePanelHeight));
        pieces.Add(Gap(3.5f));

        {
            var rows = d.TrailerUnits.Select((unit, i) => new[] {
                $"{i + 1}.",
                Waybill.Tracking.TrailerNames.Describe(unit),
                unit.Plate,
                $"{unit.Damage * 100:0.00} %",
            }).ToList();
            pieces.AddRange(Table(Strings.T("sheet.equipment"),
                new[] { Strings.T("sheet.pos"), Strings.T("sheet.kind"), Strings.T("sheet.plate"), Strings.T("sheet.condition") },
                new[] { 14f, 78f, 46f, ContentW - 138f }, rows, new[] { false, true, true, true },
                PrintedEquipmentLines));
            pieces.Add(Gap(3.5f));
        }

        {
            // Four columns rather than three. The stored detail is sometimes a unit
            // for the figure beside it ("% damage") and sometimes a fact of its own
            // ("Crash"), so folding it into the entry produced lines that read as
            // "Collision, % damage". Given a column it works either way.
            var rows = events.Select(e => new[] { e.Cas, e.Udalost, e.Hodnota, e.Detail }).ToList();
            pieces.AddRange(Table(Strings.T("sheet.remarks"),
                new[] { Strings.T("sheet.time"), Strings.T("sheet.entry"), Strings.T("sheet.figure"), Strings.T("sheet.note") },
                new[] { 20f, 62f, 26f, ContentW - 108f }, rows, new[] { false, true, true, false },
                PrintedRemarkLines));
            pieces.Add(Gap(3.5f));
        }

        return pieces;
    }

    private static Piece Gap(float mm) => new() { Height = mm, Filler = true };

    private static string Where(string company, string city) =>
        string.Join(", ", new[] { company, city }.Where(s => s.Length > 0));

    private static string Label(string key) {
        var t = Strings.T("value." + key);
        return t == "value." + key ? key : t;
    }

    // ---------- boxed fields ----------

    /// <summary>A grid of ruled boxes, the printed legend in the corner of each and
    /// the value written into it. The third element of each field says whether it is
    /// long enough to need the smaller hand.</summary>
    private static Piece Boxes((string Label, string Value, bool Long)[] fields, int columns, float rowHeight) {
        var rows = (int)Math.Ceiling(fields.Length / (double)columns);
        return new Piece {
            Height = rows * rowHeight,
            Draw = (g, y) => {
                var colW = ContentW / columns;
                using var frame = new Pen(Print, 0.45f);
                using var inner = new Pen(Rule, 0.22f);
                using var legend = F(FormFace, 2.3f, FontStyle.Bold);
                using var light = new SolidBrush(PrintLo);

                g.DrawRectangle(frame, Margin, y, ContentW, rows * rowHeight);
                for (var c = 1; c < columns; c++)
                    g.DrawLine(inner, Margin + c * colW, y, Margin + c * colW, y + rows * rowHeight);
                for (var r = 1; r < rows; r++)
                    g.DrawLine(inner, Margin, y + r * rowHeight, Margin + ContentW, y + r * rowHeight);

                for (var i = 0; i < fields.Length; i++) {
                    var bx = Margin + i % columns * colW;
                    var by = y + i / columns * rowHeight;
                    Spread(g, fields[i].Label.ToUpperInvariant(), legend, light, bx + 2.2f, by + 1.8f, 0.42f);
                    Written(g, fields[i].Value, bx + 2.2f, by + 5.6f, colW - 4.4f, fields[i].Long ? 4.3f : 4.9f, i);
                }
            },
        };
    }

    /// <summary>
    /// A value in the driver's hand.
    ///
    /// Each one is set a fraction of a degree off true, and which fraction is
    /// decided by where the field sits rather than by chance, so the same delivery
    /// exported twice produces the same sheet. Kept under a degree on purpose: past
    /// that it stops reading as a person writing and starts reading as a novelty
    /// font.
    /// </summary>
    private static void Written(Graphics g, string text, float x, float y, float maxW, float size, int seed) {
        if (text.Length == 0) return;
        using var font = F(HandFace, size);
        using var brush = new SolidBrush(Ink);

        var shrunk = font;
        var made = false;
        try {
            var w = g.MeasureString(text, font).Width;
            if (w > maxW && maxW > 0) {
                shrunk = F(HandFace, MathF.Max(2.6f, size * maxW / w));
                made = true;
            }
            var state = g.Save();
            g.TranslateTransform(x, y);
            g.RotateTransform(((seed * 37 % 17) - 8) / 11f);
            g.DrawString(text, shrunk, brush, 0, 0);
            g.Restore(state);
        } finally {
            if (made) shrunk.Dispose();
        }
    }

    // ---------- tables ----------

    /// <summary>
    /// A ruled table with a fixed number of lines, whether there is anything to put
    /// on them or not.
    ///
    /// <paramref name="lines"/> is how many the form is printed with. A delivery that
    /// used three of five leaves two ruled and empty, which is what a form does and
    /// what makes it read as one; a delivery that needs more runs onto a second sheet
    /// with the heading reprinted, which is also what a form does.
    /// </summary>
    private static List<Piece> Table(string caption, string[] heads, float[] widths,
                                     List<string[]> rows, bool[] hand, int lines) {
        const float CapH = 4.6f, HeadH = 5.4f, RowH = 5.6f;
        var pieces = new List<Piece>();

        while (rows.Count < lines) rows.Add(Array.Empty<string>());

        void Head(Graphics g, float y) {
            using var legend = F(FormFace, 2.3f, FontStyle.Bold);
            using var light = new SolidBrush(PrintLo);
            using var frame = new Pen(Print, 0.45f);
            Spread(g, caption.ToUpperInvariant(), legend, light, Margin, y, 0.42f);
            var x = Margin;
            for (var i = 0; i < heads.Length; i++) {
                if (heads[i].Length > 0) Spread(g, heads[i].ToUpperInvariant(), legend, light, x + 1.6f, y + CapH + 1.2f, 0.42f);
                x += widths[i];
            }
            g.DrawLine(frame, Margin, y + CapH + HeadH, Margin + ContentW, y + CapH + HeadH);
        }

        pieces.Add(new Piece { Height = CapH + HeadH, Draw = Head });

        for (var r = 0; r < rows.Count; r++) {
            var cells = rows[r];
            var index = r;
            pieces.Add(new Piece {
                Height = RowH,
                Reprint = Head,
                ReprintHeight = CapH + HeadH,
                Draw = (g, y) => {
                    using var rule = new Pen(Rule, 0.22f);
                    using var typed = F(FormFace, 2.7f);
                    using var light = new SolidBrush(PrintLo);
                    var x = Margin;
                    for (var i = 0; i < cells.Length && i < widths.Length; i++) {
                        if (cells[i].Length > 0) {
                            // Written above the rule rather than on it. A pen rests
                            // on the line and its descenders hang below; text placed
                            // to fill the row put them through it, which reads as
                            // struck out rather than written.
                            if (hand[i]) Written(g, cells[i], x + 1.6f, y - 0.2f, widths[i] - 3.2f, 4.1f, index * 5 + i);
                            else g.DrawString(cells[i].ToUpperInvariant(), typed, light, x + 1.4f, y + 0.9f);
                        }
                        x += widths[i];
                    }
                    g.DrawLine(rule, Margin, y + RowH, Margin + ContentW, y + RowH);
                },
            });
        }
        return pieces;
    }

    // ---------- the route, in the same pen ----------

    /// <summary>
    /// The route, sketched in the same pen.
    ///
    /// The frame is cut to the shape of the drive rather than left at the full width
    /// of the page. A run from Ogden down to Los Angeles is nearly all north to
    /// south, and in a full width box it came out as a thread in the middle of a
    /// third of a page of nothing. A box that hugs it reads as a panel someone drew
    /// in; a box that does not reads as a mistake.
    /// </summary>
    private static Piece RouteBox(List<RoutePoint> route, float boxH) {
        var runs = RouteGeometry.Split(route);
        var world = RouteGeometry.Bounds(runs);

        // The panel is the same size on every sheet, printed before anybody knew
        // what would be drawn in it. A run that is all north to south leaves the
        // sides of it empty, which is what happens on a form.
        const float Inset = 6f, Legend = 7f;
        const float boxW = ContentW;
        const float left = Margin;
        var drawW = boxW - Inset * 2;
        var drawH = boxH - Legend - Inset;
        var scale = Math.Min(drawW / Math.Max(world.Width, 1f), drawH / Math.Max(world.Height, 1f));

        return new Piece {
            Height = boxH,
            Draw = (g, y) => {
                using var frame = new Pen(Print, 0.45f);
                using var legend = F(FormFace, 2.3f, FontStyle.Bold);
                using var light = new SolidBrush(PrintLo);
                g.DrawRectangle(frame, left, y, boxW, boxH);
                Spread(g, Strings.T("sheet.route").ToUpperInvariant(), legend, light, left + 2.2f, y + 1.8f, 0.42f);

                if (runs.Count == 0) return;

                var box = new RectangleF(left + Inset, y + Legend, boxW - Inset * 2, drawH);
                var cx = world.Left + world.Width / 2;
                var cz = world.Top + world.Height / 2;
                PointF At(RoutePoint p) => new(
                    (p.X - cx) * scale + box.Left + box.Width / 2,
                    (p.Z - cz) * scale + box.Top + box.Height / 2);

                // Drawn thin and by hand's width, not the map's. This is a sketch on a
                // form, not the instrument the window gives you.
                using var pen = new Pen(Ink, 0.42f) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round };
                using var skip = new Pen(Color.FromArgb(110, InkLo), 0.3f) { DashStyle = DashStyle.Dash };
                for (var i = 0; i < runs.Count; i++) {
                    if (i > 0) g.DrawLine(skip, At(runs[i - 1][^1]), At(runs[i][0]));
                    var pts = RouteGeometry.Reduce(runs[i].Select(At).ToArray(), 0.12f);
                    if (pts.Length > 1) g.DrawLines(pen, pts);
                }

                var start = At(runs[0][0]);
                var end = At(runs[^1][^1]);
                using var ring = new Pen(Ink, 0.5f);
                using var solid = new SolidBrush(Ink);
                g.DrawEllipse(ring, start.X - 1.4f, start.Y - 1.4f, 2.8f, 2.8f);
                g.FillEllipse(solid, end.X - 1.5f, end.Y - 1.5f, 3f, 3f);
            },
        };
    }

    // ---------- stamp and signature ----------

    private static Piece Endorsement(DeliveryDetail d) => new() {
        Height = 26f,
        Draw = (g, y) => {
            var (colour, word) = d.Status switch {
                "accepted" => (StampOk, Strings.T("value.accepted")),
                "review" => (StampWarn, Strings.T("value.review")),
                "rejected" => (StampNo, Strings.T("value.rejected")),
                _ => (PrintLo, Label(d.Status)),
            };

            // Rubber, not print: struck at an angle, and the ink is thin enough that
            // the paper shows through it.
            var state = g.Save();
            g.TranslateTransform(Margin + 30, y + 12);
            g.RotateTransform(-4.2f);
            using (var edge = new Pen(Color.FromArgb(205, colour), 0.9f))
            using (var brush = new SolidBrush(Color.FromArgb(205, colour)))
            using (var big = F(FormFace, 6.4f, FontStyle.Bold))
            using (var small = F(FormFace, 2.2f, FontStyle.Bold)) {
                var w = 58f;
                g.DrawRectangle(edge, -w / 2, -9f, w, 18f);
                g.DrawRectangle(new Pen(Color.FromArgb(120, colour), 0.35f), -w / 2 + 1.1f, -7.9f, w - 2.2f, 15.8f);
                var word2 = word.ToUpperInvariant();
                var tw = g.MeasureString(word2, big).Width;
                g.DrawString(word2, big, brush, -tw / 2, -6.6f);
                var note = d.Outcome == "delivered"
                    ? $"{Strings.T("sheet.deliveredAt")} {d.FinishedAt:HH:mm}"
                    : Label(d.Outcome);
                var nw = g.MeasureString(note.ToUpperInvariant(), small).Width;
                g.DrawString(note.ToUpperInvariant(), small, brush, -nw / 2, 3.4f);
            }
            g.Restore(state);

            using var line = new Pen(Print, 0.35f);
            using var legend = F(FormFace, 2.3f, FontStyle.Bold);
            using var light = new SolidBrush(PrintLo);
            // Left blank on purpose. A signature line is signed by whoever signs it,
            // and printing a name onto one is the one thing a document must not do.
            var sx = Margin + ContentW - 74f;
            g.DrawLine(line, sx, y + 17f, Margin + ContentW, y + 17f);
            Spread(g, Strings.T("sheet.signature").ToUpperInvariant(), legend, light, sx, y + 18.2f, 0.42f);
        },
    };
}
