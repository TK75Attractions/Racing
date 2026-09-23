using UnityEngine;
using UnityEngine.UI;

/// <summary>Resolution-independent surfaces with a one-screen-pixel feathered edge.</summary>
[RequireComponent(typeof(CanvasRenderer))]
public class RacingPanelGraphic : MaskableGraphic
{
    public enum SurfaceStyle { Panel, Primary, Secondary }
    private const int CornerSteps = 12;
    private const int PointCount = CornerSteps * 4;
    private readonly Vector2[] outer = new Vector2[PointCount];
    private readonly Vector2[] inner = new Vector2[PointCount];
    private SurfaceStyle style;
    private Color accent = RacingUITheme.Cyan;
    private float selection, pressure, confirmation;

    public void Configure(SurfaceStyle value, Color tint)
    {
        style = value;
        accent = tint;
        raycastTarget = false;
        SetVerticesDirty();
    }

    public void SetState(float focus, float pedal, float flash, Color tint)
    {
        if (Mathf.Approximately(selection, focus) && Mathf.Approximately(pressure, pedal)
            && Mathf.Approximately(confirmation, flash) && accent == tint) return;
        selection = Mathf.Clamp01(focus);
        pressure = Mathf.Clamp01(pedal);
        confirmation = Mathf.Clamp01(flash);
        accent = tint;
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        Rect bounds = rectTransform.rect;
        if (bounds.width < 2f || bounds.height < 2f) return;
        float aa = 1f / Mathf.Max(0.1f, canvas != null ? canvas.scaleFactor : 1f);
        float glow = Mathf.Max(selection * 0.6f, pressure, confirmation);
        Color light = Color.Lerp(accent, RacingUITheme.Gold, Mathf.Clamp01(pressure * 3f));
        if (style != SurfaceStyle.Panel && glow > 0.001f)
            for (int i = 6; i > 0; i--)
                Ring(vh, bounds, -i * 2f, -(i - 1) * 2f,
                    Alpha(light, glow * (6 - i) * 0.012f), Alpha(light, glow * (7 - i) * 0.012f));

        Color bottom = style == SurfaceStyle.Panel ? new Color(0.025f, 0.043f, 0.065f, 0.94f) : new Color(0.037f, 0.065f, 0.093f, 0.99f);
        Color top = style == SurfaceStyle.Panel ? new Color(0.065f, 0.099f, 0.136f, 0.95f) : new Color(0.105f, 0.165f, 0.22f, 1f);
        top = Color.Lerp(top, new Color(0.08f, 0.26f, 0.37f), selection * 0.55f);
        float ready = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.65f, 1f, pressure));
        top = Color.Lerp(top, new Color(0.58f, 0.43f, 0.055f), ready * 0.85f);
        top = Color.Lerp(top, RacingUITheme.Gold, confirmation * 0.65f);
        bottom = Color.Lerp(bottom, new Color(0.20f, 0.15f, 0.025f), ready);
        Fill(vh, bounds, bottom, top);

        Color border = Color.Lerp(new Color(0.31f, 0.42f, 0.51f, style == SurfaceStyle.Panel ? 0.48f : 0.8f), light, glow);
        float thickness = style == SurfaceStyle.Panel ? 1.2f : 2f;
        Ring(vh, bounds, -aa, 0f, Alpha(border, 0f), border);
        Ring(vh, bounds, 0f, thickness, border, border);
        Ring(vh, bounds, thickness, thickness + aa, border, Alpha(border, 0f));
        if (style == SurfaceStyle.Primary) DrawChecks(vh, bounds, light);
        if (style != SurfaceStyle.Panel)
        {
            float left = bounds.xMin + 26f, right = bounds.xMax - 26f, y = bounds.yMin + 10f;
            Line(vh, new Vector2(left, y), new Vector2(right, y), 3f, new Color(0.4f, 0.6f, 0.7f, 0.13f), aa);
            if (pressure > 0.001f)
                Line(vh, new Vector2(left, y), new Vector2(Mathf.Lerp(left, right, pressure), y), 4f, RacingUITheme.Gold, aa);
        }
    }

    private void Shape(Rect r, float inset, Vector2[] points)
    {
        float skew = style == SurfaceStyle.Primary ? Mathf.Min(22f, r.height * 0.23f) : 0f;
        float left = r.xMin + skew * 0.5f + inset, right = r.xMax - skew * 0.5f - inset;
        float bottom = r.yMin + inset, top = r.yMax - inset;
        float radius = Mathf.Clamp(10f - inset, 0f, Mathf.Max(0f, Mathf.Min(right - left, top - bottom) * 0.5f));
        for (int corner = 0; corner < 4; corner++)
        {
            Vector2 center = new Vector2(corner == 0 || corner == 3 ? right - radius : left + radius,
                corner < 2 ? top - radius : bottom + radius);
            for (int step = 0; step < CornerSteps; step++)
            {
                float angle = (corner * 90f + step * 90f / (CornerSteps - 1)) * Mathf.Deg2Rad;
                Vector2 p = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
                p.x += (p.y - r.center.y) / Mathf.Max(1f, r.height) * skew;
                points[corner * CornerSteps + step] = p;
            }
        }
    }

    private void Fill(VertexHelper vh, Rect r, Color bottom, Color top)
    {
        Shape(r, 0f, outer);
        int start = vh.currentVertCount;
        for (int i = 0; i < PointCount; i++)
            vh.AddVert(outer[i], Color.Lerp(bottom, top, Mathf.InverseLerp(r.yMin, r.yMax, outer[i].y)) * color, Vector2.zero);
        for (int i = 1; i < PointCount - 1; i++) vh.AddTriangle(start, start + i, start + i + 1);
    }

    private void Ring(VertexHelper vh, Rect r, float outside, float inside, Color outerColor, Color innerColor)
    {
        Shape(r, outside, outer);
        Shape(r, inside, inner);
        int start = vh.currentVertCount;
        for (int i = 0; i < PointCount; i++)
        {
            vh.AddVert(outer[i], outerColor * color, Vector2.zero);
            vh.AddVert(inner[i], innerColor * color, Vector2.zero);
        }
        for (int i = 0; i < PointCount; i++)
        {
            int a = start + i * 2, b = start + (i + 1) % PointCount * 2;
            vh.AddTriangle(a, b, a + 1);
            vh.AddTriangle(a + 1, b, b + 1);
        }
    }

    private void DrawChecks(VertexHelper vh, Rect r, Color tint)
    {
        float size = Mathf.Min(17f, (r.height - 16f) / 5f), skew = Mathf.Min(22f, r.height * 0.23f);
        for (int row = 0; row < 5; row++)
        for (int col = 0; col < 2; col++)
        {
            float y = r.yMin + 8f + row * size;
            float x = r.xMin + 14f + col * size + (y - r.yMin) / r.height * skew;
            int start = vh.currentVertCount;
            Color tile = Alpha(tint, (row + col) % 2 == 0 ? 0.32f : 0.07f) * color;
            float shift = size / r.height * skew;
            vh.AddVert(new Vector2(x, y), tile, Vector2.zero);
            vh.AddVert(new Vector2(x + size, y), tile, Vector2.zero);
            vh.AddVert(new Vector2(x + size + shift, y + size), tile, Vector2.zero);
            vh.AddVert(new Vector2(x + shift, y + size), tile, Vector2.zero);
            vh.AddTriangle(start, start + 1, start + 2);
            vh.AddTriangle(start, start + 2, start + 3);
        }
    }

    public static Color Alpha(Color c, float a) { c.a = a; return c; }

    public static void Line(VertexHelper vh, Vector2 a, Vector2 b, float width, Color tint, float feather = 1f)
    {
        Vector2 normal = new Vector2(-(b - a).y, (b - a).x).normalized;
        int start = vh.currentVertCount;
        for (int i = 0; i < 4; i++)
        {
            float offset = i == 0 ? -width * 0.5f - feather : i == 1 ? -width * 0.5f : i == 2 ? width * 0.5f : width * 0.5f + feather;
            Color c = i == 0 || i == 3 ? Alpha(tint, 0f) : tint;
            vh.AddVert(a + normal * offset, c, Vector2.zero);
            vh.AddVert(b + normal * offset, c, Vector2.zero);
        }
        for (int i = 0; i < 3; i++)
        {
            int n = start + i * 2;
            vh.AddTriangle(n, n + 1, n + 2);
            vh.AddTriangle(n + 2, n + 1, n + 3);
        }
    }
}
