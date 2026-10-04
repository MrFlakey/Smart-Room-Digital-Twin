using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace SmartRoom
{
    /// <summary>smartroom/&lt;room&gt;/telemetry. A null field means the board didn't send it or the sensor failed.</summary>
    [Serializable]
    public class Telemetry
    {
        [JsonProperty("ts")] public long? Ts;
        [JsonProperty("occ")] public int? Occ;
        [JsonProperty("pir")] public int? Pir;
        [JsonProperty("door_open")] public int? DoorOpen;
        [JsonProperty("dist_cm")] public float? DistCm;
        [JsonProperty("temp_c")] public float? TempC;
        [JsonProperty("set_c")] public float? SetC;
        [JsonProperty("light_pct")] public float? LightPct;
        [JsonProperty("light")] public int? Light;
        [JsonProperty("fan")] public int? Fan;
        [JsonProperty("power_w")] public float? PowerW;
        [JsonProperty("energy_wh")] public float? EnergyWh;
        [JsonProperty("saved_pct")] public float? SavedPct;
        [JsonProperty("mode")] public string Mode;
        [JsonProperty("lights_ovr")] public int? LightsOvr;
        [JsonProperty("ac_ovr")] public int? AcOvr;
        [JsonProperty("ac_paused")] public int? AcPaused;
    }

    /// <summary>smartroom/&lt;room&gt;/state (retained).</summary>
    [Serializable]
    public class RoomStateData
    {
        [JsonProperty("mode")] public string Mode;
        [JsonProperty("occ")] public int? Occ;
        [JsonProperty("set_c")] public float? SetC;
        [JsonProperty("light")] public int? Light;
        [JsonProperty("fan")] public int? Fan;
        [JsonProperty("lights_ovr")] public int? LightsOvr;
        [JsonProperty("ac_ovr")] public int? AcOvr;
        [JsonProperty("ac_paused")] public int? AcPaused;
        [JsonProperty("door_open")] public int? DoorOpen;
    }

    /// <summary>smartroom/&lt;room&gt;/faults (retained): sensor key to short reason. Empty when all OK.</summary>
    public class Faults
    {
        public Dictionary<string, string> BySensor = new Dictionary<string, string>();

        public bool Any => BySensor.Count > 0;

        public static Faults Parse(string json)
        {
            var faults = new Faults();
            var parsed = JsonConvert.DeserializeObject<Dictionary<string, string>>(json);
            if (parsed != null)
                faults.BySensor = parsed;
            return faults;
        }
    }

    /// <summary>One power reading; Time is Time.realtimeSinceStartup when it arrived.</summary>
    public struct PowerSample
    {
        public float Time;
        public float Watts;
    }

    /// <summary>What kind of thing an event-log entry is about; sets its dot colour.</summary>
    public enum LogKind
    {
        Fault,
        Recovered,
        Override,
        Door,
        Occupancy,
        Esp,
    }

    /// <summary>One line of the event log: when, what kind, and a short sentence.</summary>
    public struct LogEntry
    {
        public DateTime Time;
        public LogKind Kind;
        public string Text;
    }

    /// <summary>One raw event as received (for the debug panel).</summary>
    public struct RoomEvent
    {
        public DateTime Time;
        public string Json;
    }
}
