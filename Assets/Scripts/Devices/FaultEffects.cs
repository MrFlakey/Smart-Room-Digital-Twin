using System;
using System.Collections.Generic;
using UnityEngine;

namespace SmartRoom
{
    /// <summary>
    /// Shows sensor faults in the 3D room: the affected device glows red (pulsing) and a red label floats above it.
    /// Everything clears when the fault clears or the room is no longer live.
    /// </summary>
    public class FaultEffects : MonoBehaviour
    {
        [Serializable]
        public class Target
        {
            [Tooltip("Sensor key from .../faults, e.g. lm35.")]
            public string sensor;
            [Tooltip("Renderers that glow red while the fault is active (may be empty).")]
            public Renderer[] renderers;
            public FloatingLabel label;
            [Tooltip("\"fan\" or \"lights\": while that device is overridden, the label shows overriddenText instead.")]
            public string device;
            public string overriddenText;
            [NonSerialized] public TMPro.TMP_Text text;
            [NonSerialized] public string normalText;
        }

        [SerializeField] RoomModel model;
        [SerializeField] Target[] targets;
        [SerializeField] Color glow = new Color(1f, 0.365f, 0.365f);   // #FF5D5D
        [SerializeField] float glowIntensity = 2f;
        [SerializeField] float pulseSpeed = 2.5f;

        static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");

        /// <summary>What a material looked like before it started glowing, to put it back afterwards.</summary>
        struct Saved
        {
            public bool Keyword;
            public Color Emission;
        }

        readonly Dictionary<Material, Saved> saved = new Dictionary<Material, Saved>();
        readonly HashSet<Renderer> glowing = new HashSet<Renderer>();
        readonly HashSet<Renderer> wanted = new HashSet<Renderer>();
        readonly Dictionary<Renderer, Material[]> materials = new Dictionary<Renderer, Material[]>();

        void Awake()
        {
            if (targets == null)
                return;
            foreach (var t in targets)
            {
                if (t.label == null)
                    continue;
                t.text = t.label.GetComponentInChildren<TMPro.TMP_Text>(true);
                if (t.text != null)
                    t.normalText = t.text.text;
            }
        }

        bool Overridden(string device) =>
            (device == "fan" && model.AcOverride == true) || (device == "lights" && model.LightsOverride == true);

        void Update()
        {
            if (model == null || targets == null)
                return;

            wanted.Clear();
            foreach (var t in targets)
            {
                bool on = model.Live && model.Faults.BySensor.ContainsKey(t.sensor);
                if (t.label != null)
                    t.label.SetVisible(on);
                if (on && t.text != null && !string.IsNullOrEmpty(t.overriddenText))
                {
                    string wantText = Overridden(t.device) ? t.overriddenText : t.normalText;
                    if (t.text.text != wantText)
                    {
                        t.text.text = wantText;
                        var rt = (RectTransform)t.label.transform;
                        rt.sizeDelta = new Vector2(Mathf.Ceil(t.text.GetPreferredValues(wantText).x) + 44f, rt.sizeDelta.y);
                    }
                }
                if (on && t.renderers != null)
                {
                    foreach (var r in t.renderers)
                    {
                        if (r != null)
                            wanted.Add(r);
                    }
                }
            }

            float k = 0.55f + 0.45f * Mathf.Sin(Time.time * pulseSpeed);
            Color c = glow * (glowIntensity * k);
            foreach (var r in wanted)
            {
                foreach (var m in Materials(r))
                {
                    if (!saved.ContainsKey(m))
                    {
                        saved[m] = new Saved
                        {
                            Keyword = m.IsKeywordEnabled("_EMISSION"),
                            Emission = m.HasProperty(EmissionColor) ? m.GetColor(EmissionColor) : Color.black,
                        };
                        m.EnableKeyword("_EMISSION");
                    }
                    m.SetColor(EmissionColor, c);
                }
                glowing.Add(r);
            }

            // Put back the renderers that no longer have a fault.
            if (glowing.Count > wanted.Count)
            {
                var done = new List<Renderer>();
                foreach (var r in glowing)
                {
                    if (wanted.Contains(r))
                        continue;
                    if (r != null)
                        Restore(r);
                    done.Add(r);
                }
                foreach (var r in done)
                    glowing.Remove(r);
            }
        }

        /// <summary>The renderer's own material instances (so shared FBX materials are never changed), cached.</summary>
        Material[] Materials(Renderer r)
        {
            if (!materials.TryGetValue(r, out var list))
            {
                list = r.materials;
                materials[r] = list;
            }
            return list;
        }

        void Restore(Renderer r)
        {
            foreach (var m in Materials(r))
            {
                if (!saved.TryGetValue(m, out var s))
                    continue;
                m.SetColor(EmissionColor, s.Emission);
                if (!s.Keyword)
                    m.DisableKeyword("_EMISSION");
                saved.Remove(m);
            }
        }
    }
}
