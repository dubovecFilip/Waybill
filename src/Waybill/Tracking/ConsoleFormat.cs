using System.Linq;

namespace Waybill.Tracking;

/// <summary>Console formatting for job lifecycle events. Values are converted for
/// display only - everything is stored metric (see Units).</summary>
public static class ConsoleFormat {
    /// <summary>Set once at startup from the saved preference.</summary>
    public static string UnitSetting = "auto";

    private static Units UnitsFor(string? game) => Units.For(UnitSetting, game);

    public static void PrintJobStarted(JobInfo job, string? game = null) {
        var u = UnitsFor(game);
        Console.WriteLine();
        Console.WriteLine($">> ZAČIATOK  {job.SourceCity} ({job.SourceCompany})  ->  {job.DestinationCity} ({job.DestinationCompany})");
        Console.WriteLine($"   náklad: {job.Cargo}, {u.MassTonnes(job.CargoMassKg):0.0} {u.MassUnit}, "
                        + $"plan {u.FormatDistance(job.PlannedDistanceKm, "0")}, ponuka {u.FormatMoney(job.Income)}");
    }

    public static void PrintJobFinished(JobRecord r) {
        var u = UnitsFor(r.Game);
        Console.WriteLine();
        Console.WriteLine($"<< KONIEC  výsledok: {r.Outcome}");
        Console.WriteLine($"   trasa: {r.SourceCity} -> {r.DestinationCity}");
        Console.WriteLine($"   namerané: {u.FormatDistance(r.DistanceKm, "0.000")}");
        if (r.ReportedDistanceKm is { } reported) {
            var diff = reported > 0 ? Math.Round((r.DistanceKm / reported - 1) * 100, 1) : (double?)null;
            Console.WriteLine($"   hra hlási: {u.FormatDistance(reported, "0")}  (rozdiel {(diff.HasValue ? diff.Value.ToString("0.0") : "?")} %)");
        }
        Console.WriteLine($"   reálna trasa na mape: {u.FormatDistance(r.WorldDistanceKm, "0.000")} (mapa je zmenšená)");
        if (r.Revenue is { } revenue) Console.WriteLine($"   vyplatené: {u.FormatMoney(revenue)} (ponuka bola {u.FormatMoney(r.OfferedIncome)})");

        var consumption = u.Consumption(r.AvgConsumptionLper100);
        Console.WriteLine($"   palivo: {u.FormatVolume(r.FuelUsedL)}, priemer {(consumption.HasValue ? consumption.Value.ToString("0.0") : "?")} {u.ConsumptionUnit}");
        Console.WriteLine($"   maximálka: {u.FormatSpeed(r.TopSpeedKmh, "0.0")}, prekračovanie {Math.Round(r.SpeedingShare * 100, 1)} % času");
        Console.WriteLine($"   poškodenie: kamión {Math.Round(r.TruckDamage * 100, 2)} %, náves {Math.Round(r.TrailerDamage * 100, 2)} %");

        if (r.CruiseControlShare > 0) Console.WriteLine($"   tempomat: {Math.Round(r.CruiseControlShare * 100, 1)} % času");
        if (r.RestStops > 0) Console.WriteLine($"   oddych: {r.RestStops}x, spolu {r.RestMinutes:0} herných minút");
        if (r.Collisions > 0) Console.WriteLine($"   kolízie: {r.Collisions}x");
        if (r.LateDelivery) Console.WriteLine($"   MEŠKANIE: {r.MinutesLate:0} herných minút po termine");
        if (r.Fines.Count > 0) {
            var total = r.Fines.Sum(f => f.Amount);
            Console.WriteLine($"   pokuty: {r.Fines.Count}x, spolu {u.FormatMoney(total)}");
        }

        var flags = r.Validation.Flags.Count > 0 ? $" [{string.Join(", ", r.Validation.Flags)}]" : "";
        Console.WriteLine($"   verdikt: {r.Validation.Status}{flags}");
        Console.WriteLine();
    }
}
