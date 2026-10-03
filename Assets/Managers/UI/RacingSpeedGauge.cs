using UnityEngine;
using UnityEngine.UI;

/// <summary>Segmented 250-degree speed arc with a warm high-speed zone.</summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class RacingSpeedGauge : MaskableGraphic
{
    private float speed;
    public void SetSpeed(float value)
    {
        float next = Mathf.Clamp01(value / 180f);
        if (Mathf.Abs(next - speed) < .001f) return;
        speed = next;
        SetVerticesDirty();
    }
    protected override void OnEnable() { base.OnEnable(); raycastTarget = false; }
    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        Rect r = rectTransform.rect;
        float radius = Mathf.Min(r.width, r.height) * .425f;
        Vector2 center = r.center;
        float aa = 1f / Mathf.Max(.1f, canvas != null ? canvas.scaleFactor : 1f);
        const int segments = 50;
        for (int i = 0; i < segments; i++)
        {
            float t0 = (i + .12f) / segments, t1 = (i + .88f) / segments;
            Color tint = t0 < speed ? (t0 > .80f ? RacingHUDStyle.Amber : RacingHUDStyle.Teal)
                : new Color(.39f, .48f, .50f, .38f);
            RacingPanelGraphic.Line(vh, center + Direction(t0) * radius, center + Direction(t1) * radius, 7f, tint, aa);
        }
        for (int i = 0; i <= 10; i++)
        {
            Vector2 d = Direction(i / 10f);
            RacingPanelGraphic.Line(vh, center + d * (radius - 17f), center + d * (radius - 12f), 1.5f,
                new Color(.72f, .79f, .79f, .58f), aa);
        }
    }
    private static Vector2 Direction(float t)
    {
        float angle = Mathf.Lerp(215f, -35f, t) * Mathf.Deg2Rad;
        return new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
    }
}
