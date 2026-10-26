using UnityEngine;
using UnityEngine.UI;

/// <summary>Native geometry with a one-pixel coverage fringe, including the hard arcade shadow.</summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class ArcadeSurfaceGraphic : MaskableGraphic
{
    public Vector2 ShadowOffset { get; set; }
    public Color ShadowColor { get; set; }
    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();var r=rectTransform.rect;
        if(ShadowOffset!=Vector2.zero){var shadow=r;shadow.position+=ShadowOffset;Rounded(vh,shadow,ShadowColor,12);}
        Rounded(vh,r,color,12);
    }
    public static void Rounded(VertexHelper vh,Rect r,Color tint,float radius)
    {
        radius=Mathf.Clamp(radius,0,Mathf.Min(r.width,r.height)*.5f);
        int start=vh.currentVertCount;vh.AddVert(r.center,tint,Vector2.zero);
        for(int corner=0;corner<4;corner++)
        {
            float rad=corner==0?radius*1.8f:radius;rad=Mathf.Min(rad,Mathf.Min(r.width,r.height)*.5f);
            Vector2 c=new Vector2(corner==0||corner==3?r.xMax-rad:r.xMin+rad,corner<2?r.yMin+rad:r.yMax-rad);
            for(int i=0;i<=8;i++)
            {
                float a=(-corner*90-i*11.25f)*Mathf.Deg2Rad;var normal=new Vector2(Mathf.Cos(a),Mathf.Sin(a));
                vh.AddVert(c+normal*rad,tint,Vector2.zero);
                vh.AddVert(c+normal*(rad+1),RacingPanelGraphic.Alpha(tint,0),Vector2.zero);
            }
        }
        const int count=36;
        for(int i=0;i<count;i++)
        {
            int a=start+1+i*2,b=start+1+(i+1)%count*2;
            vh.AddTriangle(start,a,b);vh.AddTriangle(a,a+1,b);vh.AddTriangle(b,a+1,b+1);
        }
    }
    public static void Disc(VertexHelper vh,Vector2 center,float radius,Color tint)
    {
        const int count=160;int start=vh.currentVertCount;vh.AddVert(center,tint,Vector2.zero);
        for(int i=0;i<count;i++)
        {
            float a=i*Mathf.PI*2/count;var normal=new Vector2(Mathf.Cos(a),Mathf.Sin(a));
            vh.AddVert(center+normal*radius,tint,Vector2.zero);vh.AddVert(center+normal*(radius+1),RacingPanelGraphic.Alpha(tint,0),Vector2.zero);
        }
        for(int i=0;i<count;i++)
        {
            int a=start+1+i*2,b=start+1+(i+1)%count*2;
            vh.AddTriangle(start,a,b);vh.AddTriangle(a,a+1,b);vh.AddTriangle(b,a+1,b+1);
        }
    }
    public static void Arc(VertexHelper vh,Vector2 c,float radius,float from,float to,float width,Color tint)
    {
        int steps=Mathf.Max(1,Mathf.CeilToInt(Mathf.Abs(to-from)/2f));
        for(int i=0;i<steps;i++)RacingPanelGraphic.Line(vh,Point(c,radius,Mathf.Lerp(from,to,(float)i/steps)),Point(c,radius,Mathf.Lerp(from,to,(float)(i+1)/steps)),width,tint,1f);
    }
    public static Vector2 Point(Vector2 c,float radius,float degrees)
    {float a=degrees*Mathf.Deg2Rad;return c+new Vector2(Mathf.Cos(a),Mathf.Sin(a))*radius;}
}
