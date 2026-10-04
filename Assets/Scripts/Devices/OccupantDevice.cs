using UnityEngine;

namespace SmartRoom
{
    /// <summary>A see-through "hologram" figure that fades in while the room is occupied.</summary>
    public class OccupantDevice : MonoBehaviour
    {
        [SerializeField] Renderer[] parts;
        [Tooltip("Alpha of the figure when fully shown.")]
        [SerializeField] float maxAlpha = 0.45f;
        [SerializeField] float fadeSeconds = 0.6f;
        [Tooltip("Gentle brightness pulse so the figure reads as a hologram.")]
        [SerializeField] float pulse = 0.08f;

        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

        MaterialPropertyBlock block;
        Color baseColor = Color.white;
        bool visible;
        float shown;   // 0 hidden .. 1 fully shown

        void Awake()
        {
            block = new MaterialPropertyBlock();
            if (parts.Length > 0 && parts[0] != null && parts[0].sharedMaterial.HasProperty(BaseColor))
                baseColor = parts[0].sharedMaterial.GetColor(BaseColor);
            shown = 0f;
            Apply();
        }

        public void SetVisible(bool value) => visible = value;

        void Update()
        {
            float target = visible ? 1f : 0f;
            if (Mathf.Approximately(shown, target) && !visible)
                return;
            float step = fadeSeconds > 0f ? Time.deltaTime / fadeSeconds : 1f;
            shown = Mathf.MoveTowards(shown, target, step);
            Apply();
        }

        void Apply()
        {
            float wobble = 1f + pulse * Mathf.Sin(Time.time * 2.4f);
            var c = baseColor * wobble;
            c.a = maxAlpha * Mathf.SmoothStep(0f, 1f, shown);
            block.SetColor(BaseColor, c);
            foreach (var r in parts)
            {
                if (r == null)
                    continue;
                r.enabled = shown > 0f;
                r.SetPropertyBlock(block);
            }
        }
    }
}
