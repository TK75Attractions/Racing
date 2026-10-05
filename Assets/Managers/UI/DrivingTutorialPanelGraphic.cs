using UnityEngine;
using UnityEngine.UI;

/// <summary>Chamfered translucent instrument glass with a bright, feathered neon outline.</summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class DrivingTutorialPanelGraphic : MaskableGraphic
{
    private Color accent;
    private bool selected = true, instrument;
    public void Configure(Color tint, bool active = true, bool inset = false) { accent = tint; selected = active; instrument = inset; raycastTarget = false; SetVerticesDirty(); }
    public void SetSelected(bool value) { if (selected == value) return; selected = value; SetVerticesDirty(); }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        Rect r = rectTransform.rect;
        // Geometry is authored in the same 1920×1080 canvas units as the reference layout.
        // Avoid camera-dependent cached thickness when switching displays or capturing offscreen.
        const float unit = 1f;
        Color border = selected || instrument ? accent : new Color(.17f, .34f, .46f, .9f);
        if (selected && !instrument)
            for (int i = 5; i > 0; i--)
                Ring(vh, r, -i * 1.5f * unit, -(i - 1) * 1.5f * unit, Alpha(accent, (8 - i) * .008f), Alpha(accent, (9 - i) * .008f));
        Vector2[] shape = Shape(r, 0f);
        int start = vh.currentVertCount;
        Color bottom = new Color(.004f, .016f, .032f, .92f);
        Color top = new Color(accent.r * .032f + .008f, accent.g * .032f + .008f, accent.b * .032f + .018f, .90f);
        foreach (Vector2 point in shape) vh.AddVert(point, Color.Lerp(bottom, top, Mathf.InverseLerp(r.yMin, r.yMax, point.y)), Vector2.zero);
        for (int i = 1; i < 7; i++) vh.AddTriangle(start, start + i, start + i + 1);
        Ring(vh, r, -unit, 0f, Alpha(border, 0f), border);
        Ring(vh, r, 0f, 2f * unit, border, border);
        Ring(vh, r, 2f * unit, 3f * unit, border, Alpha(border, 0f));
        Ring(vh, r, 5f * unit, 5.7f * unit, Alpha(accent, .15f), Alpha(accent, .15f));
    }

    private static Vector2[] Shape(Rect r, float inset)
    {
        float left = r.xMin + inset, right = r.xMax - inset, bottom = r.yMin + inset, top = r.yMax - inset;
        float cut = Mathf.Min(15f, Mathf.Min(r.width, r.height) * .12f);
        return new[] { new Vector2(left + cut, bottom), new Vector2(right - cut, bottom), new Vector2(right, bottom + cut),
            new Vector2(right, top - cut), new Vector2(right - cut, top), new Vector2(left + cut, top), new Vector2(left, top - cut), new Vector2(left, bottom + cut) };
    }

    private static void Ring(VertexHelper vh, Rect r, float outer, float inner, Color outside, Color inside)
    {
        Vector2[] a = Shape(r, outer), b = Shape(r, inner);
        for (int i = 0; i < 8; i++)
        {
            int n = (i + 1) % 8, index = vh.currentVertCount;
            vh.AddVert(a[i], outside, Vector2.zero); vh.AddVert(a[n], outside, Vector2.zero);
            vh.AddVert(b[n], inside, Vector2.zero); vh.AddVert(b[i], inside, Vector2.zero);
            vh.AddTriangle(index, index + 1, index + 2); vh.AddTriangle(index, index + 2, index + 3);
        }
    }
    private static Color Alpha(Color color, float alpha) { color.a = alpha; return color; }
}
