using UnityEngine;
using UnityEngine.UI;

/// <summary>Small line icons drawn at the current canvas resolution.</summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class RacingIconGraphic : MaskableGraphic
{
    public enum Icon { Back, Pedal }
    public Icon symbol;
    protected override void OnEnable() { base.OnEnable(); raycastTarget = false; }
    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        Rect r = rectTransform.rect;
        float size = Mathf.Min(r.width, r.height);
        float aa = 1f / Mathf.Max(0.1f, canvas != null ? canvas.scaleFactor : 1f);
        Vector2 center = r.center;
        if (symbol == Icon.Back)
        {
            RacingPanelGraphic.Line(vh, center + new Vector2(0.13f, 0.28f) * size, center + new Vector2(-0.13f, 0f) * size, size * 0.08f, color, aa);
            RacingPanelGraphic.Line(vh, center + new Vector2(-0.13f, 0f) * size, center + new Vector2(0.13f, -0.28f) * size, size * 0.08f, color, aa);
        }
        else
        {
            RacingPanelGraphic.Line(vh, center + new Vector2(-0.27f, -0.24f) * size, center + new Vector2(0.24f, -0.24f) * size, size * 0.07f, color, aa);
            RacingPanelGraphic.Line(vh, center + new Vector2(-0.03f, -0.23f) * size, center + new Vector2(0.16f, 0.23f) * size, size * 0.13f, color, aa);
        }
    }
}
