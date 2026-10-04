using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace SmartRoom
{
    /// <summary>One day of history from the PC logger (see docs/mqtt-contract.md, "history").</summary>
    public class HistoryDay
    {
        [JsonProperty("day")] public string Day;
        [JsonProperty("totals")] public HistoryTotals Totals = new HistoryTotals();
        [JsonProperty("series")] public List<HistoryPoint> Series = new List<HistoryPoint>();
        [JsonProperty("events")] public List<JObject> Events = new List<JObject>();

        public bool HasData => Series.Count > 0 || Events.Count > 0;
    }

    public class HistoryTotals
    {
        [JsonProperty("energy_wh")] public float? EnergyWh;
        [JsonProperty("saved_pct")] public float? SavedPct;
        [JsonProperty("occupied_s")] public long? OccupiedSeconds;
        [JsonProperty("faults")] public int? Faults;
    }

    public class HistoryPoint
    {
        [JsonProperty("t")] public string Time;
        [JsonProperty("power_w")] public float? PowerW;
        [JsonProperty("temp_c")] public float? TempC;
        [JsonProperty("set_c")] public float? SetC;
        [JsonProperty("saved_pct")] public float? SavedPct;
        [JsonProperty("occ")] public float? Occupied;

        /// <summary>Minutes since midnight of "t" (HH:mm).</summary>
        public float Minutes
        {
            get
            {
                if (TimeSpan.TryParseExact(Time, @"hh\:mm", CultureInfo.InvariantCulture, out var ts))
                    return (float)ts.TotalMinutes;
                return 0f;
            }
        }
    }

    /// <summary>
    /// Asks the PC history logger for history over MQTT and hands back the answers.
    /// Also starts the logger if it isn't running (it normally starts with Windows).
    /// </summary>
    public class HistoryClient : MonoBehaviour
    {
        [SerializeField] MqttConnection connection;
        [Tooltip("Seconds to wait for the logger before giving up.")]
        [SerializeField] float timeoutSeconds = 3f;

        public event Action<HistoryDay> DayReceived;
        public event Action<List<string>> DaysReceived;
        /// <summary>No answer in time: the logger (or the broker) isn't running.</summary>
        public event Action TimedOut;

        float dayAskedAt = -1f;
        string dayAsked;
        float listAskedAt = -1f;

        void Awake()
        {
            if (connection == null)
                connection = GetComponent<MqttConnection>();
        }

        void Start()
        {
            LaunchLoggerIfMissing();
        }

        void OnEnable()
        {
            if (connection != null)
                connection.MessageReceived += OnMessage;
        }

        void OnDisable()
        {
            if (connection != null)
                connection.MessageReceived -= OnMessage;
        }

        public void RequestDay(DateTime day)
        {
            dayAsked = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            dayAskedAt = Time.unscaledTime;
            Send(dayAsked);
        }

        public void RequestDayList()
        {
            listAskedAt = Time.unscaledTime;
            Send("list");
        }

        void Send(string day)
        {
            var json = new JObject { ["day"] = day, ["client"] = connection.ClientId }.ToString(Formatting.None);
            if (!connection.Publish("history/req", json, atLeastOnce: true))
            {
                // Not even connected to the broker: report it straight away.
                dayAskedAt = listAskedAt = -1f;
                TimedOut?.Invoke();
            }
        }

        void Update()
        {
            float now = Time.unscaledTime;
            bool late = (dayAskedAt >= 0f && now - dayAskedAt > timeoutSeconds) ||
                        (listAskedAt >= 0f && now - listAskedAt > timeoutSeconds);
            if (late)
            {
                dayAskedAt = listAskedAt = -1f;
                TimedOut?.Invoke();
            }
        }

        void OnMessage(string subtopic, string payload)
        {
            if (subtopic != "history/resp/" + connection.ClientId)
                return;
            JObject answer;
            try
            {
                answer = JObject.Parse(payload);
            }
            catch (JsonException e)
            {
                Debug.LogWarning("Bad history answer: " + e.Message);
                return;
            }

            if (answer["days"] is JArray days)
            {
                listAskedAt = -1f;
                var list = new List<string>();
                foreach (var d in days)
                    list.Add((string)d);
                DaysReceived?.Invoke(list);
                return;
            }

            var day = answer.ToObject<HistoryDay>();
            if (day == null || day.Day != dayAsked)
                return;   // an answer to an older request
            dayAskedAt = -1f;
            DayReceived?.Invoke(day);
        }

        void LaunchLoggerIfMissing()
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            var s = connection != null ? connection.Settings : null;
            if (s == null || !s.launchLoggerIfMissing || string.IsNullOrEmpty(s.loggerPath))
                return;
            try
            {
                if (System.Diagnostics.Process.GetProcessesByName("HistoryLogger").Length > 0)
                    return;
                if (!System.IO.File.Exists(s.loggerPath))
                {
                    Debug.LogWarning("History logger not found at " + s.loggerPath);
                    return;
                }
                var start = new System.Diagnostics.ProcessStartInfo(s.loggerPath)
                {
                    WorkingDirectory = System.IO.Path.GetDirectoryName(s.loggerPath),
                    UseShellExecute = true,
                };
                System.Diagnostics.Process.Start(start);
                Debug.Log("Started the history logger.");
            }
            catch (Exception e)
            {
                Debug.LogWarning("Couldn't start the history logger: " + e.Message);
            }
#endif
        }
    }
}
