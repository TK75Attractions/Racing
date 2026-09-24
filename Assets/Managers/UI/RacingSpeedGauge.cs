using UnityEngine;
using UnityEngine.UI;

/// <summary>Antialiased speed arc; no raster dial, needle sprite or per-frame allocations.</summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class RacingSpeedGauge : MaskableGraphic
{
    private float speed;
    public void SetSpeed(float value)
    {
        float next = Mathf.Clamp01(value / 180f);
        if (Mathf.Abs(next - speed) < 0.001f) return;
        speed = next;
        SetVerticesDirty();
    }
    protected override void OnEnable() { base.OnEnable(); raycastTarget = false; }
    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        Rect r = rectTransform.rect;
        float radius = Mathf.Min(r.width * 0.45f, r.height * 0.78f);
        Vector2 center = new Vector2(r.center.x, r.yMin + r.height * 0.22f);
        float aa = 1f / Mathf.Max(0.1f, canvas != null ? canvas.scaleFactor : 1f);
        for (int i = 0; i < 41; i++)
        {
            float t = i / 40f;
            Vector2 d = Direction(t);
            bool filled = speed > 0f && t <= speed;
            Color tint = filled ? (t > 0.8f ? RacingUITheme.Gold : RacingUITheme.Cyan) : new Color(0.28f, 0.39f, 0.48f, 0.75f);
            RacingPanelGraphic.Line(vh, center + d * (radius - (i % 5 == 0 ? 15f : 8f)), center + d * radius, i % 5 == 0 ? 3f : 2f, tint, aa);
        }
        for (int i = 0; i < 96; i++)
        {
            float t = i / 96f;
            Color tint = t < speed ? RacingUITheme.Cyan : new Color(0.22f, 0.34f, 0.43f, 0.4f);
            RacingPanelGraphic.Line(vh, center + Direction(t) * (radius + 7f), center + Direction((i + 1f) / 96f) * (radius + 7f), 2.5f, tint, aa);
        }
    }
    private static Vector2 Direction(float t)
    {
        float angle = Mathf.Lerp(195f, -15f, t) * Mathf.Deg2Rad;
        return new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
    }
}
