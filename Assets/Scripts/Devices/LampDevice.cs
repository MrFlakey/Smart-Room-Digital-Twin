using UnityEngine;

namespace SmartRoom
{
    /// <summary>A room lamp driven by a 0-100 level: scales its Light and makes the lamp mesh glow.</summary>
    public class LampDevice : MonoBehaviour
    {
        [SerializeField] Light lampLight;
        [SerializeField] Renderer lampRenderer;
        [Tooltip("Which of the lamp renderer's materials glow.")]
        [SerializeField] int[] emissiveMaterials = { 0 };
        [SerializeField] Color color = new Color(1f, 0.84f, 0.62f);
        [SerializeField] float maxIntensity = 30f;
        [SerializeField] float maxEmission = 6f;
        [Tooltip("Level change per second, in percent.")]
        [SerializeField] float fadeSpeed = 150f;

        static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");

        Material[] glowMaterials;
        float target;
        float current = -1f;

        /// <summary>Current target level, 0-100.</summary>
        public float Level => target;

        void Awake()
        {
            if (lampRenderer != null)
            {
                // .materials gives this lamp its own copies, so the shared FBX materials stay untouched.
                var all = lampRenderer.materials;
                glowMaterials = new Material[emissiveMaterials.Length];
                for (int i = 0; i < emissiveMaterials.Length; i++)
                {
                    int index = emissiveMaterials[i];
                    if (index < 0 || index >= all.Length)
                        continue;
                    glowMaterials[i] = all[index];
                    glowMaterials[i].EnableKeyword("_EMISSION");
                    glowMaterials[i].globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                }
            }
            Apply(0f);
        }

        public void SetLevel(float percent)
        {
            target = Mathf.Clamp(percent, 0f, 100f);
        }

        void Update()
        {
            if (Mathf.Approximately(current, target))
                return;
            Apply(Mathf.MoveTowards(current, target, fadeSpeed * Time.deltaTime));
        }

        void Apply(float level)
        {
            current = level;
            float k = level / 100f;

            if (lampLight != null)
            {
                lampLight.color = color;
                lampLight.intensity = maxIntensity * k;
                lampLight.enabled = level > 0f;
            }

            if (glowMaterials == null)
                return;
            Color emission = color * (maxEmission * k);
            foreach (var m in glowMaterials)
            {
                if (m != null)
                    m.SetColor(EmissionColor, emission);
            }
        }

        void OnDestroy()
        {
            if (glowMaterials == null)
                return;
            foreach (var m in glowMaterials)
            {
                if (m != null)
                    Destroy(m);
            }
        }
    }
}
