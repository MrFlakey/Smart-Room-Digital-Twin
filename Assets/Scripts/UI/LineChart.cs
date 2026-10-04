using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace SmartRoom
{
    /// <summary>
    /// A day chart drawn as a UI mesh: x is minutes since midnight (0-1440), y is the value.
    /// Draws background bands (e.g. occupied periods), faint grid lines, then one or more lines (solid or dashed).
    /// A gap of more than maxGapMinutes between points breaks the line.
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public class LineChart : MaskableGraphic
    {
        public class Series
        {
            public List<Vector2> Points = new List<Vector2>();
            public Color Color = Color.white;
            public float Thickness = 2f;
            public bool Dashed;
        }

        [SerializeField] float maxGapMinutes = 12f;
        [SerializeField] Color gridColor = new Color(1f, 1f, 1f, 0.07f);
        [SerializeField] Color bandColor = new Color(0.494f, 0.886f, 0.659f, 0.10f);
        [SerializeField] int gridLines = 3;

        readonly List<Series> series = new List<Series>();
        readonly List<Vector2> bands = new List<Vector2>();
        float yMin, yMax = 1f;

        public float YMin => yMin;
        public float YMax => yMax;

        public void Clear()
        {
            series.Clear();
            bands.Clear();
            SetVerticesDirty();
        }

        public void SetRange(float min, float max)
        {
            yMin = min;
            yMax = max > min ? max : min + 1f;
            SetVerticesDirty();
        }

        public void AddSeries(Series s)
        {
            series.Add(s);
            SetVerticesDirty();
        }

        /// <summary>A shaded vertical band from startMinutes to endMinutes.</summary>
        public void AddBand(float startMinutes, float endMinutes)
        {
            bands.Add(new Vector2(startMinutes, endMinutes));
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Rect r = GetPixelAdjustedRect();

            foreach (var b in bands)
            {
                float x0 = X(r, b.x), x1 = X(r, b.y);
                Quad(vh, new Vector2(x0, r.yMin), new Vector2(x0, r.yMax), new Vector2(x1, r.yMax), new Vector2(x1, r.yMin), bandColor);
            }

            for (int i = 0; i < gridLines; i++)
            {
                float y = r.yMin + r.height * i / Mathf.Max(1, gridLines - 1);
                Quad(vh, new Vector2(r.xMin, y - 0.5f), new Vector2(r.xMin, y + 0.5f), new Vector2(r.xMax, y + 0.5f), new Vector2(r.xMax, y - 0.5f), gridColor);
            }

            foreach (var s in series)
            {
                for (int i = 0; i + 1 < s.Points.Count; i++)
                {
                    var a = s.Points[i];
                    var b = s.Points[i + 1];
                    if (b.x - a.x > maxGapMinutes)
                        continue;
                    var pa = new Vector2(X(r, a.x), Y(r, a.y));
                    var pb = new Vector2(X(r, b.x), Y(r, b.y));
                    if (s.Dashed)
                        Dashed(vh, pa, pb, s.Thickness, s.Color);
                    else
                        Segment(vh, pa, pb, s.Thickness, s.Color);
                }
                if (s.Points.Count == 1)
                {
                    var p = new Vector2(X(r, s.Points[0].x), Y(r, s.Points[0].y));
                    Segment(vh, p - new Vector2(1.5f, 0f), p + new Vector2(1.5f, 0f), s.Thickness * 1.5f, s.Color);
                }
            }
        }

        float X(Rect r, float minutes) => r.xMin + Mathf.Clamp01(minutes / 1440f) * r.width;

        float Y(Rect r, float value) => r.yMin + Mathf.Clamp01(Mathf.InverseLerp(yMin, yMax, value)) * r.height;

        static void Dashed(VertexHelper vh, Vector2 a, Vector2 b, float thickness, Color c)
        {
            const float dash = 6f, gap = 4f;
            float length = Vector2.Distance(a, b);
            if (length < 0.01f)
                return;
            Vector2 dir = (b - a) / length;
            for (float d = 0f; d < length; d += dash + gap)
                Segment(vh, a + dir * d, a + dir * Mathf.Min(d + dash, length), thickness, c);
        }

        static void Segment(VertexHelper vh, Vector2 a, Vector2 b, float thickness, Color c)
        {
            Vector2 dir = (b - a).normalized;
            if (dir == Vector2.zero)
                dir = Vector2.right;
            Vector2 n = new Vector2(-dir.y, dir.x) * (thickness * 0.5f);
            // Extend each segment slightly so joints don't show gaps.
            Vector2 e = dir * (thickness * 0.5f);
            Quad(vh, a - n - e, a + n - e, b + n + e, b - n + e, c);
        }

        static void Quad(VertexHelper vh, Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, Color c)
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
