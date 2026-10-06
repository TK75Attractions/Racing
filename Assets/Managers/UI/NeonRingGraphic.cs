using UnityEngine;
using UnityEngine.UI;

/// <summary>Native neon arcs, starting lamps and a metallic laurel medal.</summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class NeonRingGraphic : MaskableGraphic
{
    public enum Display { Countdown, Boost, Light }
    public Display display;
    public float amount = 1f;
    private VertexHelper mesh;
    private Vector2 center;
    private float radius, aa;
    private RacingBoostArcGraphic progressArc;
    protected override void OnEnable() { base.OnEnable(); raycastTarget = false; }
    public void BindProgress(RacingBoostArcGraphic arc)
    {
        bool changed = progressArc != arc;
        progressArc = arc;
        if (progressArc != null) progressArc.SetAmount(amount);
        if (changed) SetVerticesDirty();
    }
    public void SetAmount(float value)
    {
        float next = Mathf.Clamp01(value);
        if (Mathf.Approximately(amount, next)) return;
        amount = next;
        if (progressArc != null) progressArc.SetAmount(next);
        else SetVerticesDirty();
    }
    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear(); mesh = vh; Rect r = rectTransform.rect; center=r.center;
        radius=Mathf.Min(r.width,r.height)*.45f;
        aa=1f/Mathf.Max(.1f,canvas!=null?canvas.scaleFactor:1f);
        if(display==Display.Light)
        {
            Disc(radius,new Color(.10f,.012f,.025f,.96f),new Color(.03f,.008f,.02f,1));
            Arc(radius*.77f,0,360,3f,color);
            for(int y=-4;y<=4;y++)for(int x=-4;x<=4;x++)
                if(x*x+y*y<20) Dot(center+new Vector2(x,y)*radius*.15f,radius*.045f,color);
            return;
        }
        Disc(radius,new Color(.015f,.025f,.085f,.78f),new Color(.015f,.025f,.085f,.15f));
        for(int layer=5;layer>0;layer--)
        {
            Color pink=RacingPanelGraphic.Alpha(NeonUI.Pink,.018f*(6-layer));
            Color blue=RacingPanelGraphic.Alpha(NeonUI.Cyan,.018f*(6-layer));
            Arc(radius,0,92,layer*5f,pink);Arc(radius,96,177,layer*5f,blue);
            Arc(radius,182,267,layer*5f,pink);Arc(radius,271,357,layer*5f,blue);
        }
        Arc(radius,0,92,4f,NeonUI.Pink);Arc(radius,96,177,4f,NeonUI.Cyan);
        Arc(radius,182,267,4f,NeonUI.Pink);Arc(radius,271,357,4f,NeonUI.Cyan);
        Arc(radius*1.09f,8,72,1f,RacingPanelGraphic.Alpha(NeonUI.Pink,.7f));
        Arc(radius*1.09f,103,164,1f,RacingPanelGraphic.Alpha(NeonUI.Cyan,.7f));
        Arc(radius*.84f,0,360,1f,new Color(.49f,.38f,.94f,.45f));
        for(int i=0;i<120;i++)
        {float a=i*3f;Color c=i<60?NeonUI.Pink:NeonUI.Cyan;c.a=.46f;Line(Point(radius*.83f,a),Point(radius*.86f,a),1f,c);}
        if(display==Display.Boost && progressArc==null)Arc(radius*.91f,-90,-90+360*Mathf.Clamp01(amount),9f,NeonUI.Pink);
    }
    private Vector2 Point(float r,float degrees) {float a=degrees*Mathf.Deg2Rad;return center+new Vector2(Mathf.Cos(a),Mathf.Sin(a))*r;}
    private void Line(Vector2 a,Vector2 b,float width,Color c)=>RacingPanelGraphic.Line(mesh,a,b,width,c,aa);
    private void Arc(float r,float from,float to,float width,Color c)
    {int count=Mathf.Max(2,Mathf.CeilToInt(Mathf.Abs(to-from)/3f));for(int i=0;i<count;i++)Line(Point(r,Mathf.Lerp(from,to,(float)i/count)),Point(r,Mathf.Lerp(from,to,(float)(i+1)/count)),width,c);}
    private void Disc(float r,Color rim,Color middle)
    {int k=mesh.currentVertCount;mesh.AddVert(center,middle,Vector2.zero);for(int i=0;i<=96;i++)mesh.AddVert(Point(r,i*360f/96),rim,Vector2.zero);for(int i=0;i<96;i++)mesh.AddTriangle(k,k+i+1,k+i+2);}
    private void Dot(Vector2 p,float r,Color c)
    {int k=mesh.currentVertCount;mesh.AddVert(p,c,Vector2.zero);for(int i=0;i<=12;i++){float a=i*Mathf.PI/6;mesh.AddVert(p+new Vector2(Mathf.Cos(a),Mathf.Sin(a))*r,c,Vector2.zero);}for(int i=0;i<12;i++)mesh.AddTriangle(k,k+i+1,k+i+2);}
}
