using System;
using System.Collections.Generic;
using System.Linq;

namespace Waybill.Tracking;

/// <summary>What kind of thing an award is, which is also how it is measured.</summary>
public enum AwardKind {
    /// <summary>A total that only ever climbs: kilometres, deliveries, money. Every
    /// delivery moves it, and it can never be lost.</summary>
    Milestone,
    /// <summary>Something done in one go, on one delivery or in one sitting. Either
    /// it happened or it did not.</summary>
    Feat,
    /// <summary>Ground covered: states driven in, kinds of cargo carried, trucks
    /// driven. Measured by how many different ones there have been.</summary>
    Collection,
}

/// <summary>One award, as it is defined rather than as it stands.</summary>
public sealed class Award {
    public string Id = "";
    public AwardKind Kind;
    /// <summary>Which game it is about. Empty means both together: a hundred
    /// deliveries is a hundred deliveries whichever continent they were on.</summary>
    public string Game = "";
    /// <summary>The family it belongs to, which is what the sentence under its name
    /// comes from. One sentence serves every step of a family rather than each award
    /// carrying its own paragraph in five languages.</summary>
    public string Family = "";
    /// <summary>How much of the thing it takes. Feats are all one.</summary>
    public double Target = 1;
    /// <summary>How the target is written out: a count, a distance in km, money, or
    /// hours.</summary>
    public string Unit = "count";
}

/// <summary>Where an award stands for this driver.</summary>
public sealed class AwardStanding {
    public Award Award = new();
    public double Progress;
    public bool Unlocked;
    public DateTime? At;
    public long? DeliveryId;
}

/// <summary>
/// The things worth having done.
///
/// Three kinds, because three different questions are being asked. A milestone asks
/// how far you have come and can only climb. A feat asks whether a single drive went
/// a particular way, and is either done or not. A collection asks how much of the
/// map, the market or the garage you have seen.
///
/// Nothing here is judged and nothing is lost: an award is a record of something
/// that happened, in a project whose first rule is that a delivery is never taken
/// away from the driver.
///
/// Imported deliveries are not counted. A row from TrucksBook carries a distance and
/// a payout and nothing else, no damage, no fines, no route, so half of these could
/// never be true of it and the other half would be true for free. The driver asked
/// for it that way, and it is the honest reading: these are for what Waybill watched.
/// </summary>
public static class Awards {
    public static readonly IReadOnlyList<Award> All = Build();

    private static List<Award> Build() {
        var list = new List<Award>();

        void Step(string family, AwardKind kind, string unit, string game, params double[] targets) {
            foreach (var t in targets) {
                var name = game.Length > 0 ? $"{family}.{game.ToLowerInvariant()}" : family;
                list.Add(new Award {
                    Id = $"{name}.{t:0}", Kind = kind, Family = family,
                    Game = game, Target = t, Unit = unit,
                });
            }
        }

        // ---------------- how far you have come
        Step("distance", AwardKind.Milestone, "km", "", 1_000, 10_000, 50_000, 100_000);
        Step("distance", AwardKind.Milestone, "km", "Ats", 10_000);
        Step("distance", AwardKind.Milestone, "km", "Ets2", 10_000);
        Step("deliveries", AwardKind.Milestone, "count", "", 10, 50, 100, 500);
        Step("earned", AwardKind.Milestone, "money", "", 100_000, 1_000_000);
        Step("xp", AwardKind.Milestone, "count", "", 10_000, 100_000);
        Step("wheel", AwardKind.Milestone, "hours", "", 24, 100);
        Step("slept", AwardKind.Milestone, "hours", "", 100);

        // ---------------- what one drive was like
        Step("longhaul", AwardKind.Feat, "count", "", 1);
        Step("spotless", AwardKind.Feat, "count", "", 1);
        Step("lawful", AwardKind.Feat, "count", "", 1);
        Step("oversize", AwardKind.Feat, "count", "", 1);
        Step("roadtrain", AwardKind.Feat, "count", "", 1);
        Step("electric", AwardKind.Feat, "count", "", 1);
        Step("nightshift", AwardKind.Feat, "count", "", 1);

        // ---------------- what one sitting was like
        Step("threeinone", AwardKind.Feat, "count", "", 1);
        Step("longsitting", AwardKind.Feat, "count", "", 1);

        // ---------------- ground covered
        Step("regions", AwardKind.Collection, "count", "", 5, 15);
        Step("cargo", AwardKind.Collection, "count", "", 10, 25);
        Step("trucks", AwardKind.Collection, "count", "", 3, 8);
        Step("cities", AwardKind.Collection, "count", "", 10, 30);

        return list;
    }

    /// <summary>
    /// Where every award stands, and which of them were reached and when.
    ///
    /// Walked in the order the deliveries happened rather than added up at the end,
    /// so a milestone can say which delivery carried it over: the hundredth is a fact
    /// about a particular evening, not about the size of a column.
    /// </summary>
    public static List<AwardStanding> Measure(IReadOnlyList<AwardDelivery> deliveries,
                                              IReadOnlyList<AwardSitting> sittings) {
        var standings = All.ToDictionary(a => a.Id, a => new AwardStanding { Award = a });

        var totals = new Dictionary<string, double>();
        var seen = new Dictionary<string, HashSet<string>>();
        double Total(string key) => totals.TryGetValue(key, out var v) ? v : 0;
        void Add(string key, double by) => totals[key] = Total(key) + by;
        HashSet<string> Set(string key) => seen.TryGetValue(key, out var s) ? s : seen[key] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var d in deliveries.OrderBy(d => d.FinishedAtMs)) {
            Add("distance", d.DistanceKm);
            Add($"distance.{d.Game.ToLowerInvariant()}", d.DistanceKm);
            Add("deliveries", 1);
            Add("earned", d.Paid);
            Add("xp", d.Xp);
            Add("wheel", d.DrivingMs / 3600000.0);
            Add("slept", d.RestMinutes / 60.0);

            foreach (var region in d.Regions) Set("regions").Add(region);
            foreach (var city in d.Cities) Set("cities").Add(city);
            if (d.Cargo.Length > 0) Set("cargo").Add(d.Cargo);
            if (d.Truck.Length > 0) Set("trucks").Add(d.Truck);

            // A drive can carry a feat and a milestone at the same tick, so the feats
            // are asked about here rather than in a pass of their own.
            if (d.DistanceKm >= 1_000) Reach("longhaul.1", d);
            if (d.Spotless) Reach("spotless.1", d);
            if (d.Fines == 0 && d.Collisions == 0 && d.DistanceKm >= 500) Reach("lawful.1", d);
            if (d.Special) Reach("oversize.1", d);
            if (d.Units >= 5) Reach("roadtrain.1", d);
            if (d.Electric) Reach("electric.1", d);
            if (d.Overnight) Reach("nightshift.1", d);

            foreach (var s in standings.Values.Where(s => !s.Unlocked && s.Award.Kind != AwardKind.Feat)) {
                s.Progress = Standing(s.Award, Total, Set);
                if (s.Progress >= s.Award.Target) {
                    s.Unlocked = true;
                    s.At = DateTimeOffset.FromUnixTimeMilliseconds(d.FinishedAtMs).LocalDateTime;
                    s.DeliveryId = d.Id;
                }
            }
        }

        // What a whole evening was like, which no single delivery knows.
        foreach (var s in sittings.OrderBy(s => s.EndedAtMs)) {
            if (s.Deliveries >= 3) ReachAt("threeinone.1", s.EndedAtMs);
            if (s.DistanceKm >= 1_000) ReachAt("longsitting.1", s.EndedAtMs);
        }

        // Anything still locked keeps the progress it got to.
        foreach (var s in standings.Values.Where(s => !s.Unlocked && s.Award.Kind != AwardKind.Feat)) {
            s.Progress = Standing(s.Award, Total, Set);
        }

        return standings.Values
            .OrderBy(s => s.Award.Kind)
            .ThenBy(s => s.Award.Family, StringComparer.Ordinal)
            .ThenBy(s => s.Award.Target)
            .ToList();

        void Reach(string id, AwardDelivery d) {
            if (!standings.TryGetValue(id, out var s) || s.Unlocked) return;
            s.Unlocked = true;
            s.Progress = 1;
            s.At = DateTimeOffset.FromUnixTimeMilliseconds(d.FinishedAtMs).LocalDateTime;
            s.DeliveryId = d.Id;
        }

        void ReachAt(string id, long atMs) {
            if (!standings.TryGetValue(id, out var s) || s.Unlocked) return;
            s.Unlocked = true;
            s.Progress = 1;
            s.At = DateTimeOffset.FromUnixTimeMilliseconds(atMs).LocalDateTime;
        }
    }

    private static double Standing(Award a, Func<string, double> total, Func<string, HashSet<string>> set) =>
        a.Kind == AwardKind.Collection
            ? set(a.Family).Count
            : total(a.Game.Length > 0 ? $"{a.Family}.{a.Game.ToLowerInvariant()}" : a.Family);
}

/// <summary>One delivery, reduced to what an award needs to know about it.</summary>
public sealed class AwardDelivery {
    public long Id;
    public long FinishedAtMs;
    public string Game = "";
    public double DistanceKm;
    public double Paid;
    public int Xp;
    public long DrivingMs;
    public double RestMinutes;
    public int Fines;
    public int Collisions;
    public int Units;
    public bool Special;
    public bool Electric;
    /// <summary>Nothing was scratched: not the truck, not the trailer, not the load.</summary>
    public bool Spotless;
    /// <summary>Begun on one day and finished on another, by the clock on the wall.</summary>
    public bool Overnight;
    public string Cargo = "";
    public string Truck = "";
    /// <summary>The states or countries this delivery touched, as codes.</summary>
    public List<string> Regions = new();
    public List<string> Cities = new();
}

/// <summary>One sitting, reduced the same way.</summary>
public sealed class AwardSitting {
    public long EndedAtMs;
    public int Deliveries;
    public double DistanceKm;
}
