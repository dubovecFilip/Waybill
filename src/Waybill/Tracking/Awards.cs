using System;
using System.Collections.Generic;
using System.Linq;

namespace Waybill.Tracking;

/// <summary>Which shelf an award sits on. Only a grouping for the eye: the rules
/// themselves live in <see cref="Awards.Measure"/>.</summary>
public enum AwardGroup {
    /// <summary>True of either game, and counted across both together.</summary>
    Shared,
    /// <summary>Euro Truck Simulator 2 only.</summary>
    Ets2,
    /// <summary>American Truck Simulator only.</summary>
    Ats,
    /// <summary>Not named until it is earned.</summary>
    Secret,
}

/// <summary>One award, as it is defined rather than as it stands.</summary>
public sealed class Award {
    public string Id = "";
    /// <summary>The name on the badge. Left in English on purpose, the way a stamp in
    /// a passport is: it is a title rather than a label, and the sentence under it,
    /// which is the part that explains anything, is translated.</summary>
    public string Name = "";
    public AwardGroup Group;
    /// <summary>Which game it is about. Empty means both together.</summary>
    public string Game = "";
    /// <summary>How much of the thing it takes. One for anything that either happened
    /// or did not.</summary>
    public double Threshold = 1;
    /// <summary>How the threshold is written out: a count, kilometres, miles or money.</summary>
    public string Unit = "count";
    /// <summary>Whether doing it again counts again.</summary>
    public bool Repeatable;
    public bool Secret;
    /// <summary>What it is worth in Waybill experience, which is Waybill's own and has
    /// nothing to do with the experience either game pays.</summary>
    public int Xp;
}

/// <summary>Where an award stands for this driver.</summary>
public sealed class AwardStanding {
    public Award Award = new();
    public double Progress;
    public int TimesEarned;
    public bool Earned => TimesEarned > 0;
    public DateTime? FirstAt;
    public DateTime? LastAt;
    /// <summary>The delivery that earned it, or that earned it most recently.</summary>
    public long? DeliveryId;
    public int TotalXp => Award.Xp * TimesEarned;
}

/// <summary>The driver's standing overall, which is what the top of the page says.</summary>
public sealed class AwardProfile {
    public int Xp;
    public int Level = 1;
    /// <summary>Experience at the start of this level, and at the start of the next,
    /// so the bar between them can be drawn.</summary>
    public int LevelFrom;
    public int LevelTo;
    /// <summary>How many different awards have been earned at least once, out of how
    /// many there are. Secret ones count in the total: they are there to be found.</summary>
    public int Unique;
    public int Possible;
    /// <summary>Every earning, repeats included.</summary>
    public int Earned;
}

/// <summary>
/// The things worth having done.
///
/// Nothing here is judged and nothing is lost. An award is a record of something that
/// happened, in a program whose first rule is that a delivery is never taken away from
/// the driver, so a counter only ever climbs and an award once earned stays earned
/// even if the rule behind it is rewritten afterwards.
///
/// Three things about the shape of it, since they are decisions rather than details.
///
/// Distance is kept apart by game and never converted: Europe counts in kilometres and
/// America in miles, so a thousand miles is its own award and not a rounding of a
/// thousand kilometres. An award can be repeatable, in which case doing it again counts
/// again and pays again, which is what makes a clean run worth keeping up rather than
/// worth doing once. And what a run of deliveries was like is asked in the order they
/// happened rather than of the pile at the end, so a streak is a real streak.
///
/// Imported deliveries are not counted. A row from TrucksBook carries a distance and a
/// payout and nothing else, no damage, no fines, no route, so most of these could never
/// be true of it and the rest would be true for nothing.
/// </summary>
public static class Awards {
    public static readonly IReadOnlyList<Award> All = Build();

    /// <summary>A mile in kilometres. America counts in miles and is never converted
    /// into kilometres to be compared with Europe, so the ladder needs this.</summary>
    public const double MilesPerKm = 0.621371;

    private static readonly Dictionary<string, int> Order =
        All.Select((a, i) => (a.Id, i)).ToDictionary(x => x.Id, x => x.i);

    /// <summary>Anything at least this heavy is a heavy haul. Both games load most
    /// ordinary trailers to somewhere under twenty tonnes, so this is the line where a
    /// load starts to be the reason the drive was slow.</summary>
    public const double HeavyKg = 24_000;

    /// <summary>How far either side of the deadline still counts as on time, in the
    /// game's own minutes.</summary>
    public const double TimingSlackMin = 15;

    private static readonly HashSet<string> WesternEurope = new(StringComparer.OrdinalIgnoreCase) {
        "PT", "ES", "FR", "BE", "NL", "LU", "GB", "IE", "IT", "CH", "DE", "DK", "NO", "SE", "FI", "IS",
    };
    private static readonly HashSet<string> EasternEurope = new(StringComparer.OrdinalIgnoreCase) {
        "PL", "CZ", "SK", "HU", "AT", "SI", "HR", "BA", "RS", "ME", "MK", "AL", "GR", "BG", "RO",
        "MD", "UA", "BY", "LT", "LV", "EE", "RU", "TR",
    };
    private static readonly HashSet<string> DesertStates = new(StringComparer.OrdinalIgnoreCase) {
        "NV", "AZ", "NM", "UT", "TX",
    };
    private static readonly HashSet<string> MountainStates = new(StringComparer.OrdinalIgnoreCase) {
        "CO", "WY", "MT", "ID", "UT",
    };
    /// <summary>What a load has to be about to count as farm work. Matched on the
    /// cargo name, which is the only thing either game says about what it is.</summary>
    private static readonly string[] FarmCargo = {
        "grain", "corn", "wheat", "barley", "oats", "hay", "silage", "soy", "rice",
        "livestock", "cattle", "cows", "pigs", "poultry", "chicken", "feed",
        "fertilizer", "fertiliser", "manure", "seed", "potato", "sugar beet", "beet",
        "tractor", "harvester", "combine", "plough", "plow", "farm",
    };

    private static List<Award> Build() {
        var list = new List<Award>();

        void Add(string id, string name, AwardGroup group, int xp, double threshold = 1,
                 string unit = "count", bool repeatable = false, string game = "") =>
            list.Add(new Award {
                Id = id, Name = name, Group = group, Xp = xp, Threshold = threshold,
                Unit = unit, Repeatable = repeatable, Game = game,
                Secret = group == AwardGroup.Secret,
            });

        // ---------------- true of either game
        Add("first_delivery", "FIRST DELIVERY", AwardGroup.Shared, 25);
        Add("road_warrior", "ROAD WARRIOR", AwardGroup.Shared, 50, 10);
        Add("reliable_driver", "RELIABLE DRIVER", AwardGroup.Shared, 100, 25);
        Add("clean_record", "CLEAN RECORD", AwardGroup.Shared, 100, 10, repeatable: true);
        Add("perfect_delivery", "PERFECT DELIVERY", AwardGroup.Shared, 50, repeatable: true);
        Add("flawless_haul", "FLAWLESS HAUL", AwardGroup.Shared, 30, repeatable: true);
        Add("perfect_streak", "PERFECT STREAK", AwardGroup.Shared, 150, 10, repeatable: true);
        Add("company_hopper", "COMPANY HOPPER", AwardGroup.Shared, 100, 10);
        Add("business_partner", "BUSINESS PARTNER", AwardGroup.Shared, 200, 25);
        Add("cargo_master", "CARGO MASTER", AwardGroup.Shared, 150, 25);
        Add("explorer", "EXPLORER", AwardGroup.Shared, 100, 25);
        Add("globetrotter", "GLOBETROTTER", AwardGroup.Shared, 250, 50);
        Add("professional", "PROFESSIONAL", AwardGroup.Shared, 200, 100);
        Add("veteran", "VETERAN", AwardGroup.Shared, 500, 500);
        Add("thousand_jobs", "ROAD LEGEND", AwardGroup.Shared, 1_000, 1_000);
        Add("night_owl", "NIGHT OWL", AwardGroup.Shared, 100, 10);
        Add("early_bird", "EARLY BIRD", AwardGroup.Shared, 100, 10);
        Add("heavy_hauler", "HEAVY HAULER", AwardGroup.Shared, 150, 10);
        Add("toll_collector", "TOLL COLLECTOR", AwardGroup.Shared, 100, 50);
        Add("ferry_master", "FERRY MASTER", AwardGroup.Shared, 100, 10);
        Add("cross_border", "CROSS-BORDER", AwardGroup.Shared, 100, 10);
        Add("fuel_saver", "FUEL SAVER", AwardGroup.Shared, 100);
        Add("money_maker", "MONEY MAKER", AwardGroup.Shared, 100, 100_000, "money");
        Add("road_tycoon", "ROAD TYCOON", AwardGroup.Shared, 500, 1_000_000, "money");
        Add("waybill_user", "WAYBILL USER", AwardGroup.Shared, 25);
        Add("tracking_veteran", "TRACKING VETERAN", AwardGroup.Shared, 250, 100);

        // ---------------- how far, in Europe, in kilometres
        var euro = new (string Id, string Name, int Xp, double Km)[] {
            ("ets2_1k", "FIRST 1,000 KM", 25, 1_000),
            ("ets2_5k", "ROAD TRIP", 50, 5_000),
            ("ets2_10k", "LONG HAULER", 100, 10_000),
            ("ets2_25k", "EUROPEAN DRIVER", 200, 25_000),
            ("ets2_50k", "ROAD VETERAN", 400, 50_000),
            ("ets2_100k", "ROAD LEGEND", 750, 100_000),
            ("ets2_250k", "ENDLESS ROAD", 1_500, 250_000),
            ("ets2_500k", "ETERNAL DRIVER", 3_000, 500_000),
            ("ets2_1m", "MILLION KILOMETRES", 5_000, 1_000_000),
        };
        foreach (var a in euro) Add(a.Id, a.Name, AwardGroup.Ets2, a.Xp, a.Km, "km", game: "Ets2");

        // ---------------- how far, in America, in miles
        var states = new (string Id, string Name, int Xp, double Miles)[] {
            ("ats_1k", "FIRST 1,000 MILES", 25, 1_000),
            ("ats_5k", "ROAD TRIP", 50, 5_000),
            ("ats_10k", "LONG HAULER", 100, 10_000),
            ("ats_25k", "AMERICAN DRIVER", 200, 25_000),
            ("ats_50k", "ROAD VETERAN", 400, 50_000),
            ("ats_100k", "ROAD LEGEND", 750, 100_000),
            ("ats_250k", "ENDLESS ROAD", 1_500, 250_000),
            ("ats_500k", "ETERNAL DRIVER", 3_000, 500_000),
            ("ats_1m", "MILLION MILES", 5_000, 1_000_000),
        };
        foreach (var a in states) Add(a.Id, a.Name, AwardGroup.Ats, a.Xp, a.Miles, "miles", game: "Ats");

        // ---------------- Europe
        Add("european_tour", "EUROPEAN TOUR", AwardGroup.Ets2, 150, 10, game: "Ets2");
        Add("euro_traveller", "EURO TRAVELLER", AwardGroup.Ets2, 300, 20, game: "Ets2");
        Add("continental", "CONTINENTAL", AwardGroup.Ets2, 500, 30, game: "Ets2");
        Add("border_hopper", "BORDER HOPPER", AwardGroup.Ets2, 150, 25, game: "Ets2");
        Add("channel_crossing", "CHANNEL CROSSING", AwardGroup.Ets2, 200, game: "Ets2");
        Add("east_meets_west", "EAST MEETS WEST", AwardGroup.Ets2, 150, game: "Ets2");
        Add("european_network", "EUROPEAN NETWORK", AwardGroup.Ets2, 500, 25, game: "Ets2");

        // ---------------- America
        Add("american_road_trip", "AMERICAN ROAD TRIP", AwardGroup.Ats, 100, 5, game: "Ats");
        Add("state_hopper", "STATE HOPPER", AwardGroup.Ats, 250, 10, game: "Ats");
        Add("state_line", "STATE LINE", AwardGroup.Ats, 150, 25, game: "Ats");
        Add("big_rig", "BIG RIG", AwardGroup.Ats, 150, 10, game: "Ats");
        Add("oversize_america", "OVERSIZE AMERICA", AwardGroup.Ats, 250, 10, game: "Ats");
        Add("desert_runner", "DESERT RUNNER", AwardGroup.Ats, 150, 10, game: "Ats");
        Add("mountain_driver", "MOUNTAIN DRIVER", AwardGroup.Ats, 150, 10, game: "Ats");
        Add("heartland", "HEARTLAND", AwardGroup.Ats, 150, 10, game: "Ats");
        Add("american_industry", "AMERICAN INDUSTRY", AwardGroup.Ats, 250, 25, game: "Ats");
        Add("the_open_road", "THE OPEN ROAD", AwardGroup.Ats, 150, 10, game: "Ats");

        // ---------------- not named until they are found
        Add("how_did_you_do_that", "HOW DID YOU DO THAT?", AwardGroup.Secret, 50);
        Add("the_clock_is_ticking", "THE CLOCK IS TICKING", AwardGroup.Secret, 100);
        Add("perfect_timing", "PERFECT TIMING", AwardGroup.Secret, 150, 5, repeatable: true);
        Add("not_even_a_scratch", "NOT EVEN A SCRATCH", AwardGroup.Secret, 100, repeatable: true);
        Add("ghost_driver", "GHOST DRIVER", AwardGroup.Secret, 200, repeatable: true);
        Add("wrong_turn", "WRONG TURN", AwardGroup.Secret, 75);
        Add("i_know_a_shortcut", "I KNOW A SHORTCUT", AwardGroup.Secret, 100);
        Add("the_unstoppable", "THE UNSTOPPABLE", AwardGroup.Secret, 250);
        Add("one_more_job", "ONE MORE JOB", AwardGroup.Secret, 100, 5, repeatable: true);
        Add("busy_day", "BUSY DAY", AwardGroup.Secret, 250, 10, repeatable: true);
        Add("legendary_haul", "LEGENDARY HAUL", AwardGroup.Secret, 500, 10, repeatable: true);

        return list;
    }

    /// <summary>
    /// What each level costs.
    ///
    /// A level takes fifty experience more than the one before it, so the first is a
    /// hundred and the tenth is five hundred and fifty. Levelling early should happen
    /// while a driver is finding out what the awards are, and later should take a
    /// season of driving.
    /// </summary>
    public static int LevelStart(int level) => level <= 1 ? 0 : 25 * (level * level + level - 2);

    public static int LevelAt(int xp) {
        var level = 1;
        while (LevelStart(level + 1) <= xp) level++;
        return level;
    }

    /// <summary>
    /// Where every award stands, walked in the order the deliveries happened.
    ///
    /// The order is the point. A streak is only a streak in sequence, a day's work only
    /// belongs to a day, and a milestone can name the drive that carried it over rather
    /// than being a fact about the size of a column.
    /// </summary>
    public static (List<AwardStanding> Standings, AwardProfile Profile) Measure(
            IReadOnlyList<AwardDelivery> deliveries) {
        var standings = All.ToDictionary(a => a.Id, a => new AwardStanding { Award = a });

        var totals = new Dictionary<string, double>();
        var sets = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var perDay = new Dictionary<string, int>();
        int noFineRun = 0, perfectRun = 0, onTimeRun = 0;
        var fuelSoFar = new Dictionary<bool, (double Used, double Over)>();

        double Total(string key) => totals.TryGetValue(key, out var v) ? v : 0;
        void Add(string key, double by) => totals[key] = Total(key) + by;
        HashSet<string> Set(string key) => sets.TryGetValue(key, out var s)
            ? s : sets[key] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var d in deliveries.OrderBy(d => d.FinishedAtMs)) {
            var at = DateTimeOffset.FromUnixTimeMilliseconds(d.FinishedAtMs).LocalDateTime;
            var ats = d.Game.Equals("Ats", StringComparison.OrdinalIgnoreCase);
            var ets2 = d.Game.Equals("Ets2", StringComparison.OrdinalIgnoreCase);

            // ---- what this one delivery was
            var perfect = d.CargoDamagePct <= 0 && d.Fines == 0;
            var spotless = d.CargoDamagePct <= 0;
            var fineFree = d.Fines == 0;

            Add("deliveries", 1);
            Add("earned", d.Paid);
            Add("tollgates", d.Tollgates);
            if (fineFree) Add("fineFree", 1);
            if (d.Ferries > 0) Add("ferry", 1);
            if (d.MassKg >= HeavyKg) Add("heavy", 1);
            if (at.Hour >= 22 || at.Hour < 6) Add("night", 1);
            if (at.Hour < 6) Add("dawn", 1);

            foreach (var company in d.Companies) Set("companies").Add(company);
            if (d.Cargo.Length > 0) Set("cargo").Add(d.Cargo);
            if (d.DestinationCity.Length > 0) Set("cities").Add($"{d.Game}:{d.DestinationCity}");

            // ---- Europe and America, counted apart
            if (ets2) {
                Add("ets2.km", d.DistanceKm);
                foreach (var code in d.Regions) Set("countries").Add(code);
                if (d.DestinationRegion.Length > 0) Set("deliveredCountries").Add(d.DestinationRegion);
                if (d.CrossedRegion) { Add("borders", 1); Add("crossings", 1); }
            }
            if (ats) {
                Add("ats.miles", d.DistanceKm * MilesPerKm);
                foreach (var code in d.Regions) Set("states").Add(code);
                foreach (var company in d.Companies) Set("atsCompanies").Add(company);
                if (d.CrossedRegion) Add("stateLines", 1);
                if (d.MassKg >= HeavyKg) Add("bigRig", 1);
                if (d.Special) Add("oversize", 1);
                if (d.Regions.Any(DesertStates.Contains)) Add("desert", 1);
                if (d.Regions.Any(MountainStates.Contains)) Add("mountain", 1);
                if (IsFarmCargo(d.Cargo)) Add("farm", 1);
                if (d.SpeedingFines == 0) Add("noSpeeding", 1);
            }

            // ---- the ones that are simply true of this drive
            Grant("first_delivery", d);
            Grant("waybill_user", d);
            if (perfect) Grant("perfect_delivery", d);
            if (spotless) { Grant("flawless_haul", d); Grant("how_did_you_do_that", d); }
            if (d.Collisions == 0) Grant("not_even_a_scratch", d);
            if (perfect && d.Collisions == 0) Grant("ghost_driver", d);
            if (d.PlannedKm > 0 && d.DistanceKm >= d.PlannedKm + 100) Grant("wrong_turn", d);
            if (d.PlannedKm > 0 && d.DistanceKm < d.PlannedKm) Grant("i_know_a_shortcut", d);
            if (ets2 && d.DistanceKm > 2_000) Grant("the_unstoppable", d);
            if (ats && d.DistanceKm * MilesPerKm > 1_000) Grant("the_unstoppable", d);
            if (d.MinutesLate is { } left and <= 0 and > -60) Grant("the_clock_is_ticking", d);
            if (ets2 && d.CrossedChannel) Grant("channel_crossing", d);
            if (ets2 && Divided(d, WesternEurope, EasternEurope)) Grant("east_meets_west", d);

            // Less fuel over the distance than the driver's own average until now, which
            // is why the first delivery can never win it: there is nothing to beat yet.
            // Kilowatt hours and litres are never compared, so a battery is measured
            // against batteries.
            if (d.Fuel > 0 && d.DistanceKm > 0) {
                var (usedSoFar, overSoFar) = fuelSoFar.TryGetValue(d.Electric, out var had) ? had : (0, 0);
                if (overSoFar > 0 && d.Fuel / d.DistanceKm < usedSoFar / overSoFar) Grant("fuel_saver", d);
                fuelSoFar[d.Electric] = (usedSoFar + d.Fuel, overSoFar + d.DistanceKm);
            }

            // ---- runs, which only mean anything in order
            noFineRun = fineFree ? noFineRun + 1 : 0;
            perfectRun = perfect ? perfectRun + 1 : 0;
            onTimeRun = d.MinutesLate is { } m && Math.Abs(m) <= TimingSlackMin ? onTimeRun + 1 : 0;
            if (noFineRun > 0 && noFineRun % 10 == 0) Grant("clean_record", d);
            if (perfectRun > 0 && perfectRun % 10 == 0) { Grant("perfect_streak", d); Grant("legendary_haul", d); }
            if (onTimeRun > 0 && onTimeRun % 5 == 0) Grant("perfect_timing", d);

            // ---- a day's work
            var day = at.Date.ToString("yyyy-MM-dd");
            perDay[day] = perDay.TryGetValue(day, out var done) ? done + 1 : 1;
            if (perDay[day] == 5) Grant("one_more_job", d);
            if (perDay[day] == 10) Grant("busy_day", d);

            // ---- and everything measured against a threshold
            foreach (var s in standings.Values) {
                if (s.Earned || s.Award.Threshold <= 1) continue;
                s.Progress = Standing(s.Award, Total, Set);
                if (s.Progress >= s.Award.Threshold) Grant(s.Award.Id, d);
            }
        }

        // Whatever is still out of reach keeps the progress it got to.
        foreach (var s in standings.Values.Where(s => !s.Earned && s.Award.Threshold > 1)) {
            s.Progress = Standing(s.Award, Total, Set);
        }

        var list = standings.Values
            .OrderBy(s => s.Award.Group)
            .ThenBy(s => Order[s.Award.Id])
            .ToList();

        var xp = list.Sum(s => s.TotalXp);
        var profile = new AwardProfile {
            Xp = xp,
            Level = LevelAt(xp),
            LevelFrom = LevelStart(LevelAt(xp)),
            LevelTo = LevelStart(LevelAt(xp) + 1),
            Unique = list.Count(s => s.Earned),
            Possible = list.Count,
            Earned = list.Sum(s => s.TimesEarned),
        };
        return (list, profile);

        void Grant(string id, AwardDelivery d) {
            if (!standings.TryGetValue(id, out var s)) return;
            if (s.Earned && !s.Award.Repeatable) return;
            s.TimesEarned++;
            s.Progress = Math.Max(s.Progress, s.Award.Threshold);
            var when = DateTimeOffset.FromUnixTimeMilliseconds(d.FinishedAtMs).LocalDateTime;
            s.FirstAt ??= when;
            s.LastAt = when;
            s.DeliveryId = d.Id;
        }
    }

    /// <summary>Whether the two ends of a delivery are on opposite sides of a line, in
    /// either direction.</summary>
    private static bool Divided(AwardDelivery d, HashSet<string> one, HashSet<string> other) =>
        (one.Contains(d.SourceRegion) && other.Contains(d.DestinationRegion))
        || (other.Contains(d.SourceRegion) && one.Contains(d.DestinationRegion));

    private static bool IsFarmCargo(string cargo) {
        if (cargo.Length == 0) return false;
        var name = cargo.ToLowerInvariant();
        return FarmCargo.Any(word => name.Contains(word, StringComparison.Ordinal));
    }

    private static double Standing(Award a, Func<string, double> total, Func<string, HashSet<string>> set) => a.Id switch {
        "road_warrior" or "professional" or "veteran" or "thousand_jobs" or "tracking_veteran" => total("deliveries"),
        "reliable_driver" => total("fineFree"),
        "company_hopper" or "business_partner" => set("companies").Count,
        "cargo_master" => set("cargo").Count,
        "explorer" or "globetrotter" => set("cities").Count,
        "night_owl" => total("night"),
        "early_bird" => total("dawn"),
        "heavy_hauler" => total("heavy"),
        "toll_collector" => total("tollgates"),
        "ferry_master" => total("ferry"),
        "cross_border" => total("crossings"),
        "money_maker" or "road_tycoon" => total("earned"),
        "european_tour" or "euro_traveller" or "continental" => set("countries").Count,
        "european_network" => set("deliveredCountries").Count,
        "border_hopper" => total("borders"),
        "american_road_trip" or "state_hopper" => set("states").Count,
        "state_line" => total("stateLines"),
        "big_rig" => total("bigRig"),
        "oversize_america" => total("oversize"),
        "desert_runner" => total("desert"),
        "mountain_driver" => total("mountain"),
        "heartland" => total("farm"),
        "american_industry" => set("atsCompanies").Count,
        "the_open_road" => total("noSpeeding"),
        _ => a.Unit switch {
            "km" => total("ets2.km"),
            "miles" => total("ats.miles"),
            _ => 0,
        },
    };
}

/// <summary>One delivery, reduced to what an award needs to know about it.</summary>
public sealed class AwardDelivery {
    public long Id;
    public long FinishedAtMs;
    public string Game = "";
    public double DistanceKm;
    public double PlannedKm;
    public double Paid;
    public int Fines;
    public int SpeedingFines;
    public int Collisions;
    public int Tollgates;
    public int Ferries;
    public double CargoDamagePct;
    public double MassKg;
    public bool Special;
    public bool Electric;
    /// <summary>Litres burned, or kilowatt hours drawn, over the whole delivery.</summary>
    public double Fuel;
    /// <summary>Game minutes past the deadline, negative when it was early. Null when
    /// the game never said what the deadline was.</summary>
    public double? MinutesLate;
    public string Cargo = "";
    public string DestinationCity = "";
    public string SourceRegion = "";
    public string DestinationRegion = "";
    /// <summary>Both ends are in different states or countries.</summary>
    public bool CrossedRegion;
    /// <summary>One end in Britain and the other on the continent, with a ferry or a
    /// train on the bill.</summary>
    public bool CrossedChannel;
    public List<string> Regions = new();
    public List<string> Companies = new();
}
