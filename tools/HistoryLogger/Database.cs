using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace SmartRoom.HistoryLogger;

/// <summary>
/// The history database (one SQLite file). Times are stored as UTC milliseconds; "day" is the local date
/// the row was recorded on, which is what history requests ask for. Nothing is ever deleted automatically.
/// </summary>
public sealed class Database : IDisposable
{
    readonly SqliteConnection db;

    public Database(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path }.ToString());
        db.Open();
        Execute("PRAGMA journal_mode=WAL;");
        Execute(@"
CREATE TABLE IF NOT EXISTS telemetry (
    id INTEGER PRIMARY KEY,
    ts INTEGER NOT NULL,
    day TEXT NOT NULL,
    board_ts INTEGER,
    occ INTEGER, pir INTEGER, door_open INTEGER, dist_cm REAL,
    temp_c REAL, set_c REAL, light_pct REAL, light INTEGER, fan INTEGER,
    power_w REAL, energy_wh REAL, saved_pct REAL,
    mode TEXT, lights_ovr INTEGER, ac_ovr INTEGER, ac_paused INTEGER
);
CREATE INDEX IF NOT EXISTS telemetry_day ON telemetry(day, ts);
CREATE TABLE IF NOT EXISTS events (id INTEGER PRIMARY KEY, ts INTEGER NOT NULL, day TEXT NOT NULL, type TEXT, json TEXT NOT NULL);
CREATE INDEX IF NOT EXISTS events_day ON events(day, ts);
CREATE TABLE IF NOT EXISTS status (id INTEGER PRIMARY KEY, ts INTEGER NOT NULL, day TEXT NOT NULL, value TEXT NOT NULL);
CREATE INDEX IF NOT EXISTS status_day ON status(day, ts);
CREATE TABLE IF NOT EXISTS faults (id INTEGER PRIMARY KEY, ts INTEGER NOT NULL, day TEXT NOT NULL, json TEXT NOT NULL);
CREATE INDEX IF NOT EXISTS faults_day ON faults(day, ts);
");
    }

    public static string Day(DateTimeOffset utc) => utc.ToLocalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public void InsertTelemetry(DateTimeOffset now, JsonElement t)
    {
        using var cmd = db.CreateCommand();
        cmd.CommandText = @"INSERT INTO telemetry
(ts, day, board_ts, occ, pir, door_open, dist_cm, temp_c, set_c, light_pct, light, fan, power_w, energy_wh, saved_pct, mode, lights_ovr, ac_ovr, ac_paused)
VALUES ($ts, $day, $board_ts, $occ, $pir, $door_open, $dist_cm, $temp_c, $set_c, $light_pct, $light, $fan, $power_w, $energy_wh, $saved_pct, $mode, $lights_ovr, $ac_ovr, $ac_paused);";
        cmd.Parameters.AddWithValue("$ts", now.ToUnixTimeMilliseconds());
        cmd.Parameters.AddWithValue("$day", Day(now));
        cmd.Parameters.AddWithValue("$board_ts", Number(t, "ts"));
        foreach (var key in new[] { "occ", "pir", "door_open", "dist_cm", "temp_c", "set_c", "light_pct", "light", "fan",
                                    "power_w", "energy_wh", "saved_pct", "lights_ovr", "ac_ovr", "ac_paused" })
            cmd.Parameters.AddWithValue("$" + key, Number(t, key));
        cmd.Parameters.AddWithValue("$mode", t.TryGetProperty("mode", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString() : DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    public void InsertEvent(DateTimeOffset now, string json, string? type) =>
        Insert("INSERT INTO events (ts, day, type, json) VALUES ($ts, $day, $a, $b);", now, type, json);

    public void InsertStatus(DateTimeOffset now, string value) =>
        Insert("INSERT INTO status (ts, day, value) VALUES ($ts, $day, $a);", now, value, null);

    public void InsertFaults(DateTimeOffset now, string json) =>
        Insert("INSERT INTO faults (ts, day, json) VALUES ($ts, $day, $a);", now, json, null);

    /// <summary>Last value stored in a table's text column, to store only changes.</summary>
    public string? LastText(string table, string column)
    {
        using var cmd = db.CreateCommand();
        cmd.CommandText = $"SELECT {column} FROM {table} ORDER BY ts DESC, id DESC LIMIT 1;";
        return cmd.ExecuteScalar() as string;
    }

    public List<string> Days()
    {
        using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT day FROM telemetry UNION SELECT day FROM events UNION SELECT day FROM status ORDER BY day;";
        var days = new List<string>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            days.Add(r.GetString(0));
        return days;
    }

    public sealed record TelemetryRow(long Ts, double? Power, double? Temp, double? SetC, double? Saved, double? Energy, int? Occ);

    public List<TelemetryRow> TelemetryFor(string day)
    {
        using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT ts, power_w, temp_c, set_c, saved_pct, energy_wh, occ FROM telemetry WHERE day = $day ORDER BY ts;";
        cmd.Parameters.AddWithValue("$day", day);
        var rows = new List<TelemetryRow>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            rows.Add(new TelemetryRow(r.GetInt64(0), D(r, 1), D(r, 2), D(r, 3), D(r, 4), D(r, 5),
                                      r.IsDBNull(6) ? null : r.GetInt32(6)));
        }
        return rows;
    }

    public List<(long Ts, string Json)> EventsFor(string day)
    {
        using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT ts, json FROM events WHERE day = $day ORDER BY ts;";
        cmd.Parameters.AddWithValue("$day", day);
        var list = new List<(long, string)>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add((r.GetInt64(0), r.GetString(1)));
        return list;
    }

    public List<(long Ts, string Value)> StatusFor(string day)
    {
        using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT ts, value FROM status WHERE day = $day ORDER BY ts;";
        cmd.Parameters.AddWithValue("$day", day);
        var list = new List<(long, string)>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add((r.GetInt64(0), r.GetString(1)));
        return list;
    }

    public long Count(string table)
    {
        using var cmd = db.CreateCommand();
        cmd.CommandText = $"SELECT COUNT(*) FROM {table};";
        return (long)cmd.ExecuteScalar()!;
    }

    public void Dispose() => db.Dispose();

    void Insert(string sql, DateTimeOffset now, object? a, object? b)
    {
        using var cmd = db.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("$ts", now.ToUnixTimeMilliseconds());
        cmd.Parameters.AddWithValue("$day", Day(now));
        cmd.Parameters.AddWithValue("$a", a ?? DBNull.Value);
        if (sql.Contains("$b"))
            cmd.Parameters.AddWithValue("$b", b ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    void Execute(string sql)
    {
        using var cmd = db.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    static object Number(JsonElement t, string key) =>
        t.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : DBNull.Value;

    static double? D(SqliteDataReader r, int i) => r.IsDBNull(i) ? null : r.GetDouble(i);
}
