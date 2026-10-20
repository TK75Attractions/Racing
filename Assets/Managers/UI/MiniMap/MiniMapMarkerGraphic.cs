using UnityEngine;
using UnityEngine.UI;

/// <summary>Outlined human arrows and CPU dots, independent of player color.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(CanvasRenderer))]
public sealed class MiniMapMarkerGraphic : MaskableGraphic
{
    public bool IsCpu { get; private set; }
    public bool IsAtEdge { get; private set; }
    public void Configure(bool cpu, bool edge = false)
    {
        if (IsCpu == cpu && IsAtEdge == edge) return;
        IsCpu = cpu; IsAtEdge = edge; SetVerticesDirty();
    }
    protected override void Awake() { base.Awake(); raycastTarget = false; }
    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        Rect r = GetPixelAdjustedRect();
        // Double contour stays legible over both bright track and dark background.
        Shape(vh, r, 1f, new Color(.005f,.015f,.03f,1f));
        Shape(vh, r, .80f, Color.white);
        Shape(vh, r, .59f, color);
        if (IsAtEdge)
            RacingPanelGraphic.Line(vh, r.center + new Vector2(-r.width*.22f,-r.height*.48f),
                r.center + new Vector2(r.width*.22f,-r.height*.48f), 2f, Color.white, .5f);
    }
    private void Shape(VertexHelper vh, Rect r, float scale, Color tint)
    {
        int start = vh.currentVertCount;
        if (IsCpu)
        {
            vh.AddVert(r.center, tint, Vector2.zero);
            for (int i = 0; i <= 16; i++)
            {
                float a = i * Mathf.PI / 8f;
                vh.AddVert(r.center + new Vector2(Mathf.Cos(a)*r.width,Mathf.Sin(a)*r.height)*(.5f*scale), tint, Vector2.zero);
                if (i > 0) vh.AddTriangle(start, start+i, start+i+1);
            }
        }
        else
        {
            vh.AddVert(r.center + new Vector2(0,r.height*.5f)*scale, tint, Vector2.zero);
            vh.AddVert(r.center + new Vector2(-r.width*.5f,-r.height*.5f)*scale, tint, Vector2.zero);
            vh.AddVert(r.center + new Vector2(r.width*.5f,-r.height*.5f)*scale, tint, Vector2.zero);
            vh.AddTriangle(start,start+1,start+2);
        }
    }
}