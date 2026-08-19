
namespace Waybill.Storage;

public class ImportResult {
    public int Imported;
    public int Skipped;          // already in the database
    public int Uncredited;       // TrucksBook recorded 0 accepted distance
    public double UncreditedKm;
    public List<string> Problems = new();
}

/// <summary>
/// Imports delivery history exported from TrucksBook (semicolon-separated CSV).
///
/// The export is written in whatever units the TrucksBook profile used, and the
/// values carry their unit as a suffix ("157 mi", "5.9 mpg (US gallons)", "3 262 $"),
/// so each field is converted based on what it actually says rather than on a
/// guess about the game. Everything lands in the database metric, like every
/// other row.
/// </summary>
public class TrucksBookImport {
    private readonly DeliveryStore _store;

    public TrucksBookImport(DeliveryStore store) => _store = store;

    public ImportResult Import(string csvPath) {
        var result = new ImportResult();
        var lines = File.ReadAllLines(csvPath).Where(l => !string.IsNullOrWhiteSpace(l)).ToArray();
        if (lines.Length < 2) {
            result.Problems.Add("Subor je prazdny alebo nema data.");
            return result;
        }

        var header = SplitCsv(lines[0]);
        int Col(string name) => Array.FindIndex(header, h => h.Equals(name, StringComparison.OrdinalIgnoreCase));

        var iId = Col("TrucksBookID");
        var iGame = Col("Game");
        if (iId < 0 || iGame < 0) {
            result.Problems.Add("Nevyzera to ako export z TrucksBooku (chyba stlpec Game alebo TrucksBookID).");
            return result;
        }

        var idx = new {
            Id = iId,
            Game = iGame,
            From = Col("From"),
            To = Col("To"),
            FromCompany = Col("Initial company"),
            ToCompany = Col("Target company"),
            Cargo = Col("Cargo"),
            Weight = Col("Weight"),
            Planned = Col("Planned distance"),
            Accepted = Col("Accepted distance"),
            Profit = Col("Profit"),
            Offences = Col("Offences"),
            Xp = Col("XP"),
            Damage = Col("Damage"),
            Seconds = Col("Time taken (real) [s]"),
            Truck = Col("Truck"),
            Consumption = Col("Average consumption"),
            TopSpeed = Col("Maximal reached speed"),
            JobType = Col("Job Type"),
            Date = Col("Date"),
            Time = Col("Time"),
        };

        for (var i = 1; i < lines.Length; i++) {
            var cells = SplitCsv(lines[i]);
            if (cells.Length <= idx.Id) continue;

            try {
                var row = BuildRow(cells, idx);
                if (_store.HasDelivery(row.JobUid)) {
                    result.Skipped++;
                    continue;
                }
                _store.InsertImported(row);
                result.Imported++;
                if (row.Uncredited) {
                    result.Uncredited++;
                    result.UncreditedKm += row.ActualKm;
                }
            } catch (Exception ex) {
                result.Problems.Add($"Riadok {i + 1}: {ex.Message}");
            }
        }

        return result;
    }

    private ImportedDelivery BuildRow(string[] c, dynamic idx) {
        string Get(int i) => i >= 0 && i < c.Length ? c[i].Trim() : "";

        var game = Get(idx.Game).ToUpperInvariant() switch {
            "ATS" => "Ats",
            "ETS2" => "Ets2",
            var other => other,
        };

        var plannedKm = ParseDistanceKm(Get(idx.Planned));
        var acceptedKm = ParseDistanceKm(Get(idx.Accepted));

        // TrucksBook credits 0 km when it refuses a delivery - which is exactly the
        // rule this project exists to avoid. The drive still happened, so the
        // planned distance stands in as the best estimate available.
        var uncredited = acceptedKm <= 0 && plannedKm > 0;
        var actualKm = uncredited ? plannedKm : acceptedKm;

        // Weight has no unit in the export; it follows the same profile setting as
        // the distances, so imperial distances mean pounds.
        var imperial = Get(idx.Planned).Contains("mi", StringComparison.OrdinalIgnoreCase);
        var weight = ParseNumber(Get(idx.Weight));
        var massKg = imperial ? weight / 2.20462 : weight;

        var consumption = ParseConsumptionLper100Km(Get(idx.Consumption));
        var seconds = ParseNumber(Get(idx.Seconds));
        var startedAt = ParseTimestamp(Get(idx.Date), Get(idx.Time));

        var truck = Get(idx.Truck);
        var space = truck.IndexOf(' ');
        var jobType = Get(idx.JobType);

        return new ImportedDelivery {
            // Prefixed so an imported row can never collide with a tracked one, and
            // so re-importing the same export is a no-op rather than a duplicate.
            JobUid = "trucksbook:" + Get(idx.Id),
            Game = game,
            SourceCity = Get(idx.From),
            SourceCompany = Get(idx.FromCompany),
            DestinationCity = Get(idx.To),
            DestinationCompany = Get(idx.ToCompany),
            Cargo = Get(idx.Cargo),
            CargoMassKg = massKg,
            PlannedKm = plannedKm,
            ActualKm = actualKm,
            Revenue = ParseNumber(Get(idx.Profit)),
            FinesTotal = ParseNumber(Get(idx.Offences)),
            Xp = (int)ParseNumber(Get(idx.Xp)),
            CargoDamage = ParseNumber(Get(idx.Damage)) / 100.0,
            RealDurationMs = (long)(seconds * 1000),
            StartedAtMs = startedAt,
            TruckMake = space > 0 ? truck[..space] : truck,
            TruckModel = space > 0 ? truck[(space + 1)..] : "",
            AvgConsumption = consumption,
            FuelUsedL = consumption > 0 ? actualKm / 100.0 * consumption : 0,
            TopSpeedKmh = ParseSpeedKmh(Get(idx.TopSpeed)),
            JobType = jobType,
            Uncredited = uncredited,
            Notes = uncredited ? "TrucksBook tuto zasielku nezapocital" : "",
        };
    }

    // --- parsing helpers ---

    /// <summary>Splits one CSV line on semicolons, honouring double quotes.</summary>
    private static string[] SplitCsv(string line) {
        var cells = new List<string>();
        var current = "";
        var inQuotes = false;

        foreach (var ch in line) {
            if (ch == '"') inQuotes = !inQuotes;
            else if (ch == ';' && !inQuotes) { cells.Add(current); current = ""; }
            else current += ch;
        }
        cells.Add(current);
        return cells.ToArray();
    }

    /// <summary>Numbers arrive space-grouped and with a unit or currency attached
    /// ("34 191", "3 262 $", "157 mi"), so strip everything that isn't part of the
    /// number itself.</summary>
    private static double ParseNumber(string raw) {
        var digits = new string(raw.Where(ch => char.IsDigit(ch) || ch == '.' || ch == ',' || ch == '-').ToArray());
        digits = digits.Replace(",", ".");
        return double.TryParse(digits, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var value) ? value : 0;
    }

    private static double ParseDistanceKm(string raw) {
        var value = ParseNumber(raw);
        return raw.Contains("mi", StringComparison.OrdinalIgnoreCase) ? value / 0.621371 : value;
    }

    private static double ParseSpeedKmh(string raw) {
        var value = ParseNumber(raw);
        return raw.Contains("mph", StringComparison.OrdinalIgnoreCase) ? value / 0.621371 : value;
    }

    /// <summary>"5.9 mpg (US gallons)" or "32.4 l/100km" - mpg is a reciprocal of
    /// litres per 100 km, not a scale factor.</summary>
    private static double ParseConsumptionLper100Km(string raw) {
        var value = ParseNumber(raw);
        if (value <= 0) return 0;
        return raw.Contains("mpg", StringComparison.OrdinalIgnoreCase) ? 235.215 / value : value;
    }

    private static long ParseTimestamp(string date, string time) {
        if (DateTime.TryParse($"{date} {time}", System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var parsed)) {
            return new DateTimeOffset(parsed, TimeZoneInfo.Local.GetUtcOffset(parsed)).ToUnixTimeMilliseconds();
        }
        return 0;
    }
}

public class ImportedDelivery {
    public string JobUid = "";
    public string Game = "";
    public string SourceCity = "";
    public string SourceCompany = "";
    public string DestinationCity = "";
    public string DestinationCompany = "";
    public string Cargo = "";
    public double CargoMassKg;
    public double PlannedKm;
    public double ActualKm;
    public double Revenue;
    public double FinesTotal;
    public int Xp;
    public double CargoDamage;
    public long RealDurationMs;
    public long StartedAtMs;
    public string TruckMake = "";
    public string TruckModel = "";
    public double AvgConsumption;
    public double FuelUsedL;
    public double TopSpeedKmh;
    public string JobType = "";
    public bool Uncredited;
    public string Notes = "";
}
