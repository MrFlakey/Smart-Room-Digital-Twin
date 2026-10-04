using UnityEngine;

namespace SmartRoom
{
    /// <summary>
    /// Swings a door around this object's position and up axis. Put this on an empty object
    /// placed at the hinge edge; the door itself stays where it is in the model hierarchy.
    /// </summary>
    public class DoorDevice : MonoBehaviour
    {
        [SerializeField] Transform door;
        [Tooltip("Degrees around the hinge's up axis when fully open. Negative swings the other way.")]
        [SerializeField] float openAngle = 90f;
        [Tooltip("Seconds for a full swing.")]
        [SerializeField] float duration = 0.8f;
        [SerializeField] bool open;

        Vector3 closedPosition;
        Quaternion closedRotation;
        float progress;

        public bool IsOpen => open;
        /// <summary>0 = closed, 1 = fully open.</summary>
        public float Progress => progress;

        void Awake()
        {
            if (door == null)
                return;
            closedPosition = door.position;
            closedRotation = door.rotation;
            progress = open ? 1f : 0f;
            Apply();
        }

        public void SetOpen(bool value)
        {
            open = value;
        }

        void Update()
        {
            if (door == null)
                return;
            float target = open ? 1f : 0f;
            if (Mathf.Approximately(progress, target))
                return;
            float step = duration > 0f ? Time.deltaTime / duration : 1f;
            progress = Mathf.MoveTowards(progress, target, step);
            Apply();
        }

        void Apply()
        {
            float angle = openAngle * Mathf.SmoothStep(0f, 1f, progress);
            Quaternion swing = Quaternion.AngleAxis(angle, transform.up);
            door.SetPositionAndRotation(
                transform.position + swing * (closedPosition - transform.position),
                swing * closedRotation);
        }
    }
}
