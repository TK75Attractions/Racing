using UnityEngine;
using UnityEngine.UI;

/// <summary>Meter ticks and forward motion accents shared by boost and item announcements.</summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class ArcadeToastGraphic : MaskableGraphic
{
    private bool fast,warning;
    public void SetStyle(bool speed,bool warn){if(fast==speed&&warning==warn)return;fast=speed;warning=warn;SetVerticesDirty();}
    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();Rect r=rectTransform.rect;var p=ArcadeUI.For(transform);
        Rect shadow=r;shadow.position+=new Vector2(7,-9);ArcadeSurfaceGraphic.Rounded(vh,shadow,p.Ink,12);
        ArcadeSurfaceGraphic.Rounded(vh,r,fast?p.Primary:warning?ArcadeUI.Hex("ffd4a8"):p.Paper,12);
        RacingPanelGraphic.Line(vh,new Vector2(r.xMin+4,r.yMin+2),new Vector2(r.xMax-22,r.yMin+2),8,fast?p.Accent:p.Primary,.5f);
        ArcadeSurfaceGraphic.Disc(vh,new Vector2(r.xMin+r.height*.38f,r.center.y),r.height*.22f,p.Ink);
        for(int i=0;i<3;i++){float x=r.xMax-35-i*23;RacingPanelGraphic.Line(vh,new Vector2(x-10,r.center.y-23),new Vector2(x+12,r.center.y+23),fast?12:6,fast?p.Ink:p.Primary,.5f);}
        if(fast)for(int i=0;i<3;i++){float y=r.yMin+r.height*(.2f+i*.28f);RacingPanelGraphic.Line(vh,new Vector2(r.xMin-30-i*18,y),new Vector2(r.xMin+24,y),5,i==1?p.Accent:p.Soft,.4f);}
        if(fast){UIVertex v=new UIVertex();for(int i=0;i<vh.currentVertCount;i++){vh.PopulateUIVertex(ref v,i);v.position.x+=(v.position.y-r.center.y)*.14f;vh.SetUIVertex(v,i);}}
    }
}
