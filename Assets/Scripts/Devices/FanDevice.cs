using UnityEngine;

namespace SmartRoom
{
    /// <summary>A fan driven by a 0-100 level: spins the hub at a speed proportional to it.</summary>
    public class FanDevice : MonoBehaviour
    {
        [Tooltip("The part that spins. Kept separate so a real fan model can replace the placeholder.")]
        [SerializeField] Transform hub;
        [SerializeField] Vector3 spinAxis = Vector3.forward;
        [SerializeField] float maxRpm = 600f;
        [Tooltip("How fast the fan speeds up or slows down, in rpm per second.")]
        [SerializeField] float acceleration = 400f;

        float targetRpm;
        float rpm;

        /// <summary>Current target level, 0-100.</summary>
        public float Level { get; private set; }

        public void SetLevel(float percent)
        {
            Level = Mathf.Clamp(percent, 0f, 100f);
            targetRpm = maxRpm * Level / 100f;
        }

        void Update()
        {
            rpm = Mathf.MoveTowards(rpm, targetRpm, acceleration * Time.deltaTime);
            if (hub != null && rpm > 0f)
                hub.Rotate(spinAxis, rpm * 6f * Time.deltaTime, Space.Self);
        }
    }
}
