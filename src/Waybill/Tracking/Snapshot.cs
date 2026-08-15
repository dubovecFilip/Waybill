namespace Waybill.Tracking;

/// <summary>
/// Normalised view of one telemetry line, independent of SCSSdkClient's field
/// names. Everything here is metric, as the SDK reports it; conversion for
/// display happens in Units.
/// </summary>
public class Snapshot {
    public bool Connected;
    public bool Paused;
    public string Game = "";
    public string GameVersion = "";
    public double GameTimeMin;

    public bool OnJob;
    public JobInfo? Job;

    public TruckInfo Truck = new();
    public TrailerInfo Trailer = new();

    public double PosX;
    public double PosY;
    public double PosZ;

    public double SpeedLimitKmh;

    /// <summary>How much faster the game clock runs than real time, as the game
    /// itself reports it (20 on the recordings measured). Used to tell a hole left
    /// by a pause from one left by this app: during a pause the clock barely moves,
    /// during a real stall it keeps running at this rate.</summary>
    public double GameTimeScale;

    public bool CruiseControlOn;
    public double CruiseControlSpeedKmh;

    public SnapshotEvents Events = new();
}

public class JobInfo {
    public string SourceCity = "";
    public string SourceCityId = "";
    public string SourceCompany = "";
    public string SourceCompanyId = "";
    public string DestinationCity = "";
    public string DestinationCityId = "";
    public string DestinationCompany = "";
    public string DestinationCompanyId = "";
    public string Cargo = "";
    public string CargoId = "";
    public double CargoMassKg;
    public double Income;
    public double DeadlineMin;
    public double PlannedDistanceKm;
    public bool SpecialJob;
    public int Market;
}

public class TruckWear {
    public double Engine;
    public double Transmission;
    public double Cabin;
    public double Chassis;
    public double Wheels;

    public double Total() => (Engine + Transmission + Cabin + Chassis + Wheels) / 5.0;
}

public class TruckInfo {
    public string Make = "";
    public string Model = "";
    public string TruckId = "";
    public double OdometerKm;
    public double SpeedKmh;
    public double FuelL;
    public double FuelCapacityL;
    public TruckWear Wear = new();
}

public class TrailerInfo {
    public bool Attached;
    public string Name = "";
    public string TrailerId = "";
    /// <summary>Whether telemetry reported a trailer at all on this snapshot. During
    /// a loading screen the trailer drops out of the data entirely, and reading the
    /// missing one as undamaged then makes its return look like a fresh impact.</summary>
    public bool Present;
    public double Wear;
    public double CargoDamage;
}

public class SnapshotEvents {
    public JobDeliveredEvent? JobDelivered;
    public JobCancelledEvent? JobCancelled;
    public FinedEvent? Fined;
    public TollgateEvent? TollgatePaid;
    public TransportEvent? FerryUsed;
    public TransportEvent? TrainUsed;
    public RefuelEvent? RefuelPaid;
}

public class JobDeliveredEvent {
    public double Revenue;
    public double EarnedXp;
    public double CargoDamage;
    public double DistanceKm;
    public double DeliveryTimeMin;
    public bool AutoparkUsed;
    public bool AutoLoaded;
    public double StartedGameMin;
    public double FinishedGameMin;
}

public class JobCancelledEvent {
    public double Penalty;
    public double StartedGameMin;
}

public class FinedEvent {
    public double Amount;
    public string Offence = "";
}

public class TollgateEvent {
    public double Amount;
}

public class TransportEvent {
    public string Source = "";
    public string Target = "";
    public double Price;
}

public class RefuelEvent {
    public double Amount;
}
