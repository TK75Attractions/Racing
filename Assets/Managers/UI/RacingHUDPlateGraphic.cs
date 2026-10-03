using UnityEngine;
using UnityEngine.UI;

/// <summary>Angular, resolution-independent instrument plates used only by the race HUD.</summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class RacingHUDPlateGraphic : MaskableGraphic
{
    public enum PlateShape { Position, Lap, Timer, Speed, Boost }

    [SerializeField] private PlateShape shape;
    [SerializeField] private Color accent = new Color(0.20f, 0.82f, 1f, 1f);

    public void Configure(PlateShape value, Color tint)
    {
        shape = value;
        accent = tint;
        raycastTarget = false;
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        Rect r = rectTransform.rect;
        if (r.width < 2f || r.height < 2f) return;

        Vector2[] points = Outline(r);
        Vector2 center = Vector2.zero;
        foreach (Vector2 point in points) center += point;
        center /= points.Length;

        int first = vh.currentVertCount;
        vh.AddVert(center, new Color(0.025f, 0.055f, 0.080f, 0.90f) * color, Vector2.zero);
        foreach (Vector2 point in points)
        {
            float elevation = Mathf.InverseLerp(r.yMin, r.yMax, point.y);
            Color tint = Color.Lerp(new Color(0.025f, 0.055f, 0.080f, 0.93f),
                new Color(0.075f, 0.135f, 0.175f, 0.95f), elevation);
            vh.AddVert(point, tint * color, Vector2.zero);
        }
        for (int i = 0; i < points.Length; i++)
            vh.AddTriangle(first, first + 1 + i, first + 1 + (i + 1) % points.Length);

        float feather = 1f / Mathf.Max(0.1f, canvas != null ? canvas.scaleFactor : 1f);
        Color edge = new Color(0.34f, 0.54f, 0.63f, 0.64f);
        for (int i = 0; i < points.Length; i++)
            RacingPanelGraphic.Line(vh, points[i], points[(i + 1) % points.Length], 1.3f, edge, feather);

        // A short illuminated edge gives each instrument a directional cue.
        Color highlight = accent;
        highlight.a = 0.95f;
        int start = shape == PlateShape.Speed ? 9 : 2;
        RacingPanelGraphic.Line(vh, points[start], points[(start + 1) % points.Length],
            shape == PlateShape.Speed ? 3f : 3.5f, highlight, feather);
    }

    private Vector2[] Outline(Rect r)
    {
        float x = r.xMin, y = r.yMin, w = r.width, h = r.height;
        switch (shape)
        {
            case PlateShape.Position:
                return new[] {
                    new Vector2(x + w * .08f, y), new Vector2(x + w * .79f, y),
                    new Vector2(x + w, y + h * .32f), new Vector2(x + w, y + h * .78f),
                    new Vector2(x + w * .88f, y + h), new Vector2(x + w * .08f, y + h),
                    new Vector2(x, y + h * .82f), new Vector2(x, y + h * .15f) };
            case PlateShape.Lap:
                return new[] {
                    new Vector2(x, y + h * .13f), new Vector2(x + w * .82f, y + h * .13f),
                    new Vector2(x + w, y + h * .50f), new Vector2(x + w * .82f, y + h * .88f),
                    new Vector2(x, y + h * .88f), new Vector2(x + w * .11f, y + h * .50f) };
            case PlateShape.Timer:
                return new[] {
                    new Vector2(x, y), new Vector2(x + w * .93f, y),
                    new Vector2(x + w, y + h * .17f), new Vector2(x + w, y + h),
                    new Vector2(x + w * .15f, y + h), new Vector2(x, y + h * .76f) };
            case PlateShape.Speed:
                return new[] {
                    new Vector2(x + w * .27f, y), new Vector2(x + w * .73f, y),
                    new Vector2(x + w * .94f, y + h * .17f), new Vector2(x + w, y + h * .40f),
                    new Vector2(x + w * .89f, y + h * .78f), new Vector2(x + w * .69f, y + h),
                    new Vector2(x + w * .31f, y + h), new Vector2(x + w * .11f, y + h * .78f),
                    new Vector2(x, y + h * .40f), new Vector2(x + w * .06f, y + h * .17f) };
            default:
                return new[] {
                    new Vector2(x + w * .04f, y), new Vector2(x + w * .94f, y),
                    new Vector2(x + w, y + h * .38f), new Vector2(x + w * .96f, y + h),
                    new Vector2(x, y + h), new Vector2(x + w * .04f, y + h * .62f) };
        }
    }
}
