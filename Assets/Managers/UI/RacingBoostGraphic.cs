using UnityEngine;
using UnityEngine.UI;

/// <summary>ブーストゲージのグラデーションと、画面端の加速演出を描画します。</summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class RacingBoostGraphic : MaskableGraphic
{
    public enum DisplayMode { Bar, Screen }

    private DisplayMode mode;
    private float intensity;
    private float flash;
    private float phase;

    public void Configure(DisplayMode displayMode)
    {
        mode = displayMode;
        raycastTarget = false;
        SetVerticesDirty();
    }

    public void SetEffect(float value, float entryFlash, float animationPhase)
    {
        intensity = Mathf.Clamp01(value);
        flash = Mathf.Clamp01(entryFlash);
        phase = Mathf.Repeat(animationPhase, 1f);
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        Rect bounds = rectTransform.rect;
        if (bounds.width <= 0f || bounds.height <= 0f || intensity <= 0f) return;
        if (mode == DisplayMode.Bar) DrawBar(vh, bounds);
        else DrawScreen(vh, bounds);
    }

    private void DrawBar(VertexHelper vh, Rect bounds)
    {
        Color red = new Color(.78f, .44f, .27f, 1f);
        Color orange = RacingHUDStyle.Amber;
        Color gold = new Color(1f, .86f, .61f, 1f);
        float pulse = 0.86f + 0.14f * Mathf.Sin(phase * Mathf.PI * 2f);
        const int segments = 32;
        for (int i = 0; i < segments; i++)
        {
            float left = i / (float)segments;
            float right = (i + 1f) / segments;
            float mid = (left + right) * 0.5f;
            Color baseColor = mid < 0.55f
                ? Color.Lerp(red, orange, mid / 0.55f)
                : Color.Lerp(orange, gold, (mid - 0.55f) / 0.45f);
            float sweep = Mathf.Max(0f, 1f - Mathf.Abs(mid - phase) * 9f);
            Color tint = Color.Lerp(baseColor * pulse, Color.white, sweep * 0.5f + flash * 0.25f);
            tint.a = intensity;
            float x0 = Mathf.Lerp(bounds.xMin, bounds.xMax, left);
            float x1 = Mathf.Lerp(bounds.xMin, bounds.xMax, right);
            Quad(vh, new Vector2(x0, bounds.yMin), new Vector2(x1, bounds.yMax),
                tint * 0.7f, tint * 0.7f, tint, tint);
        }

        // 細い斜線を横へ流し、残り時間が静止した帯に見えないようにする。
        for (int i = 0; i < 5; i++)
        {
            float x = Mathf.Repeat(phase + i * 0.22f, 1f);
            if (x < 0.08f || x > 0.92f) continue;
            float center = Mathf.Lerp(bounds.xMin, bounds.xMax, x);
            RacingPanelGraphic.Line(vh,
                new Vector2(center - bounds.height * 0.2f, bounds.yMin + 2f),
                new Vector2(center + bounds.height * 0.2f, bounds.yMax - 2f),
                2.5f, new Color(1f, 1f, 1f, 0.35f * intensity));
        }
    }

    private void DrawScreen(VertexHelper vh, Rect bounds)
    {
        float width = Mathf.Min(bounds.width * 0.11f, 150f);
        float height = Mathf.Min(bounds.height * 0.14f, 100f);
        float pulse = 0.75f + 0.25f * Mathf.Sin(phase * Mathf.PI * 2f);
        Color edge = new Color(.96f, .55f + flash * .15f, .24f,
            (.14f * pulse + .15f * flash) * intensity);
        Color clear = edge;
        clear.a = 0f;

        Quad(vh, new Vector2(bounds.xMin, bounds.yMin),
            new Vector2(bounds.xMin + width, bounds.yMax), edge, clear, clear, edge);
        Quad(vh, new Vector2(bounds.xMax - width, bounds.yMin),
            new Vector2(bounds.xMax, bounds.yMax), clear, edge, edge, clear);
        Quad(vh, new Vector2(bounds.xMin, bounds.yMax - height),
            new Vector2(bounds.xMax, bounds.yMax), clear, clear, edge, edge);
        Quad(vh, new Vector2(bounds.xMin, bounds.yMin),
            new Vector2(bounds.xMax, bounds.yMin + height), edge, edge, clear, clear);

        Color streak = new Color(1f, 0.56f, 0.18f, (0.24f + flash * 0.38f) * intensity);
        for (int i = 0; i < 9; i++)
        {
            float x = Mathf.Lerp(bounds.xMin, bounds.xMax, Mathf.Repeat(phase + i / 9f, 1f));
            RacingPanelGraphic.Line(vh,
                new Vector2(x - 35f, bounds.yMax - 8f),
                new Vector2(x + 15f, bounds.yMax - height * 0.4f), 2f, streak);
            RacingPanelGraphic.Line(vh,
                new Vector2(x - 35f, bounds.yMin + height * 0.4f),
                new Vector2(x + 15f, bounds.yMin + 8f), 2f, streak);
        }
    }

    private static void Quad(VertexHelper vh, Vector2 min, Vector2 max,
        Color bottomLeft, Color bottomRight, Color topRight, Color topLeft)
    {
        int first = vh.currentVertCount;
        vh.AddVert(new Vector2(min.x, min.y), bottomLeft, Vector2.zero);
        vh.AddVert(new Vector2(max.x, min.y), bottomRight, Vector2.zero);
        vh.AddVert(new Vector2(max.x, max.y), topRight, Vector2.zero);
        vh.AddVert(new Vector2(min.x, max.y), topLeft, Vector2.zero);
        vh.AddTriangle(first, first + 1, first + 2);
        vh.AddTriangle(first, first + 2, first + 3);
    }
}
