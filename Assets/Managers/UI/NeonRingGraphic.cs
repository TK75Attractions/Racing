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
            Color signal = Color.Lerp(new Color(.16f,.18f,.22f,1f), color, amount);
            Disc(radius, Color.Lerp(new Color(.025f,.03f,.04f,1f), signal * .6f, amount),
                Color.Lerp(new Color(.01f,.012f,.02f,1f), signal, amount));
            Arc(radius*.77f,0,360,3f,signal);
            for(int y=-4;y<=4;y++)for(int x=-4;x<=4;x++)
                if(x*x+y*y<20) Dot(center+new Vector2(x,y)*radius*.15f,radius*.045f,signal);
            return;
        }
        var p=ArcadeUI.For(transform);
        Disc(radius,p.Ink,p.Ink);
        for(int i=0;i<48;i++){float a=-135+i*7.5f;Arc(radius,a,a+5,8,i<48*Mathf.Clamp01(amount)?p.Primary:RacingPanelGraphic.Alpha(p.Soft,.25f));}
        for(int i=0;i<12;i++){float a=i*30;Line(Point(radius*.78f,a),Point(radius*.87f,a),2,p.Paper);}
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
