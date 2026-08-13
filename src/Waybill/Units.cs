namespace Waybill;

public enum UnitSystem { Metric, Imperial }

/// <summary>
/// Display-side unit conversion. The SDK reports everything metric and the
/// database stores it that way - metric is the single canonical form, so
/// history never depends on what the setting happened to be when a delivery was
/// recorded. Conversion happens only on the way to the screen.
/// </summary>
public class Units {
    private const double KmToMiles = 0.621371;
    private const double LitresToGallons = 0.264172; // US gallons, to match ATS
    private const double KgToPounds = 2.20462;
    private const double TonnesToShortTons = 1.10231;

    public UnitSystem System { get; }
    public string Currency { get; }

    private Units(UnitSystem system, string currency) {
        System = system;
        Currency = currency;
    }

    /// <summary>Picks the units for a game. "auto" follows the convention of each
    /// title - American Truck Simulator in imperial, Euro Truck Simulator 2 in
    /// metric - and anything else forces one system for everything.</summary>
    public static Units For(string setting, string? game) {
        var isAts = string.Equals(game, "Ats", StringComparison.OrdinalIgnoreCase);
        var system = setting switch {
            "metric" => UnitSystem.Metric,
            "imperial" => UnitSystem.Imperial,
            _ => isAts ? UnitSystem.Imperial : UnitSystem.Metric,
        };
        // Currency follows the game, not the unit setting: an ATS delivery paid in
        // dollars is still dollars even when the numbers are shown in kilometres.
        return new Units(system, isAts ? "$" : "EUR");
    }

    private bool Imp => System == UnitSystem.Imperial;

    public string DistanceUnit => Imp ? "mi" : "km";
    public string SpeedUnit => Imp ? "mph" : "km/h";
    public string VolumeUnit => Imp ? "gal" : "l";
    public string MassUnit => Imp ? "t (short)" : "t";
    public string ConsumptionUnit => Imp ? "mpg" : "l/100km";

    public double Distance(double km) => Imp ? km * KmToMiles : km;
    public double Speed(double kmh) => Imp ? kmh * KmToMiles : kmh;
    public double Volume(double litres) => Imp ? litres * LitresToGallons : litres;
    public double MassTonnes(double kg) => Imp ? kg / 1000.0 * TonnesToShortTons : kg / 1000.0;
    public double MassKg(double kg) => Imp ? kg * KgToPounds : kg;

    /// <summary>Fuel economy flips direction between systems: litres per 100 km gets
    /// worse as it grows, miles per gallon gets better, so it is a reciprocal
    /// rather than a scale factor.</summary>
    public double? Consumption(double? litresPer100Km) {
        if (litresPer100Km is not { } v || v <= 0) return litresPer100Km;
        return Imp ? 235.215 / v : v;
    }

    public string FormatDistance(double km, string format = "0.0") => $"{Distance(km).ToString(format)} {DistanceUnit}";
    public string FormatSpeed(double kmh, string format = "0") => $"{Speed(kmh).ToString(format)} {SpeedUnit}";
    public string FormatVolume(double litres, string format = "0.0") => $"{Volume(litres).ToString(format)} {VolumeUnit}";
    public string FormatMoney(double amount) => $"{amount:0} {Currency}";
}
