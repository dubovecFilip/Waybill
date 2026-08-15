using System.Linq;
using Microsoft.Data.Sqlite;
using Newtonsoft.Json;
using Waybill.Tracking;

namespace Waybill.Storage;

/// <summary>
/// Local-first SQLite storage for finished deliveries, per the project's data
/// model (deliveries + events tables). Lives outside bin/ (see DbPath) so a
/// rebuild or `dotnet clean` never wipes delivery history.
/// </summary>
public class DeliveryStore : IDisposable {
    public string DbPath { get; }

    private readonly SqliteConnection _conn;

    /// <summary>
    /// One SQLite connection serves both the tracking engine (background thread,
    /// writing finished deliveries) and the window (UI thread, reading history).
    /// A connection is not safe to use from two threads at once, so every public
    /// operation takes this lock.
    /// </summary>
    private readonly object _gate = new();

    public DeliveryStore(string? dbPath = null) {
        DbPath = dbPath ?? DefaultPath();
        Directory.CreateDirectory(Path.GetDirectoryName(DbPath)!);

        _conn = new SqliteConnection($"Data Source={DbPath}");
        _conn.Open();
        CreateSchema();
        MigrateSchema();
    }

    /// <summary>
    /// `CREATE TABLE IF NOT EXISTS` does nothing to a table that already exists, so
    /// columns added later would silently be missing on any database created before
    /// them. Adding whatever the current schema expects and the file doesn't have
    /// keeps old databases working without anyone having to delete their history.
    /// </summary>
    private void MigrateSchema() {
        var expected = new (string Table, string Column, string Definition)[] {
            ("deliveries", "driving_style", "TEXT"),
            ("deliveries", "hard_speeding_share", "REAL"),
            ("deliveries", "world_distance_km", "REAL"),
            ("deliveries", "sim_speed_distance_km", "REAL"),
            ("deliveries", "driving_game_min", "REAL"),
            ("deliveries", "delivery_time_min", "REAL"),
            ("deliveries", "collisions", "INTEGER"),
            ("deliveries", "late_delivery", "INTEGER"),
            ("deliveries", "minutes_late", "REAL"),
            ("deliveries", "cruise_control_share", "REAL"),
            ("deliveries", "rest_stops", "INTEGER"),
            ("deliveries", "rest_minutes", "REAL"),
            ("deliveries", "notes", "TEXT DEFAULT ''"),
            // Where the row came from: this app's own tracking, or an import.
            ("deliveries", "source", "TEXT DEFAULT 'waybill'"),
            ("deliveries", "xp", "INTEGER"),
            ("deliveries", "job_type", "TEXT"),
            // The coupled set: how the game names the configuration, whether the
            // trailer was the driver's own, and each unit with what it took. Stored
            // as JSON because it is a list whose length is the point, and the events
            // table already keeps its extra detail the same way.
            ("deliveries", "trailer_chain_type", "TEXT"),
            ("deliveries", "trailer_owned", "INTEGER"),
            ("deliveries", "trailer_units", "TEXT"),
        };

        foreach (var group in expected.GroupBy(e => e.Table)) {
            var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (var info = _conn.CreateCommand()) {
                info.CommandText = $"PRAGMA table_info({group.Key});";
                using var reader = info.ExecuteReader();
                while (reader.Read()) existing.Add(reader.GetString(1));
            }
            if (existing.Count == 0) continue; // table doesn't exist yet; CreateSchema owns it

            foreach (var (table, column, definition) in group) {
                if (existing.Contains(column)) continue;
                using var alter = _conn.CreateCommand();
                alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {definition};";
                alter.ExecuteNonQuery();
            }
        }
    }

    public static string DefaultDir() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Waybill");

    public static string DefaultPath() => Path.Combine(DefaultDir(), "deliveries.db");

    private void CreateSchema() {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS deliveries (
                id                      INTEGER PRIMARY KEY AUTOINCREMENT,
                job_uid                 TEXT NOT NULL UNIQUE,
                game                    TEXT,
                game_version            TEXT,
                outcome                 TEXT,
                validation_status       TEXT,
                validation_flags        TEXT,
                truck_make              TEXT,
                truck_model             TEXT,
                truck_id                TEXT,
                trailer_name            TEXT,
                trailer_id              TEXT,
                cargo                   TEXT,
                cargo_id                TEXT,
                cargo_mass_kg           REAL,
                source_city             TEXT,
                source_company          TEXT,
                destination_city        TEXT,
                destination_company     TEXT,
                planned_distance_km     REAL,
                reported_distance_km    REAL,
                actual_distance_km      REAL,
                world_distance_km       REAL,
                sim_speed_distance_km   REAL,
                driving_game_min        REAL,
                delivery_time_min       REAL,
                offered_income          REAL,
                revenue                 REAL,
                penalty                 REAL,
                fuel_used_l             REAL,
                avg_consumption_l_100km REAL,
                top_speed_kmh           REAL,
                driving_ms              INTEGER,
                paused_ms               INTEGER,
                speeding_share          REAL,
                hard_speeding_share     REAL,
                driving_style           TEXT,
                truck_damage_pct        REAL,
                trailer_damage_pct      REAL,
                cargo_damage_pct        REAL,
                tolls_paid              REAL,
                ferries_used            INTEGER,
                refuels                 INTEGER,
                collisions              INTEGER,
                late_delivery           INTEGER,
                minutes_late            REAL,
                cruise_control_share    REAL,
                rest_stops              INTEGER,
                rest_minutes            REAL,
                fines_count             INTEGER,
                fines_total             REAL,
                started_at_ms           INTEGER,
                finished_at_ms          INTEGER,
                real_duration_ms        INTEGER,
                game_duration_min       REAL,
                notes                   TEXT DEFAULT '',
                source                  TEXT DEFAULT 'waybill',
                xp                      INTEGER,
                job_type                TEXT,
                created_at              TEXT DEFAULT CURRENT_TIMESTAMP
            );

            CREATE TABLE IF NOT EXISTS events (
                id           INTEGER PRIMARY KEY AUTOINCREMENT,
                delivery_id  INTEGER NOT NULL REFERENCES deliveries(id),
                at_ms        INTEGER,
                event_type   TEXT NOT NULL,
                value        REAL,
                extra_json   TEXT
            );

            CREATE TABLE IF NOT EXISTS trip_points (
                id           INTEGER PRIMARY KEY AUTOINCREMENT,
                delivery_id  INTEGER NOT NULL REFERENCES deliveries(id),
                at_ms        INTEGER,
                x            REAL,
                y            REAL,
                z            REAL,
                speed_kmh    REAL
            );

            CREATE INDEX IF NOT EXISTS idx_events_delivery ON events(delivery_id);
            CREATE INDEX IF NOT EXISTS idx_trip_points_delivery ON trip_points(delivery_id);
            CREATE INDEX IF NOT EXISTS idx_deliveries_started ON deliveries(started_at_ms);
            """;
        cmd.ExecuteNonQuery();
    }

    public void SaveDelivery(JobRecord r) {
        lock (_gate) {
        using var tx = _conn.BeginTransaction();

        var finesTotal = r.Fines.Sum(f => f.Amount);

        // A delivery has a stable identity, so storing one that is already here is a
        // better reading of the same drive rather than a second drive. It replaces
        // the old one outright, children included: ignoring the row but inserting
        // the events again would have doubled the timeline and the route behind it.
        using (var replace = _conn.CreateCommand()) {
            replace.Transaction = tx;
            replace.CommandText = """
                DELETE FROM events WHERE delivery_id IN (SELECT id FROM deliveries WHERE job_uid = $job_uid);
                DELETE FROM trip_points WHERE delivery_id IN (SELECT id FROM deliveries WHERE job_uid = $job_uid);
                DELETE FROM deliveries WHERE job_uid = $job_uid;
                """;
            replace.Parameters.AddWithValue("$job_uid", r.JobUid);
            replace.ExecuteNonQuery();
        }

        using (var cmd = _conn.CreateCommand()) {
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO deliveries (
                    job_uid, game, game_version, outcome, validation_status, validation_flags,
                    truck_make, truck_model, truck_id, trailer_name, trailer_id,
                    cargo, cargo_id, cargo_mass_kg,
                    source_city, source_company, destination_city, destination_company,
                    planned_distance_km, reported_distance_km, actual_distance_km,
                    world_distance_km, sim_speed_distance_km, driving_game_min, delivery_time_min,
                    offered_income, revenue, penalty,
                    fuel_used_l, avg_consumption_l_100km, top_speed_kmh,
                    driving_ms, paused_ms, speeding_share, hard_speeding_share, driving_style,
                    truck_damage_pct, trailer_damage_pct, cargo_damage_pct,
                    tolls_paid, ferries_used, refuels, collisions, late_delivery, minutes_late,
                    cruise_control_share, rest_stops, rest_minutes,
                    fines_count, fines_total,
                    started_at_ms, finished_at_ms, real_duration_ms, game_duration_min,
                    job_type, trailer_chain_type, trailer_owned, trailer_units
                ) VALUES (
                    $job_uid, $game, $game_version, $outcome, $validation_status, $validation_flags,
                    $truck_make, $truck_model, $truck_id, $trailer_name, $trailer_id,
                    $cargo, $cargo_id, $cargo_mass_kg,
                    $source_city, $source_company, $destination_city, $destination_company,
                    $planned_distance_km, $reported_distance_km, $actual_distance_km,
                    $world_distance_km, $sim_speed_distance_km, $driving_game_min, $delivery_time_min,
                    $offered_income, $revenue, $penalty,
                    $fuel_used_l, $avg_consumption_l_100km, $top_speed_kmh,
                    $driving_ms, $paused_ms, $speeding_share, $hard_speeding_share, $driving_style,
                    $truck_damage_pct, $trailer_damage_pct, $cargo_damage_pct,
                    $tolls_paid, $ferries_used, $refuels, $collisions, $late_delivery, $minutes_late,
                    $cruise_control_share, $rest_stops, $rest_minutes,
                    $fines_count, $fines_total,
                    $started_at_ms, $finished_at_ms, $real_duration_ms, $game_duration_min,
                    $job_type, $trailer_chain_type, $trailer_owned, $trailer_units
                );
                """;

            cmd.Parameters.AddWithValue("$job_uid", r.JobUid);
            cmd.Parameters.AddWithValue("$game", r.Game);
            cmd.Parameters.AddWithValue("$game_version", r.GameVersion);
            cmd.Parameters.AddWithValue("$outcome", r.Outcome);
            cmd.Parameters.AddWithValue("$job_type", r.JobType);
            cmd.Parameters.AddWithValue("$trailer_chain_type", r.TrailerChainType);
            cmd.Parameters.AddWithValue("$trailer_owned", r.TrailerOwned ? 1 : 0);
            cmd.Parameters.AddWithValue("$trailer_units",
                r.TrailerUnits.Count > 0 ? Newtonsoft.Json.JsonConvert.SerializeObject(r.TrailerUnits) : "");
            cmd.Parameters.AddWithValue("$validation_status", r.Validation.Status);
            cmd.Parameters.AddWithValue("$validation_flags", string.Join(",", r.Validation.Flags));
            cmd.Parameters.AddWithValue("$truck_make", r.TruckMake);
            cmd.Parameters.AddWithValue("$truck_model", r.TruckModel);
            cmd.Parameters.AddWithValue("$truck_id", r.TruckId);
            cmd.Parameters.AddWithValue("$trailer_name", (object?)r.TrailerName ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$trailer_id", r.TrailerId);
            cmd.Parameters.AddWithValue("$cargo", r.Cargo);
            cmd.Parameters.AddWithValue("$cargo_id", r.CargoId);
            cmd.Parameters.AddWithValue("$cargo_mass_kg", r.CargoMassKg);
            cmd.Parameters.AddWithValue("$source_city", r.SourceCity);
            cmd.Parameters.AddWithValue("$source_company", r.SourceCompany);
            cmd.Parameters.AddWithValue("$destination_city", r.DestinationCity);
            cmd.Parameters.AddWithValue("$destination_company", r.DestinationCompany);
            cmd.Parameters.AddWithValue("$planned_distance_km", r.PlannedDistanceKm);
            cmd.Parameters.AddWithValue("$reported_distance_km", (object?)r.ReportedDistanceKm ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$actual_distance_km", r.DistanceKm);
            cmd.Parameters.AddWithValue("$world_distance_km", r.WorldDistanceKm);
            cmd.Parameters.AddWithValue("$sim_speed_distance_km", r.SimSpeedDistanceKm);
            cmd.Parameters.AddWithValue("$driving_game_min", r.DrivingGameMinutes);
            cmd.Parameters.AddWithValue("$delivery_time_min", (object?)r.DeliveryTimeMin ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$offered_income", r.OfferedIncome);
            cmd.Parameters.AddWithValue("$revenue", (object?)r.Revenue ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$penalty", (object?)r.Penalty ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$fuel_used_l", r.FuelUsedL);
            cmd.Parameters.AddWithValue("$avg_consumption_l_100km", (object?)r.AvgConsumptionLper100 ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$top_speed_kmh", r.TopSpeedKmh);
            cmd.Parameters.AddWithValue("$driving_ms", r.DrivingMs);
            cmd.Parameters.AddWithValue("$paused_ms", r.PausedMs);
            cmd.Parameters.AddWithValue("$speeding_share", r.SpeedingShare);
            cmd.Parameters.AddWithValue("$hard_speeding_share", r.HardSpeedingShare);
            cmd.Parameters.AddWithValue("$driving_style", r.DrivingStyle);
            cmd.Parameters.AddWithValue("$truck_damage_pct", r.TruckDamage);
            cmd.Parameters.AddWithValue("$trailer_damage_pct", r.TrailerDamage);
            cmd.Parameters.AddWithValue("$cargo_damage_pct", (object?)r.DeliveredCargoDamage ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$tolls_paid", r.TollsPaid);
            cmd.Parameters.AddWithValue("$ferries_used", r.FerriesUsed);
            cmd.Parameters.AddWithValue("$refuels", r.Refuels);
            cmd.Parameters.AddWithValue("$collisions", r.Collisions);
            cmd.Parameters.AddWithValue("$late_delivery", r.LateDelivery ? 1 : 0);
            cmd.Parameters.AddWithValue("$minutes_late", (object?)r.MinutesLate ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$cruise_control_share", r.CruiseControlShare);
            cmd.Parameters.AddWithValue("$rest_stops", r.RestStops);
            cmd.Parameters.AddWithValue("$rest_minutes", r.RestMinutes);
            cmd.Parameters.AddWithValue("$fines_count", r.Fines.Count);
            cmd.Parameters.AddWithValue("$fines_total", finesTotal);
            cmd.Parameters.AddWithValue("$started_at_ms", r.StartedAtMs);
            cmd.Parameters.AddWithValue("$finished_at_ms", r.FinishedAtMs);
            cmd.Parameters.AddWithValue("$real_duration_ms", r.RealDurationMs);
            cmd.Parameters.AddWithValue("$game_duration_min", r.GameDurationMin);

            cmd.ExecuteNonQuery();
        }

        long deliveryId;
        using (var idCmd = _conn.CreateCommand()) {
            idCmd.Transaction = tx;
            idCmd.CommandText = "SELECT id FROM deliveries WHERE job_uid = $job_uid";
            idCmd.Parameters.AddWithValue("$job_uid", r.JobUid);
            deliveryId = (long)idCmd.ExecuteScalar()!;
        }

        InsertEvents(tx, deliveryId, r);
        InsertTripPoints(tx, deliveryId, r);

        tx.Commit();
        }
    }

    /// <summary>Removes the rows that came from a TrucksBook export. The mirror of
    /// <see cref="DeleteTrackedDeliveriesWithin"/>, and the only way back out of an
    /// import: nothing regenerates these, so once they are in, they stay until asked
    /// to go.</summary>
    public int DeleteImportedDeliveries() {
        lock (_gate) {
            using var tx = _conn.BeginTransaction();

            foreach (var table in new[] { "events", "trip_points" }) {
                using var child = _conn.CreateCommand();
                child.Transaction = tx;
                child.CommandText = $"""
                    DELETE FROM {table} WHERE delivery_id IN (
                        SELECT id FROM deliveries WHERE source = 'trucksbook'
                    );
                    """;
                child.ExecuteNonQuery();
            }

            int removed;
            using (var cmd = _conn.CreateCommand()) {
                cmd.Transaction = tx;
                cmd.CommandText = "DELETE FROM deliveries WHERE source = 'trucksbook';";
                removed = cmd.ExecuteNonQuery();
            }

            tx.Commit();
            return removed;
        }
    }

    public int CountTrackedDeliveries() {
        lock (_gate) {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM deliveries WHERE source IS NULL OR source = 'waybill';";
            return Convert.ToInt32(cmd.ExecuteScalar());
        }
    }

    /// <summary>
    /// Removes tracked deliveries driven inside any of the given stretches of time,
    /// which are the periods the recordings cover and therefore the only ones a
    /// rebuild can put back. A delivery whose recording is gone falls outside all of
    /// them and stays: it is real history, and deleting it in the hope that
    /// something replaces it would be a guess made on the user's data.
    /// </summary>
    public int DeleteTrackedDeliveriesWithin(IReadOnlyCollection<(long From, long To)> spans) {
        if (spans.Count == 0) return 0;

        lock (_gate) {
        using var tx = _conn.BeginTransaction();

        var ranges = string.Join(" OR ", spans.Select((_, i) => $"(started_at_ms BETWEEN $from{i} AND $to{i})"));
        var tracked = $"SELECT id FROM deliveries WHERE (source IS NULL OR source = 'waybill') AND ({ranges})";

        void Bind(SqliteCommand cmd) {
            var i = 0;
            foreach (var (from, to) in spans) {
                cmd.Parameters.AddWithValue($"$from{i}", from);
                cmd.Parameters.AddWithValue($"$to{i}", to);
                i++;
            }
        }

        foreach (var table in new[] { "events", "trip_points" }) {
            using var child = _conn.CreateCommand();
            child.Transaction = tx;
            child.CommandText = $"DELETE FROM {table} WHERE delivery_id IN ({tracked});";
            Bind(child);
            child.ExecuteNonQuery();
        }

        int removed;
        using (var cmd = _conn.CreateCommand()) {
            cmd.Transaction = tx;
            cmd.CommandText = $"DELETE FROM deliveries WHERE id IN ({tracked});";
            Bind(cmd);
            removed = cmd.ExecuteNonQuery();
        }

        tx.Commit();
        return removed;
        }
    }

    public bool HasDelivery(string jobUid) {
        lock (_gate) {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "SELECT 1 FROM deliveries WHERE job_uid = $uid LIMIT 1;";
            cmd.Parameters.AddWithValue("$uid", jobUid);
            return cmd.ExecuteScalar() != null;
        }
    }

    /// <summary>Writes a delivery that came from an import rather than live tracking.
    /// Marked source='trucksbook' and given the "imported" verdict - there is no
    /// telemetry behind it to validate, and calling it "accepted" would put unearned
    /// confidence on a row this app never watched.</summary>
    public void InsertImported(ImportedDelivery d) {
        lock (_gate) {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            INSERT OR IGNORE INTO deliveries (
                job_uid, game, outcome, validation_status, validation_flags, source,
                truck_make, truck_model, cargo, cargo_mass_kg,
                source_city, source_company, destination_city, destination_company,
                planned_distance_km, actual_distance_km,
                offered_income, revenue, fuel_used_l, avg_consumption_l_100km,
                top_speed_kmh, cargo_damage_pct, fines_count, fines_total,
                xp, job_type, notes,
                started_at_ms, finished_at_ms, real_duration_ms
            ) VALUES (
                $job_uid, $game, 'delivered', 'imported', '', 'trucksbook',
                $truck_make, $truck_model, $cargo, $cargo_mass_kg,
                $source_city, $source_company, $destination_city, $destination_company,
                $planned_distance_km, $actual_distance_km,
                $revenue, $revenue, $fuel_used_l, $avg_consumption,
                $top_speed_kmh, $cargo_damage, 0, $fines_total,
                $xp, $job_type, $notes,
                $started_at_ms, $finished_at_ms, $real_duration_ms
            );
            """;

        cmd.Parameters.AddWithValue("$job_uid", d.JobUid);
        cmd.Parameters.AddWithValue("$game", d.Game);
        cmd.Parameters.AddWithValue("$truck_make", d.TruckMake);
        cmd.Parameters.AddWithValue("$truck_model", d.TruckModel);
        cmd.Parameters.AddWithValue("$cargo", d.Cargo);
        cmd.Parameters.AddWithValue("$cargo_mass_kg", d.CargoMassKg);
        cmd.Parameters.AddWithValue("$source_city", d.SourceCity);
        cmd.Parameters.AddWithValue("$source_company", d.SourceCompany);
        cmd.Parameters.AddWithValue("$destination_city", d.DestinationCity);
        cmd.Parameters.AddWithValue("$destination_company", d.DestinationCompany);
        cmd.Parameters.AddWithValue("$planned_distance_km", d.PlannedKm);
        cmd.Parameters.AddWithValue("$actual_distance_km", d.ActualKm);
        cmd.Parameters.AddWithValue("$revenue", d.Revenue);
        cmd.Parameters.AddWithValue("$fuel_used_l", d.FuelUsedL);
        cmd.Parameters.AddWithValue("$avg_consumption", d.AvgConsumption);
        cmd.Parameters.AddWithValue("$top_speed_kmh", d.TopSpeedKmh);
        cmd.Parameters.AddWithValue("$cargo_damage", d.CargoDamage);
        cmd.Parameters.AddWithValue("$fines_total", d.FinesTotal);
        cmd.Parameters.AddWithValue("$xp", d.Xp);
        cmd.Parameters.AddWithValue("$job_type", d.JobType);
        cmd.Parameters.AddWithValue("$notes", d.Notes);
        cmd.Parameters.AddWithValue("$started_at_ms", d.StartedAtMs);
        cmd.Parameters.AddWithValue("$finished_at_ms", d.StartedAtMs + d.RealDurationMs);
        cmd.Parameters.AddWithValue("$real_duration_ms", d.RealDurationMs);

        cmd.ExecuteNonQuery();
        }
    }

    /// <summary>Deliveries as grid rows for the UI, newest first. Each row is
    /// formatted in its own game's units, so an ATS run reads in miles even when an
    /// ETS2 one sits next to it in the list.</summary>
    public List<DeliveryRow> RecentDeliveryRows(int limit, string unitSetting) {
        lock (_gate) {
            var rows = new List<DeliveryRow>();
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = """
                SELECT id, started_at_ms, source_city, destination_city, cargo,
                       truck_make || ' ' || truck_model, actual_distance_km,
                       -- Only a delivery that arrived earns anything. Falling back to
                       -- the offer for every row showed a cancelled job as if its
                       -- money had been paid; what it actually did was cost a penalty.
                       CASE WHEN outcome = 'delivered' OR source = 'trucksbook'
                            THEN COALESCE(revenue, offered_income)
                            ELSE -COALESCE(penalty, 0) END,
                       fines_count, collisions,
                       validation_status, COALESCE(notes, ''), COALESCE(game, ''),
                       COALESCE(outcome, ''), COALESCE(driving_style, ''),
                       COALESCE(validation_flags, '')
                FROM deliveries
                ORDER BY started_at_ms DESC
                LIMIT $limit;
                """;
            cmd.Parameters.AddWithValue("$limit", limit);

            using var reader = cmd.ExecuteReader();
            while (reader.Read()) {
                var game = reader.GetString(12);
                var units = Units.For(unitSetting, game);
                var km = reader.GetDouble(6);
                var money = reader.GetDouble(7);
                rows.Add(new DeliveryRow {
                    Id = reader.GetInt64(0),
                    Datum = DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(1)).LocalDateTime,
                    Hra = game,
                    Odkial = reader.GetString(2),
                    Kam = reader.GetString(3),
                    Naklad = reader.GetString(4),
                    Tahac = reader.GetString(5),
                    DistanceKm = km,
                    Vzdialenost = units.FormatDistance(km),
                    Zarobok = money,
                    Odmena = units.FormatMoney(money),
                    Pokuty = reader.GetInt32(8),
                    Kolizie = reader.IsDBNull(9) ? 0 : reader.GetInt32(9),
                    Vysledok = reader.GetString(13),
                    Styl = reader.GetString(14),
                    Stav = reader.GetString(10),
                    Flags = reader.GetString(15),
                    Poznamky = reader.GetString(11),
                });
            }
            return rows;
        }
    }

    /// <summary>Game of the most recent delivery - what "auto" units follow for
    /// aggregate figures that can't belong to one game.</summary>
    public string? MostRecentGame() {
        lock (_gate) {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "SELECT game FROM deliveries ORDER BY started_at_ms DESC LIMIT 1;";
            return cmd.ExecuteScalar() as string;
        }
    }

    /// <summary>The event timeline of one delivery, oldest first.</summary>
    public List<TimelineRow> TimelineRows(long deliveryId) {
        lock (_gate) {
            var rows = new List<TimelineRow>();
            using var cmd = _conn.CreateCommand();
            // Anomalies are debugging detail about the SDK, not things the driver did,
            // so the timeline shows only real gameplay events.
            cmd.CommandText = """
                SELECT at_ms, event_type, value, extra_json
                FROM events
                WHERE delivery_id = $id AND event_type NOT LIKE 'anomaly:%'
                ORDER BY at_ms;
                """;
            cmd.Parameters.AddWithValue("$id", deliveryId);

            using var reader = cmd.ExecuteReader();
            while (reader.Read()) {
                var extra = reader.IsDBNull(3) ? null : reader.GetString(3);
                string detail = "";
                if (extra != null) {
                    try {
                        detail = Newtonsoft.Json.Linq.JObject.Parse(extra)["Detail"]?.ToString() ?? "";
                    } catch { detail = ""; }
                }
                // Stored event types are identifiers, deliberately: they are data, and
                // they outlive whatever language the window happens to be in. The
                // reading of them belongs here, at the point they are shown.
                var type = reader.GetString(1);
                rows.Add(new TimelineRow {
                    Cas = DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(0)).LocalDateTime.ToString("HH:mm:ss"),
                    Udalost = Strings.T("event." + type) is var t && t != "event." + type ? t : type,
                    Hodnota = reader.IsDBNull(2) ? "" : reader.GetDouble(2).ToString("0.##"),
                    Detail = detail,
                });
            }
            return rows;
        }
    }

    /// <summary>Free-text note the user can attach to a delivery (roadmap: "edit notes").</summary>
    /// <summary>Everything about one delivery, for the detail card. The list itself
    /// carries only what is worth scanning; the rest is fetched when one is opened.</summary>
    public DeliveryDetail? Detail(long id) {
        lock (_gate) {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = """
                SELECT started_at_ms, finished_at_ms, game, source_city, source_company,
                       destination_city, destination_company, cargo, cargo_mass_kg,
                       truck_make || ' ' || truck_model, COALESCE(trailer_name, ''),
                       planned_distance_km, actual_distance_km, reported_distance_km,
                       COALESCE(revenue, 0), offered_income, COALESCE(penalty, 0),
                       fuel_used_l, avg_consumption_l_100km, top_speed_kmh,
                       speeding_share, COALESCE(hard_speeding_share, 0), cruise_control_share,
                       truck_damage_pct, trailer_damage_pct,
                       driving_game_min, real_duration_ms, rest_stops, rest_minutes,
                       fines_count, fines_total, collisions, tolls_paid, ferries_used, refuels,
                       outcome, validation_status, COALESCE(validation_flags, ''),
                       COALESCE(driving_style, ''), COALESCE(notes, ''), COALESCE(source, ''),
                       -- Appended rather than slotted in beside the other distances:
                       -- the reader below indexes by position, so a column added in
                       -- the middle silently shifts every one after it.
                       sim_speed_distance_km, COALESCE(job_type, ''), cargo_damage_pct,
                       COALESCE(trailer_chain_type, ''), COALESCE(trailer_owned, 0),
                       COALESCE(trailer_units, '')
                FROM deliveries WHERE id = $id;
                """;
            cmd.Parameters.AddWithValue("$id", id);

            using var r = cmd.ExecuteReader();
            if (!r.Read()) return null;

            double? Opt(int i) => r.IsDBNull(i) ? null : r.GetDouble(i);
            double Num(int i) => r.IsDBNull(i) ? 0 : r.GetDouble(i);

            return new DeliveryDetail {
                Id = id,
                StartedAt = DateTimeOffset.FromUnixTimeMilliseconds(r.GetInt64(0)).LocalDateTime,
                FinishedAt = DateTimeOffset.FromUnixTimeMilliseconds(r.GetInt64(1)).LocalDateTime,
                Game = r.GetString(2),
                SourceCity = r.GetString(3), SourceCompany = r.IsDBNull(4) ? "" : r.GetString(4),
                DestinationCity = r.GetString(5), DestinationCompany = r.IsDBNull(6) ? "" : r.GetString(6),
                Cargo = r.IsDBNull(7) ? "" : r.GetString(7), CargoMassKg = Num(8),
                Truck = r.IsDBNull(9) ? "" : r.GetString(9), Trailer = r.GetString(10),
                PlannedDistanceKm = Num(11), DistanceKm = Num(12), ReportedDistanceKm = Opt(13),
                Revenue = Num(14), OfferedIncome = Num(15), Penalty = Num(16),
                FuelUsedL = Num(17), AvgConsumption = Opt(18), TopSpeedKmh = Num(19),
                SpeedingShare = Num(20), HardSpeedingShare = Num(21), CruiseShare = Num(22),
                TruckDamage = Num(23), TrailerDamage = Num(24),
                DrivingGameMin = Num(25), RealDurationMs = r.IsDBNull(26) ? 0 : r.GetInt64(26),
                RestStops = r.IsDBNull(27) ? 0 : r.GetInt32(27), RestMinutes = Num(28),
                FinesCount = r.IsDBNull(29) ? 0 : r.GetInt32(29), FinesTotal = Num(30),
                Collisions = r.IsDBNull(31) ? 0 : r.GetInt32(31), TollsPaid = Num(32),
                Ferries = r.IsDBNull(33) ? 0 : r.GetInt32(33), Refuels = r.IsDBNull(34) ? 0 : r.GetInt32(34),
                Outcome = r.IsDBNull(35) ? "" : r.GetString(35),
                Status = r.IsDBNull(36) ? "" : r.GetString(36),
                Flags = r.GetString(37), Style = r.GetString(38),
                Notes = r.GetString(39), Source = r.GetString(40),
                SimSpeedDistanceKm = Num(41),
                JobType = r.GetString(42),
                CargoDamage = Num(43),
                TrailerChainType = r.GetString(44),
                TrailerOwned = r.GetInt32(45) != 0,
                TrailerUnits = ReadUnits(r.GetString(46)),
            };
        }
    }

    /// <summary>Rows recorded before the coupled set was kept have nothing here, and
    /// a damaged one must not stop a delivery from opening.</summary>
    private static List<TrailerUnitRecord> ReadUnits(string json) {
        if (json.Length == 0) return new List<TrailerUnitRecord>();
        try {
            return Newtonsoft.Json.JsonConvert.DeserializeObject<List<TrailerUnitRecord>>(json)
                   ?? new List<TrailerUnitRecord>();
        } catch {
            return new List<TrailerUnitRecord>();
        }
    }

    public void SetNotes(long deliveryId, string notes) {
        lock (_gate) {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "UPDATE deliveries SET notes = $notes WHERE id = $id;";
            cmd.Parameters.AddWithValue("$notes", notes);
            cmd.Parameters.AddWithValue("$id", deliveryId);
            cmd.ExecuteNonQuery();
        }
    }

    /// <summary>One compact line per delivery, newest first - for `--list`. Each line
    /// is in its own game's units.</summary>
    public List<string> RecentDeliveries(int limit, string unitSetting) {
        lock (_gate) {
            var lines = new List<string>();
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = """
                SELECT started_at_ms, source_city, destination_city, cargo,
                       actual_distance_km, revenue, offered_income, validation_status,
                       validation_flags, COALESCE(game, '')
                FROM deliveries
                ORDER BY started_at_ms DESC
                LIMIT $limit;
                """;
            cmd.Parameters.AddWithValue("$limit", limit);

            using var reader = cmd.ExecuteReader();
            while (reader.Read()) {
                var startedAt = DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(0)).LocalDateTime;
                var source = reader.GetString(1);
                var dest = reader.GetString(2);
                var cargo = reader.GetString(3);
                var units = Units.For(unitSetting, reader.GetString(9));
                var distance = units.Distance(reader.GetDouble(4));
                var revenue = reader.IsDBNull(5) ? reader.GetDouble(6) : reader.GetDouble(5);
                var status = reader.GetString(7);
                var flags = reader.GetString(8);
                var flagsSuffix = string.IsNullOrEmpty(flags) ? "" : $" [{flags}]";
                lines.Add($"{startedAt:yyyy-MM-dd HH:mm}  {source,-15} -> {dest,-15}  {cargo,-20}  "
                        + $"{distance,7:0.0} {units.DistanceUnit,-3} {revenue,7:0} {units.Currency,-3} {status}{flagsSuffix}");
            }
            return lines;
        }
    }

    private void InsertEvents(SqliteTransaction tx, long deliveryId, JobRecord r) {
        using var cmd = _conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO events (delivery_id, at_ms, event_type, value, extra_json)
            VALUES ($delivery_id, $at_ms, $event_type, $value, $extra_json);
            """;
        var pDeliveryId = cmd.Parameters.Add("$delivery_id", SqliteType.Integer);
        var pAtMs = cmd.Parameters.Add("$at_ms", SqliteType.Integer);
        var pType = cmd.Parameters.Add("$event_type", SqliteType.Text);
        var pValue = cmd.Parameters.Add("$value", SqliteType.Real);
        var pExtra = cmd.Parameters.Add("$extra_json", SqliteType.Text);

        void AddEvent(long atMs, string type, double? value, object? extra) {
            pDeliveryId.Value = deliveryId;
            pAtMs.Value = atMs;
            pType.Value = type;
            pValue.Value = (object?)value ?? DBNull.Value;
            pExtra.Value = extra == null ? DBNull.Value : JsonConvert.SerializeObject(extra);
            cmd.ExecuteNonQuery();
        }

        // The timeline carries each event at the moment it happened, which is what
        // makes a delivery readable after the fact. Counters stamped with the finish
        // time (what this used to write) told you a fine happened but not where.
        foreach (var ev in r.Timeline) {
            AddEvent(ev.AtMs, ev.Type, ev.Value, ev.Detail == null ? null : new { ev.Detail });
        }
        foreach (var a in r.Anomalies) {
            AddEvent(a.AtMs, $"anomaly:{a.Code}", a.Delta ?? a.MovedKm ?? a.ImpliedKmh, a);
        }
        // Only known once the job closes, so it genuinely belongs at the finish.
        if (r.LateDelivery) AddEvent(r.FinishedAtMs, "late_delivery", r.MinutesLate, null);
    }

    private void InsertTripPoints(SqliteTransaction tx, long deliveryId, JobRecord r) {
        using var cmd = _conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO trip_points (delivery_id, at_ms, x, y, z, speed_kmh)
            VALUES ($delivery_id, $at_ms, $x, $y, $z, $speed_kmh);
            """;
        var pDeliveryId = cmd.Parameters.Add("$delivery_id", SqliteType.Integer);
        var pAtMs = cmd.Parameters.Add("$at_ms", SqliteType.Integer);
        var pX = cmd.Parameters.Add("$x", SqliteType.Real);
        var pY = cmd.Parameters.Add("$y", SqliteType.Real);
        var pZ = cmd.Parameters.Add("$z", SqliteType.Real);
        var pSpeed = cmd.Parameters.Add("$speed_kmh", SqliteType.Real);

        foreach (var p in r.TripPoints) {
            pDeliveryId.Value = deliveryId;
            pAtMs.Value = p.AtMs;
            pX.Value = p.X;
            pY.Value = p.Y;
            pZ.Value = p.Z;
            pSpeed.Value = p.SpeedKmh;
            cmd.ExecuteNonQuery();
        }
    }

    /// <summary>All trip points for one delivery, in order - the raw material for a
    /// future route replay/map view. Coordinates are the SDK's raw world-space
    /// units, not GPS; no map projection exists yet (see project_vision memory).</summary>
    public List<(long AtMs, double X, double Y, double Z, double SpeedKmh)> TripPoints(long deliveryId) {
        lock (_gate) {
            var points = new List<(long, double, double, double, double)>();
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "SELECT at_ms, x, y, z, speed_kmh FROM trip_points WHERE delivery_id = $id ORDER BY at_ms";
            cmd.Parameters.AddWithValue("$id", deliveryId);
            using var reader = cmd.ExecuteReader();
            while (reader.Read()) {
                points.Add((reader.GetInt64(0), reader.GetDouble(1), reader.GetDouble(2), reader.GetDouble(3), reader.GetDouble(4)));
            }
            return points;
        }
    }

    /// <summary>Aggregate stats for `--stats`. Pass sinceMs to scope to a window
    /// (e.g. this week); null means all-time.</summary>
    public StatsSummary GetStats(long? sinceMs = null) {
        lock (_gate) {
        var summary = new StatsSummary();

        using (var cmd = _conn.CreateCommand()) {
            cmd.CommandText = """
                SELECT
                    COUNT(*),
                    SUM(CASE WHEN validation_status = 'accepted' THEN 1 ELSE 0 END),
                    SUM(CASE WHEN validation_status = 'review' THEN 1 ELSE 0 END),
                    SUM(CASE WHEN validation_status = 'rejected' THEN 1 ELSE 0 END),
                    COALESCE(SUM(actual_distance_km), 0),
                    COALESCE(SUM(revenue), 0),
                    -- What the deliveries that never arrived cost, kept apart from
                    -- revenue rather than netted off it: they are two different
                    -- things and burying one inside the other hides both.
                    COALESCE(SUM(penalty), 0),
                    SUM(CASE WHEN driving_style = 'clean' THEN 1 ELSE 0 END),
                    SUM(CASE WHEN driving_style = 'spirited' THEN 1 ELSE 0 END),
                    COALESCE(SUM(fuel_used_l), 0),
                    COALESCE(SUM(driving_ms), 0),
                    COALESCE(SUM(driving_game_min), 0),
                    COALESCE(SUM(collisions), 0),
                    COALESCE(SUM(late_delivery), 0),
                    COALESCE(SUM(fines_total), 0),
                    -- Distance of rows that also have game time, so average speed
                    -- divides two figures that describe the same drives. Imported
                    -- rows carry distance but no game clock, and mixing them in
                    -- inflated the average to hundreds of km/h.
                    COALESCE(SUM(CASE WHEN driving_game_min > 0 THEN actual_distance_km ELSE 0 END), 0)
                FROM deliveries
                WHERE $since IS NULL OR started_at_ms >= $since;
                """;
            cmd.Parameters.AddWithValue("$since", (object?)sinceMs ?? DBNull.Value);
            using var reader = cmd.ExecuteReader();
            if (reader.Read() && !reader.IsDBNull(0)) {
                summary.TotalDeliveries = reader.GetInt32(0);
                summary.Accepted = reader.IsDBNull(1) ? 0 : reader.GetInt32(1);
                summary.Review = reader.IsDBNull(2) ? 0 : reader.GetInt32(2);
                summary.Rejected = reader.IsDBNull(3) ? 0 : reader.GetInt32(3);
                summary.TotalDistanceKm = reader.GetDouble(4);
                summary.TotalRevenue = reader.GetDouble(5);
                summary.TotalPenalties = reader.GetDouble(6);
                summary.Clean = reader.IsDBNull(7) ? 0 : reader.GetInt32(7);
                summary.Spirited = reader.IsDBNull(8) ? 0 : reader.GetInt32(8);
                summary.TotalFuelL = reader.GetDouble(9);
                summary.TotalDrivingMs = reader.GetInt64(10);
                summary.TotalGameMinutes = reader.GetDouble(11);
                summary.TotalCollisions = reader.GetInt32(12);
                summary.LateDeliveries = reader.GetInt32(13);
                summary.TotalFines = reader.GetDouble(14);
                summary.TimedDistanceKm = reader.GetDouble(15);
            }
        }

        summary.FavoriteTruck = TopValue("truck_make || ' ' || truck_model", sinceMs);
        summary.FavoriteRoute = TopValue("source_city || ' → ' || destination_city", sinceMs);
        summary.FavoriteCargo = TopValue("cargo", sinceMs);

        return summary;
        }
    }

    private string? TopValue(string groupExpr, long? sinceMs) {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT {groupExpr} AS v, COUNT(*) AS c
            FROM deliveries
            WHERE ($since IS NULL OR started_at_ms >= $since) AND {groupExpr} IS NOT NULL AND {groupExpr} != ''
            GROUP BY v
            ORDER BY c DESC
            LIMIT 1;
            """;
        cmd.Parameters.AddWithValue("$since", (object?)sinceMs ?? DBNull.Value);
        return cmd.ExecuteScalar() as string;
    }

    /// <summary>Dumps the deliveries table to CSV or JSON, per the roadmap's
    /// "export CSV/JSON" MVP item. Column names match the SQL schema above.</summary>
    public void Export(string path, string format) {
        lock (_gate) {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM deliveries ORDER BY started_at_ms;";
        using var reader = cmd.ExecuteReader();
        var columns = Enumerable.Range(0, reader.FieldCount).Select(reader.GetName).ToArray();

        if (format == "json") {
            var rows = new List<Dictionary<string, object?>>();
            while (reader.Read()) {
                var row = new Dictionary<string, object?>();
                foreach (var c in columns) row[c] = reader.IsDBNull(reader.GetOrdinal(c)) ? null : reader.GetValue(reader.GetOrdinal(c));
                rows.Add(row);
            }
            File.WriteAllText(path, JsonConvert.SerializeObject(rows, Formatting.Indented));
            return;
        }

        using var writer = new StreamWriter(path);
        writer.WriteLine(string.Join(",", columns));
        while (reader.Read()) {
            var cells = new string[reader.FieldCount];
            for (var i = 0; i < reader.FieldCount; i++) {
                cells[i] = CsvCell(reader.IsDBNull(i) ? "" : reader.GetValue(i).ToString() ?? "");
            }
            writer.WriteLine(string.Join(",", cells));
        }
        }
    }

    /// <summary>Writes a clean, consistent copy of the database to <paramref name="path"/>.
    /// Uses VACUUM INTO rather than a file copy so it is safe to run while the tracker
    /// is mid-delivery and holding the database open.</summary>
    public string Backup(string? path = null) {
        lock (_gate) {
        path ??= Path.Combine(DefaultDir(), "backups", $"deliveries-{DateTime.Now:yyyyMMdd-HHmmss}.db");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (File.Exists(path)) File.Delete(path); // VACUUM INTO refuses an existing target

        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "VACUUM INTO $path;";
        cmd.Parameters.AddWithValue("$path", path);
        cmd.ExecuteNonQuery();
        return path;
        }
    }

    /// <summary>Replaces the live database with a backup. The current database is
    /// backed up first and its path returned, so a restore is never a one-way door.
    /// The store must be disposed afterwards - the connection now points at replaced
    /// files - so this is static and takes paths rather than acting on an open store.</summary>
    public static string RestoreFromBackup(string backupPath, string? dbPath = null) {
        dbPath ??= DefaultPath();
        if (!File.Exists(backupPath)) throw new FileNotFoundException("Zaloha neexistuje", backupPath);

        // Verify it's actually a readable database with the expected table before
        // overwriting anything.
        using (var probe = new SqliteConnection($"Data Source={backupPath};Mode=ReadOnly")) {
            probe.Open();
            using var check = probe.CreateCommand();
            check.CommandText = "SELECT COUNT(*) FROM deliveries;";
            check.ExecuteScalar();
        }
        SqliteConnection.ClearAllPools();

        var safety = Path.Combine(DefaultDir(), "backups", $"pred-obnovou-{DateTime.Now:yyyyMMdd-HHmmss}.db");
        Directory.CreateDirectory(Path.GetDirectoryName(safety)!);
        if (File.Exists(dbPath)) File.Copy(dbPath, safety, overwrite: true);

        File.Copy(backupPath, dbPath, overwrite: true);
        // SQLite side files would otherwise contradict the restored database.
        foreach (var side in new[] { dbPath + "-wal", dbPath + "-shm" }) {
            if (File.Exists(side)) File.Delete(side);
        }
        return safety;
    }

    private static string CsvCell(string s) =>
        s.Contains(',') || s.Contains('"') || s.Contains('\n') ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;

    public void Dispose() {
        _conn.Dispose();
        GC.SuppressFinalize(this);
    }
}

public class StatsSummary {
    public int TotalDeliveries;
    public int Accepted;
    public int Review;
    public int Rejected;
    public double TotalDistanceKm;
    public double TotalRevenue;
    /// <summary>What the cancelled deliveries cost. Kept apart from revenue rather
    /// than subtracted from it: netting them hides both figures.</summary>
    public double TotalPenalties;
    public int Clean;
    public int Spirited;
    public double TotalFuelL;
    public long TotalDrivingMs;
    /// <summary>In-game minutes elapsed across all deliveries. Distances are in
    /// simulated km, so average speed must divide by this, never by real time.</summary>
    public double TotalGameMinutes;
    /// <summary>Distance of the deliveries that also have game time behind them.
    /// Pair this with TotalGameMinutes for average speed - TotalDistanceKm includes
    /// imported rows that have no game clock.</summary>
    public double TimedDistanceKm;
    public int TotalCollisions;
    public int LateDeliveries;
    public double TotalFines;
    public string? FavoriteTruck;
    public string? FavoriteRoute;
    public string? FavoriteCargo;
}
