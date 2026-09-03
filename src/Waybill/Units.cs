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
    private const double TonnesToShortTons = 1.10231;

    /// <summary>
    /// What one game dollar is worth in game euros.
    ///
    /// A fixed number, on purpose. It is not looked up and it does not move with the
    /// real rate, because a rate that moves would rewrite the past: the same
    /// delivery would be worth a different amount next month without anything about
    /// the drive having changed, and a logbook whose totals drift is not a logbook.
    ///
    /// Worth knowing what it is and is not. The two games do not share an economy:
    /// SCS pay roughly the same figures for the same work in each, so a haul that
    /// earns 30 000 in one earns about 30 000 in the other. Putting a real world
    /// rate between them says a game dollar is worth what a real dollar is, which is
    /// not a thing anybody measured. It is here because one column of money is
    /// easier to read than two, and it is honest about being a convention.
    /// </summary>
    public const double EurPerDollar = 0.92;

    public UnitSystem System { get; }

    /// <summary>The symbol every figure on the screen is in.</summary>
    public string Currency { get; }

    /// <summary>What the game's own money has to be multiplied by to reach it. One
    /// whenever the two are already the same currency, which includes every delivery
    /// while the units follow the game.</summary>
    private readonly double _rate;

    private Units(UnitSystem system, string currency, double rate) {
        System = system;
        Currency = currency;
        _rate = rate;
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
        // Following the game means the money stays in whatever the game paid, and
        // nothing is converted: two games, two currencies, never added together.
        if (setting is not ("metric" or "imperial")) {
            return new Units(system, isAts ? "$" : "€", 1);
        }

        // Forcing a system forces the currency with it, and now that one column has
        // to hold both games, the other game's money is converted into it rather
        // than relabelled. A figure that says € is in euros.
        var euros = system == UnitSystem.Metric;
        var rate = euros
            ? (isAts ? EurPerDollar : 1)
            : (isAts ? 1 : 1 / EurPerDollar);
        return new Units(system, euros ? "€" : "$", rate);
    }

    /// <summary>An amount of the game's own money, in the currency being shown.</summary>
    public double Money(double gameAmount) => gameAmount * _rate;

    /// <summary>
    /// A total spanning both games, said properly.
    ///
    /// While the units follow the game there is no one currency to say it in, so the
    /// games are reported side by side and never summed: adding euros to dollars
    /// makes a number that is neither. With a system forced, each game's share is
    /// converted into the chosen currency first and then added, which is the whole
    /// reason for forcing one.
    /// </summary>
    public static string FormatTotal(string setting, IReadOnlyDictionary<string, double> byGame) {
        var parts = byGame.Where(p => Math.Abs(p.Value) > 0.5).OrderBy(p => p.Key).ToList();
        if (parts.Count == 0) return For(setting, null).FormatMoney(0);

        if (setting is not ("metric" or "imperial")) {
            return string.Join("  ·  ", parts.Select(p => For(setting, p.Key).FormatMoney(p.Value)));
        }
        var shown = For(setting, parts[0].Key);
        var total = parts.Sum(p => For(setting, p.Key).Money(p.Value));
        return $"{total:0} {shown.Currency}";
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
    /// <summary>Grouped in thousands, like every other figure in the window: a
    /// six figure sum without the separators has to be counted digit by digit.</summary>
    public string FormatMoney(double amount) => $"{Money(amount):N0} {Currency}";

    /// <summary>
    /// What a battery holds, which is never litres and never gallons.
    ///
    /// The telemetry field is the same one a diesel tank uses, and on an electric
    /// truck the game fills it with kilowatt hours: the 565 in a VNR Electric is
    /// 565 kWh, not 565 of anything pourable. Converting it produced a delivery that
    /// claimed 228.8 gal and 1.4 mpg. Nothing is converted here, in either system,
    /// because a kilowatt hour is a kilowatt hour on both sides of the Atlantic.
    /// </summary>
    public static string FormatEnergy(double kwh) => $"{kwh:0.0} kWh";

    /// <summary>The same, per hundred of whatever distance is being counted in.</summary>
    public string FormatEnergyPer100(double kwhPer100Km) =>
        $"{(Imp ? kwhPer100Km / KmToMiles : kwhPer100Km):0.0} kWh/100 {DistanceUnit}";

    /// <summary>
    /// A stretch of game time as a driver would say it.
    ///
    /// Sleep is measured in hours, not in the 601 minutes the game happens to have
    /// counted, and nobody has ever said they slept for six hundred and one minutes.
    /// Under an hour it stays in minutes, because "0 h" is not an answer.
    ///
    /// The symbols are left as they are in every language here: h and min are read
    /// the same in all of them, and a translation table entry for "h" would be a
    /// translation table entry for "h".
    /// </summary>
    public static string Duration(double gameMinutes) {
        var minutes = (int)Math.Round(Math.Abs(gameMinutes));
        if (minutes < 60) return $"{minutes} min";
        var hours = minutes / 60;
        var rest = minutes % 60;
        // The minutes are dropped when there are none, so a ten hour sleep reads as
        // "10 h" rather than "10 h 00 min", and an hour and a quarter at the wheel
        // still reads as an hour and a quarter.
        return rest == 0 ? $"{hours} h" : $"{hours} h {rest:00} min";
    }
}
