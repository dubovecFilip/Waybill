using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;
using Waybill.Storage;

namespace Waybill;

/// <summary>
/// The card for one finished delivery.
///
/// The list says what is worth scanning down a column. This says everything else,
/// composed rather than listed: the verdict first, because it is the question anyone
/// opening a delivery is asking; four figures about the drive; then the particulars in
/// four panels, each answering one question about the run. The drawings and the log of
/// what happened along the way are folded away behind one button, since they are worth
/// reading when something went wrong and worth nothing when nothing did.
/// </summary>
public partial class MainForm {
    /// <summary>One line inside a panel of particulars: what it is, what it says, and
    /// an optional remark pushed to the right end of the line.</summary>
    private sealed class Fact {
        public string Label = "";
        public string Value = "";
        public string Remark = "";
        public Color Ink = Look.Secondary;
    }

    /// <summary>The block the header's own button opens and shuts, and how tall it
    /// stands when it is open.</summary>
    private Panel? _cardAlong;
    private int _cardAlongHeight = 402;

    // ---------- the head of the card ----------

    private Control DetailHeader(DeliveryDetail d, Units u) {
        var head = new Panel { Dock = DockStyle.Top, Height = 106, BackColor = Look.Chrome };

        // The way out sits where a way out belongs, at the top left, and reads as the
        // place it goes back to rather than as the word "back".
        var back = new Label {
            AutoSize = true, Font = Look.Small, ForeColor = Look.Muted, BackColor = Look.Chrome,
            Text = "‹   " + Strings.T("detail.allDeliveries"), Cursor = Cursors.Hand,
            Location = new Point(22, 16),
        };
        back.MouseEnter += (_, _) => back.ForeColor = Look.Ink;
        back.MouseLeave += (_, _) => back.ForeColor = Look.Muted;
        back.Click += (_, _) => ShowPage("deliveries");
        head.Controls.Add(back);

        // One primary on the page, which is the sheet: it is the only thing here that
        // makes something that did not exist before.
        var save = MakePrimaryButton(Strings.T("detail.saveSheet"), () => SaveSheet(d, u));
        var along = MakeButton(Strings.T("detail.timelineOpen") + "   ⌄", () => { });
        along.Click += (_, _) => ToggleAlong(along);
        head.Controls.Add(save);
        head.Controls.Add(along);

        void Place() {
            save.Location = new Point(head.ClientSize.Width - 24 - save.Width, 14);
            along.Location = new Point(save.Left - 10 - along.Width, 14);
        }
        head.Resize += (_, _) => Place();
        Place();

        var marks = new List<string>();
        if (d.SpecialTransport) marks.Add(Strings.T("detail.special").ToLowerInvariant());
        if (Tracking.Trucks.IsElectric(d.TruckId, d.Truck)) marks.Add(Strings.T("detail.electric").ToLowerInvariant());

        var particulars = string.Join("  ·  ", new[] {
            $"{d.StartedAt:dd.MM.yyyy}",
            $"{d.StartedAt:HH:mm} → {d.FinishedAt:HH:mm}",
            d.Cargo,
            $"{u.MassTonnes(d.CargoMassKg):0.0} {u.MassUnit}",
            d.Truck,
        }.Concat(marks).Where(part => part.Length > 0));

        head.Paint += (_, e) => {
            var g = e.Graphics;
            g.Clear(Look.Chrome);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            using var hairline = new Pen(Look.Hairline);
            g.DrawLine(hairline, 0, head.Height - 1, head.Width, head.Height - 1);

            // The route at the size of a title, since it is the name of this delivery.
            // The arrow is dim so the two cities read as the two things they are.
            var from = Where(d, d.SourceCity, d.SourceCityId);
            var to = Where(d, d.DestinationCity, d.DestinationCityId);
            var x = 24f;
            Look.Text(g, from, Look.CardTitle, Look.Ink, x, 44);
            x += Look.Measure(g, from, Look.CardTitle).Width + 14;
            Look.Text(g, "→", Look.CardTitle, Look.Dim, x, 44);
            x += Look.Measure(g, "→", Look.CardTitle).Width + 14;
            Look.Text(g, to, Look.CardTitle, Look.Ink, x, 44);
            x += Look.Measure(g, to, Look.CardTitle).Width + 14;

            // Which game, as a tag rather than as another word in the line under it:
            // it frames every figure on the card, the units included.
            Look.Pill(g, new PointF(x, 52), GameName(d.Game).ToUpperInvariant(), Look.Accent);

            Look.Text(g, Look.Clip(g, particulars, Look.Body, head.Width - 48), Look.Body, Look.Muted, 24, 78);
        };
        return head;
    }

    /// <summary>The one button on a page that makes something new. Amber, with the
    /// window's own ground for ink, which is the only place that pairing appears.</summary>
    private static Button MakePrimaryButton(string text, Action onClick) {
        var b = new Button {
            Text = text, AutoSize = true, Height = Look.InputHeight, Font = Look.Small,
            Padding = new Padding(14, 0, 14, 0), FlatStyle = FlatStyle.Flat,
            BackColor = Look.Accent, ForeColor = Look.Window, Cursor = Cursors.Hand, TabStop = false,
        };
        b.FlatAppearance.BorderColor = Look.Accent;
        b.FlatAppearance.MouseOverBackColor = Look.AccentDeep;
        b.FlatAppearance.MouseDownBackColor = Look.AccentDeep;
        b.Click += (_, _) => onClick();
        return b;
    }

    // ---------- the body ----------

    private Control DetailBody(DeliveryDetail d, Units u) {
        var body = new Panel {
            Dock = DockStyle.Fill, BackColor = Look.Window, AutoScroll = true,
            Padding = new Padding(Look.PagePad, 14, Look.PagePad, 16),
        };

        // Docked children stack in reverse order of adding, so the page goes in from
        // the bottom up and comes out in the order it is read.
        body.Controls.Add(CardNotes(d));
        body.Controls.Add(CardFacts(d, u));
        body.Controls.Add(CardFigures(d, u));
        body.Controls.Add(CardAlong(d, u));
        body.Controls.Add(CardBanner(d, u));
        return body;
    }

    /// <summary>
    /// The verdict, across the head of the card.
    ///
    /// Green when nothing went wrong and amber when something did, and never red: a
    /// finished delivery is a delivery that happened, whatever it cost, and the red in
    /// this window means money or cargo lost rather than a bad mark.
    /// </summary>
    private Control CardBanner(DeliveryDetail d, Units u) {
        var reasons = Reasons(d, u);
        var clean = d.Status == "accepted" && reasons.Count == 0;
        var hue = clean ? Look.Whole : Look.Accent;
        var headline = $"{Label(d.Outcome)} · {Label(d.Status)}";
        var sentence = reasons.Count > 0 ? string.Join("  ·  ", reasons) : Strings.T("verdict.nothingUnusual");
        var paid = d.Outcome == "delivered" ? d.Revenue : -d.Penalty;

        var band = new Panel { Dock = DockStyle.Top, Height = 74, BackColor = Look.Window };
        band.Paint += (_, e) => {
            var g = e.Graphics;
            g.Clear(Look.Window);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            var box = new RectangleF(0, 0, band.Width, band.Height - 12);
            Look.Banner(g, box, hue, headline, Look.Clip(g, sentence, Look.Body, band.Width * 0.55f));

            // One or two figures at the far end, each under its own label, with a
            // hairline between them: what the drive was worth, and what it taught.
            var right = box.Right - 20;
            var figures = new List<(string Label, string Value, Color Ink)> {
                (Strings.T("detail.pay"), u.FormatMoney(paid), paid >= 0 ? Look.Ink : Look.Lost),
            };
            if (d.Xp > 0) figures.Add((Strings.T("detail.xp"), $"{d.Xp:N0} XP", Look.Ink));

            for (var i = figures.Count - 1; i >= 0; i--) {
                var (label, value, ink) = figures[i];
                var wide = Math.Max(Look.TrackedWidth(g, label.ToUpperInvariant(), Look.Label),
                                    Look.Measure(g, value, Look.Figure).Width);
                var at = right - wide;
                Look.Tracked(g, label.ToUpperInvariant(), Look.Label, Look.Dim, at, box.Y + 16);
                Look.TextRight(g, value, Look.Figure, ink, right, box.Y + 32);
                right = at - 24;
                if (i > 0) {
                    using var line = new Pen(Look.TintEdge(hue, 22));
                    g.DrawLine(line, right + 12, box.Y + 16, right + 12, box.Bottom - 16);
                }
            }
        };
        return band;
    }

    /// <summary>
    /// The drawings and the log, folded away behind the header's own button.
    ///
    /// The route across the width with the height profile beside it, and under them
    /// what happened along the way. Shut, the card is a page of figures; open, it is
    /// the drive itself.
    /// </summary>
    private Control CardAlong(DeliveryDetail d, Units u) {
        var block = new Panel { Dock = DockStyle.Top, Height = 0, BackColor = Look.Window, Visible = false };
        _cardAlong = block;

        var events = _store.TimelineRows(d.Id, u, Tracking.Trucks.IsElectric(d.TruckId, d.Truck));

        var rail = CardRail(events);
        rail.Dock = DockStyle.Bottom;
        // As tall as what it holds. Four to a column is where a rail stops reading as
        // a list of moments and starts reading as a table.
        // Three columns is what a card this wide holds, so the panel is as tall as the
        // longest of them, and never taller than four lines: past that it stops reading
        // as a list of moments and starts reading as a table.
        var deep = Math.Clamp((int)Math.Ceiling(events.Count / 3f), 1, 4);
        rail.Height = events.Count == 0 ? 78 : 56 + deep * 26;
        _cardAlongHeight = 252 + rail.Height;
        block.Controls.Add(rail);

        var drawings = new Panel { Dock = DockStyle.Fill, BackColor = Look.Window, Padding = new Padding(0, 0, 0, 12) };

        var map = NewMap(u);
        map.GameMap = MapForDelivery(d);
        map.Show(Layers(RoutesFor(d.Game)), d.Id, RoutesFor(d.Game).Cities, events);
        _cardMap = map;

        var profile = new HeightView {
            Dock = DockStyle.Fill,
            FormatSpeed = kmh => u.FormatSpeed(kmh),
            EmptyText = Strings.T("height.none"),
            Hint = Strings.T("height.hint"),
        };
        profile.Show(_store.HeightsFor(d.Id));
        _cardProfile = profile;

        // Pointing at either drawing marks the same moment in the other.
        map.Hovering += profile.MarkAt;
        profile.Hovering += map.MarkAt;

        var height = CardFrame(Strings.T("detail.heightTitle"), "", profile);
        height.Dock = DockStyle.Right;
        height.Width = 320;
        height.Padding = new Padding(1, 28, 1, 1);

        var route = CardFrame(Strings.T("detail.routeTitle"),
                              $"{u.FormatDistance(d.DistanceKm)}  ·  {d.RestStops}× {Strings.T("detail.rest").ToLowerInvariant()}", map);
        route.Dock = DockStyle.Fill;
        route.Margin = new Padding(0, 0, 12, 0);
        MapButtons(route, map, () => BigMap(d, u), replay: true, about: d);

        drawings.Controls.Add(route);
        drawings.Controls.Add(new Panel { Dock = DockStyle.Right, Width = 12, BackColor = Look.Window });
        drawings.Controls.Add(height);
        block.Controls.Add(drawings);
        return block;
    }

    /// <summary>A drawing in a panel of its own: a small capital title over a hairline,
    /// a dim note beside it, and the drawing itself filling the rest.</summary>
    private static Panel CardFrame(string title, string note, Control inside) {
        var frame = new Panel { BackColor = Look.Window, Padding = new Padding(1, 28, 1, 1) };
        frame.Paint += (_, e) => {
            var g = e.Graphics;
            g.Clear(Look.Window);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            Look.Surface(g, new RectangleF(0, 0, frame.Width, frame.Height), Look.Panel, Look.Hairline);
            var wide = Look.Tracked(g, title.ToUpperInvariant(), Look.Label, Look.Dim, 14, 10);
            if (note.Length > 0) Look.Text(g, note, Look.Caption, Look.Faint, 14 + wide + 12, 8);
        };
        inside.Dock = DockStyle.Fill;
        frame.Controls.Add(inside);
        return frame;
    }

    /// <summary>What happened along the way, on the same rail the live page draws.</summary>
    private Control CardRail(List<TimelineRow> events) {
        var rail = new Panel { BackColor = Look.Window, Padding = new Padding(0, 0, 0, 0) };
        rail.Paint += (_, e) => {
            var g = e.Graphics;
            g.Clear(Look.Window);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            Look.Surface(g, new RectangleF(0, 0, rail.Width, rail.Height), Look.Panel, Look.Hairline);
            Look.Tracked(g, Strings.T("detail.timeline").ToUpperInvariant(), Look.Label, Look.Dim, 16, 12);

            if (events.Count == 0) {
                Look.Text(g, Strings.T("timeline.none"), Look.Caption, Look.Faint, 16, 42);
                return;
            }

            // Across rather than down: a card is wider than it is tall, and a rail of
            // rings down one edge of it would leave the rest of the panel empty.
            var columns = Math.Max(1, (int)((rail.Width - 32) / 240f));
            var perColumn = (int)Math.Ceiling(events.Count / (float)columns);
            var wide = (rail.Width - 32) / (float)columns;

            for (var i = 0; i < events.Count && i < columns * perColumn; i++) {
                var row = events[i];
                var column = i / perColumn;
                var down = i % perColumn;
                var x = 16 + column * wide;
                var y = 44 + down * 26;
                if (y + 20 > rail.Height) continue;

                var hue = row.Type switch {
                    "fine" or "collision" => Look.Lost,
                    "cargo_loaded" or "trailer_coupled" => Look.Accent,
                    _ => Look.Route,
                };
                var said = row.Udalost + (row.Hodnota.Length > 0 ? $"  {row.Hodnota}" : "");
                Look.RailStep(g, new PointF(x + 5, y + 7), hue, down == perColumn - 1 || i == events.Count - 1, 26);
                Look.Text(g, Look.Clip(g, said, Look.Caption, wide - 84), Look.Caption, Look.Secondary, x + 18, y);
                Look.TextRight(g, row.Cas, Look.Caption, Look.Faint, x + wide - 20, y);
            }
        };
        return rail;
    }

    /// <summary>Opens the drawings and shuts them, and turns the button's chevron over
    /// so the button says which of the two it will do next.</summary>
    private void ToggleAlong(Button button) {
        if (_cardAlong is not { } block) return;
        var open = !block.Visible;
        block.Visible = open;
        block.Height = open ? _cardAlongHeight : 0;
        button.Text = Strings.T("detail.timelineOpen") + (open ? "   ⌃" : "   ⌄");
        button.BackColor = open ? Look.ControlHover : Look.Control;
        // The line draws itself out once it is on the screen, which is the moment it
        // has just become. A replay nobody can see is a replay wasted.
        if (open && _cardMap is { IsDisposed: false } shown) shown.Replay();
        if (open && _cardProfile is { IsDisposed: false } beside) beside.Replay();
    }

    // ---------- four figures, then four panels ----------

    private Control CardFigures(DeliveryDetail d, Units u) {
        var real = d.RealDurationMs / 60000.0;
        var hours = d.DrivingGameMin / 60;
        var speed = hours > 0 ? d.DistanceKm / hours : 0;
        var battery = Tracking.Trucks.IsElectric(d.TruckId, d.Truck);

        var tiles = new (string Label, string Figure, string Under, Color Ink)[] {
            (Strings.T("detail.driven"), u.FormatDistance(d.DistanceKm),
             d.PlannedDistanceKm > 0 ? $"{Strings.T("detail.planned")} {u.FormatDistance(d.PlannedDistanceKm)}" : "", Look.Ink),
            (Strings.T("sessions.atTheWheel"), Units.Duration(real),
             $"{Units.Duration(d.DrivingGameMin)} {Strings.T("detail.inGame")}", Look.Ink),
            (Strings.T("stats.avgSpeed"), u.FormatSpeed(speed),
             d.RestStops > 0 ? $"{d.RestStops}× {Strings.T("detail.rest").ToLowerInvariant()}  ·  {Units.Duration(d.RestMinutes)}"
                             : Strings.T("detail.noRest"), Look.Ink),
            (Strings.T("detail.fuel"), battery ? Units.FormatEnergy(d.FuelUsedL) : u.FormatVolume(d.FuelUsedL),
             Consumed(d, u, battery), Look.Ink),
        };

        var strip = new Panel { Dock = DockStyle.Top, Height = 84, BackColor = Look.Window };
        strip.Paint += (_, e) => {
            var g = e.Graphics;
            g.Clear(Look.Window);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            var wide = (strip.Width - 12 * (tiles.Length - 1)) / (float)tiles.Length;
            for (var i = 0; i < tiles.Length; i++) {
                var box = new RectangleF(i * (wide + 12), 0, wide, strip.Height - 12);
                Look.Surface(g, box, Look.Panel, Look.Hairline);
                var (label, figure, under, ink) = tiles[i];
                Look.FigureTile(g, new RectangleF(box.X + 15, box.Y + 13, box.Width - 26, box.Height - 20),
                                label, figure, Look.Clip(g, under, Look.Caption, box.Width - 26), ink,
                                figureFont: Look.FigureSmall);
            }
        };
        return strip;
    }

    /// <summary>What the drive drank, said the way the truck measures it.</summary>
    private string Consumed(DeliveryDetail d, Units u, bool battery) {
        if (battery && d.AvgConsumption is { } kwh) return u.FormatEnergyPer100(kwh);
        if (!battery && u.Consumption(d.AvgConsumption) is { } c) return $"{c:0.0} {u.ConsumptionUnit}";
        return d.Refuels > 0 ? $"{d.Refuels}× {Strings.T("detail.refuels").ToLowerInvariant()}" : "";
    }

    /// <summary>
    /// The particulars, in four panels of one question each: what was carried, how far
    /// and how long, how it was driven, and what it paid.
    ///
    /// One long list answered all four equally, which is another way of saying it
    /// answered none of them first, and it ran off the bottom of the card.
    /// </summary>
    private Control CardFacts(DeliveryDetail d, Units u) {
        var battery = Tracking.Trucks.IsElectric(d.TruckId, d.Truck);
        var reported = d.ReportedDistanceKm is > 0 ? $"{u.Distance(d.ReportedDistanceKm.Value):0}" : "?";
        var paid = d.Outcome == "delivered" ? d.Revenue : -d.Penalty;
        var net = paid - d.FinesTotal - d.TollsPaid;
        var truckDamage = (d.TruckDamageStart ?? 0) + d.TruckDamage;
        var trailerDamage = (d.TrailerDamageStart ?? 0) + d.TrailerDamage;

        var load = new List<Fact> {
            new() { Label = Strings.T("col.cargo"), Value = d.Cargo, Ink = Look.Ink },
            new() { Label = Strings.T("detail.weight"), Value = $"{u.MassTonnes(d.CargoMassKg):0.0} {u.MassUnit}" },
            new() { Label = Strings.T("col.truck"), Value = d.Truck, Remark = battery ? Strings.T("detail.electric").ToLowerInvariant() : "" },
        };
        if (d.Trailer.Length > 0 || d.TrailerUnits.Count > 0) {
            load.Add(new Fact {
                Label = Strings.T("detail.trailer"),
                Value = d.Trailer.Length > 0 ? d.Trailer : $"{d.TrailerUnits.Count}× {Strings.T("detail.trailer").ToLowerInvariant()}",
                Remark = d.TrailerOwned ? Strings.T("detail.owned") : "",
            });
        }
        if (d.JobType.Length > 0) load.Add(new Fact { Label = Strings.T("detail.jobType"), Value = Label(d.JobType) });
        if (d.SpecialTransport) {
            load.Add(new Fact { Label = Strings.T("filter.oversize"), Value = Strings.T("detail.special"), Ink = Look.Accent });
        }

        var went = new List<Fact> {
            new() { Label = Strings.T("detail.distances"),
                    Value = $"{u.Distance(d.PlannedDistanceKm):0} / {u.Distance(d.DistanceKm):0.0} / {reported} {u.DistanceUnit}" },
            new() { Label = Strings.T("detail.timeGame"), Value = Units.Duration(d.DrivingGameMin) },
            new() { Label = Strings.T("detail.timeReal"), Value = Units.Duration(d.RealDurationMs / 60000.0) },
            new() { Label = Strings.T("detail.rest"), Value = $"{d.RestStops}×  ·  {Units.Duration(d.RestMinutes)}" },
        };
        if (d.DistanceToLoadKm > 0.05) {
            went.Insert(1, new Fact {
                Label = Strings.T("detail.legs"),
                Value = $"{u.Distance(d.DistanceKm - d.DistanceToLoadKm):0.0}  +  {u.Distance(d.DistanceToLoadKm):0.0} {u.DistanceUnit}",
            });
        }
        if (d.Ferries > 0) went.Add(new Fact { Label = Strings.T("detail.ferries"), Value = $"{d.Ferries}×" });

        var conduct = new List<Fact> {
            new() { Label = Strings.T("detail.collisions"), Value = d.Collisions == 0 ? Strings.T("detail.none") : $"{d.Collisions}×",
                    Ink = d.Collisions == 0 ? Look.Whole : Look.Accent },
            new() { Label = Strings.T("detail.fines"), Value = u.FormatMoney(d.FinesTotal), Remark = d.FinesCount > 0 ? $"{d.FinesCount}×" : "",
                    Ink = d.FinesTotal <= 0 ? Look.Whole : Look.Lost },
            new() { Label = Strings.T("col.cargo"), Value = $"{d.CargoDamage * 100:0.00} %",
                    Remark = d.CargoDamage < 0.0005 ? Strings.T("live.notAScratch") : "",
                    Ink = d.CargoDamage < 0.0005 ? Look.Whole : Look.Lost },
            new() { Label = Strings.T("col.truck"), Value = $"{truckDamage * 100:0.00} %",
                    Remark = Strings.T("detail.wearOnly"), Ink = truckDamage < 0.01 ? Look.Secondary : Look.Accent },
            new() { Label = Strings.T("col.style"), Value = Label(d.Style),
                    Remark = $"{Strings.T("detail.speeding").ToLowerInvariant()} {d.SpeedingShare * 100:0.0} %" },
        };
        if (d.Trailer.Length > 0) {
            conduct.Insert(4, new Fact {
                Label = Strings.T("detail.trailer"), Value = $"{trailerDamage * 100:0.00} %",
                Ink = trailerDamage < 0.01 ? Look.Secondary : Look.Accent,
            });
        }

        var money = new List<Fact> {
            new() { Label = Strings.T("detail.pay"), Value = u.FormatMoney(paid), Ink = paid >= 0 ? Look.Ink : Look.Lost },
            new() { Label = Strings.T("detail.offered"), Value = u.FormatMoney(d.OfferedIncome) },
            new() { Label = Strings.T("detail.fines"), Value = u.FormatMoney(d.FinesTotal), Ink = d.FinesTotal > 0 ? Look.Lost : Look.Secondary },
            new() { Label = Strings.T("detail.tolls"), Value = u.FormatMoney(d.TollsPaid) },
            new() { Label = Strings.T("detail.net"), Value = u.FormatMoney(net),
                    Remark = d.DistanceKm > 1 ? $"{u.FormatMoney(net / u.Distance(d.DistanceKm))} / {u.DistanceUnit}" : "",
                    Ink = net >= 0 ? Look.Whole : Look.Lost },
        };
        if (d.Xp > 0) money.Add(new Fact { Label = Strings.T("detail.xp"), Value = $"{d.Xp:N0} XP" });

        // Two panels to a line, each pair as tall as the taller of the two, so a panel
        // never ends in a strip of empty ground beside a fuller one.
        var top = Math.Max(load.Count, went.Count);
        var bottom = Math.Max(conduct.Count, money.Count);

        var grid = new TableLayoutPanel {
            Dock = DockStyle.Top, BackColor = Look.Window, ColumnCount = 2, RowCount = 2,
            Margin = new Padding(0), Padding = new Padding(0),
            Height = FactHeight(top) + FactHeight(bottom) + 12,
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, FactHeight(top) + 12));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, FactHeight(bottom)));

        grid.Controls.Add(FactPanel(Strings.T("detail.groupLoad"), load, new Padding(0, 0, 6, 12)), 0, 0);
        grid.Controls.Add(FactPanel(Strings.T("detail.groupDistance"), went, new Padding(6, 0, 0, 12)), 1, 0);
        grid.Controls.Add(FactPanel(Strings.T("detail.groupConduct"), conduct, new Padding(0, 0, 6, 0)), 0, 1);
        grid.Controls.Add(FactPanel(Strings.T("detail.groupMoney"), money, new Padding(6, 0, 0, 0)), 1, 1);
        return grid;
    }

    private const int FactRow = 26;

    private static int FactHeight(int rows) => 40 + rows * FactRow + 12;

    /// <summary>One panel of particulars: a small capital title over a hairline, then a
    /// line for each fact with its name on the left, its figure in the middle and any
    /// remark about it at the right end.</summary>
    private static Control FactPanel(string title, List<Fact> facts, Padding margin) {
        var panel = new Panel { Dock = DockStyle.Fill, BackColor = Look.Window, Margin = margin };
        panel.Paint += (_, e) => {
            var g = e.Graphics;
            g.Clear(Look.Window);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            Look.Surface(g, new RectangleF(0, 0, panel.Width, panel.Height), Look.Panel, Look.Hairline);
            Look.Tracked(g, title.ToUpperInvariant(), Look.Label, Look.Dim, 16, 14);
            using var rule = new Pen(Look.Hairline);
            g.DrawLine(rule, 16, 34, panel.Width - 16, 34);

            // The value column starts at a fixed share of the width rather than after
            // the longest name: a translated label runs a third longer than the English
            // one, and a column that follows the words moves under every language.
            var valueAt = Math.Max(140, panel.Width * 0.42f);
            for (var i = 0; i < facts.Count; i++) {
                var fact = facts[i];
                var y = 44 + i * FactRow;
                Look.Text(g, Look.Clip(g, fact.Label, Look.Small, valueAt - 28), Look.Small, Look.Muted, 16, y);
                var remark = fact.Remark.Length > 0
                    ? Look.Measure(g, fact.Remark, Look.Caption).Width + 16 : 0;
                Look.Text(g, Look.Clip(g, fact.Value, Look.Body, panel.Width - valueAt - 16 - remark),
                          Look.Body, fact.Ink, valueAt, y - 1);
                if (fact.Remark.Length > 0) {
                    Look.TextRight(g, fact.Remark, Look.Caption, Look.Faint, panel.Width - 16, y + 1);
                }
            }
        };
        return panel;
    }

    /// <summary>Anything worth remembering about this run, in the driver's own words.
    /// A well rather than a panel: it is the one thing on the card that is written to
    /// rather than read.</summary>
    private Control CardNotes(DeliveryDetail d) {
        var well = new Panel { Dock = DockStyle.Top, Height = 92, BackColor = Look.Window, Padding = new Padding(16, 36, 16, 14) };
        well.Paint += (_, e) => {
            var g = e.Graphics;
            g.Clear(Look.Window);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            Look.Surface(g, new RectangleF(0, 0, well.Width, well.Height), Look.Well, Look.Hairline);
            Look.Tracked(g, Strings.T("col.notes").ToUpperInvariant(), Look.Label, Look.Dim, 16, 14);
        };

        var box = new TextBox {
            Dock = DockStyle.Fill, Multiline = true, Text = d.Notes, BorderStyle = BorderStyle.None,
            BackColor = Look.Well, ForeColor = Look.Ink, Font = Look.Body,
            PlaceholderText = Strings.T("detail.notesHint"),
        };
        box.Leave += (_, _) => { _store.SetNotes(d.Id, box.Text); ReloadHistory(); };
        well.Controls.Add(box);
        return well;
    }
}
