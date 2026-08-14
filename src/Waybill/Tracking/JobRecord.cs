namespace Waybill.Tracking;

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
    public string Game = "";
    public string GameVersion = "";
    public long StartedAtMs;
    public long FinishedAtMs;
    public long RealDurationMs;
    public double GameDurationMin;

    public string SourceCity = "";
    public string SourceCompany = "";
    public string DestinationCity = "";
    public string DestinationCompany = "";
    public string Cargo = "";
    public string CargoId = "";
    public double CargoMassKg;
    public double PlannedDistanceKm;
    public double OfferedIncome;

    public string TruckMake = "";
    public string TruckModel = "";
    public string TruckId = "";
    public string? TrailerName;
    public string TrailerId = "";

    /// <summary>Distance in the game's own "simulated km" - the units the job offer,
    /// the dashboard odometer, the payout and the delivery screen all use. This is
    /// the number a delivery tracker reports.</summary>
    public double DistanceKm;
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
