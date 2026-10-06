using UnityEngine;
using UnityEngine.UI;

/// <summary>Only the changing charge arc; the original glow and tick geometry stays static.</summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class RacingBoostArcGraphic : MaskableGraphic
{
    private float amount;
    public float Amount => amount;

    protected override void OnEnable() { base.OnEnable(); raycastTarget = false; }

    public void SetAmount(float value)
    {
        float next = Mathf.Clamp01(value);
        if (Mathf.Approximately(amount, next)) return;
        amount = next;
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        // Match the existing arc exactly, including its subdivision and feather width.
        Rect r = rectTransform.rect;
        Vector2 center = r.center;
        float radius = Mathf.Min(r.width, r.height) * .45f * .91f;
        float aa = 1f / Mathf.Max(.1f, canvas != null ? canvas.scaleFactor : 1f);
        float to = -90f + 360f * amount;
        int count = Mathf.Max(2, Mathf.CeilToInt(Mathf.Abs(to + 90f) / 3f));
        for (int i = 0; i < count; i++)
        {
            float a = Mathf.Lerp(-90f, to, (float)i / count) * Mathf.Deg2Rad;
            float b = Mathf.Lerp(-90f, to, (float)(i + 1) / count) * Mathf.Deg2Rad;
            RacingPanelGraphic.Line(vh,
                center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius,
                center + new Vector2(Mathf.Cos(b), Mathf.Sin(b)) * radius, 9f, NeonUI.Pink, aa);
        }
    }
}
