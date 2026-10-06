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
        vh.Clear();Rect r=rectTransform.rect;
        Color edge=unavailable?new Color(.32f,.37f,.47f):Color.Lerp(tint,Color.white,.28f);
        Polygon(vh,r,new Color(.005f,.015f,.05f,.99f));
        Outline(vh,r,1.5f,edge);
        Rect inside=new Rect(r.xMin+5f,r.yMin+4f,r.width-10f,r.height-8f);
        float gap=2f, width=(inside.width-gap*4f)/5f;
        for(int i=0;i<5;i++)
        {
            Rect cell=new Rect(inside.xMin+i*(width+gap),inside.yMin,width,inside.height);
            Quad(vh,cell,new Color(.035f,.065f,.12f));
            float value=Mathf.Clamp01(charge*5f-i);
            if(value>0f)
            {
                Rect fill=cell;fill.width*=value;
                Color lit=Color.Lerp(edge,Color.white,.35f+flash*.35f);
                Quad(vh,fill,Color.Lerp(edge,lit,.5f));
                RacingPanelGraphic.Line(vh,new Vector2(fill.xMin,fill.yMax),new Vector2(fill.xMax,fill.yMax),1.5f,lit);
            }
            Outline(vh,cell,.7f,RacingPanelGraphic.Alpha(edge,.42f));
        }
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
