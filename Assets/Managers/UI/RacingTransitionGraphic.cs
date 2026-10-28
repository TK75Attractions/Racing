using UnityEngine;
using UnityEngine.UI;

/// <summary>Five slanted racing shutters. Coverage is geometric; the scene never depends on a black alpha fade.</summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class RacingTransitionGraphic : MaskableGraphic
{
    public float Coverage { get; private set; }
    private bool revealing;
    public void SetCoverage(float value,bool reveal)
    {
        value=Mathf.Clamp01(value);if(Coverage==value&&revealing==reveal)return;
        Coverage=value;revealing=reveal;SetVerticesDirty();
    }
    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();if(Coverage<=0)return;
        Rect r=rectTransform.rect;var p=ArcadeUI.For(transform);
        // Only travel far enough beyond the edge to hide the slant. The old wide
        // margin and ease-out spent most of the duration off screen or fully covered.
        float margin=r.height*.16f;
        for(int i=0;i<5;i++)
        {
            float phase=revealing?1-Coverage:Coverage;
            float t=UIMotion.InOut((phase-i*.045f)/.82f);
            float y0=r.yMin+r.height*i/5f,y1=r.yMin+r.height*(i+1)/5f;
            float edge=Mathf.Lerp(r.xMin-margin,r.xMax+margin,t);
            float bottom=edge+(y0-r.center.y)*.25f,top=edge+(y1-r.center.y)*.25f;
            Color ink=i%2==0?p.Ink:Color.Lerp(p.Ink,Color.black,.12f);
            Quad(vh,r,y0,y1,revealing?bottom:r.xMin,revealing?r.xMax:bottom,revealing?top:r.xMin,revealing?r.xMax:top,ink);
            if(Coverage<1)
            {
                Quad(vh,r,y0,y1,bottom-18,bottom-4,top-18,top-4,p.Primary);
                Quad(vh,r,y0,y1,bottom-27,bottom-23,top-27,top-23,p.Paper);
            }
        }
    }
    private static void Quad(VertexHelper vh,Rect clip,float y0,float y1,float l0,float r0,float l1,float r1,Color tint)
    {
        l0=Mathf.Clamp(l0,clip.xMin,clip.xMax);r0=Mathf.Clamp(r0,clip.xMin,clip.xMax);
        l1=Mathf.Clamp(l1,clip.xMin,clip.xMax);r1=Mathf.Clamp(r1,clip.xMin,clip.xMax);
        int first=vh.currentVertCount;
        vh.AddVert(new Vector3(l0,y0),tint,Vector2.zero);vh.AddVert(new Vector3(l1,y1),tint,Vector2.up);
        vh.AddVert(new Vector3(r1,y1),tint,Vector2.one);vh.AddVert(new Vector3(r0,y0),tint,Vector2.right);
        vh.AddTriangle(first,first+1,first+2);vh.AddTriangle(first,first+2,first+3);
    }
    protected override void OnEnable(){base.OnEnable();raycastTarget=false;}
}
