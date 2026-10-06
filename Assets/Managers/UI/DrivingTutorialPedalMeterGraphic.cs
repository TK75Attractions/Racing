using UnityEngine;
using UnityEngine.UI;

/// <summary>Outlined vertical accelerator meter with clipped diagonal stripes.</summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class DrivingTutorialPedalMeterGraphic : MaskableGraphic
{
    private float pressure;
    public float Pressure { set { float next = Mathf.Clamp01(value); if (Mathf.Approximately(next, pressure)) return; pressure = next; SetVerticesDirty(); } }
    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        Rect r = rectTransform.rect;
        Rectangle(vh, r, new Color(.018f, .01f, .026f, .9f));
        Color pink = DrivingTutorialUI.Pink;
        Rectangle(vh, new Rect(r.xMin, r.yMin, 2f, r.height), pink);
        Rectangle(vh, new Rect(r.xMax - 2f, r.yMin, 2f, r.height), pink);
        Rectangle(vh, new Rect(r.xMin, r.yMax - 2f, r.width, 2f), pink);
        Rectangle(vh, new Rect(r.xMin, r.yMin, r.width, 2f), pink);
        float left = r.xMin + 3f, right = r.xMax - 3f, bottom = r.yMin + 3f, top = Mathf.Lerp(bottom, r.yMax - 3f, pressure);
        if (top <= bottom) return;
        Rectangle(vh, Rect.MinMaxRect(left, bottom, right, top), pink);
        float slope = .45f, spacing = 28f, band = 12f;
        // Clip each stripe against the filled meter; no textures or overflowing children.
        for (float y = bottom - r.width * slope - spacing; y < top; y += spacing)
        {
            var points = new System.Collections.Generic.List<Vector2> { new Vector2(left, y), new Vector2(right, y + (right - left) * slope),
                new Vector2(right, y + (right - left) * slope + band), new Vector2(left, y + band) };
            points = Clip(points, bottom, true); points = Clip(points, top, false);
            int index = vh.currentVertCount;
            foreach (Vector2 point in points) vh.AddVert(point, new Color(1f, .51f, .62f, .5f), Vector2.zero);
            for (int i = 1; i + 1 < points.Count; i++) vh.AddTriangle(index, index + i, index + i + 1);
        }
    }
    private static System.Collections.Generic.List<Vector2> Clip(System.Collections.Generic.List<Vector2> input, float y, bool above)
    {
        var output = new System.Collections.Generic.List<Vector2>();
        if (input.Count == 0) return output;
        Vector2 previous = input[input.Count - 1]; bool wasInside = above ? previous.y >= y : previous.y <= y;
        foreach (Vector2 current in input)
        {
            bool inside = above ? current.y >= y : current.y <= y;
            if (inside != wasInside) output.Add(Vector2.Lerp(previous, current, (y - previous.y) / (current.y - previous.y)));
            if (inside) output.Add(current);
            previous = current; wasInside = inside;
        }
        return output;
    }
    private static void Rectangle(VertexHelper vh, Rect r, Color tint)
    {
        int i = vh.currentVertCount;
        vh.AddVert(new Vector2(r.xMin, r.yMin), tint, Vector2.zero); vh.AddVert(new Vector2(r.xMax, r.yMin), tint, Vector2.zero);
        vh.AddVert(new Vector2(r.xMax, r.yMax), tint, Vector2.zero); vh.AddVert(new Vector2(r.xMin, r.yMax), tint, Vector2.zero);
        vh.AddTriangle(i, i + 1, i + 2); vh.AddTriangle(i, i + 2, i + 3);
    }
}
