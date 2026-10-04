using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SmartRoom.HistoryLogger;

/// <summary>Builds the answers to history requests (see docs/mqtt-contract.md, "history").</summary>
public static class HistoryQuery
{
    /// <summary>Chart buckets: 5 minutes.</summary>
    const int BucketMinutes = 5;
    /// <summary>A gap longer than this between telemetry rows counts as no data, not as time spent.</summary>
    const double MaxGapSeconds = 10;
    /// <summary>At most this many events in one answer (the newest).</summary>
    const int MaxEvents = 500;

    public static JsonObject DayList(Database db) =>
        new JsonObject { ["days"] = new JsonArray(db.Days().Select(d => (JsonNode)JsonValue.Create(d)!).ToArray()) };

    public static JsonObject Day(Database db, string day)
    {
        var rows = db.TelemetryFor(day);

        // Totals. Energy: add up the increases of energy_wh; a drop means the board reset its counter.
        double energy = 0, savedWeighted = 0, savedSeconds = 0, occupied = 0;
        for (int i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            double seconds = i + 1 < rows.Count
                ? Math.Min((rows[i + 1].Ts - r.Ts) / 1000.0, MaxGapSeconds)
                : Math.Min(2.0, MaxGapSeconds);

            if (i > 0 && r.Energy is double e && rows[i - 1].Energy is double prev)
                energy += e >= prev ? e - prev : e;
            if (r.Saved is double s)
            {
                savedWeighted += s * seconds;
                savedSeconds += seconds;
            }
            if (r.Occ == 1)
                occupied += seconds;
        }

        var events = db.EventsFor(day);
        int faults = 0;
        foreach (var (_, json) in events)
        {
            if (TypeOf(json) == "fault")
                faults++;
        }

        var totals = new JsonObject
        {
            ["energy_wh"] = Round(energy, 3),
            ["saved_pct"] = savedSeconds > 0 ? Round(savedWeighted / savedSeconds, 1) : null,
            ["occupied_s"] = (long)Math.Round(occupied),
            ["faults"] = faults,
        };

        // 5-minute series: averages, except saved_pct (last value) and occ (fraction of readings occupied).
        var series = new JsonArray();
        foreach (var bucket in rows.GroupBy(r => BucketOf(r.Ts)).OrderBy(g => g.Key))
        {
            var list = bucket.ToList();
            var point = new JsonObject
            {
                ["t"] = TimeSpan.FromMinutes(bucket.Key * BucketMinutes).ToString(@"hh\:mm", CultureInfo.InvariantCulture),
                ["power_w"] = Avg(list.Select(r => r.Power), 3),
                ["temp_c"] = Avg(list.Select(r => r.Temp), 2),
                ["set_c"] = Avg(list.Select(r => r.SetC), 2),
                ["saved_pct"] = list.LastOrDefault(r => r.Saved.HasValue)?.Saved is double last ? Round(last, 1) : null,
                ["occ"] = list.Any(r => r.Occ.HasValue)
                    ? Round(list.Count(r => r.Occ == 1) / (double)list.Count(r => r.Occ.HasValue), 2)
                    : null,
            };
            series.Add(point);
        }

        // Events and ESP online/offline changes, oldest first, the newest MaxEvents.
        var timeline = new List<(long Ts, JsonObject Item)>();
        foreach (var (ts, json) in events)
        {
            JsonObject item;
            try
            {
                item = JsonNode.Parse(json) as JsonObject ?? new JsonObject();
            }
            catch (JsonException)
            {
                continue;
            }
            timeline.Add((ts, item));
        }
        foreach (var (ts, value) in db.StatusFor(day))
            timeline.Add((ts, new JsonObject { ["type"] = "status", ["value"] = value }));

        var eventArray = new JsonArray();
        foreach (var (ts, item) in timeline.OrderBy(e => e.Ts).TakeLast(MaxEvents))
        {
            var withTime = new JsonObject { ["t"] = LocalTime(ts).ToString("HH:mm:ss", CultureInfo.InvariantCulture) };
            foreach (var kv in item)
                withTime[kv.Key] = kv.Value?.DeepClone();
            eventArray.Add(withTime);
        }

        return new JsonObject
        {
            ["day"] = day,
            ["totals"] = totals,
            ["series"] = series,
            ["events"] = eventArray,
        };
    }

    static int BucketOf(long ts)
    {
        var local = LocalTime(ts);
        return (local.Hour * 60 + local.Minute) / BucketMinutes;
    }

    static DateTime LocalTime(long ts) => DateTimeOffset.FromUnixTimeMilliseconds(ts).ToLocalTime().DateTime;

    static string? TypeOf(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("type", out var t) ? t.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    static JsonNode? Avg(IEnumerable<double?> values, int digits)
    {
        var present = values.Where(v => v.HasValue).Select(v => v!.Value).ToList();
        return present.Count == 0 ? null : Round(present.Average(), digits);
    }

    static JsonNode Round(double value, int digits) => JsonValue.Create(Math.Round(value, digits))!;
}
