using UnityEngine;
using UnityEngine.UI;

/// <summary>Soft charcoal instruments with chamfered edges and a circular speed dial.</summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class RacingHUDPlateGraphic : MaskableGraphic
{
    public enum PlateShape { Position, Lap, Timer, Speed, Boost, Map, Notification }
    [SerializeField] private PlateShape shape;
    [SerializeField] private Color accent;
    private readonly Vector2[] points = new Vector2[80];

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
        int count = Outline(r);
        Fill(vh, r, count, new Vector2(0f, -5f), true);
        Fill(vh, r, count, Vector2.zero, false);
        float aa = 1f / Mathf.Max(.1f, canvas != null ? canvas.scaleFactor : 1f);
        for (int i = 0; i < count; i++)
            RacingPanelGraphic.Line(vh, points[i], points[(i + 1) % count], 1f,
                new Color(.62f, .75f, .76f, .26f) * color, aa);
        if (shape == PlateShape.Speed) return; // The live arc is this instrument's accent.
        float x = r.xMin + r.width * .10f;
        RacingPanelGraphic.Line(vh, new Vector2(x, r.yMax), new Vector2(x + r.width * .23f, r.yMax),
            3f, accent * color, aa);
    }

    private void Fill(VertexHelper vh, Rect r, int count, Vector2 offset, bool shadow)
    {
        int first = vh.currentVertCount;
        vh.AddVert(r.center + offset, Tint(r, r.center.y, shadow), Vector2.zero);
        for (int i = 0; i < count; i++) vh.AddVert(points[i] + offset, Tint(r, points[i].y, shadow), Vector2.zero);
        for (int i = 0; i < count; i++) vh.AddTriangle(first, first + 1 + i, first + 1 + (i + 1) % count);
    }

    private Color Tint(Rect r, float y, bool shadow)
    {
        if (shadow) return new Color(0f, 0f, 0f, .17f) * color;
        return Color.Lerp(new Color(.075f, .095f, .11f, .94f), new Color(.14f, .18f, .20f, .94f),
            Mathf.InverseLerp(r.yMin, r.yMax, y)) * color;
    }

    private int Outline(Rect r)
    {
        if (shape == PlateShape.Speed)
        {
            float radius = Mathf.Min(r.width, r.height) * .48f;
            for (int i = 0; i < points.Length; i++)
            {
                float a = i * Mathf.PI * 2f / points.Length;
                points[i] = r.center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius;
            }
            return points.Length;
        }
        float cut = Mathf.Min(r.height * .14f, r.width * .08f);
        float largeCut = shape == PlateShape.Position ? Mathf.Min(r.height * .20f, r.width * .22f) : cut;
        points[0] = new Vector2(r.xMin + cut, r.yMin);
        points[1] = new Vector2(r.xMax - largeCut, r.yMin);
        points[2] = new Vector2(r.xMax, r.yMin + largeCut);
        points[3] = new Vector2(r.xMax, r.yMax - cut);
        points[4] = new Vector2(r.xMax - cut, r.yMax);
        points[5] = new Vector2(r.xMin + cut, r.yMax);
        points[6] = new Vector2(r.xMin, r.yMax - cut);
        points[7] = new Vector2(r.xMin, r.yMin + cut);
        return 8;
    }
}
