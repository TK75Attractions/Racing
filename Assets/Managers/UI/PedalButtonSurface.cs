using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>角丸の斜めボタン、金色の縁光、踏み込みバーをコードで描画します。</summary>
public sealed class PedalButtonSurface : MaskableGraphic
{
    private float pedal;
    private float flash;
    private bool selected;
    public void SetVisual(float amount, bool active, float confirmation)
    {
        pedal = Mathf.Clamp01(amount);
        selected = active;
        flash = Mathf.Clamp01(confirmation);
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        Rect bounds = rectTransform.rect;
        float glow = Mathf.Max(pedal, flash);
        Color gold = new Color(1f, 0.8f, 0.12f, 1f);
        Color border = glow > 0.01f ? gold : new Color(0.4f, 0.51f, 0.61f, selected ? 0.95f : 0.7f);
        // 同心の半透明リングで、UI用Bloomがなくても縁を柔らかく発光させる。
        for (int layer = 5; layer >= 1; layer--)
        {
            float width = layer * 2.4f;
            Color halo = gold;
            halo.a = glow * (6 - layer) * 0.025f;
            Ring(vh, Shape(bounds, -width), Shape(bounds, -width + 2.5f), halo);
        }
        Color body = Color.Lerp(new Color(0.065f, 0.105f, 0.15f, 0.98f), new Color(0.56f, 0.39f, 0.025f, 0.98f), pedal >= 0.8f ? 0.8f : pedal * 0.25f);
        body = Color.Lerp(body, new Color(1f, 0.89f, 0.38f, 1f), flash * 0.85f);
        Polygon(vh, Shape(bounds, 0f), body);
        Ring(vh, Shape(bounds, 0f), Shape(bounds, 2.8f), border);
        // 控えめな上辺のハイライト。
        Rect shine = new Rect(bounds.xMin + 18f, bounds.yMax - 5f, bounds.width - 36f, 1.5f);
        Polygon(vh, Shape(shine, 0f), new Color(1f, 1f, 1f, 0.1f + flash * 0.5f));
        if (pedal > 0f)
        {
            Rect bar = new Rect(bounds.xMin + 14f, bounds.yMin + 9f, bounds.width - 28f, 7f);
            List<Vector2> points = Shape(bar, 0f);
            points = ClipRight(points, bar.xMin + bar.width * pedal);
            Polygon(vh, points, new Color(1f, 0.88f, 0.2f, 1f));
        }
    }

    private static List<Vector2> Shape(Rect r, float inset)
    {
        float left = r.xMin + inset + 12f;
        float right = r.xMax - inset - 12f;
        float bottom = r.yMin + inset;
        float top = r.yMax - inset;
        float radius = Mathf.Min(10f, Mathf.Max(0f, (top - bottom) * 0.35f));
        var result = new List<Vector2>(32);
        Vector2[] centers = {
            new Vector2(right - radius, top - radius), new Vector2(left + radius, top - radius),
            new Vector2(left + radius, bottom + radius), new Vector2(right - radius, bottom + radius)
        };
        for (int corner = 0; corner < 4; corner++)
        for (int step = 0; step < 8; step++)
        {
            float angle = (corner * 90f + step * 90f / 7f) * Mathf.Deg2Rad;
            Vector2 point = centers[corner] + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
            point.x += (point.y - r.center.y) / Mathf.Max(1f, r.height) * 22f;
            result.Add(point);
        }
        return result;
    }

    private static void Polygon(VertexHelper vh, List<Vector2> points, Color color)
    {
        if (points.Count < 3) return;
        int start = vh.currentVertCount;
        foreach (Vector2 p in points) vh.AddVert(new Vector3(p.x, p.y), color, Vector2.zero);
        for (int i = 1; i < points.Count - 1; i++) vh.AddTriangle(start, start + i, start + i + 1);
    }

    private static void Ring(VertexHelper vh, List<Vector2> outer, List<Vector2> inner, Color color)
    {
        int start = vh.currentVertCount;
        for (int i = 0; i < outer.Count; i++)
        {
            vh.AddVert(new Vector3(outer[i].x, outer[i].y), color, Vector2.zero);
            vh.AddVert(new Vector3(inner[i].x, inner[i].y), color, Vector2.zero);
        }
        for (int i = 0; i < outer.Count; i++)
        {
            int a = start + i * 2;
            int b = start + ((i + 1) % outer.Count) * 2;
            vh.AddTriangle(a, b, a + 1);
            vh.AddTriangle(a + 1, b, b + 1);
        }
    }

    private static List<Vector2> ClipRight(List<Vector2> input, float x)
    {
        var output = new List<Vector2>();
        for (int i = 0; i < input.Count; i++)
        {
            Vector2 a = input[i], b = input[(i + 1) % input.Count];
            bool insideA = a.x <= x, insideB = b.x <= x;
            if (insideA) output.Add(a);
            if (insideA != insideB) output.Add(Vector2.Lerp(a, b, (x - a.x) / (b.x - a.x)));
        }
        return output;
    }
}
