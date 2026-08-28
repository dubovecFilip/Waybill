namespace Waybill.Tracking;

/// <summary>One unit of the coupled set as it ended the delivery.</summary>
public class TrailerUnitRecord {
    public string Id = "";
    public string Name = "";
    public string Plate = "";
    public string BodyType = "";
    /// <summary>"trailer" or "dolly". A dolly carries no cargo and takes its own
    /// damage, so counting it among the trailers would flatter the average.</summary>
    public string Kind = "";
    public bool Owned;
    /// <summary>Damage taken during this delivery, as a share, measured from the
    /// condition the unit was in when it was hitched.</summary>
    public double Damage;
    /// <summary>The condition it was in when it was hitched. Damage taken says what
    /// this delivery did to it and nothing about what it was handed over as, and a
    /// unit that arrives at eighteen percent is a different conversation depending on
    /// whether it left at nothing or at seventeen. Null on units recorded before this
    /// was kept, where zero would be a claim that they were hitched undamaged.</summary>
    public double? StartDamage;
}

/// <summary>
/// A stretch driven with nothing on the hook: between jobs, out to a trailer, or
/// simply going somewhere. Kept because those kilometres happened and belong in a
/// total, and because the roads they cover are as much a part of where this driver
/// has been as the deliveries are.
///
/// Deliberately thin. A delivery is a claim about work done and carries everything
/// needed to judge it; this is a line on a map and a number, and nothing here is
/// ever refused or verified.
/// </summary>
public class FreeroamRecord {
    public string Game = "";
    public long StartedAtMs;
    public long EndedAtMs;
    /// <summary>From the odometer, like every other distance in this project.</summary>
    public double DistanceKm;
}

/// <summary>
/// Turns a trailer's identifier into something a person would say.
///
/// The game gives no readable name for a job trailer: <c>Name</c> comes back empty
/// on every one of them and the identifier is all there is, so
/// "blade_hauler.chassis_40x2esii" is what a delivery ended up showing. Nothing
/// here is invented; it is only the parts of what the game already said, arranged
/// so they read:
///
///   scs_box.ins_53_3ax2esii        insulated  ->  Insulated, 53 ft
///   scs_flatbed.curtain28_hookx..  curtainside -> Curtainside, 28 ft
///   blade_hauler.chassis_40x2esii  (none)     ->  Blade hauler, 40 ft
///   scs_flatbed.dolly_cx2esii      (none)     ->  Dolly
///
/// The body type is the game's own word for what the trailer is and is preferred
/// wherever it exists. Only the leading unit of a coupled set carries one, so the
/// ones behind it fall back to the family in the identifier. The raw identifier is
/// still worth keeping to hand: it is what the data actually says, and the reading
/// of it is a convenience.
/// </summary>
public static class TrailerNames {
    public static string Describe(TrailerUnitRecord unit) {
        if (unit.Name.Length > 0) return unit.Name;
        if (unit.Kind == "dolly") return Pretty(Waybill.Strings.T("value.dolly"));

        // A body type beginning with an underscore is not one. An oversize load
        // reports "_oversize" there, which is the game marking the job rather than
        // saying what the trailer is, and printing it left a delivery reading
        // "_oversize, 40 ft".
        var body = unit.BodyType.StartsWith('_') ? "" : unit.BodyType;
        var head = body.Length > 0 ? Pretty(body) : Family(unit.Id);
        var feet = Length(unit.Id);
        if (head.Length == 0) return unit.Id;
        return feet > 0 ? $"{head}, {feet} ft" : head;
    }

    /// <summary>The part before the dot, which names the kind of trailer. The "scs_"
    /// on the studio's own ones says nothing to anybody.</summary>
    private static string Family(string id) {
        // A trailer the driver owns is filed under "vehicle.", which names nothing;
        // what it is comes after that.
        if (id.StartsWith("vehicle.", StringComparison.OrdinalIgnoreCase)) id = id["vehicle.".Length..];
        var dot = id.IndexOf('.');
        var family = (dot > 0 ? id[..dot] : id).Replace("scs_", "");
        return Pretty(family.Replace('_', ' '));
    }

    /// <summary>
    /// The trailer's length, when the identifier plainly carries one.
    ///
    /// Deliberately narrow: only a bare two digit run inside the variant, and only
    /// in the range trailers actually come in. The identifier also holds axle counts
    /// and a suffix full of digits, and a number pulled out of those would be a
    /// confident lie rather than a missing figure.
    /// </summary>
    private static int Length(string id) {
        var dot = id.IndexOf('.');
        if (dot < 0 || dot + 1 >= id.Length) return 0;

        var variant = id[(dot + 1)..];
        for (var i = 0; i < variant.Length - 1; i++) {
            if (!char.IsDigit(variant[i])) continue;
            if (i > 0 && char.IsDigit(variant[i - 1])) continue;
            if (!char.IsDigit(variant[i + 1])) continue;
            // Two digits, and nothing but a separator after them: "53_3a" yes,
            // "40x2esii" yes, but "28_hook" only up to the underscore.
            var after = i + 2 < variant.Length ? variant[i + 2] : '_';
            if (char.IsDigit(after)) continue;
            var feet = (variant[i] - '0') * 10 + (variant[i + 1] - '0');
            if (feet is >= 20 and <= 60) return feet;
        }
        return 0;
    }

    private static string Pretty(string text) =>
        text.Length == 0 ? "" : char.ToUpperInvariant(text[0]) + text[1..];
}

public class FineRecord {
    public double Amount;
    public string Offence = "";
}

public class Anomaly {
    public long AtMs;
    public string Code = "";
    public double? Delta;
    public double? MovedKm;
    public double? Allowed;
    public double? ImpliedKmh;
    public long? DtMs;
    public string? From;
    public string? To;
}

public class Validation {
    public List<string> Flags = new();
    public string Status = ""; // accepted | review | rejected
}

/// <summary>One thing that happened during the job, at the moment it happened.
/// This is the delivery's timeline - fines, tolls, ferries, refuels, collisions,
/// rest stops - rather than bare counters stamped with the finish time.</summary>
public class JobEvent {
    public long AtMs;
    public string Type = "";
    public double? Value;
    public string? Detail;
    /// <summary>How much fuel went in, for a refuel. The game only tells us what it
    /// cost, and a price with no quantity beside it says very little about a stop.
    /// Measured from the tank rather than reported: the level rises over the seconds
    /// the pump runs, and the total of that rise is what was put in.</summary>
    public double? Litres;
}

/// <summary>One GPS-style sample along the route. Stored from day one even
/// before any map/replay UI exists, per the project's data model.</summary>
public class TripPoint {
    public long AtMs;
    public double X;
    public double Y;
    public double Z;
    public double SpeedKmh;
}

/// <summary>Result of one finished (delivered/cancelled/unresolved/reloaded) job - what
/// gets written to the database and shown in the history.</summary>
public class JobRecord {
    public string JobUid = "";
    public string Outcome = ""; // delivered | cancelled | unresolved | reloaded

    /// <summary>How the delivery was driven: "clean" or "race". Derived from what was
    /// measured, never chosen in advance, so it can be recomputed for old deliveries
    /// from their recordings. Informational only, exactly like the assists: it sorts
    /// deliveries into comparable groups and never affects the verdict.</summary>
    public string DrivingStyle = "";
    /// <summary>Share of driving time spent clearly over the limit, which is what the
    /// style is judged on. <see cref="SpeedingShare"/> remains the strict measure.</summary>
    public double HardSpeedingShare;
    /// <summary>Which market the job was taken from, as the SDK names it. A World
    /// of Trucks contract (`external_contracts`) is a different thing from a quick
    /// job, and without this the history cannot tell them apart.</summary>
    public string JobType = "";
    /// <summary>An oversize load: wide, tall or heavy enough that the game gives it
    /// an escort and its own rules. Worth marking because it is a different kind of
    /// driving, not a different kind of cargo.</summary>
    public bool SpecialTransport;
    public string Game = "";
    public string GameVersion = "";
    public long StartedAtMs;
    public long FinishedAtMs;
    public long RealDurationMs;
    public double GameDurationMin;

    public string SourceCity = "";
    /// <summary>What the game calls the two ends when it is talking to itself. Two
    /// cities can share a name inside one game and never an identifier, which is the
    /// only thing that tells the Salina in Utah from the one in Kansas.</summary>
    public string SourceCityId = "", DestinationCityId = "";
    public string SourceCompany = "";
    public string DestinationCity = "";
    public string DestinationCompany = "";
    public string Cargo = "";
    public string CargoId = "";
    public double CargoMassKg;
    public double PlannedDistanceKm;
    public double OfferedIncome;

    /// <summary>What the game paid in experience for the delivery. Reported in the
    /// delivered event and worth keeping: it is the one figure of a job that has
    /// nothing to do with money or distance.</summary>
    public int Xp;

    /// <summary>Whether the truck ran on a battery, which changes what the fuel
    /// figure above is counted in. See <see cref="Trucks.IsElectric"/>.</summary>
    public bool Electric;

    public string TruckMake = "";
    public string TruckModel = "";
    public string TruckId = "";
    public string? TrailerName;
    public string TrailerId = "";
    /// <summary>The game's own word for the configuration: `single`, `double`,
    /// `triple`. Read from the leading unit, the only one that carries it.</summary>
    public string TrailerChainType = "";
    /// <summary>The driver's own trailer rather than one handed over with the job.</summary>
    public bool TrailerOwned;
    /// <summary>Every coupled unit in hitching order, with what each of them took.
    /// A double or a triple is one condition as far as the game is concerned, and
    /// this is the only place the parts survive.</summary>
    public List<TrailerUnitRecord> TrailerUnits = new();

    /// <summary>Distance in the game's own "simulated km" - the units the job offer,
    /// the dashboard odometer, the payout and the delivery screen all use. This is
    /// the number a delivery tracker reports.</summary>
    public double DistanceKm;
    /// <summary>How much of <see cref="DistanceKm"/> was driven before the trailer
    /// was hitched. Zero on a quick job, where the truck is put down at the depot
    /// already loaded; on a World of Trucks contract it is the run out to the load,
    /// which the game starts counting the moment the offer is accepted.</summary>
    public double DistanceToLoadKm;
    /// <summary>Distance actually covered in world space. The map is compressed
    /// (~13.5x on the routes measured), so this is much smaller than DistanceKm.
    /// Kept for route/map work and as an independent anti-cheat signal.</summary>
    public double WorldDistanceKm;
    /// <summary>Speed integrated over game time - an independent path to the same
    /// simulated km as the odometer, used to cross-check it.</summary>
    public double SimSpeedDistanceKm;
    /// <summary>Game minutes spent actually driving, excluding sleep and pauses.
    /// The correct denominator for average speed, since distances are simulated km
    /// (raw start-to-finish game time includes rest stops and is far larger).</summary>
    public double DrivingGameMinutes;
    public double FuelUsedL;
    public double? AvgConsumptionLper100;
    public double TopSpeedKmh;
    public long DrivingMs;
    public long PausedMs;
    public double SpeedingShare;
    public double TruckDamage;
    /// <summary>What the truck, the trailer and the load were in when the load went
    /// on. Null on rows recorded before this was kept, where only the difference is
    /// known and the delivery says just that.</summary>
    public double? StartTruckDamage, StartTrailerDamage, StartCargoDamage;
    public double TrailerDamage;

    public List<FineRecord> Fines = new();
    public double TollsPaid;
    public int FerriesUsed;
    public int Refuels;
    /// <summary>Impacts detected from sudden damage steps. Metadata for safety
    /// stats only - never a reason to invalidate a delivery.</summary>
    public int Collisions;
    /// <summary>True when the job finished after its delivery window closed.
    /// Recorded for statistics; the game already applies its own penalty.</summary>
    public bool LateDelivery;
    /// <summary>Game minutes late (negative = delivered early), when a deadline
    /// was known.</summary>
    public double? MinutesLate;
    public List<Anomaly> Anomalies = new();
    public List<JobEvent> Timeline = new();
    public List<TripPoint> TripPoints = new();

    /// <summary>Share of driving time with cruise control engaged. Metadata for
    /// driving-style stats - never a reason to invalidate anything.</summary>
    public double CruiseControlShare;
    /// <summary>Rest stops taken during the job, and game minutes spent resting.</summary>
    public int RestStops;
    public double RestMinutes;

    public double? Revenue;
    public double? DeliveredCargoDamage;
    public bool? AutoparkUsed;
    public double? ReportedDistanceKm;
    public double? DeliveryTimeMin;
    public double? Penalty;

    public Validation Validation = new();
}
