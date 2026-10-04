using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace SmartRoom
{
    /// <summary>Holds the latest data received from the room. Everything visual reads from here.</summary>
    public class RoomModel : MonoBehaviour
    {
        public const int MaxEvents = 10;
        /// <summary>Entries kept in the event log.</summary>
        public const int MaxLog = 50;
        /// <summary>Seconds of power history kept for the sparkline.</summary>
        public const float PowerHistorySeconds = 150f;
        /// <summary>Without a state or telemetry message for this long, the room no longer counts as live.</summary>
        public const float StaleSeconds = 10f;

        [SerializeField] MqttConnection connection;

        public Telemetry Telemetry { get; private set; }
        public RoomStateData State { get; private set; }
        public Faults Faults { get; private set; } = new Faults();
        /// <summary>True while the board's retained status is "online".</summary>
        public bool Online { get; private set; }
        /// <summary>
        /// True while the board is online AND has sent state or telemetry since it came online, within the
        /// last StaleSeconds. Until then, everything else here may be left over from before and shouldn't be shown.
        /// </summary>
        public bool Live { get; private set; }
        public DateTime LastTelemetryTime { get; private set; }
        /// <summary>Light output 0-100 from whichever of telemetry or state arrived last. Null until known.</summary>
        public int? LightLevel { get; private set; }
        /// <summary>Fan output 0-100 from whichever of telemetry or state arrived last. Null until known.</summary>
        public int? FanLevel { get; private set; }
        /// <summary>Door state from whichever of telemetry, state or a door event arrived last. Null until known.</summary>
        public bool? DoorOpen { get; private set; }
        /// <summary>"auto" or "manual", from whichever of telemetry or state arrived last.</summary>
        public string Mode { get; private set; }
        public bool? Occupied { get; private set; }
        public float? SetpointC { get; private set; }
        public bool? LightsOverride { get; private set; }
        public bool? AcOverride { get; private set; }
        /// <summary>True while the AC is paused, for example because the door is open.</summary>
        public bool? AcPaused { get; private set; }
        /// <summary>Power readings, oldest first, covering the last PowerHistorySeconds.</summary>
        public IReadOnlyList<PowerSample> PowerHistory => powerHistory;
        /// <summary>Newest first, at most MaxEvents.</summary>
        public IReadOnlyList<RoomEvent> Events => events;
        /// <summary>Readable event log, newest first, at most MaxLog.</summary>
        public IReadOnlyList<LogEntry> Log => log;

        public event Action Changed;

        readonly List<RoomEvent> events = new List<RoomEvent>();
        readonly List<LogEntry> log = new List<LogEntry>();
        readonly List<PowerSample> powerHistory = new List<PowerSample>();
        float lastFreshTime = float.NegativeInfinity;   // last state/telemetry received while online

        void Awake()
        {
            if (connection == null)
                connection = GetComponent<MqttConnection>();
        }

        void OnEnable()
        {
            if (connection == null)
                return;
            connection.TelemetryReceived += OnTelemetry;
            connection.StateReceived += OnState;
            connection.StatusReceived += OnStatus;
            connection.FaultsReceived += OnFaults;
            connection.EventReceived += OnEvent;
            connection.ConnectionChanged += OnConnectionChanged;
        }

        void OnDisable()
        {
            if (connection == null)
                return;
            connection.TelemetryReceived -= OnTelemetry;
            connection.StateReceived -= OnState;
            connection.StatusReceived -= OnStatus;
            connection.FaultsReceived -= OnFaults;
            connection.EventReceived -= OnEvent;
            connection.ConnectionChanged -= OnConnectionChanged;
        }

        void OnTelemetry(string json)
        {
            if (!TryParse(json, "telemetry", out Telemetry parsed))
                return;
            Telemetry = parsed;
            LastTelemetryTime = DateTime.Now;
            MarkFresh();
            Merge(parsed.Mode, parsed.Occ, parsed.SetC, parsed.Light, parsed.Fan,
                  parsed.LightsOvr, parsed.AcOvr, parsed.AcPaused, parsed.DoorOpen);

            float now = Time.realtimeSinceStartup;
            if (parsed.PowerW.HasValue)
                powerHistory.Add(new PowerSample { Time = now, Watts = parsed.PowerW.Value });
            while (powerHistory.Count > 0 && powerHistory[0].Time < now - PowerHistorySeconds)
                powerHistory.RemoveAt(0);
            UpdateLive();
            Changed?.Invoke();
        }

        void OnState(string json)
        {
            if (!TryParse(json, "state", out RoomStateData parsed))
                return;
            State = parsed;
            MarkFresh();
            Merge(parsed.Mode, parsed.Occ, parsed.SetC, parsed.Light, parsed.Fan,
                  parsed.LightsOvr, parsed.AcOvr, parsed.AcPaused, parsed.DoorOpen);
            UpdateLive();
            Changed?.Invoke();
        }

        /// <summary>Takes every field the message carried; missing fields keep their last value.</summary>
        void Merge(string mode, int? occ, float? setC, int? light, int? fan,
                   int? lightsOvr, int? acOvr, int? acPaused, int? doorOpen)
        {
            if (!string.IsNullOrEmpty(mode))
                Mode = mode;
            if (occ.HasValue) Occupied = occ.Value != 0;
            SetpointC = setC ?? SetpointC;
            LightLevel = light ?? LightLevel;
            FanLevel = fan ?? FanLevel;
            if (lightsOvr.HasValue)
            {
                bool on = lightsOvr.Value != 0;
                if (LightsOverride.HasValue && LightsOverride.Value != on)
                    AddLog(LogKind.Override, "Lights override " + (on ? "on" : "off"));
                LightsOverride = on;
            }
            if (acOvr.HasValue)
            {
                bool on = acOvr.Value != 0;
                if (AcOverride.HasValue && AcOverride.Value != on)
                    AddLog(LogKind.Override, "AC override " + (on ? "on" : "off"));
                AcOverride = on;
            }
            if (acPaused.HasValue) AcPaused = acPaused.Value != 0;
            if (doorOpen.HasValue) DoorOpen = doorOpen.Value != 0;
        }

        void OnStatus(string payload)
        {
            bool wasOnline = Online;
            Online = payload.Trim() == "online";
            if (Online && !wasOnline)
            {
                ResetLiveValues();
                AddLog(LogKind.Esp, "ESP connected");
            }
            else if (!Online && wasOnline)
            {
                AddLog(LogKind.Esp, "ESP disconnected");
            }
            UpdateLive();
            Changed?.Invoke();
        }

        /// <summary>Only data that arrives while the board is online makes the room live.</summary>
        void MarkFresh()
        {
            if (Online)
                lastFreshTime = Time.realtimeSinceStartup;
        }

        /// <summary>
        /// Forgets everything the board reported before it (re)connected, so the twin waits for the
        /// board's real state instead of showing old values. Energy comes back with the next telemetry.
        /// </summary>
        void ResetLiveValues()
        {
            Telemetry = null;
            State = null;
            Mode = null;
            Occupied = null;
            SetpointC = null;
            LightLevel = null;
            FanLevel = null;
            LightsOverride = null;
            AcOverride = null;
            AcPaused = null;
            DoorOpen = null;
            powerHistory.Clear();
            lastFreshTime = float.NegativeInfinity;
        }

        void UpdateLive()
        {
            Live = Online && Time.realtimeSinceStartup - lastFreshTime < StaleSeconds;
        }

        void Update()
        {
            bool was = Live;
            UpdateLive();
            if (Live != was)
                Changed?.Invoke();
        }

        void OnFaults(string json)
        {
            try
            {
                Faults = Faults.Parse(json);
            }
            catch (JsonException e)
            {
                Debug.LogWarning("Bad faults payload: " + e.Message);
                return;
            }
            Changed?.Invoke();
        }

        void OnEvent(string json)
        {
            events.Insert(0, new RoomEvent { Time = DateTime.Now, Json = json.Trim() });
            if (events.Count > MaxEvents)
                events.RemoveAt(events.Count - 1);

            try
            {
                var e = JObject.Parse(json);
                // A door event moves the door right away instead of waiting for the next telemetry.
                if ((string)e["type"] == "door" && e["value"] != null)
                    DoorOpen = (int)e["value"] != 0;
                LogEvent(e);
            }
            catch (Exception ex) when (ex is JsonException || ex is InvalidCastException || ex is FormatException)
            {
                Debug.LogWarning("Bad event payload: " + ex.Message);
            }
            Changed?.Invoke();
        }

        void OnConnectionChanged(bool connected)
        {
            // Without the broker we can't know the board's status.
            if (!connected && Online)
                AddLog(LogKind.Esp, "Broker connection lost");
            if (!connected)
                Online = false;
            UpdateLive();
            Changed?.Invoke();
        }

        /// <summary>Turns an event from the board into a log line.</summary>
        void LogEvent(JObject e)
        {
            if (EventText.TryDescribe(e, Mode == "manual", out var kind, out var text))
                AddLog(kind, text);
        }

        void AddLog(LogKind kind, string text)
        {
            log.Insert(0, new LogEntry { Time = DateTime.Now, Kind = kind, Text = text });
            if (log.Count > MaxLog)
                log.RemoveAt(log.Count - 1);
        }

        static bool TryParse<T>(string json, string what, out T result) where T : class
        {
            result = null;
            try
            {
                result = JsonConvert.DeserializeObject<T>(json);
            }
            catch (JsonException e)
            {
                Debug.LogWarning("Bad " + what + " payload: " + e.Message);
            }
            return result != null;
        }
    }
}
