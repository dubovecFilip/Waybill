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
    public string Stav { get; set; } = "";
    public string Poznamky { get; set; } = "";
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

    public SortableBindingList(IList<T> list) : base(list) { }

    protected override bool SupportsSortingCore => true;
    protected override bool IsSortedCore => _sorted;
    protected override ListSortDirection SortDirectionCore => _direction;
    protected override PropertyDescriptor? SortPropertyCore => _property;

    protected override void ApplySortCore(PropertyDescriptor prop, ListSortDirection direction) {
        _property = prop;
        _direction = direction;

        if (Items is List<T> items) {
            items.Sort((a, b) => {
                var x = prop.GetValue(a) as IComparable;
                var y = prop.GetValue(b);
                var cmp = x?.CompareTo(y) ?? 0;
                return direction == ListSortDirection.Ascending ? cmp : -cmp;
            });
        }

        _sorted = true;
        ResetBindings();
    }

    protected override void RemoveSortCore() => _sorted = false;
}
