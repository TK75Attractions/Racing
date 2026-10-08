using UnityEngine;
using UnityEngine.UI;

/// <summary>Reference semicircle: cyan-to-pink arc, radial scale, luminous needle.</summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class DrivingTutorialGaugeGraphic : MaskableGraphic
{
    private float angle;
    public float Angle { set { float next = Mathf.Clamp(value, -90f, 90f); if (Mathf.Approximately(angle, next)) return; angle = next; SetVerticesDirty(); } }
    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        Rect r = rectTransform.rect;
        float radius = r.width * .49f;
        Vector2 center = new Vector2(r.center.x, r.yMin - radius * .27f);
        Color cyan = DrivingTutorialUI.Cyan, pink = DrivingTutorialUI.Pink;
        for (int i = 0; i < 80; i++)
        {
            float a = Mathf.Lerp(140f, 40f, i / 80f) * Mathf.Deg2Rad;
            float b = Mathf.Lerp(140f, 40f, (i + 1) / 80f) * Mathf.Deg2Rad;
            Color tint = Color.Lerp(cyan, pink, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.4f, 1f, i / 80f)));
            Quad(vh, center + D(a) * radius, center + D(b) * radius,
                center + D(b) * radius * .83f, center + D(a) * radius * .83f, tint);
            Color glow = tint; glow.a = .045f;
            for (int j = 1; j <= 4; j++)
                Quad(vh, center + D(a) * (radius + j * 3f), center + D(b) * (radius + j * 3f),
                    center + D(b) * (radius + (j - 1) * 3f), center + D(a) * (radius + (j - 1) * 3f), glow);
        }
        for (int i = 0; i <= 16; i++)
        {
            float a = Mathf.Lerp(142f, 38f, i / 16f) * Mathf.Deg2Rad;
            float outer = radius * .74f, inner = radius * (i % 4 == 0 ? .63f : .66f);
            Color tint = i == 8 ? cyan : new Color(.09f, .36f, .53f, .9f);
            Line(vh, center + D(a) * inner, center + D(a) * outer, i == 8 ? 3f : 2.5f, tint);
        }
        float needleAngle = (90f - angle / 90f * 50f) * Mathf.Deg2Rad;
        Vector2 start = center + D(needleAngle) * radius * .59f, end = center + D(needleAngle) * radius * 1.00f;
        for (int i = 6; i >= 1; i--) { Color glow = cyan; glow.a = .04f; Line(vh, start, end, 4f + i * 2f, glow); }
        Line(vh, start, end, 3.5f, Color.white);
    }
    private static Vector2 D(float angle) => new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
    private static void Line(VertexHelper vh, Vector2 a, Vector2 b, float width, Color color)
    {
        Vector2 side = new Vector2(-(b - a).y, (b - a).x).normalized * width * .5f;
        Quad(vh, a - side, a + side, b + side, b - side, color);
    }
    private static void Quad(VertexHelper vh, Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color tint)
    {
        int index = vh.currentVertCount;
        vh.AddVert(a, tint, Vector2.zero); vh.AddVert(b, tint, Vector2.zero); vh.AddVert(c, tint, Vector2.zero); vh.AddVert(d, tint, Vector2.zero);
        vh.AddTriangle(index, index + 1, index + 2); vh.AddTriangle(index, index + 2, index + 3);
    }
}
