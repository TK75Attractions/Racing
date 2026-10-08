using UnityEngine;
using UnityEngine.UI;

/// <summary>Semicircular 50-segment speed arc; rebuilds only when visible lighting changes.</summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class RacingSpeedGauge : MaskableGraphic
{
    private const int Segments = 50;
    private int litSegments;
    public void SetSpeed(float value)
    {
        // The original i + .12 threshold determines exactly which segments are visible.
        int next = Mathf.Clamp(Mathf.CeilToInt(Mathf.Clamp01(value / 180f) * Segments - .12f), 0, Segments);
        if (next == litSegments) return;
        litSegments = next;
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
        for (int i = 0; i < Segments; i++)
        {
            float t0 = (i + .12f) / Segments, t1 = (i + .88f) / Segments;
            Color tint = t0 > .60f ? NeonUI.Pink : NeonUI.Cyan;
            tint.a = i < litSegments ? 1f : .46f;
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
