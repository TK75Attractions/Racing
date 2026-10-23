using UnityEngine;
using UnityEngine.UI;

/// <summary>Rounded, asymmetric surfaces and shared drawing primitives, without glow stacks.</summary>
public sealed class ArcadeSurfaceGraphic : MaskableGraphic
{
    protected override void OnPopulateMesh(VertexHelper vh){vh.Clear();Rounded(vh,rectTransform.rect,color,12);}
    public static void Rounded(VertexHelper vh,Rect r,Color tint,float radius)
    {
        radius=Mathf.Clamp(radius,0,Mathf.Min(r.width,r.height)*.5f);
        int start=vh.currentVertCount;vh.AddVert(r.center,tint,Vector2.zero);
        for(int corner=0;corner<4;corner++)
        {
            float rad=corner==0?radius*1.8f:radius;rad=Mathf.Min(rad,Mathf.Min(r.width,r.height)*.5f);
            Vector2 c=new Vector2(corner==0||corner==3?r.xMax-rad:r.xMin+rad,corner<2?r.yMin+rad:r.yMax-rad);
            for(int i=0;i<=6;i++){float a=(-corner*90-i*15)*Mathf.Deg2Rad;vh.AddVert(c+new Vector2(Mathf.Cos(a),Mathf.Sin(a))*rad,tint,Vector2.zero);}
        }
        for(int i=0;i<28;i++)vh.AddTriangle(start,start+1+i,start+1+(i+1)%28);
    }
    public static void Disc(VertexHelper vh,Vector2 center,float radius,Color tint)
    {
        int start=vh.currentVertCount;vh.AddVert(center,tint,Vector2.zero);
        for(int i=0;i<=80;i++){float a=i*Mathf.PI/40;vh.AddVert(center+new Vector2(Mathf.Cos(a),Mathf.Sin(a))*radius,tint,Vector2.zero);}
        for(int i=0;i<80;i++)vh.AddTriangle(start,start+i+1,start+i+2);
    }
    public static void Arc(VertexHelper vh,Vector2 c,float radius,float from,float to,float width,Color tint)
    {
        int steps=Mathf.Max(1,Mathf.CeilToInt(Mathf.Abs(to-from)/3f));
        for(int i=0;i<steps;i++)RacingPanelGraphic.Line(vh,Point(c,radius,Mathf.Lerp(from,to,(float)i/steps)),Point(c,radius,Mathf.Lerp(from,to,(float)(i+1)/steps)),width,tint,.35f);
    }
    public static Vector2 Point(Vector2 c,float radius,float degrees)
    {float a=degrees*Mathf.Deg2Rad;return c+new Vector2(Mathf.Cos(a),Mathf.Sin(a))*radius;}
}
