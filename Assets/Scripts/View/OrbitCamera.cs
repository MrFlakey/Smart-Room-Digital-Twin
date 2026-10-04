using UnityEngine;
using UnityEngine.InputSystem;

namespace SmartRoom
{
    /// <summary>
    /// Looks around a point in the room. Right-drag or one-finger drag to rotate,
    /// scroll or pinch to zoom. The camera is kept inside the room bounds.
    /// </summary>
    public class OrbitCamera : MonoBehaviour
    {
        [SerializeField] Vector3 pivot;
        [SerializeField] float distance = 2f;
        [SerializeField] float minDistance = 0.5f;
        [SerializeField] float maxDistance = 4f;
        [SerializeField] float minPitch = -20f;
        [SerializeField] float maxPitch = 60f;
        [Tooltip("Degrees per pixel dragged.")]
        [SerializeField] float rotateSpeed = 0.25f;
        [SerializeField] float scrollZoomSpeed = 0.4f;
        [SerializeField] float pinchZoomSpeed = 0.005f;
        [Tooltip("The camera position is clamped to this box (world space).")]
        [SerializeField] Bounds roomBounds = new Bounds(Vector3.zero, Vector3.one * 100f);

        float yaw;
        float pitch;
        float lastPinch;

        void Start()
        {
            // Start from wherever the camera was placed in the scene.
            Vector3 offset = transform.position - pivot;
            if (offset.sqrMagnitude > 0.0001f)
            {
                distance = Mathf.Clamp(offset.magnitude, minDistance, maxDistance);
                Vector3 back = -offset.normalized;
                yaw = Mathf.Atan2(back.x, back.z) * Mathf.Rad2Deg;
                pitch = Mathf.Clamp(-Mathf.Asin(back.y) * Mathf.Rad2Deg, minPitch, maxPitch);
            }
            Apply();
        }

        void LateUpdate()
        {
            Vector2 drag = Vector2.zero;
            float zoom = 0f;

            var mouse = Mouse.current;
            if (mouse != null)
            {
                if (mouse.rightButton.isPressed)
                    drag += mouse.delta.ReadValue();
                float scroll = mouse.scroll.ReadValue().y;
                if (scroll != 0f)
                    zoom -= Mathf.Sign(scroll) * scrollZoomSpeed;
            }

            var touch = Touchscreen.current;
            if (touch != null)
            {
                var t0 = touch.touches[0];
                var t1 = touch.touches[1];
                bool first = t0.press.isPressed;
                bool second = t1.press.isPressed;

                if (first && second)
                {
                    float pinch = Vector2.Distance(t0.position.ReadValue(), t1.position.ReadValue());
                    if (lastPinch > 0f)
                        zoom -= (pinch - lastPinch) * pinchZoomSpeed;
                    lastPinch = pinch;
                }
                else
                {
                    lastPinch = 0f;
                    if (first)
                        drag += t0.delta.ReadValue();
                }
            }

            if (drag == Vector2.zero && zoom == 0f)
                return;

            yaw += drag.x * rotateSpeed;
            pitch = Mathf.Clamp(pitch - drag.y * rotateSpeed, minPitch, maxPitch);
            distance = Mathf.Clamp(distance + zoom, minDistance, maxDistance);
            Apply();
        }

        void Apply()
        {
            Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
            Vector3 position = pivot - rotation * Vector3.forward * distance;
            position = roomBounds.ClosestPoint(position);

            transform.position = position;
            Vector3 toPivot = pivot - position;
            if (toPivot.sqrMagnitude > 0.0001f)
                transform.rotation = Quaternion.LookRotation(toPivot, Vector3.up);
        }
    }
}
