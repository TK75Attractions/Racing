using UnityEngine;
using UnityEngine.UI;

/// <summary>A paint sample with a metallic highlight and an explicit neon selection ring.</summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class CarPaintSwatchGraphic : MaskableGraphic
{
    private bool selected;
    public void SetSelected(bool value) { if (selected == value) return; selected = value; SetVerticesDirty(); }
    protected override void OnEnable() { base.OnEnable(); raycastTarget = false; }
    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear(); Vector2 center = rectTransform.rect.center;
        float radius = Mathf.Min(rectTransform.rect.width, rectTransform.rect.height) * .32f;
        float aa = 1f / Mathf.Max(.1f, canvas != null ? canvas.scaleFactor : 1f);
        const int segments = 64;
        vh.AddVert(center, color, Vector2.zero);
        for (int i = 0; i <= segments; i++)
        {
            float angle = i * Mathf.PI * 2 / segments;
            Vector2 point = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            Color shade = point.y > 0 ? Color.Lerp(color, Color.white, point.y * .42f) : Color.Lerp(color, Color.black, -point.y * .50f);
            vh.AddVert(center + point * radius, shade, Vector2.zero);
        }
        for (int i = 0; i < segments; i++) vh.AddTriangle(0, i + 1, i + 2);
        Ring(vh, center, radius, 1.2f, new Color(.78f, .84f, .95f), aa);
        if (!selected) return;
        for (int glow = 5; glow > 0; glow--)
            Ring(vh, center, radius + 4f, glow * 2f, RacingPanelGraphic.Alpha(NeonUI.Pink, .045f), aa);
        Ring(vh, center, radius + 4f, 2f, NeonUI.Pink, aa);
    }
    private static void Ring(VertexHelper vh, Vector2 center, float radius, float width, Color tint, float aa)
    {
        for (int i = 0; i < 64; i++)
        {
            float a = i * Mathf.PI / 32, b = (i + 1) * Mathf.PI / 32;
            RacingPanelGraphic.Line(vh, center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius,
                center + new Vector2(Mathf.Cos(b), Mathf.Sin(b)) * radius, width, tint, aa);
        }
    }
}
