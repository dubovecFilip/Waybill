using Newtonsoft.Json.Linq;
using SCSSdkClient;
using SCSSdkClient.Object;

namespace Waybill.Tracking;

/// <summary>
/// Maps a raw SCSSdkClient telemetry object onto the Snapshot shape the
/// JobTracker expects. This is the only file that knows about SDK field
/// names - when the plugin/SDK changes, this is the file to fix.
/// </summary>
public static class Adapter {
    public static Snapshot ToSnapshot(SCSTelemetry d, string kind) {
        var job = d.JobValues;
        var cargo = job?.CargoValues;
        var truck = d.TruckValues;
        var consts = truck?.ConstantsValues;
        var cur = truck?.CurrentValues;
        var dash = cur?.DashboardValues;
        var dmg = cur?.DamageValues;
        var pos = truck?.Positioning?.HeadPositionInWorldSpace;
        var nav = d.NavigationValues;
        var spec = d.SpecialEventsValues;
        var gp = d.GamePlay;
        var trailer = (d.TrailerValues != null && d.TrailerValues.Length > 0) ? d.TrailerValues[0] : null;

        // The plugin clears JobValues the moment a job ends, so income of zero with
        // no destination means there is no live job, not a job worth nothing.
        var hasJob = (spec?.OnJob ?? false) && !string.IsNullOrEmpty(job?.CityDestinationId);

        return new Snapshot {
            Connected = d.SdkActive,
            Paused = d.Paused,
            Game = d.Game.ToString(),
            GameVersion = $"{d.GameVersion?.Major ?? 0}.{d.GameVersion?.Minor ?? 0}",
            GameTimeMin = d.CommonValues?.GameTime?.Value ?? 0,

            OnJob = hasJob,
            Job = hasJob
                ? new JobInfo {
                    SourceCity = job!.CitySource ?? "",
                    SourceCityId = job.CitySourceId ?? "",
                    SourceCompany = job.CompanySource ?? "",
                    SourceCompanyId = job.CompanySourceId ?? "",
                    DestinationCity = job.CityDestination ?? "",
                    DestinationCityId = job.CityDestinationId ?? "",
                    DestinationCompany = job.CompanyDestination ?? "",
                    DestinationCompanyId = job.CompanyDestinationId ?? "",
                    Cargo = cargo?.Name ?? "",
                    CargoId = cargo?.Id ?? "",
                    CargoMassKg = cargo?.Mass ?? 0,
                    Income = job.Income,
                    DeadlineMin = job.DeliveryTime?.Value ?? 0,
                    PlannedDistanceKm = job.PlannedDistanceKm,
                    SpecialJob = job.SpecialJob,
                    Market = (int)job.Market,
                }
                : null,

            Truck = new TruckInfo {
                Make = consts?.Brand ?? "",
                Model = consts?.Name ?? "",
                TruckId = consts?.Id ?? "",
                OdometerKm = dash?.Odometer ?? 0,
                SpeedKmh = dash?.Speed?.Kph ?? 0,
                FuelL = dash?.FuelValue?.Amount ?? 0,
                FuelCapacityL = consts?.CapacityValues?.Fuel ?? 0,
                Wear = new TruckWear {
                    Engine = dmg?.Engine ?? 0,
                    Transmission = dmg?.Transmission ?? 0,
                    Cabin = dmg?.Cabin ?? 0,
                    Chassis = dmg?.Chassis ?? 0,
                    Wheels = dmg?.WheelsAvg ?? 0,
                },
            },

            Trailer = new TrailerInfo {
                Attached = trailer?.Attached ?? false,
                Name = trailer == null ? "" : (!string.IsNullOrEmpty(trailer.Name) ? trailer.Name : (trailer.Id ?? "")),
                TrailerId = trailer?.Id ?? "",
                Wear = TrailerWear(trailer),
                CargoDamage = trailer?.DamageValues?.Cargo ?? 0,
            },

            PosX = pos?.X ?? 0,
            PosY = pos?.Y ?? 0,
            PosZ = pos?.Z ?? 0,

            SpeedLimitKmh = nav?.SpeedLimit?.Kph ?? 0,

            CruiseControlOn = dash?.CruiseControl ?? false,
            CruiseControlSpeedKmh = dash?.CruiseControlSpeed?.Kph ?? 0,

            Events = BuildEvents(kind, gp),
        };
    }

    private static double TrailerWear(SCSTelemetry.Trailer? t) {
        if (t?.DamageValues == null) return 0;
        var dv = t.DamageValues;
        return Math.Max(dv.Body, Math.Max(dv.Chassis, dv.Wheels));
    }

    /// <summary>
    /// The recorder writes an extra line whenever a gameplay event fires, tagged
    /// with the event name. Only that line carries the event, which avoids the
    /// classic bug of firing a delivery ten times because a flag stayed set.
    /// </summary>
    private static SnapshotEvents BuildEvents(string kind, SCSTelemetry.GamePlayEvents? gp) {
        var e = new SnapshotEvents();
        if (gp == null) return e;

        switch (kind) {
            case "JobDelivered" when gp.JobDelivered != null: {
                var j = gp.JobDelivered;
                e.JobDelivered = new JobDeliveredEvent {
                    Revenue = j.Revenue,
                    EarnedXp = j.EarnedXp,
                    CargoDamage = j.CargoDamage,
                    DistanceKm = j.DistanceKm,
                    DeliveryTimeMin = j.DeliveryTime?.Value ?? 0,
                    AutoparkUsed = j.AutoParked,
                    AutoLoaded = j.AutoLoaded,
                    // Started is unreliable, the plugin author documents that it gets
                    // overwritten. StartedBackup is the value to trust.
                    StartedGameMin = j.StartedBackup?.Value ?? 0,
                    FinishedGameMin = j.Finished?.Value ?? 0,
                };
                break;
            }
            case "JobCancelled" when gp.JobCancelled != null: {
                var j = gp.JobCancelled;
                e.JobCancelled = new JobCancelledEvent {
                    Penalty = j.Penalty,
                    StartedGameMin = j.Started?.Value ?? 0,
                };
                break;
            }
            case "Fined" when gp.FinedEvent != null: {
                var f = gp.FinedEvent;
                e.Fined = new FinedEvent { Amount = f.Amount, Offence = f.Offence.ToString() };
                break;
            }
            case "Tollgate" when gp.TollgateEvent != null: {
                var t = gp.TollgateEvent;
                e.TollgatePaid = new TollgateEvent { Amount = t.PayAmount };
                break;
            }
            case "Ferry" when gp.FerryEvent != null: {
                var f = gp.FerryEvent;
                e.FerryUsed = new TransportEvent { Source = f.SourceName ?? "", Target = f.TargetName ?? "", Price = f.PayAmount };
                break;
            }
            case "Train" when gp.TrainEvent != null: {
                var t = gp.TrainEvent;
                e.TrainUsed = new TransportEvent { Source = t.SourceName ?? "", Target = t.TargetName ?? "", Price = t.PayAmount };
                break;
            }
            case "RefuelPayed" when gp.RefuelEvent != null: {
                var r = gp.RefuelEvent;
                e.RefuelPaid = new RefuelEvent { Amount = r.Amount };
                break;
            }
        }

        return e;
    }

    // --- Offline replay path (--replay <file>) ---
    //
    // SCSTelemetry's world-position getters (HeadPositionInWorldSpace and friends)
    // are computed from an `internal` TruckPosition field that only the live SDK
    // struct converter ever populates - it never round-trips through JSON, so a
    // recorded line can't be deserialized back into a working SCSTelemetry object.
    // Reading the raw JSON tree directly sidesteps that entirely. Keep this in sync
    // with ToSnapshot() above - same fields, same fallbacks, just a JObject source
    // instead of a live object.

    public static Snapshot FromRecordedJson(JObject d, string kind) {
        var job = d["JobValues"];
        var cargo = job?["CargoValues"];
        var truck = d["TruckValues"];
        var consts = truck?["ConstantsValues"];
        var cur = truck?["CurrentValues"];
        var dash = cur?["DashboardValues"];
        var dmg = cur?["DamageValues"];
        var pos = truck?["Positioning"]?["HeadPositionInWorldSpace"];
        var nav = d["NavigationValues"];
        var spec = d["SpecialEventsValues"];
        var gp = d["GamePlay"];
        var trailerValues = d["TrailerValues"] as JArray;
        var trailer = trailerValues is { Count: > 0 } ? trailerValues[0] : null;

        var hasJob = B(spec?["OnJob"]) && !string.IsNullOrEmpty(S(job?["CityDestinationId"]));

        return new Snapshot {
            Connected = B(d["SdkActive"]),
            Paused = B(d["Paused"]),
            // The recorder serializes the SCSGame enum as its raw int (no
            // StringEnumConverter configured), so match d.Game.ToString() from
            // the live path above by name instead of leaving "0"/"1"/"2" in
            // stored/exported data.
            Game = ((SCSGame)(int)N(d["Game"])).ToString(),
            GameVersion = $"{N(d["GameVersion"]?["Major"])}.{N(d["GameVersion"]?["Minor"])}",
            GameTimeMin = N(d["CommonValues"]?["GameTime"]?["Value"]),

            OnJob = hasJob,
            Job = hasJob
                ? new JobInfo {
                    SourceCity = S(job?["CitySource"]),
                    SourceCityId = S(job?["CitySourceId"]),
                    SourceCompany = S(job?["CompanySource"]),
                    SourceCompanyId = S(job?["CompanySourceId"]),
                    DestinationCity = S(job?["CityDestination"]),
                    DestinationCityId = S(job?["CityDestinationId"]),
                    DestinationCompany = S(job?["CompanyDestination"]),
                    DestinationCompanyId = S(job?["CompanyDestinationId"]),
                    Cargo = S(cargo?["Name"]),
                    CargoId = S(cargo?["Id"]),
                    CargoMassKg = N(cargo?["Mass"]),
                    Income = N(job?["Income"]),
                    DeadlineMin = N(job?["DeliveryTime"]?["Value"]),
                    PlannedDistanceKm = N(job?["PlannedDistanceKm"]),
                    SpecialJob = B(job?["SpecialJob"]),
                    Market = (int)N(job?["Market"]),
                }
                : null,

            Truck = new TruckInfo {
                Make = S(consts?["Brand"]),
                Model = S(consts?["Name"]),
                TruckId = S(consts?["Id"]),
                OdometerKm = N(dash?["Odometer"]),
                SpeedKmh = N(dash?["Speed"]?["Kph"]),
                FuelL = N(dash?["FuelValue"]?["Amount"]),
                FuelCapacityL = N(consts?["CapacityValues"]?["Fuel"]),
                Wear = new TruckWear {
                    Engine = N(dmg?["Engine"]),
                    Transmission = N(dmg?["Transmission"]),
                    Cabin = N(dmg?["Cabin"]),
                    Chassis = N(dmg?["Chassis"]),
                    Wheels = N(dmg?["WheelsAvg"]),
                },
            },

            Trailer = new TrailerInfo {
                Attached = B(trailer?["Attached"]),
                Name = trailer == null ? "" : (S(trailer["Name"]) is { Length: > 0 } n ? n : S(trailer["Id"])),
                TrailerId = S(trailer?["Id"]),
                Wear = TrailerWearJson(trailer?["DamageValues"]),
                CargoDamage = N(trailer?["DamageValues"]?["Cargo"]),
            },

            PosX = N(pos?["X"]),
            PosY = N(pos?["Y"]),
            PosZ = N(pos?["Z"]),

            SpeedLimitKmh = N(nav?["SpeedLimit"]?["Kph"]),

            CruiseControlOn = B(dash?["CruiseControl"]),
            CruiseControlSpeedKmh = N(dash?["CruiseControlSpeed"]?["Kph"]),

            Events = BuildEventsJson(kind, gp),
        };
    }

    private static double TrailerWearJson(JToken? dv) {
        if (dv == null) return 0;
        return Math.Max(N(dv["Body"]), Math.Max(N(dv["Chassis"]), N(dv["Wheels"])));
    }

    private static SnapshotEvents BuildEventsJson(string kind, JToken? gp) {
        var e = new SnapshotEvents();
        if (gp == null) return e;

        switch (kind) {
            case "JobDelivered": {
                var j = gp["JobDelivered"];
                if (j == null) break;
                var finished = N(j["Finished"]?["Value"]);
                var deliveryTime = N(j["DeliveryTime"]?["Value"]);
                e.JobDelivered = new JobDeliveredEvent {
                    Revenue = N(j["Revenue"]),
                    EarnedXp = N(j["EarnedXp"]),
                    CargoDamage = N(j["CargoDamage"]),
                    DistanceKm = N(j["DistanceKm"]),
                    DeliveryTimeMin = deliveryTime,
                    AutoparkUsed = B(j["AutoParked"]),
                    AutoLoaded = B(j["AutoLoaded"]),
                    // StartedBackup isn't in the JSON (see the class comment on that
                    // property) - it's just Finished - DeliveryTime, in game minutes.
                    StartedGameMin = finished - deliveryTime,
                    FinishedGameMin = finished,
                };
                break;
            }
            case "JobCancelled": {
                var j = gp["JobCancelled"];
                if (j == null) break;
                e.JobCancelled = new JobCancelledEvent {
                    Penalty = N(j["Penalty"]),
                    StartedGameMin = N(j["Started"]?["Value"]),
                };
                break;
            }
            case "Fined": {
                var f = gp["FinedEvent"];
                if (f == null) break;
                e.Fined = new FinedEvent { Amount = N(f["Amount"]), Offence = S(f["Offence"]) };
                break;
            }
            case "Tollgate": {
                var t = gp["TollgateEvent"];
                if (t == null) break;
                e.TollgatePaid = new TollgateEvent { Amount = N(t["PayAmount"]) };
                break;
            }
            case "Ferry": {
                var f = gp["FerryEvent"];
                if (f == null) break;
                e.FerryUsed = new TransportEvent { Source = S(f["SourceName"]), Target = S(f["TargetName"]), Price = N(f["PayAmount"]) };
                break;
            }
            case "Train": {
                var t = gp["TrainEvent"];
                if (t == null) break;
                e.TrainUsed = new TransportEvent { Source = S(t["SourceName"]), Target = S(t["TargetName"]), Price = N(t["PayAmount"]) };
                break;
            }
            case "RefuelPayed": {
                var r = gp["RefuelEvent"];
                if (r == null) break;
                e.RefuelPaid = new RefuelEvent { Amount = N(r["Amount"]) };
                break;
            }
        }

        return e;
    }

    private static double N(JToken? t) => t != null && t.Type != JTokenType.Null ? (double)t : 0;
    private static string S(JToken? t) => t != null && t.Type != JTokenType.Null ? (string?)t ?? "" : "";
    private static bool B(JToken? t) => t != null && t.Type != JTokenType.Null && (bool)t;
}
