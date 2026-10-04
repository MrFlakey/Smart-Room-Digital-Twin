using UnityEngine;

namespace SmartRoom
{
    /// <summary>Makes the room's devices follow the latest data in the RoomModel.</summary>
    public class RoomVisuals : MonoBehaviour
    {
        [SerializeField] RoomModel model;
        [Tooltip("All lamps follow the single \"light\" level.")]
        [SerializeField] LampDevice[] lamps;
        [SerializeField] FanDevice fan;
        [SerializeField] DoorDevice door;
        [SerializeField] OccupantDevice occupant;
        [SerializeField] RoomTint tint;
        [Tooltip("\"AC paused: door open\" label above the fan.")]
        [SerializeField] FloatingLabel acPausedLabel;

        void Awake()
        {
            if (model == null)
                model = GetComponent<RoomModel>();
        }

        void OnEnable()
        {
            if (model != null)
                model.Changed += Refresh;
        }

        void OnDisable()
        {
            if (model != null)
                model.Changed -= Refresh;
        }

        void Refresh()
        {
            // Until the board is live, show the room switched off rather than old values.
            if (!model.Live)
            {
                SetLamps(0);
                if (fan != null)
                    fan.SetLevel(0);
                if (door != null)
                    door.SetOpen(false);
                if (occupant != null)
                    occupant.SetVisible(false);
                if (tint != null)
                    tint.SetDelta(null);
                if (acPausedLabel != null)
                    acPausedLabel.SetVisible(false);
                return;
            }

            int? light = model.LightLevel;
            if (light.HasValue)
                SetLamps(light.Value);

            int? fanLevel = model.FanLevel;
            if (fanLevel.HasValue && fan != null)
                fan.SetLevel(fanLevel.Value);

            bool? doorOpen = model.DoorOpen;
            if (doorOpen.HasValue && door != null)
                door.SetOpen(doorOpen.Value);

            if (occupant != null)
                occupant.SetVisible(model.Occupied == true);

            // Warm when hotter than the target, cool when colder; nothing if either is unknown.
            float? temp = model.Telemetry?.TempC;
            float? target = model.SetpointC;
            if (tint != null)
                tint.SetDelta(temp.HasValue && target.HasValue ? temp.Value - target.Value : (float?)null);

            if (acPausedLabel != null)
                acPausedLabel.SetVisible(model.AcPaused == true);
        }

        void SetLamps(float level)
        {
            if (lamps == null)
                return;
            foreach (var lamp in lamps)
            {
                if (lamp != null)
                    lamp.SetLevel(level);
            }
        }
    }
}
