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
        float radius = Mathf.Min(r.width * .47f, r.height * .92f);
        Vector2 center = new Vector2(r.center.x, r.yMin + r.height * .035f);
        float aa = 1f / Mathf.Max(.1f, canvas != null ? canvas.scaleFactor : 1f);
        const int segments = 50;
        for (int i = 0; i < segments; i++)
        {
            float t0 = (i + .12f) / segments, t1 = (i + .88f) / segments;
            Color tint = t0 > .60f ? NeonUI.Pink : NeonUI.Cyan;
            tint.a = t0 < speed ? 1f : .46f;
            RacingPanelGraphic.Line(vh, center + Direction(t0) * radius, center + Direction(t1) * radius, 10f, tint, aa);
        }
        for (int i = 0; i < 90; i++)
        {
            float a=i/90f,b=(i+1)/90f;
            RacingPanelGraphic.Line(vh,center+Direction(a)*(radius+11f),center+Direction(b)*(radius+11f),2f,a>.60f?NeonUI.Pink:NeonUI.Cyan,aa);
        }
        for (int i = 0; i <= 20; i++)
        {
            Vector2 d = Direction(i / 20f);
            RacingPanelGraphic.Line(vh, center + d * (radius - (i%2==0?22f:15f)), center + d * (radius - 12f), 1.5f,
                new Color(.89f, .93f, 1f, .9f), aa);
        }
    }
    private static Vector2 Direction(float t)
    {
        float angle = Mathf.Lerp(180f, 0f, t) * Mathf.Deg2Rad;
        return new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
    }
}
