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
        /// <summary>Starts a sheet of its own, whether or not the one before it had
        /// the room. The document is two sided by design rather than by overflow.</summary>
        public bool Break;
    }

    /// <summary>Renders the delivery, one bitmap per sheet. 150 dpi is enough to
    /// read on a screen and to post; 300 is what a printer wants.</summary>
    public static Bitmap[] Render(DeliveryDetail d, List<TimelineRow> events, List<RoutePoint> route, Units u,
                                  float dpi = 150f, GameRoutes? atlas = null) {
        var pieces = Build(d, events, route, atlas, u);
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
            var top = Bottom - SheetRoom;
            var y = top;

            foreach (var piece in pieces) {
                if ((piece.Break || y + piece.Height > limit) && page.Count > 0) {
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

    /// <summary>
    /// Renders and writes the sheets, in whichever of the two forms the name asks
    /// for.
    ///
    /// A PDF is one file however many sheets there are, which is what a document is.
    /// Pictures are one file each, and they are numbered: the sheets are a front and
    /// a back, and a page two that has quietly written over page one is worse than no
    /// export at all.
    /// </summary>
    public static string[] Save(DeliveryDetail d, List<TimelineRow> events, List<RoutePoint> route, Units u,
                                string path, float dpi = 150f, GameRoutes? atlas = null) {
        // A name with nothing on the end of it is a picture, which is what every
        // caller wanted before there was a choice to make.
        if (Path.GetExtension(path).Length == 0) path += ".png";

        var sheets = Render(d, events, route, u, dpi, atlas);
        try {
            if (Path.GetExtension(path).Equals(".pdf", StringComparison.OrdinalIgnoreCase)) {
                Pdf.Write(sheets, path);
                return new[] { path };
            }

            var written = new string[sheets.Length];
            var dir = Path.GetDirectoryName(path) ?? ".";
            var stem = Path.GetFileNameWithoutExtension(path);
            for (var i = 0; i < sheets.Length; i++) {
                written[i] = sheets.Length == 1 ? path : Path.Combine(dir, $"{stem}-{i + 1}.png");
                sheets[i].Save(written[i], ImageFormat.Png);
            }
            return written;
        } finally {
            foreach (var s in sheets) s.Dispose();
        }
    }

    /// <summary>
    /// The units a sheet is written in: the game's own, always.
    ///
    /// The window follows whatever the driver set, and converting a dollar into a
    /// euro to keep one column of money readable is the right answer there. On the
    /// document it is the wrong one. A waybill records what was carried and what was
    /// paid, and what was paid was dollars in Arizona; a sheet that says 68 459 € for
    /// a job the game paid 74 412 $ for is a translation of a receipt rather than the
    /// receipt.
    /// </summary>
    public static Units UnitsFor(string game) => Units.For("game", game);

    /// <summary>A name that sorts by date and says what it is without being opened.
    /// Bare, with nothing on the end: what kind of file it becomes is the save
    /// dialog's question, and a suggested name that answers it first leaves ".png"
    /// sitting on the end of a PDF.</summary>
    public static string SuggestedName(DeliveryDetail d) {
        static string Slug(string s) {
            var clean = new string(s.Where(c => char.IsLetterOrDigit(c) || c == ' ').ToArray());
            return clean.Trim().Replace(' ', '-').ToLowerInvariant();
        }
        return $"waybill-{d.StartedAt:yyyyMMdd-HHmm}-{Slug(d.SourceCity)}-{Slug(d.DestinationCity)}";
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

    // How many lines the form is printed with, and printed with on every delivery
    // whether they are used or not. Eight covers a road train and leaves most of them
    // ruled and empty behind one trailer, which is what a form does.
    private const int PrintedEquipmentLines = 8;
    // The log fills its sheet, less what the driver's own hand and the stamp need
    // under it: 246 mm of room, less the caption, the note pad, and the endorsement
    // at the foot, is twenty-nine ruled lines. A drive with more remarks than that
    // runs onto a fourth sheet with the heading reprinted, which is what a form does.
    private const int PrintedRemarkLines = 29;

    /// <summary>The pad at the foot of the log, for whatever the driver wants to say
    /// about the run that no event of it recorded.</summary>
    private const float NotePadHeight = 40f;

    /// <summary>The caption, the column headings and one ruled line of a table.
    /// Named up here because the layout has to know how tall a line is before it can
    /// work out how many of them a sheet has room for.</summary>
    private const float TableCaptionH = 4.6f, TableHeadH = 5.4f, TableRowH = 5.6f;

    /// <summary>The stamp and the signature line at the foot of the last sheet.</summary>
    private const float EndorsementHeight = 26f;

    /// <summary>
    /// What one sheet holds, under the masthead.
    ///
    /// The hazard band is counted whether the load carries one or not. It is seven
    /// millimetres, and letting them go back into the page on an ordinary delivery
    /// would move every rule and every panel on all three sheets by that much: the
    /// form would be a slightly different form for an oversize load, which is the one
    /// thing a form must never be. Unused, they are seven millimetres of air under
    /// the rule and nobody can tell.
    /// </summary>
    private const float SheetRoom = Bottom - Margin - MastheadHeight - SpecialBandHeight;

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

    /// <summary>
    /// The blocks of the document, in the order they are printed.
    ///
    /// Two sheets, deliberately, rather than one that occasionally spills. A
    /// consignment note has always been a document with a front and a back: what was
    /// carried, and what happened while it was. Pressed onto one page the two fought
    /// each other, and the loser was always the same, a map the size of a postage
    /// stamp and six lines for a drive that took nine hours.
    ///
    /// So the front holds the parties, the load, the figures, the coupled set and
    /// the route, and the route is given every millimetre the other four leave. The
    /// back holds the log, ruled to the foot of the page, and the stamp under it.
    /// Both are worked out from the paper rather than written down here, so an
    /// oversize load, whose hazard band costs the sheet seven millimetres, loses a
    /// line of the log and a little off the map instead of quietly running to three
    /// sheets.
    /// </summary>
    /// <summary>
    /// The blocks of the document, in the order they are printed.
    ///
    /// Three sheets, always the same three, and always laid out the same way. That is
    /// the whole idea of a form: the paper is printed once and the delivery is
    /// written into it, so a driver who has seen one of these knows where to look on
    /// every other one. Nothing here is sized from the delivery. Every panel, every
    /// ruled line and every box is worked out from the paper alone, and a quiet run
    /// leaves them empty rather than closing them up.
    ///
    /// The front is the consignment: who, what, how far, how much, and the route as
    /// it was actually driven. The second sheet is the equipment and the load: the
    /// tractor, the coupled set unit by unit, what the run cost in fuel and time, and
    /// the speed trace. The back is the log of everything that happened along the
    /// way, with the stamp under it.
    /// </summary>
    private static List<Piece> Build(DeliveryDetail d, List<TimelineRow> events, List<RoutePoint> route,
                                     GameRoutes? atlas, Units u) {
        var pieces = new List<Piece>();

        var reported = d.ReportedDistanceKm is > 0 ? $"{u.Distance(d.ReportedDistanceKm.Value):0}" : "?";
        var paid = d.Outcome == "delivered" ? d.Revenue : -d.Penalty;

        // ---------------------------------------------------------- sheet one

        pieces.Add(Boxes(new[] {
            // Joined rather than glued together with a comma. A special transport
            // names no company at either end, and the comma was printed anyway, so
            // the box read ", Stockton" as though something had gone missing.
            (Strings.T("sheet.shipper"), Where(d.SourceCompany, Town(d, d.SourceCity, d.SourceCityId)), true),
            (Strings.T("sheet.consignee"), Where(d.DestinationCompany, Town(d, d.DestinationCity, d.DestinationCityId)), true),
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
        pieces.Add(RouteBox(route, atlas, d, Remainder(pieces)));

        // ---------------------------------------------------------- sheet two

        // Named where the game names it, and described from the first unit where it
        // does not. The count is only worth saying once there is more than one thing
        // to count: "single · 1×" is a figure explaining itself. Written the way the
        // window writes it, tight against the figure.
        var set = d.TrailerChainType.Length > 0 ? Label(d.TrailerChainType) : Describe(d);
        if (d.TrailerUnits.Count > 1) set += $" · {d.TrailerUnits.Count}×";

        pieces.Add(Break(Boxes(new[] {
            (Strings.T("sheet.unit"), d.Truck, true),
            (Strings.T("sheet.set"), set, true),
            (Strings.T("sheet.owner"), Strings.T(d.TrailerOwned ? "detail.owned" : "value.hired"), false),
            (Strings.T("detail.timeReal"), Units.Duration(d.RealDurationMs / 60000.0), false),
            (Strings.T("detail.timeGame"), Units.Duration(d.DrivingGameMin), false),
            (Strings.T("detail.rest"), $"{d.RestStops}× · {Units.Duration(d.RestMinutes)}", false),
        }, 3, 15f)));

        pieces.Add(Gap(3.5f));

        pieces.Add(Boxes(new[] {
            (Strings.T("detail.fuel"), u.FormatVolume(d.FuelUsedL), false),
            (Strings.T("detail.consumption"), u.Consumption(d.AvgConsumption) is { } c ? $"{c:0.0} {u.ConsumptionUnit}" : "—", false),
            (Strings.T("detail.refuels"), d.Refuels.ToString(), false),
            (Strings.T("detail.tolls"), u.FormatMoney(d.TollsPaid), false),
        }, 4, 13f));

        pieces.Add(Gap(3.5f));

        pieces.Add(Boxes(new[] {
            (Strings.T("detail.topSpeed"), u.FormatSpeed(d.TopSpeedKmh), false),
            (Strings.T("col.style"), Label(d.Style), false),
            (Strings.T("detail.cruise"), $"{d.CruiseShare * 100:0} %", false),
            (Strings.T("detail.speeding"), $"{d.SpeedingShare * 100:0.0} %", false),
        }, 4, 13f));

        pieces.Add(Gap(3.5f));

        {
            // Every unit on its own line, in the order it is hitched. A road train is
            // three separate things with three plates and three conditions, and a
            // form that says "trailer" once cannot describe what came back damaged.
            var rows = d.TrailerUnits.Select((unit, i) => new[] {
                $"{i + 1}.",
                Waybill.Tracking.TrailerNames.Describe(unit),
                unit.BodyType.StartsWith('_') ? "" : unit.BodyType,
                unit.Plate,
                Strings.T(unit.Owned ? "detail.owned" : "value.hired"),
                Condition(unit.StartDamage, (unit.StartDamage ?? 0) + unit.Damage),
            }).ToList();
            pieces.AddRange(Table(Strings.T("sheet.equipment"),
                new[] {
                    Strings.T("sheet.pos"), Strings.T("sheet.kind"), Strings.T("sheet.body"),
                    Strings.T("sheet.plate"), Strings.T("sheet.owner"), Strings.T("sheet.condition"),
                },
                new[] { 12f, 54f, 30f, 34f, 30f, ContentW - 160f }, rows,
                // The position numbers are written in too: they are part of what the
                // driver put on the form, not part of what was printed on it.
                new[] { true, true, true, true, true, true },
                PrintedEquipmentLines));
            pieces.Add(Gap(3.5f));
        }

        pieces.Add(Boxes(new[] {
            (Strings.T("sheet.commodity"), d.Cargo, true),
            (Strings.T("sheet.weight"), $"{u.MassTonnes(d.CargoMassKg):0.0} {u.MassUnit}", false),
            (Strings.T("detail.special"), Strings.T(d.SpecialTransport ? "value.yes" : "value.no"), false),
            // Before and after, which on a consignment note is the point of the box:
            // the driver signs for what they took and what they brought back, not for
            // the difference between them.
            ($"{Strings.T("col.truck")} · {Strings.T("sheet.condition")}",
             Condition(d.TruckDamageStart, (d.TruckDamageStart ?? 0) + d.TruckDamage), false),
            ($"{Strings.T("detail.trailer")} · {Strings.T("sheet.condition")}",
             Condition(d.TrailerDamageStart, (d.TrailerDamageStart ?? 0) + d.TrailerDamage), false),
            ($"{Strings.T("col.cargo")} · {Strings.T("sheet.condition")}",
             Condition(d.CargoDamageStart, d.CargoDamage), false),
        }, 3, 15f));

        pieces.Add(Gap(3.5f));
        pieces.Add(TachoBox(route, u, Remainder(pieces)));

        // ---------------------------------------------------------- sheet three

        {
            // Four columns rather than three. The stored detail is sometimes a unit
            // for the figure beside it ("% damage") and sometimes a fact of its own
            // ("Crash"), so folding it into the entry produced lines that read as
            // "Collision, % damage". Given a column it works either way.
            var log = Table(Strings.T("sheet.remarks"),
                new[] { Strings.T("sheet.time"), Strings.T("sheet.entry"), Strings.T("sheet.figure"), Strings.T("sheet.note") },
                new[] { 20f, 62f, 26f, ContentW - 108f }, events.Select(e => new[] { e.Cas, e.Udalost, e.Hodnota, e.Detail }).ToList(),
                // All four in the hand. Everything on a line of this table is what
                // somebody wrote down about the run, the note beside the figure most
                // of all: "crash", "190.5 gal", "truck · cargo" are the answer to what
                // the figure was for, and set in the form's own typeface they read as
                // though the paper came printed knowing them.
                new[] { true, true, true, true },
                PrintedRemarkLines);
            log[0].Break = true;
            pieces.AddRange(log);
        }

        pieces.Add(Gap(3.5f));
        pieces.Add(NotesBox(d.Notes, NotePadHeight));

        return pieces;
    }

    /// <summary>What is left of the sheet the blocks so far are standing on, less a
    /// hair, so that a rounding error cannot push the panel that fills it onto a
    /// sheet of its own.</summary>
    private static float Remainder(List<Piece> pieces) {
        var used = 0f;
        foreach (var piece in pieces) used = piece.Break ? piece.Height : used + piece.Height;
        return MathF.Max(50f, SheetRoom - used - 0.6f);
    }

    /// <summary>Marks a block as the first of a fresh sheet.</summary>
    private static Piece Break(Piece piece) {
        piece.Break = true;
        return piece;
    }

    /// <summary>The set, when the game did not name the configuration: older rows
    /// carry no word for it, and one trailer described is better than a blank.</summary>
    private static string Describe(DeliveryDetail d) =>
        d.TrailerUnits.Count > 0 ? Waybill.Tracking.TrailerNames.Describe(d.TrailerUnits[0]) : d.Trailer;

    private static Piece Gap(float mm) => new() { Height = mm, Filler = true };

    /// <summary>What something was in when it was taken on, and what it was in when
    /// it was handed over. A form asks for both: the difference between them is
    /// arithmetic anybody can do, and neither figure can be recovered from it.</summary>
    private static string Condition(double? before, double after) =>
        before is { } b ? $"{b * 100:0.00} → {after * 100:0.00} %" : $"{after * 100:0.00} %";

    private static string Where(string company, string city) =>
        string.Join(", ", new[] { company, city }.Where(s => s.Length > 0));

    /// <summary>The city with its state or country, if the driver has asked for that.
    /// On a document naming two places a thousand miles apart it is worth the two
    /// letters; the sketch of the route is left alone, where a code beside every town
    /// would be a page of abbreviations with a route somewhere underneath.</summary>
    private static string Town(DeliveryDetail d, string city, string cityId) =>
        Settings.Load().CityRegions ? Places.Say(d.Game, city, cityId) : city;

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
        const float CapH = TableCaptionH, HeadH = TableHeadH, RowH = TableRowH;
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
    /// The route, sketched in the same pen, over the roads this profile has already
    /// been down.
    ///
    /// The drive itself is drawn firmly and everything else faintly, which is the
    /// difference between a map and a line: a thread across an empty panel says how
    /// far the truck went and nothing about where. With the rest of the network
    /// behind it and the towns named, the same thread says which way it came out of
    /// Yakima and what it passed on the way south.
    ///
    /// The panel is scaled to this drive rather than to the network, so the delivery
    /// always fills the frame and the surroundings run off the edges. A run that is
    /// all north to south leaves the sides of it empty, which is what happens on a
    /// form.
    /// </summary>
    private static Piece RouteBox(List<RoutePoint> route, GameRoutes? atlas, DeliveryDetail d, float boxH) {
        var runs = RouteGeometry.Split(route);
        var world = RouteGeometry.Bounds(runs);

        const float Inset = 6f, Legend = 7f;
        const float boxW = ContentW;
        const float left = Margin;
        var drawW = boxW - Inset * 2;
        var drawH = boxH - Legend - Inset;
        var scale = Math.Min(drawW / Math.Max(world.Width, 1f), drawH / Math.Max(world.Height, 1f));

        // Every other drive of the same game, this one excepted, and the stretches
        // run without a load. Gathered here rather than in the drawing so a sheet
        // that is painted twice does not sort through the whole history twice.
        var elsewhere = new List<List<RoutePoint>>();
        if (atlas != null) {
            foreach (var other in atlas.Routes) {
                if (other.Key != d.Id) elsewhere.AddRange(RouteGeometry.Split(other.Value));
            }
            foreach (var runUp in atlas.RunUps) elsewhere.AddRange(RouteGeometry.Split(runUp));
        }
        var cities = atlas?.Cities ?? new List<CityAnchor>();

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
                PointF At(float x, float z) => new(
                    (x - cx) * scale + box.Left + box.Width / 2,
                    (z - cz) * scale + box.Top + box.Height / 2);

                // Nothing drawn from here on may leave the panel. The network carries
                // on for a thousand miles in every direction and the frame is where
                // this sheet stops looking.
                var state = g.Save();
                g.SetClip(box);

                // The roads already driven, in a hand light enough to read as ground
                // rather than as another route.
                using (var faint = new Pen(Color.FromArgb(48, PrintLo), 0.2f) { LineJoin = LineJoin.Round }) {
                    foreach (var run in elsewhere) {
                        var pts = RouteGeometry.Reduce(run.Select(p => At(p.X, p.Z)).ToArray(), 0.25f);
                        if (pts.Length > 1) g.DrawLines(faint, pts);
                    }
                }

                // The two this delivery is about are drawn separately, at the ends
                // of the line rather than at the town's own mark, so they are left
                // out here or the same name is printed twice a centimetre apart.
                Towns(g, cities, At, box, d.SourceCity, d.DestinationCity);

                // Drawn thin and by hand's width, not the map's. This is a sketch on a
                // form, not the instrument the window gives you.
                using var pen = new Pen(Ink, 0.42f) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round };
                using var skip = new Pen(Color.FromArgb(110, InkLo), 0.3f) { DashStyle = DashStyle.Dash };
                for (var i = 0; i < runs.Count; i++) {
                    if (i > 0) g.DrawLine(skip, At(runs[i - 1][^1].X, runs[i - 1][^1].Z), At(runs[i][0].X, runs[i][0].Z));
                    var pts = RouteGeometry.Reduce(runs[i].Select(p => At(p.X, p.Z)).ToArray(), 0.12f);
                    if (pts.Length > 1) g.DrawLines(pen, pts);
                }

                var start = At(runs[0][0].X, runs[0][0].Z);
                var end = At(runs[^1][^1].X, runs[^1][^1].Z);
                using var ring = new Pen(Ink, 0.5f);
                using var solid = new SolidBrush(Ink);
                g.DrawEllipse(ring, start.X - 1.4f, start.Y - 1.4f, 2.8f, 2.8f);
                g.FillEllipse(solid, end.X - 1.5f, end.Y - 1.5f, 3f, 3f);

                // Over the line rather than under it. Every other name on the panel
                // gives way to the drawing; these two are what the drawing is of.
                Terminus(g, d.SourceCity, start, box);
                Terminus(g, d.DestinationCity, end, box);

                g.Restore(state);
            },
        };
    }

    /// <summary>
    /// The towns, named.
    ///
    /// Taken in the order the atlas keeps them, which is the ones seen in most places
    /// first, so where two names will not both fit the one this driver knows better
    /// is the one that stays. A name is set on whichever side of its mark has room
    /// for it, and dropped rather than shortened: half a town name on a document is
    /// worse than none.
    /// </summary>
    private static void Towns(Graphics g, List<CityAnchor> cities, Func<float, float, PointF> at, RectangleF box,
                              params string[] except) {
        if (cities.Count == 0) return;

        using var name = F(FormFace, 2.4f);
        using var ink = new SolidBrush(PrintLo);
        using var mark = new SolidBrush(Color.FromArgb(150, Print));
        using var halo = new SolidBrush(Color.FromArgb(190, Paper));

        var placed = new List<RectangleF>();
        foreach (var city in cities) {
            if (except.Any(name => string.Equals(name, city.Name, StringComparison.OrdinalIgnoreCase))) continue;
            var spot = at(city.X, city.Z);
            if (!box.Contains(spot)) continue;

            var size = g.MeasureString(city.Name, name);
            // Clear of the mark, and on the other side of it where this side has no
            // room. A route turned to fill the panel puts towns hard against both
            // edges, and a name running off the right came out as "Salt Lak".
            var right = spot.X + 1.8f + size.Width <= box.Right;
            var label = new RectangleF(
                right ? spot.X + 1.8f : spot.X - 1.8f - size.Width,
                spot.Y - size.Height / 2, size.Width, size.Height);
            if (label.Left < box.Left || label.Top < box.Top || label.Bottom > box.Bottom) continue;
            if (placed.Any(p => p.IntersectsWith(label))) continue;
            placed.Add(RectangleF.Inflate(label, 1.2f, 0.4f));

            g.FillEllipse(mark, spot.X - 0.7f, spot.Y - 0.7f, 1.4f, 1.4f);
            // The stock shows through everything else on the sheet; under a name it
            // must not, or the town is read against a road.
            g.FillRectangle(halo, RectangleF.Inflate(label, 0.4f, -0.5f));
            g.DrawString(city.Name, name, ink, label.Location);
        }
    }

    /// <summary>
    /// Where the load was picked up and where it was set down, named at the marks
    /// themselves.
    ///
    /// Written rather than printed, and in the ink the route is drawn in, because
    /// these two are the delivery: every other name on the panel is there to say
    /// where that happened.
    /// </summary>
    private static void Terminus(Graphics g, string name, PointF spot, RectangleF box) {
        if (name.Length == 0) return;

        using var font = F(FormFace, 3.1f, FontStyle.Bold);
        using var ink = new SolidBrush(Ink);
        using var halo = new SolidBrush(Color.FromArgb(225, Paper));

        var size = g.MeasureString(name, font);
        var right = spot.X + 2.6f + size.Width <= box.Right;
        var label = new RectangleF(
            right ? spot.X + 2.6f : spot.X - 2.6f - size.Width,
            // Above the mark rather than beside it: the mark sits on the end of the
            // line, and a name level with it lands on the last mile of the drive.
            spot.Y - size.Height - 1.2f, size.Width, size.Height);
        // Nudged back inside where the drive finishes against an edge of the panel.
        label.X = Math.Clamp(label.X, box.Left + 0.5f, box.Right - size.Width - 0.5f);
        label.Y = Math.Max(label.Y, box.Top + 0.5f);

        g.FillRectangle(halo, RectangleF.Inflate(label, 0.6f, -0.6f));
        g.DrawString(name, font, ink, label.Location);
    }

    // ---------- the driver's own hand ----------

    /// <summary>
    /// The note pad at the foot of the log.
    ///
    /// Whatever the driver typed against the delivery, written onto ruled lines in
    /// the same hand as everything else they filled in. It is the one part of the
    /// document that is theirs rather than the game's, so printing it in the form's
    /// own typeface would have been the sheet quoting them back at themselves.
    ///
    /// The lines are printed whether anything was written on them or not, which is
    /// what a pad is.
    /// </summary>
    private static Piece NotesBox(string text, float boxH) {
        const float Legend = 7.4f, Ruled = 6.6f, Side = 4f;

        return new Piece {
            Height = boxH,
            Draw = (g, y) => {
                using var frame = new Pen(Print, 0.45f);
                using var rule = new Pen(Rule, 0.22f);
                using var legend = F(FormFace, 2.3f, FontStyle.Bold);
                using var light = new SolidBrush(PrintLo);

                g.DrawRectangle(frame, Margin, y, ContentW, boxH);
                Spread(g, Strings.T("sheet.notes").ToUpperInvariant(), legend, light, Margin + 2.2f, y + 1.8f, 0.42f);

                var rules = new List<float>();
                for (var at = y + Legend + Ruled; at <= y + boxH - 1.5f; at += Ruled) {
                    g.DrawLine(rule, Margin + Side, at, Margin + ContentW - Side, at);
                    rules.Add(at);
                }

                if (text.Trim().Length == 0 || rules.Count == 0) return;
                Handwriting(g, text.Trim(), new RectangleF(Margin + Side + 1f, y, ContentW - Side * 2 - 2f, 0), rules, Ruled);
            },
        };
    }

    /// <summary>
    /// A passage in the driver's hand, sat on the ruled lines and sized to fit
    /// between them.
    ///
    /// The size is chosen from how much there is to say: a word takes the pen the
    /// whole height of the line, and a paragraph is written smaller to get it in,
    /// which is what anybody does with a form and a fixed box. Only when it will not
    /// go at the smallest hand is it cut, and then it is cut with an ellipsis rather
    /// than in the middle of a word, so it is plain that there was more.
    /// </summary>
    private static void Handwriting(Graphics g, string text, RectangleF area, List<float> rules, float spacing) {
        foreach (var size in new[] { 5.2f, 4.6f, 4f, 3.5f, 3.1f }) {
            using var font = F(HandFace, size);
            var lines = Wrap(g, text, font, area.Width);
            var last = size <= 3.11f;
            if (lines.Count > rules.Count && !last) continue;

            if (lines.Count > rules.Count) {
                lines = lines.Take(rules.Count).ToList();
                lines[^1] = lines[^1] + " …";
            }

            using var ink = new SolidBrush(Ink);
            var state = g.Save();
            // The whole passage set a fraction off true, rather than each line
            // separately: a hand drifts, it does not zigzag.
            g.TranslateTransform(area.Left, 0);
            g.RotateTransform(-0.35f);
            g.TranslateTransform(-area.Left, 0);
            for (var i = 0; i < lines.Count; i++) {
                // Sitting on the rule, not through it. Descenders below the line are
                // what writing on ruled paper looks like.
                g.DrawString(lines[i], font, ink, area.Left, rules[i] - spacing + (spacing - size) / 2 - 0.6f);
            }
            g.Restore(state);
            return;
        }
    }

    /// <summary>Breaks a passage into lines that fit a width, keeping the driver's
    /// own paragraph breaks.</summary>
    private static List<string> Wrap(Graphics g, string text, Font font, float width) {
        var lines = new List<string>();
        foreach (var paragraph in text.Replace("\r", "").Split('\n')) {
            var line = "";
            foreach (var word in paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries)) {
                var joined = line.Length == 0 ? word : line + " " + word;
                if (line.Length > 0 && g.MeasureString(joined, font).Width > width) {
                    lines.Add(line);
                    line = word;
                } else {
                    line = joined;
                }
            }
            lines.Add(line);
        }
        return lines;
    }

    // ---------- the speed trace ----------

    /// <summary>
    /// How fast the truck was going, from the first mile to the last.
    ///
    /// The chart a tachograph would have drawn, and on a consignment note it is there
    /// for the same reason: the log says a fine was issued at 08:34, and this says
    /// whether that was one moment in an otherwise steady run or the shape of the
    /// whole drive. Read against the log on the back sheet, the two say most of what
    /// there is to say about how the load travelled.
    ///
    /// Laid out by reading rather than by the clock, so a night's sleep is one step
    /// across the paper instead of a third of it. The scale is printed at the same
    /// height for every delivery and only ever rises, so two sheets side by side can
    /// be compared without reading the numbers off them first.
    /// </summary>
    private static Piece TachoBox(List<RoutePoint> route, Units u, float boxH) {
        var runs = RouteGeometry.Split(route);
        var readings = runs.Sum(r => r.Count);
        var fastest = runs.Count > 0 ? runs.Max(r => r.Max(p => p.SpeedKmh)) : 0;

        // 160 km/h, or 100 mph, which are the same number said twice and both divide
        // into four round figures up the side. Raised in steps of twenty if somebody
        // managed more than that, and never lowered: the same chart height means the
        // same speed on every sheet, so two of them can be held side by side.
        var step = MathF.Round((float)u.Speed(160) / 20f) * 20f;
        var ceiling = step;
        while (ceiling < u.Speed(fastest)) ceiling += 20f;

        const float Inset = 5f, Legend = 7f, Gutter = 12f;

        return new Piece {
            Height = boxH,
            Draw = (g, y) => {
                using var frame = new Pen(Print, 0.45f);
                using var legend = F(FormFace, 2.3f, FontStyle.Bold);
                using var light = new SolidBrush(PrintLo);
                using var figures = F(TypeFace, 2.2f);
                g.DrawRectangle(frame, Margin, y, ContentW, boxH);
                // The unit belongs in the caption. Set against the scale it landed on
                // the top figure of it, and read as part of the number.
                Spread(g, $"{Strings.T("sheet.tacho")} · {u.SpeedUnit}".ToUpperInvariant(),
                       legend, light, Margin + 2.2f, y + 1.8f, 0.42f);

                var plot = new RectangleF(Margin + Gutter, y + Legend, ContentW - Gutter - Inset, boxH - Legend - Inset);

                // Four rules across, labelled up the left. Printed whether or not
                // there is a trace to hang on them.
                using (var rule = new Pen(Rule, 0.22f)) {
                    for (var i = 0; i <= 4; i++) {
                        var speed = ceiling * i / 4;
                        var at = plot.Bottom - plot.Height * i / 4;
                        g.DrawLine(rule, plot.Left, at, plot.Right, at);
                        var text = $"{speed:0}";
                        var w = g.MeasureString(text, figures).Width;
                        g.DrawString(text, figures, light, plot.Left - w - 1f, at - 1.6f);
                    }
                }

                if (readings < 2) return;

                var state = g.Save();
                g.SetClip(plot);
                using (var pen = new Pen(Ink, 0.28f) { LineJoin = LineJoin.Round })
                using (var skip = new Pen(Color.FromArgb(110, InkLo), 0.25f) { DashStyle = DashStyle.Dash }) {
                    var i = 0;
                    var last = PointF.Empty;
                    foreach (var run in runs) {
                        var pts = new PointF[run.Count];
                        for (var k = 0; k < run.Count; k++, i++) {
                            pts[k] = new PointF(
                                plot.Left + plot.Width * i / (readings - 1),
                                plot.Bottom - plot.Height * MathF.Min(1f, (float)u.Speed(run[k].SpeedKmh) / ceiling));
                        }
                        // The gap where the recording stops: a break in the paper
                        // rather than a line drawn through hours nobody was driving.
                        if (!last.IsEmpty) g.DrawLine(skip, last, pts[0]);
                        var thinned = RouteGeometry.Reduce(pts, 0.08f);
                        if (thinned.Length > 1) g.DrawLines(pen, thinned);
                        last = pts[^1];
                    }
                }
                g.Restore(state);
            },
        };
    }

    // ---------- stamp and signature ----------

    private static Piece Endorsement(DeliveryDetail d) => new() {
        Height = EndorsementHeight,
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
            // Signed by the driver's own hand, drawn once in the settings and kept.
            // Blank until they draw one: a signature line is signed by whoever signs
            // it, and printing a name onto one is the thing a document must not do.
            var sx = Margin + ContentW - 74f;
            Signature.Draw(g, Settings.Load().SignatureStrokes,
                new RectangleF(sx + 4f, y + 1f, 70f, 15f), Ink, 0.45f);
            g.DrawLine(line, sx, y + 17f, Margin + ContentW, y + 17f);
            Spread(g, Strings.T("sheet.signature").ToUpperInvariant(), legend, light, sx, y + 18.2f, 0.42f);
        },
    };
}
