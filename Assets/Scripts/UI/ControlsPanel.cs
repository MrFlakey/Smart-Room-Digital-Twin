using System;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SmartRoom
{
    /// <summary>
    /// The controls card: mode, on/off switches for the lights and the fan, and the target temperature.
    /// A switch works in both modes; in Auto it also puts that device into override until the room empties.
    /// Sends commands on .../cmd and shows the values the room confirms, not just what was clicked.
    /// </summary>
    public class ControlsPanel : MonoBehaviour
    {
        [SerializeField] MqttConnection connection;
        [SerializeField] RoomModel model;
        [Tooltip("Dimmed and locked while the room is offline.")]
        [SerializeField] CanvasGroup card;

        [Header("Mode")]
        [SerializeField] Button autoButton;
        [SerializeField] Image autoBg;
        [SerializeField] TMP_Text autoLabel;
        [SerializeField] Button manualButton;
        [SerializeField] Image manualBg;
        [SerializeField] TMP_Text manualLabel;

        [Header("Lights switch (B1)")]
        [SerializeField] Button lightSwitch;
        [SerializeField] Image lightSwitchTrack;
        [SerializeField] RectTransform lightSwitchKnob;
        [SerializeField] TMP_Text lightStatus;
        [SerializeField] GameObject lightOverrideBadge;

        [Header("Fan switch (B2)")]
        [SerializeField] Button fanSwitch;
        [SerializeField] Image fanSwitchTrack;
        [SerializeField] RectTransform fanSwitchKnob;
        [SerializeField] TMP_Text fanStatus;
        [SerializeField] GameObject fanOverrideBadge;

        [Tooltip("Seconds for a knob to slide across.")]
        [SerializeField] float switchAnimSeconds = 0.15f;

        [Header("Target temperature")]
        [SerializeField] Button setpointDown;
        [SerializeField] Button setpointUp;
        [SerializeField] TMP_Text setpointValue;

        [Header("Feedback")]
        [SerializeField] TMP_Text feedback;

        [SerializeField] float confirmTimeout = 3f;
        [SerializeField] float confirmedShowSeconds = 3f;

        public const float MinSetpoint = 18f;
        public const float MaxSetpoint = 30f;
        public const float SetpointStep = 0.5f;

        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        static readonly Color Teal = Hex("2EC4B6");
        static readonly Color Dark = Hex("0E1115");
        static readonly Color Text = Hex("E8ECF1");
        static readonly Color Secondary = new Color(0.91f, 0.925f, 0.945f, 0.58f);
        static readonly Color Green = Hex("7EE2A8");
        static readonly Color Amber = Hex("FFB84D");
        static readonly Color RedText = Hex("FF8A8A");
        static readonly Color SwitchOffTrack = new Color(1f, 1f, 1f, 0.15f);

        /// <summary>A command waiting for the room to report the matching value.</summary>
        class Pending
        {
            public string What;
            public Func<bool> Confirmed;
            public float Since;
        }

        /// <summary>One on/off switch. On sends 100, off sends 0; it shows on while the reported level is above 0.</summary>
        class SwitchState
        {
            public string Key;
            public string Name;
            public Button Button;
            public Image Track;
            public RectTransform Knob;
            public TMP_Text Status;
            public GameObject Badge;
            public Func<int?> Reported;
            public Func<bool> Overridden;
            public Func<bool> Faulted;      // true while a sensor this switch's automation needs has failed
            public CanvasGroup Group;
            public bool? Requested;   // shown while the command is in flight
            public Pending Pending;
            public float Position;    // 0 = off (left), 1 = on (right), animated

            public bool ShownOn => Requested ?? (Reported() is int level && level > 0);
        }

        SwitchState lights;
        SwitchState fan;
        Pending pending;          // latest command, drives the feedback line
        float? pendingSetpoint;   // shown while the setpoint change is in flight
        float confirmedAt = -100f;
        float timedOutAt = -100f;

        void Awake()
        {
            lights = new SwitchState
            {
                Key = "light", Name = "lights", Button = lightSwitch, Track = lightSwitchTrack, Knob = lightSwitchKnob,
                Status = lightStatus, Badge = lightOverrideBadge,
                Reported = () => model.LightLevel, Overridden = () => model.LightsOverride == true,
                Faulted = () => HasFault(FaultInfo.Light) || HasFault(FaultInfo.Pir)
            };
            fan = new SwitchState
            {
                Key = "fan", Name = "fan", Button = fanSwitch, Track = fanSwitchTrack, Knob = fanSwitchKnob,
                Status = fanStatus, Badge = fanOverrideBadge,
                Reported = () => model.FanLevel, Overridden = () => model.AcOverride == true,
                Faulted = () => HasFault(FaultInfo.Lm35) || HasFault(FaultInfo.Pir)
            };
            foreach (var s in new[] { lights, fan })
            {
                s.Group = s.Button.GetComponent<CanvasGroup>();
                if (s.Group == null)
                    s.Group = s.Button.gameObject.AddComponent<CanvasGroup>();
            }

            autoButton.onClick.AddListener(() => SetMode(false));
            manualButton.onClick.AddListener(() => SetMode(true));
            setpointDown.onClick.AddListener(() => StepSetpoint(-SetpointStep));
            setpointUp.onClick.AddListener(() => StepSetpoint(SetpointStep));
            lightSwitch.onClick.AddListener(() => SetSwitch(lights, !lights.ShownOn));
            fanSwitch.onClick.AddListener(() => SetSwitch(fan, !fan.ShownOn));
        }

        // ---- Actions (also callable from tests) ----

        public void SetMode(bool manual)
        {
            string mode = manual ? "manual" : "auto";
            if (model.Mode == mode)
                return;
            Send("{\"mode\":\"" + mode + "\"}", "mode", () => model.Mode == mode);
        }

        public void StepSetpoint(float delta)
        {
            float current = pendingSetpoint ?? model.SetpointC ?? 25f;
            float target = Mathf.Clamp(Mathf.Round((current + delta) / SetpointStep) * SetpointStep, MinSetpoint, MaxSetpoint);
            if (Mathf.Approximately(target, current))
                return;
            pendingSetpoint = target;
            Send("{\"set_c\":" + target.ToString("0.0", Inv) + "}", "target",
                 () => model.SetpointC.HasValue && Mathf.Abs(model.SetpointC.Value - target) < 0.05f);
        }

        /// <summary>Turns the lights on (100) or off (0). In Auto this also starts the lights override.</summary>
        public void SetLight(bool on) => SetSwitch(lights, on);

        /// <summary>Turns the fan on (100) or off (0). In Auto this also starts the AC override.</summary>
        public void SetFan(bool on) => SetSwitch(fan, on);

        void SetSwitch(SwitchState s, bool on)
        {
            if (!s.Button.interactable)
                return;
            bool auto = model.Mode != "manual";
            s.Requested = on;
            s.Pending = Send("{\"" + s.Key + "\":" + (on ? 100 : 0) + "}", s.Name + (on ? " on" : " off"),
                             () => Matches(s, on, auto));
        }

        /// <summary>
        /// True once the room reports the switch's new state. In Auto the override flag must be set too.
        /// The fan counts as on while the AC is paused by an open door, since the board holds it at 0 then.
        /// </summary>
        bool Matches(SwitchState s, bool on, bool auto)
        {
            if (auto && !s.Overridden())
                return false;
            if (!(s.Reported() is int level))
                return false;
            if ((level > 0) == on)
                return true;
            return on && s == fan && model.AcPaused == true;
        }

        Pending Send(string json, string what, Func<bool> confirmed)
        {
            if (connection == null || !connection.Publish("cmd", json, atLeastOnce: true))
            {
                timedOutAt = Time.unscaledTime;
                pending = null;
                return null;
            }
            pending = new Pending { What = what, Confirmed = confirmed, Since = Time.unscaledTime };
            return pending;
        }

        // ---- Display ----

        void Update()
        {
            if (model == null)
                return;

            bool online = model.Live && connection != null && connection.IsConnected;
            bool manual = model.Mode == "manual";

            card.interactable = online;
            card.alpha = online ? 1f : 0.6f;

            // Mode
            Segment(autoBg, autoLabel, !manual && model.Mode != null);
            Segment(manualBg, manualLabel, manual);

            UpdatePending();

            // Switches work in both modes while the room is online.
            UpdateSwitch(lights, online, manual);
            UpdateSwitch(fan, online, manual);

            // Setpoint
            float? setpoint = pendingSetpoint ?? model.SetpointC;
            setpointValue.text = setpoint.HasValue ? setpoint.Value.ToString("0.0", Inv) + "°" : "--";
            setpointDown.interactable = setpoint.HasValue && setpoint.Value > MinSetpoint;
            setpointUp.interactable = setpoint.HasValue && setpoint.Value < MaxSetpoint;

            UpdateFeedback(online);
        }

        void UpdatePending()
        {
            float now = Time.unscaledTime;
            if (pending != null)
            {
                if (pending.Confirmed())
                {
                    pending = null;
                    confirmedAt = now;
                    timedOutAt = -100f;
                }
                else if (now - pending.Since > confirmTimeout)
                {
                    pending = null;
                    timedOutAt = now;
                }
            }

            foreach (var s in new[] { lights, fan })
            {
                if (s.Pending != null && (s.Pending.Confirmed() || now - s.Pending.Since > confirmTimeout))
                    s.Pending = null;
                if (s.Pending == null)
                    s.Requested = null; // confirmed, timed out, or never sent: show what the room reports
            }

            if (pendingSetpoint.HasValue && model.SetpointC.HasValue && Mathf.Abs(model.SetpointC.Value - pendingSetpoint.Value) < 0.05f)
                pendingSetpoint = null;
            if (pendingSetpoint.HasValue && pending == null)
                pendingSetpoint = null; // timed out or superseded: fall back to what the room reports
        }

        bool HasFault(string key) => model.Live && model.Faults.BySensor.ContainsKey(key);

        void UpdateSwitch(SwitchState s, bool online, bool manual)
        {
            // A failed sensor turns off the automation for this device, but the switch keeps working:
            // in auto, flipping it starts an override as usual.
            bool faulted = !manual && s.Faulted();
            s.Button.interactable = online;
            s.Group.alpha = 1f;
            if (faulted)
            {
                s.Status.text = "Auto off: sensor fault";
                s.Status.color = RedText;
            }
            else
            {
                s.Status.text = model.Mode == null ? "--" : (manual ? "Manual" : "Auto");
                s.Status.color = Secondary;
            }
            s.Badge.SetActive(!manual && s.Overridden());

            float step = switchAnimSeconds > 0f ? Time.unscaledDeltaTime / switchAnimSeconds : 1f;
            s.Position = Mathf.MoveTowards(s.Position, s.ShownOn ? 1f : 0f, step);
            float eased = Mathf.SmoothStep(0f, 1f, s.Position);

            var track = (RectTransform)s.Track.transform;
            float travel = track.rect.width - s.Knob.rect.width - 6f;
            s.Knob.anchoredPosition = new Vector2(3f + travel * eased, s.Knob.anchoredPosition.y);
            s.Track.color = Color.Lerp(SwitchOffTrack, Teal, eased);
        }

        void UpdateFeedback(bool online)
        {
            float now = Time.unscaledTime;
            if (!online)
            {
                feedback.text = "Room offline: controls disabled";
                feedback.color = Secondary;
            }
            else if (pending != null)
            {
                feedback.text = "Sending " + pending.What + "...";
                feedback.color = Secondary;
            }
            else if (now - confirmedAt < confirmedShowSeconds)
            {
                feedback.text = "Room confirmed your last change";
                feedback.color = Green;
            }
            else if (now - timedOutAt < 5f)
            {
                feedback.text = "No response from the room";
                feedback.color = Amber;
            }
            else
            {
                feedback.text = "";
            }
        }

        static void Segment(Image bg, TMP_Text label, bool active)
        {
            bg.color = active ? Teal : Color.clear;
            label.color = active ? Dark : new Color(Text.r, Text.g, Text.b, 0.6f);
            label.fontStyle = active ? FontStyles.Bold : FontStyles.Normal;
        }

        static Color Hex(string hex)
        {
            ColorUtility.TryParseHtmlString("#" + hex, out var c);
            return c;
        }
    }
}
