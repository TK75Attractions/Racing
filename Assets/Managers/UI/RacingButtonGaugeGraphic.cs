using UnityEngine;
using UnityEngine.UI;

/// <summary>High-contrast five-cell hold gauge. No bitmap textures or polling rebuilds.</summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class RacingButtonGaugeGraphic : MaskableGraphic
{
    private float charge, flash;
    private Color tint = NeonUI.Pink;
    private bool unavailable;
    protected override void OnEnable() { base.OnEnable(); raycastTarget = false; }
    public void SetVisual(float amount, Color accent, float confirmation, bool disabled)
    {
        amount=Mathf.Clamp01(amount);
        if(Mathf.Approximately(charge,amount) && tint==accent && Mathf.Approximately(flash,confirmation) && unavailable==disabled)return;
        charge=amount;tint=accent;flash=confirmation;unavailable=disabled;SetVerticesDirty();
    }
    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();Rect r=rectTransform.rect;if(r.width<1||r.height<1)return;
        var p=ArcadeUI.For(transform);
        if(charge>0)Quad(vh,new Rect(r.xMin,r.yMin,r.width*charge,r.height),unavailable?p.Soft:p.Accent);
    }
    private static void Quad(VertexHelper vh,Rect r,Color c)
    {
        int k=vh.currentVertCount;vh.AddVert(new Vector2(r.xMin,r.yMin),c,Vector2.zero);vh.AddVert(new Vector2(r.xMax,r.yMin),c,Vector2.zero);
        vh.AddVert(new Vector2(r.xMax,r.yMax),c,Vector2.zero);vh.AddVert(new Vector2(r.xMin,r.yMax),c,Vector2.zero);
        vh.AddTriangle(k,k+1,k+2);vh.AddTriangle(k,k+2,k+3);
    }
    private static void Polygon(VertexHelper vh,Rect r,Color c)
    {
        float cut=Mathf.Min(7f,r.height*.35f);
        int k=vh.currentVertCount;
        vh.AddVert(new Vector2(r.xMin+cut,r.yMin),c,Vector2.zero);vh.AddVert(new Vector2(r.xMax-cut,r.yMin),c,Vector2.zero);
        vh.AddVert(new Vector2(r.xMax,r.center.y),c,Vector2.zero);vh.AddVert(new Vector2(r.xMax-cut,r.yMax),c,Vector2.zero);
        vh.AddVert(new Vector2(r.xMin+cut,r.yMax),c,Vector2.zero);vh.AddVert(new Vector2(r.xMin,r.center.y),c,Vector2.zero);
        for(int i=1;i<5;i++)vh.AddTriangle(k,k+i,k+i+1);
    }
    private static void Outline(VertexHelper vh,Rect r,float width,Color c)
    {
        float cut=Mathf.Min(7f,r.height*.35f);
        Vector2 a=new Vector2(r.xMin+cut,r.yMin),b=new Vector2(r.xMax-cut,r.yMin),d=new Vector2(r.xMax,r.center.y),e=new Vector2(r.xMax-cut,r.yMax),f=new Vector2(r.xMin+cut,r.yMax),g=new Vector2(r.xMin,r.center.y);
        RacingPanelGraphic.Line(vh,a,b,width,c);RacingPanelGraphic.Line(vh,b,d,width,c);RacingPanelGraphic.Line(vh,d,e,width,c);
        RacingPanelGraphic.Line(vh,e,f,width,c);RacingPanelGraphic.Line(vh,f,g,width,c);RacingPanelGraphic.Line(vh,g,a,width,c);
    }
}
