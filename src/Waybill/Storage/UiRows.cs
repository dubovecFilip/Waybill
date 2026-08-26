using System.ComponentModel;

namespace Waybill.Storage;

/// <summary>One row in the deliveries grid. Property names are the column headers,
/// so they are deliberately short and in Slovak like the rest of the UI.</summary>
/// <summary>One tractor's whole life, for holding two of them side by side.</summary>
public class TruckRow {
    public string Kamion { get; set; } = "";
    public int Zasielky { get; set; }
    public double DistanceKm { get; set; }
    public string Vzdialenost { get; set; } = "";
    public double Zarobok { get; set; }
    public string Odmena { get; set; } = "";
    /// <summary>Litres for a tank and kilowatt hours for a battery, which is why the
    /// figure is kept raw beside the words: the words differ per row.</summary>
    public double PalivoRaw { get; set; }
    public string Palivo { get; set; } = "";
    public double SpeedKmh { get; set; }
    public string Priemer { get; set; } = "";
    public double PokutyRaw { get; set; }
    public string Pokuty { get; set; } = "";
    public int Kolizie { get; set; }
    /// <summary>Damage the tractor took on an average delivery, as a share. Averaged
    /// rather than totalled: a total grows with how much a truck was driven, so it
    /// compares the driver's history rather than the trucks.</summary>
    public double DamagePerJob { get; set; }
    public string Poskodenie { get; set; } = "";
    public string Styl { get; set; } = "";
    public int Ostro { get; set; }
    public bool Elektricky { get; set; }
    public string Hra { get; set; } = "";
}

/// <summary>One sitting at the wheel: when it ran, and what was driven in it. See
/// <see cref="Waybill.Tracking.Sessions"/> for what counts as one.</summary>
public class SessionRow {
    public DateTime Od { get; set; }
    public DateTime Do { get; set; }
    public long FromMs { get; set; }
    public long ToMs { get; set; }
    public string Trvanie { get; set; } = "";
    public int Zasielky { get; set; }
    public double DistanceKm { get; set; }
    public string Vzdialenost { get; set; } = "";
    public double Zarobok { get; set; }
    public string Odmena { get; set; } = "";
    public string Priemer { get; set; } = "";
    public string Oddych { get; set; } = "";
    public string Hra { get; set; } = "";
    /// <summary>Game minutes driven and slept, as stored. The columns above are these
    /// read out; sorting works on the numbers underneath.</summary>
    public double GameMinutes { get; set; }
    public double RestMinutes { get; set; }
    /// <summary>How long the sitting ran, and how fast it averaged, as numbers.
    /// "1 h 08 min" and "55 min" are words, and sorted as words the shorter sitting
    /// comes first because "5" is after "1". Every column written as words keeps the
    /// figure it was written from, and the grid sorts on that.</summary>
    public long DurationMs { get; set; }
    public double SpeedKmh { get; set; }
    /// <summary>Kilometres driven with nothing on the hook, counted apart so a sitting
    /// spent shunting trailers around a yard does not read as a delivery.</summary>
    public double FreeroamKm { get; set; }
}

public class DeliveryRow {
    public long Id { get; set; }
    public DateTime Datum { get; set; }
    /// <summary>When it was finished. Not a column in the list, which is ordered by
    /// when a delivery began; it is here so a sitting can say which deliveries were
    /// being driven during it rather than only which ones began in it.</summary>
    public DateTime Dokoncene { get; set; }
    public string Hra { get; set; } = "";
    public string Odkial { get; set; } = "";
    public string Kam { get; set; } = "";
    /// <summary>The identifiers behind those two names. Not shown in any column and
    /// not meant to be: they are what the region beside a city is looked up by.</summary>
    public string OdkialId { get; set; } = "";
    public string KamId { get; set; } = "";
    /// <summary>Whether the tractor ran on a battery. Marked in the gutter the way an
    /// oversize load is, and not a column: it is one fact about the truck and reads at
    /// a glance or not at all.</summary>
    public bool Elektricky { get; set; }
    public string Naklad { get; set; } = "";
    public string Tahac { get; set; } = "";
    /// <summary>Always metric, as stored. The grid shows <see cref="Vzdialenost"/>,
    /// which is this converted for display; sorting still works because the grid
    /// sorts on the underlying numeric column.</summary>
    public double DistanceKm { get; set; }
    public string Vzdialenost { get; set; } = "";
    public double Zarobok { get; set; }
    public string Odmena { get; set; } = "";
    public int Pokuty { get; set; }
    public int Kolizie { get; set; }
    /// <summary>How the job ended. Without it a cancelled job is indistinguishable
    /// from a delivered one in the list.</summary>
    public string Vysledok { get; set; } = "";
    /// <summary>Driving style, derived from how the delivery was actually driven.</summary>
    public string Styl { get; set; } = "";
    public string Stav { get; set; } = "";
    /// <summary>The stored flag identifiers. Not a column: it feeds the tooltip on
    /// the state cell, so a row saying "review" can say why without being opened.</summary>
    public string Flags { get; set; } = "";
    public string Poznamky { get; set; } = "";
    /// <summary>An oversize load. Not a column of its own: it paints the narrow
    /// marker at the head of the row, left of the date.</summary>
    public bool Special { get; set; }
}

/// <summary>Everything about one delivery, for the detail card. All distances,
/// volumes and masses are metric as stored; the card converts for display.</summary>
public class DeliveryDetail {
    public long Id;
    public DateTime StartedAt, FinishedAt;
    public string Game = "", SourceCity = "", SourceCompany = "", DestinationCity = "", DestinationCompany = "";
    /// <summary>What the game calls the two ends when it is talking to itself. Empty
    /// on rows recorded before they were kept, where the name is all there is.</summary>
    public string SourceCityId = "", DestinationCityId = "";
    public string Cargo = "", Truck = "", Trailer = "";
    /// <summary>The identifier of the tractor, which is the only thing that says
    /// whether it runs on a battery. See <see cref="Waybill.Tracking.Trucks"/>.</summary>
    public string TruckId = "";
    /// <summary>What the game paid in experience for it. Zero on rows recorded before
    /// it was kept, and on everything imported from elsewhere.</summary>
    public int Xp;
    public double CargoMassKg, PlannedDistanceKm, DistanceKm;
    /// <summary>The part of <see cref="DistanceKm"/> driven before the trailer was
    /// hitched, which the plan does not describe. Zero on most jobs.</summary>
    public double DistanceToLoadKm;
    /// <summary>An oversize load, with the escort and the rules that come with it.</summary>
    public bool SpecialTransport;
    /// <summary>The second, independent distance measurement. Only shown when the
    /// two disagree, which is the whole reason a delivery gets flagged for it.</summary>
    public double SimSpeedDistanceKm;
    public double? ReportedDistanceKm;
    public double Revenue, OfferedIncome, Penalty;
    public double FuelUsedL, TopSpeedKmh, SpeedingShare, HardSpeedingShare, CruiseShare;
    public double? AvgConsumption;
    public double TruckDamage, TrailerDamage, CargoDamage, DrivingGameMin, RestMinutes, FinesTotal, TollsPaid;
    /// <summary>What each was in when the load went on, where it is known. The three
    /// above are what this delivery added to them, except the cargo, which the game
    /// reports outright on arrival. Null on rows recorded before it was kept.</summary>
    public double? TruckDamageStart, TrailerDamageStart, CargoDamageStart;
    public long RealDurationMs;
    public int RestStops, FinesCount, Collisions, Ferries, Refuels;
    public string Outcome = "", Status = "", Flags = "", Style = "", Notes = "", Source = "";
    /// <summary>Which market the job came from, as the SDK names it. Empty for rows
    /// recorded before it was stored, and for imports.</summary>
    public string JobType = "";
    /// <summary>The coupled set: how the game names it, whether it was the driver's
    /// own, and each unit in hitching order. Empty on older rows, which a rebuild
    /// fills in from their recordings.</summary>
    public string TrailerChainType = "";
    public bool TrailerOwned;
    public List<Waybill.Tracking.TrailerUnitRecord> TrailerUnits = new();
}

/// <summary>
/// One recorded position along a drive, in the game's world space.
///
/// These are not kilometres and must never be added up as distance: the game's
/// world is compressed against the real one, and unevenly, so the length of a
/// line drawn from these says nothing. Distance comes from the odometer. World
/// space is for shape only, which is all the map claims to show.
/// </summary>
public readonly struct RoutePoint {
    public readonly long AtMs;
    public readonly float X, Z, SpeedKmh;

    public RoutePoint(long atMs, float x, float z, float speedKmh) {
        AtMs = atMs; X = x; Z = z; SpeedKmh = speedKmh;
    }
}

/// <summary>
/// Where a city sits in world space, learned from the driver's own deliveries
/// rather than from any external list: every job names the city it loaded in and
/// the one it unloaded in, and the recording says where the truck was at both
/// moments.
///
/// <see cref="Seen"/> counts distinct places used in that city, not deliveries
/// that mentioned it: dropping a load and taking the next job from the same depot
/// is one place however many jobs passed through it. <see cref="Spread"/> is how
/// far apart those places were.
///
/// A city seen once is one depot's position wearing the city's name; a city seen
/// in several places converges on the middle of them, which is close enough to
/// label but is not the city centre. Both numbers are kept so the map can say how
/// much to trust the dot.
/// </summary>
public class CityAnchor {
    public string Name = "";
    public float X, Z;
    public int Seen;
    public float Spread;
}

/// <summary>Every tracked route of one game, read in a single pass, plus the
/// cities derived from the same rows. The map needs both at once and they come
/// from the same 20-thousand-row read, so splitting it into two queries would
/// only mean doing the work twice.</summary>
public class GameRoutes {
    public Dictionary<long, List<RoutePoint>> Routes = new();
    public List<CityAnchor> Cities = new();
    /// <summary>The heads cut off the routes above: the drive out to a trailer, or
    /// to the dock with your own. Not part of the load's journey, but driven all the
    /// same, so the map shows them the way it shows any other driving off the job.
    /// The kilometres stay counted against the delivery, since the game counts them
    /// there, and are not repeated in the freeroam total.</summary>
    public List<List<RoutePoint>> RunUps = new();
}

/// <summary>One route to draw. Carries its delivery so the map can say which one
/// was clicked without the caller having to match coordinates back to a row.</summary>
/// <summary>
/// One recorded moment of a drive, for the elevation profile: when, how high the
/// game had the truck, and how fast it was going.
///
/// Height is the game's own vertical and is never reported as metres. Measured
/// across this history it is not a scaled version of the real thing: the drop at
/// Winslow and the drop at Tucson sit at the same height in the game while the
/// real places are 1500 m and 728 m apart in elevation, and the ratio between the
/// two runs from under one to over thirty-six across the cities driven to. It is
/// the same answer the map gives horizontally, for the same reason.
/// </summary>
public readonly struct HeightPoint {
    public readonly long AtMs;
    public readonly float Y, SpeedKmh;

    public HeightPoint(long atMs, float y, float speedKmh) {
        AtMs = atMs; Y = y; SpeedKmh = speedKmh;
    }
}

public class RouteLayer {
    public long Id;
    public List<RoutePoint> Points = new();
}

/// <summary>One row in the per-delivery event timeline.
///
/// <see cref="AtMs"/> and <see cref="Type"/> are the raw stored values rather than
/// anything for reading. The map places its pins by matching the time against the
/// route, and picks the shape by the identifier, so both have to survive the
/// translation that produces the rest of these fields.</summary>
public class TimelineRow {
    public string Cas { get; set; } = "";
    public string Udalost { get; set; } = "";
    public string Hodnota { get; set; } = "";
    public string Detail { get; set; } = "";
    public long AtMs { get; set; }
    public string Type { get; set; } = "";
    /// <summary>The offence exactly as the game named it, for a fine. Kept beside
    /// the readable version because folding a crash fine into its collision has to
    /// compare identifiers: the readable version changes with the language.</summary>
    public string Offence { get; set; } = "";
}

/// <summary>
/// DataGridView only offers click-to-sort when its data source says it supports
/// sorting, and the stock BindingList does not. This adds just enough for the
/// history grid's columns to be sortable.
/// </summary>
public class SortableBindingList<T> : BindingList<T> {
    private bool _sorted;
    private ListSortDirection _direction;
    private PropertyDescriptor? _property;
    private readonly Dictionary<string, string> _sortBy;

    /// <summary><paramref name="sortBy"/> maps a displayed property to the one to
    /// actually sort on. Distance and pay are shown as formatted text carrying a unit,
    /// and sorting those as text puts 283,0 km before 64,5 km because the digit 2
    /// comes before 6. The numbers behind them do the sorting instead.</summary>
    public SortableBindingList(IList<T> list, Dictionary<string, string>? sortBy = null) : base(list) {
        _sortBy = sortBy ?? new Dictionary<string, string>();
    }

    protected override bool SupportsSortingCore => true;
    protected override bool IsSortedCore => _sorted;
    protected override ListSortDirection SortDirectionCore => _direction;
    protected override PropertyDescriptor? SortPropertyCore => _property;

    protected override void ApplySortCore(PropertyDescriptor prop, ListSortDirection direction) {
        // The glyph belongs on the column that was clicked, so the displayed property
        // is remembered even when a different one does the comparing.
        _property = prop;
        _direction = direction;

        var key = prop;
        if (_sortBy.TryGetValue(prop.Name, out var numericName)
            && TypeDescriptor.GetProperties(typeof(T))[numericName] is { } numeric) {
            key = numeric;
        }

        if (Items is List<T> items) {
            items.Sort((a, b) => {
                var x = key.GetValue(a);
                var y = key.GetValue(b);
                // Nulls sort last rather than throwing, which a missing payout would.
                var cmp = (x, y) switch {
                    (null, null) => 0,
                    (null, _) => 1,
                    (_, null) => -1,
                    (IComparable c, _) => c.CompareTo(y),
                    _ => 0,
                };
                return direction == ListSortDirection.Ascending ? cmp : -cmp;
            });
        }

        _sorted = true;
        ResetBindings();
    }

    protected override void RemoveSortCore() => _sorted = false;
}
