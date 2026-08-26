using System;

namespace Waybill.Tracking;

/// <summary>
/// What the game says about the truck itself, read out of the little it gives.
/// </summary>
public static class Trucks {
    /// <summary>
    /// Whether the tractor runs on a battery.
    ///
    /// Telemetry has no field for it. What it does have is the identifier and the
    /// name, and the games are consistent about both: an electric variant is the
    /// diesel one with "_e" on the end of its identifier, and its name says so out
    /// loud, "VNR Electric", "eActros 600", "E-Tech T". Nothing here is guessed at
    /// from behaviour, because a battery and a fuel tank behave identically in
    /// telemetry: both are a number that falls while driving and jumps when filled.
    ///
    /// It matters for more than a marking. The tank capacity of an electric truck is
    /// reported in kilowatt hours rather than litres, so everything downstream of the
    /// fuel figure is in kilowatt hours too, and converting that to gallons produced
    /// a delivery claiming 228.8 gal and 1.4 mpg.
    /// </summary>
    public static bool IsElectric(string truckId, string truckName) {
        var id = truckId ?? "";
        var name = truckName ?? "";
        return name.Contains("electric", StringComparison.OrdinalIgnoreCase)
            || id.EndsWith("_e", StringComparison.OrdinalIgnoreCase)
            || id.Contains("_e.", StringComparison.OrdinalIgnoreCase)
            || id.Contains("eactros", StringComparison.OrdinalIgnoreCase)
            || id.Contains("etech", StringComparison.OrdinalIgnoreCase)
            || id.Contains("e_tech", StringComparison.OrdinalIgnoreCase);
    }
}
