using System;
using System.Collections.Generic;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace SmartRoom
{
    /// <summary>The right-hand panel: live energy, climate, lights and room status, all read from the RoomModel.</summary>
    public class SidePanel : MonoBehaviour
    {
        [SerializeField] MqttConnection connection;
        [SerializeField] RoomModel model;

        [Header("Header")]
        [SerializeField] Image statusPill;
        [SerializeField] Image statusDot;
        [SerializeField] TMP_Text statusText;
        [SerializeField] TMP_Text brokerText;
        [Tooltip("Everything below the header; hidden until the ESP32 is live.")]
        [SerializeField] CanvasGroup body;
        [Tooltip("Shown instead of the body until the ESP32 is live.")]
        [SerializeField] GameObject waiting;
        [SerializeField] TMP_Text waitingText;
        [SerializeField] Image[] waitingDots;

        [Header("Fault banners")]
        [Tooltip("Banners sit between the header and the body; the body moves down to make room.")]
        [SerializeField] GameObject[] banners;
        [SerializeField] TMP_Text[] bannerTitles;
        [SerializeField] TMP_Text[] bannerDetails;
        [SerializeField] float bodyTop = 58f;
        [SerializeField] float bannerHeight = 58f;
        [SerializeField] float bannerGap = 6f;

        [Header("Compact layout while banners show")]
        [SerializeField] RectTransform energyCard;
        [Tooltip("Sparkline and its axis; hidden while banners show.")]
        [SerializeField] GameObject sparkGroup;
        [Tooltip("Energy rows and saved bar; move up when the sparkline hides.")]
        [SerializeField] RectTransform energyLower;
        [Tooltip("Tiles and controls; move up when the sparkline hides.")]
        [SerializeField] RectTransform[] belowEnergy;
        [SerializeField] float sparkSpace = 60f;
        [SerializeField] Image energyBorder;

        [Header("Energy")]
        [SerializeField] TMP_Text powerValue;
        [SerializeField] Sparkline sparkline;
        [Tooltip("Label under the left end of the sparkline.")]
        [SerializeField] TMP_Text sparklineStart;
        [SerializeField] TMP_Text energyValue;
        [SerializeField] TMP_Text savedValue;
        [SerializeField] RectTransform savedFill;

        [Header("Tiles")]
        [SerializeField] TMP_Text tempValue;
        [SerializeField] TMP_Text tempTarget;
        [SerializeField] TMP_Text acValue;
        [SerializeField] TMP_Text acDetail;
        [SerializeField] TMP_Text lightValue;
        [SerializeField] TMP_Text lightDetail;
        [SerializeField] Image occupancyDot;
        [SerializeField] TMP_Text occupancyText;
        [SerializeField] TMP_Text doorText;
        [SerializeField] TMP_Text modeText;
        [SerializeField] RectTransform lightsOverrideTag;
        [SerializeField] RectTransform acOverrideTag;
        [Tooltip("Tile backgrounds, red outlines and captions, in order: temperature, AC, lights, status.")]
        [SerializeField] Image[] tileBgs;
        [SerializeField] Image[] tileAlerts;
        [SerializeField] TMP_Text[] tileTitles;

        [Header("Footer")]
        [SerializeField] TMP_Text footer;

        [Header("Debug")]
        [Tooltip("Shown and hidden with F1.")]
        [SerializeField] GameObject debugPanel;

        [SerializeField] float tweenSpeed = 8f;
        [SerializeField] float staleSeconds = 6f;

        static readonly Color Text = Hex("E8ECF1");
        static readonly Color Secondary = new Color(0.91f, 0.925f, 0.945f, 0.58f);
        static readonly Color Warm = Hex("FFB36B");
        static readonly Color Yellow = Hex("FFD27A");
        static readonly Color Amber = Hex("FFB84D");
        static readonly Color Green = Hex("7EE2A8");
        static readonly Color PillOnBg = Hex("1F6F43");
        static readonly Color PillOnText = Hex("BFF5D4");
        static readonly Color PillOffBg = Hex("3A3F47");
        static readonly Color PillOffText = Hex("C0C6CE");
        static readonly Color DotOff = Hex("6B727C");
        static readonly Color Red = Hex("FF5D5D");
        static readonly Color RedText = Hex("FF8A8A");
        static readonly Color TileBg = new Color(1f, 1f, 1f, 0.05f);
        static readonly Color TileBgAlert = new Color(1f, 0.365f, 0.365f, 0.10f);
        static readonly Color EnergyBorderNormal = new Color(0.18f, 0.769f, 0.714f, 0.35f);
        static readonly IReadOnlyDictionary<string, string> NoFaults = new Dictionary<string, string>();

        // Values as shown, eased towards the latest reading. NaN means no reading.
        float power = float.NaN, energy = float.NaN, saved = float.NaN, temp = float.NaN;
        float ac = float.NaN, lightLevel = float.NaN, ambient = float.NaN;
        float nextSparkline;

        // Positions from the scene, so the compact layout can be undone exactly.
        Vector2 energyLowerPos;
        float energyCardHeight;
        Vector2[] belowEnergyPos;
        int shownBanners = -1;

        void Awake()
        {
            energyLowerPos = energyLower.anchoredPosition;
            energyCardHeight = energyCard.sizeDelta.y;
            belowEnergyPos = new Vector2[belowEnergy.Length];
            for (int i = 0; i < belowEnergy.Length; i++)
                belowEnergyPos[i] = belowEnergy[i].anchoredPosition;
        }

        /// <summary>The faults that matter right now: none unless the room is live.</summary>
        IReadOnlyDictionary<string, string> ActiveFaults => model.Live ? model.Faults.BySensor : NoFaults;

        bool HasFault(string key) => ActiveFaults.ContainsKey(key);

        void Update()
        {
            if (Keyboard.current != null && Keyboard.current.f1Key.wasPressedThisFrame && debugPanel != null)
                debugPanel.SetActive(!debugPanel.activeSelf);

            if (model == null)
                return;

            UpdateHeader();
            UpdateBanners();
            if (!model.Live)
            {
                ShowWaiting();
                return;
            }
            HideWaiting();

            var t = model.Telemetry;
            float k = 1f - Mathf.Exp(-tweenSpeed * Time.deltaTime);
            Ease(ref power, t?.PowerW, k);
            Ease(ref energy, t?.EnergyWh, k);
            Ease(ref saved, t?.SavedPct, k);
            Ease(ref temp, t?.TempC, k);
            Ease(ref ac, model.FanLevel, k);
            Ease(ref lightLevel, model.LightLevel, k);
            Ease(ref ambient, t?.LightPct, k);

            UpdateEnergy();
            UpdateTiles();
            UpdateFooter();
        }

        void UpdateHeader()
        {
            bool online = model.Live;
            statusPill.color = online ? PillOnBg : PillOffBg;
            statusText.color = online ? PillOnText : PillOffText;
            statusText.text = online ? "ESP32 online" : "ESP32 offline";
            if (statusDot != null)
                statusDot.color = statusText.color;

            bool connected = connection != null && connection.IsConnected;
            bool cloud = connected && connection.Settings != null && connection.Settings.useCloud;
            brokerText.text = "Broker: " + (connected ? (cloud ? "Cloud" : "Local") : "Disconnected");

        }

        void ShowWaiting()
        {
            body.alpha = 0f;
            body.interactable = false;
            body.blocksRaycasts = false;
            waiting.SetActive(true);

            bool broker = connection != null && connection.IsConnected;
            waitingText.text = broker ? "Waiting for ESP to connect" : "Waiting for broker connection";

            // Three dots pulsing one after another.
            for (int i = 0; i < waitingDots.Length; i++)
            {
                float phase = Mathf.Repeat(Time.unscaledTime * 1.2f - i * 0.2f, 1f);
                float a = 0.25f + 0.75f * Mathf.Max(0f, Mathf.Sin(phase * Mathf.PI));
                var c = waitingDots[i].color;
                waitingDots[i].color = new Color(c.r, c.g, c.b, a);
            }

            // Forget the shown values so fresh ones appear without easing from old ones.
            power = energy = saved = temp = ac = lightLevel = ambient = float.NaN;
        }

        void HideWaiting()
        {
            body.alpha = 1f;
            body.interactable = true;
            body.blocksRaycasts = true;
            waiting.SetActive(false);
        }

        /// <summary>
        /// One banner per active fault (at most two; the second says how many more there are).
        /// While banners show, the sparkline hides to make room so the panel still fits the screen.
        /// </summary>
        void UpdateBanners()
        {
            var faults = ActiveFaults;
            int count = faults.Count;
            int shown = Mathf.Min(count, banners.Length);

            int i = 0;
            foreach (var f in faults)
            {
                if (i >= shown)
                    break;
                string title = FaultInfo.Name(f.Key) + " failed";
                if (i == shown - 1 && count > shown)
                    title += "  (and " + (count - shown) + " more)";
                bannerTitles[i].text = title;
                string reason = string.IsNullOrEmpty(f.Value) ? "" : Capitalise(f.Value.Trim()) + ". ";
                bannerDetails[i].text = reason + FaultInfo.Effect(f.Key);
                i++;
            }

            if (shown == shownBanners)
                return;
            shownBanners = shown;

            for (int b = 0; b < banners.Length; b++)
            {
                banners[b].SetActive(b < shown);
                var rt = (RectTransform)banners[b].transform;
                rt.anchoredPosition = new Vector2(rt.anchoredPosition.x, -b * (bannerHeight + bannerGap));
            }

            float stack = shown == 0 ? 0f : shown * bannerHeight + (shown - 1) * bannerGap + 8f;
            body.GetComponent<RectTransform>().offsetMax = new Vector2(0f, -(bodyTop + stack));

            bool compact = shown > 0;
            float lift = compact ? sparkSpace : 0f;
            sparkGroup.SetActive(!compact);
            energyCard.sizeDelta = new Vector2(energyCard.sizeDelta.x, energyCardHeight - lift);
            energyLower.anchoredPosition = energyLowerPos + new Vector2(0f, lift);
            for (int b = 0; b < belowEnergy.Length; b++)
                belowEnergy[b].anchoredPosition = belowEnergyPos[b] + new Vector2(0f, lift);

            // With two banners there is no room left for the footer.
            footer.gameObject.SetActive(shown < 2);
        }

        void SetTileAlert(int index, bool on)
        {
            tileBgs[index].color = on ? TileBgAlert : TileBg;
            tileAlerts[index].color = on ? Red : Color.clear;
            tileTitles[index].color = on ? RedText : Secondary;
        }

        void UpdateEnergy()
        {
            energyBorder.color = HasFault(FaultInfo.Sen0291) ? Red : EnergyBorderNormal;
            powerValue.text = float.IsNaN(power) ? "--" : Fmt(power, "0.00") + "<size=20> W</size>";
            energyValue.text = float.IsNaN(energy) ? "--" : Fmt(energy, energy < 10f ? "0.00" : "0.0") + " Wh";
            savedValue.text = float.IsNaN(saved) ? "--" : Fmt(saved, "0") + " %";
            float fill = float.IsNaN(saved) ? 0f : Mathf.Clamp01(saved / 100f);
            savedFill.anchorMax = new Vector2(fill, 1f);

            if (Time.unscaledTime >= nextSparkline)
            {
                nextSparkline = Time.unscaledTime + 0.25f;
                sparkline.SetData(model.PowerHistory, Time.realtimeSinceStartup);
                if (sparklineStart != null)
                {
                    float span = sparkline.VisibleSeconds;
                    sparklineStart.text = span >= sparkline.WindowSeconds - 1f
                        ? "-" + Fmt(sparkline.WindowSeconds / 60f, "0") + " min"
                        : "-" + Fmt(span, "0") + " s";
                }
            }
        }

        void UpdateTiles()
        {
            bool lm35 = HasFault(FaultInfo.Lm35);
            bool lightFault = HasFault(FaultInfo.Light);
            bool pir = HasFault(FaultInfo.Pir);
            bool door = HasFault(FaultInfo.Hcsr04);
            SetTileAlert(0, lm35);
            SetTileAlert(1, lm35);
            SetTileAlert(2, lightFault);
            SetTileAlert(3, pir || door);

            float? target = model.SetpointC;
            tempValue.text = float.IsNaN(temp) || lm35 ? "--" : Fmt(temp, "0.0") + "°";
            tempValue.color = !float.IsNaN(temp) && !lm35 && target.HasValue && temp > target.Value ? Warm : Text;
            if (lm35)
            {
                tempTarget.text = "Sensor fault";
                tempTarget.color = RedText;
            }
            else
            {
                tempTarget.text = "target " + (target.HasValue ? Fmt(target.Value, "0.0") + "°" : "--");
                tempTarget.color = Secondary;
            }

            acValue.text = float.IsNaN(ac) ? "--" : Fmt(ac, "0") + " %";
            if (lm35)
            {
                acDetail.text = model.AcOverride == true && model.FanLevel > 0 ? "Override: no temperature" : "Off: no temperature";
                acDetail.color = RedText;
            }
            else if (model.AcPaused == true)
            {
                acDetail.text = "Paused: door open";
                acDetail.color = Yellow;
            }
            else
            {
                acDetail.text = !float.IsNaN(ac) && model.FanLevel > 0 ? "Running" : "Off";
                acDetail.color = Secondary;
            }

            lightValue.text = float.IsNaN(lightLevel) ? "--" : Fmt(lightLevel, "0") + " %";
            if (lightFault)
            {
                lightDetail.text = "daylight --  ·  Sensor fault";
                lightDetail.color = RedText;
            }
            else
            {
                lightDetail.text = "daylight " + (float.IsNaN(ambient) ? "--" : Fmt(ambient, "0") + " %");
                lightDetail.color = Secondary;
            }

            bool? occ = model.Occupied;
            if (pir)
            {
                occupancyDot.color = DotOff;
                occupancyText.text = "Occupancy unknown";
                occupancyText.color = RedText;
            }
            else
            {
                occupancyDot.color = occ == true ? Green : DotOff;
                occupancyText.text = occ.HasValue ? (occ.Value ? "Occupied" : "Empty") : "--";
                occupancyText.color = Text;
            }
            if (door)
            {
                doorText.text = "Door: unknown";
                doorText.color = RedText;
            }
            else
            {
                doorText.text = "Door: " + (model.DoorOpen.HasValue ? (model.DoorOpen.Value ? "Open" : "Closed") : "--");
                doorText.color = Secondary;
            }
            modeText.text = "Mode: " + (string.IsNullOrEmpty(model.Mode) ? "--" : Capitalise(model.Mode));

            // Tags sit side by side; the second wraps below if they don't fit.
            bool lightsOvr = model.LightsOverride == true;
            bool acOvr = model.AcOverride == true;
            lightsOverrideTag.gameObject.SetActive(lightsOvr);
            acOverrideTag.gameObject.SetActive(acOvr);
            if (lightsOvr && acOvr)
            {
                float width = ((RectTransform)lightsOverrideTag.parent).rect.width;
                bool fits = lightsOverrideTag.rect.width + 6f + acOverrideTag.rect.width <= width;
                acOverrideTag.anchoredPosition = fits
                    ? new Vector2(lightsOverrideTag.rect.width + 6f, 0f)
                    : new Vector2(0f, -(lightsOverrideTag.rect.height + 4f));
            }
            else if (acOvr)
            {
                acOverrideTag.anchoredPosition = Vector2.zero;
            }
        }

        void UpdateFooter()
        {
            if (model.Telemetry == null)
            {
                footer.text = "Waiting for data";
                footer.color = Secondary;
                return;
            }
            double age = (DateTime.Now - model.LastTelemetryTime).TotalSeconds;
            footer.text = "Last update " + FormatAge(age) + " ago";
            footer.color = age > staleSeconds ? Amber : new Color(Text.r, Text.g, Text.b, 0.5f);
        }

        static void Ease(ref float shown, float? target, float k)
        {
            if (!target.HasValue)
            {
                shown = float.NaN;
                return;
            }
            shown = float.IsNaN(shown) ? target.Value : Mathf.Lerp(shown, target.Value, k);
        }

        static void Ease(ref float shown, int? target, float k) =>
            Ease(ref shown, target.HasValue ? (float?)target.Value : null, k);

        static string FormatAge(double seconds)
        {
            if (seconds < 60)
                return Math.Max(0, (int)seconds) + " s";
            if (seconds < 3600)
                return (int)(seconds / 60) + " min";
            return (int)(seconds / 3600) + " h";
        }

        static string Fmt(float v, string format) => v.ToString(format, CultureInfo.InvariantCulture);

        static string Capitalise(string s) => char.ToUpperInvariant(s[0]) + s.Substring(1);

        static Color Hex(string hex)
        {
            ColorUtility.TryParseHtmlString("#" + hex, out var c);
            return c;
        }
    }
}
