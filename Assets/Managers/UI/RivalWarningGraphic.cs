using UnityEngine;
using UnityEngine.UI;

/// <summary>A soft red edge; the middle 76% stays transparent and the Graphic never takes input.</summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class RivalWarningGraphic : MaskableGraphic
{
    protected override void Awake() { base.Awake(); raycastTarget = false; }
    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        Rect r = rectTransform.rect;
        for (int i = 0; i < 12; i++)
        {
            float a = i / 12f, b = (i + 1) / 12f;
            Color ca = color, cb = color;
            ca.a *= (1f - a) * (1f - a); cb.a *= (1f - b) * (1f - b);
            float xa = r.width * .12f * a, xb = r.width * .12f * b;
            float ya = r.height * .12f * a, yb = r.height * .12f * b;
            Strip(vh, new Vector2(r.xMin + xa,r.yMin), new Vector2(r.xMin + xb,r.yMin), new Vector2(r.xMin + xb,r.yMax), new Vector2(r.xMin + xa,r.yMax), ca,cb,cb,ca);
            Strip(vh, new Vector2(r.xMax - xb,r.yMin), new Vector2(r.xMax - xa,r.yMin), new Vector2(r.xMax - xa,r.yMax), new Vector2(r.xMax - xb,r.yMax), cb,ca,ca,cb);
            Strip(vh, new Vector2(r.xMin,r.yMin + ya), new Vector2(r.xMax,r.yMin + ya), new Vector2(r.xMax,r.yMin + yb), new Vector2(r.xMin,r.yMin + yb), ca,ca,cb,cb);
            Strip(vh, new Vector2(r.xMin,r.yMax - yb), new Vector2(r.xMax,r.yMax - yb), new Vector2(r.xMax,r.yMax - ya), new Vector2(r.xMin,r.yMax - ya), cb,cb,ca,ca);
        }
    }
    private static void Strip(VertexHelper vh, Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color ca, Color cb, Color cc, Color cd)
    {
        int n = vh.currentVertCount;
        vh.AddVert(a, ca, Vector2.zero); vh.AddVert(b, cb, Vector2.zero);
        vh.AddVert(c, cc, Vector2.zero); vh.AddVert(d, cd, Vector2.zero);
        vh.AddTriangle(n,n+1,n+2); vh.AddTriangle(n,n+2,n+3);
    }
}
