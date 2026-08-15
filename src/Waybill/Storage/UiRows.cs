using System.ComponentModel;

namespace Waybill.Storage;

/// <summary>One row in the deliveries grid. Property names are the column headers,
/// so they are deliberately short and in Slovak like the rest of the UI.</summary>
public class DeliveryRow {
    public long Id { get; set; }
    public DateTime Datum { get; set; }
    public string Hra { get; set; } = "";
    public string Odkial { get; set; } = "";
    public string Kam { get; set; } = "";
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
}

/// <summary>Everything about one delivery, for the detail card. All distances,
/// volumes and masses are metric as stored; the card converts for display.</summary>
public class DeliveryDetail {
    public long Id;
    public DateTime StartedAt, FinishedAt;
    public string Game = "", SourceCity = "", SourceCompany = "", DestinationCity = "", DestinationCompany = "";
    public string Cargo = "", Truck = "", Trailer = "";
    public double CargoMassKg, PlannedDistanceKm, DistanceKm;
    /// <summary>The second, independent distance measurement. Only shown when the
    /// two disagree, which is the whole reason a delivery gets flagged for it.</summary>
    public double SimSpeedDistanceKm;
    public double? ReportedDistanceKm;
    public double Revenue, OfferedIncome, Penalty;
    public double FuelUsedL, TopSpeedKmh, SpeedingShare, HardSpeedingShare, CruiseShare;
    public double? AvgConsumption;
    public double TruckDamage, TrailerDamage, CargoDamage, DrivingGameMin, RestMinutes, FinesTotal, TollsPaid;
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
}

/// <summary>One row in the per-delivery event timeline.</summary>
public class TimelineRow {
    public string Cas { get; set; } = "";
    public string Udalost { get; set; } = "";
    public string Hodnota { get; set; } = "";
    public string Detail { get; set; } = "";
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
