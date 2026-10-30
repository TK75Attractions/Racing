using UnityEngine;
using UnityEngine.UI;

/// <summary>A wheel turning back to neutral, with a fixed arc showing the physical return direction.</summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class SteeringReturnGraphic : MaskableGraphic
{
    private int direction = 1;
    private float wheelDegrees = -55f;
    public int ReturnDirection => direction; // +1 = counterclockwise / left, -1 = clockwise / right.
    public float WheelDegrees => wheelDegrees;
    public void SetAnimation(int returnDirection, float seconds)
    {
        direction = returnDirection >= 0 ? 1 : -1;
        float phase = Mathf.Clamp01(Mathf.Repeat(Mathf.Max(0f, seconds), 1.5f) / 1.1f);
        wheelDegrees = -direction * 55f * (1f - Mathf.SmoothStep(0f, 1f, phase));
        SetVerticesDirty();
    }
    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        Rect r = rectTransform.rect;
        float size = Mathf.Min(r.width, r.height);
        float aa = 1f / Mathf.Max(.1f, canvas != null ? canvas.scaleFactor : 1f);
        Vector2 center = r.center;
        Vector2 At(float degrees, float radius)
        {
            float angle = degrees * Mathf.Deg2Rad;
            return center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * size * radius;
        }
        void Line(Vector2 a, Vector2 b, float width) => RacingPanelGraphic.Line(vh, a, b, size * width, color, aa);
        for (int i = 0; i < 64; i++) Line(At(i*360f/64,.29f), At((i+1)*360f/64,.29f), .035f);
        foreach (float spoke in new[] { 0f, 180f, 270f }) Line(center, At(spoke + wheelDegrees,.28f), .035f);
        // A bright index mark moves from the oversteered angle toward twelve o'clock.
        Line(At(90f+wheelDegrees,.25f),At(90f+wheelDegrees,.32f),.055f);
        float start = direction > 0 ? 25f : 155f;
        float end = start + direction * 100f;
        for (int i = 0; i < 32; i++) Line(At(start+direction*i*100f/32,.43f),At(start+direction*(i+1)*100f/32,.43f),.028f);
        Vector2 tip=At(end,.43f);
        float tangent=(end+direction*90f)*Mathf.Deg2Rad;
        Vector2 back=new Vector2(Mathf.Cos(tangent),Mathf.Sin(tangent))*size*.11f;
        Vector2 wing=new Vector2(-back.y,back.x)*.65f;
        Line(tip,tip-back+wing,.032f);Line(tip,tip-back-wing,.032f);
    }
}
