using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace SmartRoom
{
    /// <summary>A small line chart of recent power readings, drawn as a UI mesh with a faded fill underneath.</summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public class Sparkline : MaskableGraphic
    {
        [SerializeField] float windowSeconds = 120f;
        [SerializeField] float thickness = 2f;
        [SerializeField] [Range(0f, 1f)] float fillAlpha = 0.22f;
        [Tooltip("The chart never scales below this, so tiny loads don't look like big swings.")]
        [SerializeField] float minRange = 0.5f;

        IReadOnlyList<PowerSample> samples;
        float now;

        /// <summary>Seconds covered by the chart: the full window, or less while history is still short.</summary>
        public float VisibleSeconds { get; private set; }
        public float WindowSeconds => windowSeconds;

        public void SetData(IReadOnlyList<PowerSample> data, float currentTime)
        {
            samples = data;
            now = currentTime;
            // Until a full window of history exists, stretch what there is across the chart.
            float span = data != null && data.Count > 0 ? now - data[0].Time : 0f;
            VisibleSeconds = Mathf.Clamp(span, 10f, windowSeconds);
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (samples == null || samples.Count < 2)
                return;

            Rect r = GetPixelAdjustedRect();
            float start = now - VisibleSeconds;

            float max = minRange;
            for (int i = 0; i < samples.Count; i++)
            {
                if (samples[i].Time >= start)
                    max = Mathf.Max(max, samples[i].Watts * 1.15f);
            }

            var points = new List<Vector2>(samples.Count);
            for (int i = 0; i < samples.Count; i++)
            {
                if (samples[i].Time < start && (i + 1 >= samples.Count || samples[i + 1].Time < start))
                    continue;
                float x = Mathf.InverseLerp(start, now, samples[i].Time);
                float y = Mathf.Clamp01(samples[i].Watts / max);
                points.Add(new Vector2(r.xMin + x * r.width, r.yMin + y * (r.height - thickness) + thickness * 0.5f));
            }
            if (points.Count < 2)
                return;

            Color line = color;
            Color top = color;
            top.a *= fillAlpha;
            Color bottom = color;
            bottom.a = 0f;

            // Fill: one quad per segment, from the line down to the bottom edge.
            for (int i = 0; i < points.Count - 1; i++)
            {
                Vector2 a = points[i];
                Vector2 b = points[i + 1];
                int v = vh.currentVertCount;
                vh.AddVert(new Vector3(a.x, r.yMin), bottom, Vector4.zero);
                vh.AddVert(new Vector3(a.x, a.y), top, Vector4.zero);
                vh.AddVert(new Vector3(b.x, b.y), top, Vector4.zero);
                vh.AddVert(new Vector3(b.x, r.yMin), bottom, Vector4.zero);
                vh.AddTriangle(v, v + 1, v + 2);
                vh.AddTriangle(v, v + 2, v + 3);
            }

            // Line: a thick quad per segment, plus a square at each joint to hide gaps.
            float h = thickness * 0.5f;
            for (int i = 0; i < points.Count - 1; i++)
            {
                Vector2 a = points[i];
                Vector2 b = points[i + 1];
                Vector2 dir = (b - a).normalized;
                Vector2 n = new Vector2(-dir.y, dir.x) * h;
                AddQuad(vh, a - n, a + n, b + n, b - n, line);
                AddQuad(vh, b + new Vector2(-h, -h), b + new Vector2(-h, h), b + new Vector2(h, h), b + new Vector2(h, -h), line);
            }
        }

        static void AddQuad(VertexHelper vh, Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, Color c)
        {
            int v = vh.currentVertCount;
            vh.AddVert(p0, c, Vector4.zero);
            vh.AddVert(p1, c, Vector4.zero);
            vh.AddVert(p2, c, Vector4.zero);
            vh.AddVert(p3, c, Vector4.zero);
            vh.AddTriangle(v, v + 1, v + 2);
            vh.AddTriangle(v, v + 2, v + 3);
        }
    }
}
