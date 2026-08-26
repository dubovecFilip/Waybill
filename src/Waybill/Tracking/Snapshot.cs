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

    /// <summary>
    /// Which way the truck is pointing, nought to one counterclockwise from north,
    /// exactly as the game gives it.
    ///
    /// Where it is going can be worked out from two positions; where it is pointing
    /// cannot, and the two are different things when reversing onto a dock or sitting
    /// still. Null when it is not known, which is every replayed drive: the field it
    /// comes from is internal to the SDK and never written into a recording.
    /// </summary>
    public double? HeadingTurns;

    public double SpeedLimitKmh;

    /// <summary>How much faster the game clock runs than real time, as the game
    /// itself reports it (20 on the recordings measured). Used to tell a hole left
    /// by a pause from one left by this app: during a pause the clock barely moves,
    /// during a real stall it keeps running at this rate.</summary>
    public double GameTimeScale;

    /// <summary>
    /// How many game minutes the driver has left before the game makes them sleep.
    ///
    /// The one thing that tells a sleep from every other way of losing two hours.
    /// Sleeping puts this back up to its maximum; a charging stop, a repair or a job
    /// taken from a menu all move the clock forward while this counts down by exactly
    /// as much, because the driver was awake for every minute of it.
    /// </summary>
    public double NextRestMin;

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
    /// <summary>Whether the cargo is on the trailer yet. Loading and unloading skip
    /// the game clock forward, and that is not the driver resting.</summary>
    public bool CargoLoaded;
}

/// <summary>
/// One coupled unit, in the order the game reports them, which is the order they
/// are hitched. A double or a triple arrives as several of these and the game shows
/// a single condition for the lot, so the parts are only visible here.
/// </summary>
public class TrailerUnit {
    public string Id = "";
    public string Name = "";
    public string Plate = "";
    /// <summary>Empty on a unit that is not the head of the set. A dolly has no body
    /// of its own, and neither do the follow on sections of a car transporter.</summary>
    public string BodyType = "";
    /// <summary>The game's own word for the configuration, on the head unit only:
    /// `single`, `double`, `triple`.</summary>
    public string ChainType = "";
    public double Wear;
    public double CargoDamage;

    /// <summary>A converter dolly rather than something that carries cargo. Named in
    /// the identifier, which is the only place the game says so.</summary>
    public bool IsDolly => Id.Contains("dolly", StringComparison.OrdinalIgnoreCase);

    /// <summary>Bought and owned rather than handed over with the job. Owned units
    /// are identified the way trucks are, as `vehicle.something`, and carry a name.</summary>
    public bool IsOwned => Id.StartsWith("vehicle.", StringComparison.OrdinalIgnoreCase);
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
    /// <summary>Every coupled unit in hitching order. <see cref="Wear"/> stays the
    /// worst of them, which is the set's condition and what the game itself shows.</summary>
    public List<TrailerUnit> Units = new();
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
