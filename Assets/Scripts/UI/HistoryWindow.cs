using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace SmartRoom
{
    /// <summary>
    /// The History window: one day of data from the PC logger, with totals, power / temperature / saving charts
    /// (occupied periods shaded) and that day's events. Works whether or not the ESP32 is live.
    /// </summary>
    public class HistoryWindow : MonoBehaviour
    {
        [SerializeField] HistoryClient history;
        [SerializeField] GameObject root;
        [SerializeField] Button openButton;

        [Header("Header")]
        [SerializeField] Button closeButton;
        [SerializeField] Button prevButton;
        [SerializeField] Button nextButton;
        [SerializeField] Button pickButton;
        [SerializeField] TMP_Text dateLabel;

        [Header("Day picker")]
        [SerializeField] GameObject picker;
        [SerializeField] RectTransform pickerContent;
        [SerializeField] GameObject pickerItemTemplate;

        [Header("Stats")]
        [SerializeField] TMP_Text energyValue;
        [SerializeField] TMP_Text savedValue;
        [SerializeField] TMP_Text occupiedValue;
        [SerializeField] TMP_Text faultsValue;

        [Header("Charts")]
        [SerializeField] LineChart powerChart;
        [SerializeField] TMP_Text powerNote;
        [SerializeField] TMP_Text[] powerAxis;   // bottom, middle, top
        [SerializeField] LineChart tempChart;
        [SerializeField] TMP_Text tempNote;
        [SerializeField] TMP_Text[] tempAxis;
        [SerializeField] LineChart savedChart;
        [SerializeField] TMP_Text savedNote;
        [SerializeField] TMP_Text[] savedAxis;

        [Header("Events")]
        [SerializeField] RectTransform eventsContent;
        [SerializeField] GameObject eventRowTemplate;
        [SerializeField] TMP_Text eventsEmpty;

        [Header("Messages")]
        [Tooltip("Covers the stats and charts while loading, when the logger doesn't answer, or when the day is empty.")]
        [SerializeField] GameObject messageRoot;
        [SerializeField] TMP_Text messageText;

        static readonly Color Teal = Hex("2EC4B6");
        static readonly Color Orange = Hex("FFB36B");
        static readonly Color Yellow = Hex("FFD27A");
        static readonly Color Green = Hex("7EE2A8");

        DateTime day = DateTime.Today;
        List<string> days = new List<string>();
        readonly List<GameObject> eventRows = new List<GameObject>();
        readonly List<GameObject> pickerItems = new List<GameObject>();

        public bool IsOpen => root.activeSelf;

        void Awake()
        {
            openButton.onClick.AddListener(Open);
            closeButton.onClick.AddListener(Close);
            prevButton.onClick.AddListener(() => ShowDay(day.AddDays(-1)));
            nextButton.onClick.AddListener(() => ShowDay(day.AddDays(1)));
            pickButton.onClick.AddListener(TogglePicker);
            eventRowTemplate.SetActive(false);
            pickerItemTemplate.SetActive(false);
            picker.SetActive(false);
            root.SetActive(false);
        }

        void OnEnable()
        {
            history.DayReceived += OnDay;
            history.DaysReceived += OnDays;
            history.TimedOut += OnTimedOut;
        }

        void OnDisable()
        {
            history.DayReceived -= OnDay;
            history.DaysReceived -= OnDays;
            history.TimedOut -= OnTimedOut;
        }

        void Update()
        {
            if (IsOpen && Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
                Close();
        }

        public void Open()
        {
            root.SetActive(true);
            picker.SetActive(false);
            ShowDay(DateTime.Today);
            history.RequestDayList();
        }

        public void Close()
        {
            root.SetActive(false);
        }

        /// <summary>Asks the logger for a day (never later than today).</summary>
        public void ShowDay(DateTime value)
        {
            day = value.Date > DateTime.Today ? DateTime.Today : value.Date;
            picker.SetActive(false);
            dateLabel.text = (day == DateTime.Today ? "Today, " : day.ToString("ddd", CultureInfo.InvariantCulture) + ", ") +
                             day.ToString("d MMM yyyy", CultureInfo.InvariantCulture);
            nextButton.interactable = day < DateTime.Today;
            ShowMessage("Loading...");
            history.RequestDay(day);
        }

        void TogglePicker()
        {
            if (picker.activeSelf)
            {
                picker.SetActive(false);
                return;
            }
            foreach (var item in pickerItems)
                Destroy(item);
            pickerItems.Clear();

            var list = days.Count > 0 ? days : new List<string> { DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) };
            // Newest first.
            for (int i = list.Count - 1; i >= 0; i--)
            {
                if (!DateTime.TryParseExact(list[i], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
                    continue;
                var item = Instantiate(pickerItemTemplate, pickerContent);
                item.SetActive(true);
                item.GetComponentInChildren<TMP_Text>().text = d.ToString("ddd d MMM yyyy", CultureInfo.InvariantCulture);
                item.GetComponent<Button>().onClick.AddListener(() => ShowDay(d));
                pickerItems.Add(item);
            }
            picker.SetActive(true);
        }

        void OnDays(List<string> list)
        {
            days = list ?? new List<string>();
        }

        void OnTimedOut()
        {
            if (IsOpen)
            {
                ClearDay();
                ShowMessage("History unavailable: logger not running");
            }
        }

        void OnDay(HistoryDay d)
        {
            if (!IsOpen)
                return;
            if (!d.HasData)
            {
                ClearDay();
                ShowMessage("No data for this day");
                return;
            }
            messageRoot.SetActive(false);
            FillStats(d.Totals);
            FillCharts(d);
            FillEvents(d.Events);
        }

        void ShowMessage(string text)
        {
            messageRoot.SetActive(true);
            messageText.text = text;
        }

        void ClearDay()
        {
            energyValue.text = savedValue.text = occupiedValue.text = faultsValue.text = "--";
            foreach (var c in new[] { powerChart, tempChart, savedChart })
                c.Clear();
            powerNote.text = tempNote.text = savedNote.text = "";
            FillEvents(new List<Newtonsoft.Json.Linq.JObject>());
        }

        void FillStats(HistoryTotals t)
        {
            energyValue.text = t.EnergyWh.HasValue ? Fmt(t.EnergyWh.Value, t.EnergyWh.Value < 10f ? "0.00" : "0.0") + " Wh" : "--";
            savedValue.text = t.SavedPct.HasValue ? Fmt(t.SavedPct.Value, "0") + " %" : "--";
            if (t.OccupiedSeconds.HasValue)
            {
                var span = TimeSpan.FromSeconds(t.OccupiedSeconds.Value);
                occupiedValue.text = (int)span.TotalHours + " h " + span.Minutes + " m";
            }
            else
            {
                occupiedValue.text = "--";
            }
            faultsValue.text = t.Faults.HasValue ? t.Faults.Value.ToString(CultureInfo.InvariantCulture) : "--";
        }

        void FillCharts(HistoryDay d)
        {
            var power = Points(d.Series, p => p.PowerW);
            var temp = Points(d.Series, p => p.TempC);
            var target = Points(d.Series, p => p.SetC);
            var saved = Points(d.Series, p => p.SavedPct);

            // Power: from 0 to a bit above the peak.
            float peak = power.Count > 0 ? power.Max(p => p.y) : 0f;
            Chart(powerChart, 0f, Mathf.Max(0.5f, peak * 1.15f), powerAxis, "0.0", " W",
                  new LineChart.Series { Points = power, Color = Teal, Thickness = 2f });
            powerNote.text = power.Count > 0 ? "peak " + Fmt(peak, "0.00") + " W" : "";

            // Temperature and target: a degree of space above and below both.
            var both = temp.Concat(target).ToList();
            float tMin = both.Count > 0 ? Mathf.Floor(both.Min(p => p.y) - 1f) : 20f;
            float tMax = both.Count > 0 ? Mathf.Ceil(both.Max(p => p.y) + 1f) : 30f;
            Chart(tempChart, tMin, tMax, tempAxis, "0", "°",
                  new LineChart.Series { Points = target, Color = Yellow, Thickness = 1.5f, Dashed = true },
                  new LineChart.Series { Points = temp, Color = Orange, Thickness = 2f });
            tempNote.text = temp.Count > 0 ? "avg " + Fmt(temp.Average(p => p.y), "0.0") + "°" : "";

            // Saved: always 0-100 %.
            Chart(savedChart, 0f, 100f, savedAxis, "0", " %",
                  new LineChart.Series { Points = saved, Color = Green, Thickness = 2f });
            savedNote.text = d.Totals.SavedPct.HasValue ? Fmt(d.Totals.SavedPct.Value, "0") + " % today" : "";

            // Occupied periods: 5-minute buckets occupied at least half the time, merged into bands.
            var bands = new List<Vector2>();
            foreach (var p in d.Series)
            {
                if (!(p.Occupied >= 0.5f))
                    continue;
                float start = p.Minutes, end = start + 5f;
                if (bands.Count > 0 && Mathf.Abs(bands[bands.Count - 1].y - start) < 0.5f)
                    bands[bands.Count - 1] = new Vector2(bands[bands.Count - 1].x, end);
                else
                    bands.Add(new Vector2(start, end));
            }
            foreach (var c in new[] { powerChart, tempChart, savedChart })
            {
                foreach (var b in bands)
                    c.AddBand(b.x, b.y);
            }
        }

        static void Chart(LineChart chart, float min, float max, TMP_Text[] axis, string format, string unit, params LineChart.Series[] series)
        {
            chart.Clear();
            chart.SetRange(min, max);
            foreach (var s in series)
                chart.AddSeries(s);
            for (int i = 0; i < axis.Length; i++)
            {
                float v = Mathf.Lerp(min, max, axis.Length == 1 ? 0f : i / (float)(axis.Length - 1));
                axis[i].text = Fmt(v, format) + unit;
            }
        }

        static List<Vector2> Points(List<HistoryPoint> series, Func<HistoryPoint, float?> value)
        {
            var list = new List<Vector2>();
            foreach (var p in series)
            {
                var v = value(p);
                // Plot each 5-minute bucket at its middle.
                if (v.HasValue)
                    list.Add(new Vector2(p.Minutes + 2.5f, v.Value));
            }
            return list;
        }

        void FillEvents(List<Newtonsoft.Json.Linq.JObject> events)
        {
            foreach (var row in eventRows)
                Destroy(row);
            eventRows.Clear();

            // Newest on top.
            for (int i = events.Count - 1; i >= 0; i--)
            {
                var e = events[i];
                if (!EventText.TryDescribe(e, false, out var kind, out var text))
                    continue;
                var row = Instantiate(eventRowTemplate, eventsContent);
                row.SetActive(true);
                row.transform.Find("Time").GetComponent<TMP_Text>().text = (string)e["t"] ?? "";
                row.transform.Find("Dot").GetComponent<Image>().color = EventLogPanel.KindColor(kind);
                row.transform.Find("Text").GetComponent<TMP_Text>().text = text;
                eventRows.Add(row);
            }
            eventsEmpty.gameObject.SetActive(eventRows.Count == 0);
            eventsContent.anchoredPosition = Vector2.zero;
        }

        static string Fmt(float v, string format) => v.ToString(format, CultureInfo.InvariantCulture);

        static Color Hex(string hex)
        {
            ColorUtility.TryParseHtmlString("#" + hex, out var c);
            return c;
        }
    }
}
