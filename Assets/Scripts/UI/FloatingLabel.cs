using UnityEngine;

namespace SmartRoom
{
    /// <summary>A small world-space label that always faces the camera and fades in and out.</summary>
    [RequireComponent(typeof(CanvasGroup))]
    public class FloatingLabel : MonoBehaviour
    {
        [SerializeField] float fadeSeconds = 0.3f;

        CanvasGroup group;
        bool visible;

        void Awake()
        {
            group = GetComponent<CanvasGroup>();
            group.alpha = 0f;
        }

        public void SetVisible(bool value) => visible = value;

        void LateUpdate()
        {
            float step = fadeSeconds > 0f ? Time.deltaTime / fadeSeconds : 1f;
            group.alpha = Mathf.MoveTowards(group.alpha, visible ? 1f : 0f, step);

            var cam = Camera.main;
            if (cam != null && group.alpha > 0f)
                transform.rotation = Quaternion.LookRotation(transform.position - cam.transform.position, cam.transform.up);
        }
    }
}
