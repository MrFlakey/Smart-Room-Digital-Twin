using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace SmartRoom
{
    /// <summary>
    /// Washes the room warm when it's hotter than the target and cool when it's colder,
    /// using the colour filter of a global post-processing Volume. The UI overlay isn't affected.
    /// </summary>
    public class RoomTint : MonoBehaviour
    {
        [SerializeField] Volume volume;
        [SerializeField] Color warm = new Color(1f, 0.549f, 0.235f);   // #FF8C3C
        [SerializeField] Color cool = new Color(0.314f, 0.627f, 1f);   // #50A0FF
        [Tooltip("Degrees above or below the target for the full tint.")]
        [SerializeField] float fullTintDegrees = 3f;
        [Tooltip("How far the colour filter moves towards warm/cool at full tint (0-1). Keep it subtle.")]
        [SerializeField] [Range(0f, 1f)] float maxStrength = 0.35f;
        [SerializeField] float transitionSeconds = 1f;

        ColorAdjustments adjustments;
        float target;    // -1 full cool .. 0 none .. 1 full warm
        float current;

        void Awake()
        {
            // volume.profile gives this Volume its own copy, so the profile asset isn't changed at runtime.
            if (volume != null && !volume.profile.TryGet(out adjustments))
                adjustments = volume.profile.Add<ColorAdjustments>(true);
            if (adjustments != null)
            {
                adjustments.colorFilter.overrideState = true;
                adjustments.colorFilter.value = Color.white;
            }
        }

        /// <summary>Degrees above (+) or below (-) the target; null for no tint.</summary>
        public void SetDelta(float? degrees)
        {
            target = degrees.HasValue ? Mathf.Clamp(degrees.Value / fullTintDegrees, -1f, 1f) : 0f;
        }

        void Update()
        {
            if (adjustments == null)
                return;
            float speed = transitionSeconds > 0f ? Time.deltaTime / transitionSeconds : 1f;
            current = Mathf.MoveTowards(current, target, speed);
            Color tint = current >= 0f ? warm : cool;
            adjustments.colorFilter.value = Color.Lerp(Color.white, tint, Mathf.Abs(current) * maxStrength);
        }
    }
}
