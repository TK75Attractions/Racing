using UnityEngine;
using UnityEngine.UI;

/// <summary>270-degree segmented instrument, with a separate outer boost track on the tachometer.</summary>
[RequireComponent(typeof(CanvasRenderer))]
public class ArcadeDialGraphic : MaskableGraphic
{
    public bool Tachometer { get; set; }
    private int segments, boostSegments, needleStep=-1;
    public float Reading { get; private set; }
    public float Boost { get; private set; }
    public void SetReading(float value)
    {
        Reading=float.IsNaN(value)||float.IsInfinity(value)?0:Mathf.Clamp(value,0,Tachometer?9000:240);
        float fraction=Reading/(Tachometer?9000:240);
        int next=Mathf.RoundToInt(fraction*48),needle=Mathf.RoundToInt(fraction*270);
        if(segments==next&&needleStep==needle)return;segments=next;needleStep=needle;SetVerticesDirty();
    }
    public void SetBoost(float fraction)
    {
        Boost=Mathf.Clamp01(fraction);int next=Mathf.RoundToInt(Boost*40);
        if(next==boostSegments)return;boostSegments=next;SetVerticesDirty();
    }
    protected override void OnEnable(){base.OnEnable();raycastTarget=false;}
    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();Rect r=rectTransform.rect;if(r.width<2||r.height<2)return;
        var p=ArcadeUI.For(transform);Vector2 c=new Vector2(r.center.x,r.yMin+r.height*.515f);float radius=r.width*(Tachometer?.399f:.426f);
        ArcadeSurfaceGraphic.Arc(vh,c,radius*.89f,225,-45,radius*.38f,p.Ink);
        for(int i=0;i<48;i++)
        {
            bool red=Tachometer&&(i+.5f)/48>=7500f/9000;
            Color tint=i<segments?(red?ArcadeUI.Hex("ff876e"):p.Primary):(red?ArcadeUI.Hex("b6656f"):new Color(.58f,.67f,.73f,.44f));
            float a=Mathf.Lerp(223.2f,-43.2f,i/48f);ArcadeSurfaceGraphic.Arc(vh,c,radius*.98f,a,a-266.4f/48*.72f,Mathf.Max(8,r.width*.024f),tint);
        }
        for(int i=0;i<=48;i++)
        {
            float a=Mathf.Lerp(223.2f,-43.2f,i/48f);bool major=i%12==0;
            RacingPanelGraphic.Line(vh,ArcadeSurfaceGraphic.Point(c,radius*(major?.74f:.81f),a),ArcadeSurfaceGraphic.Point(c,radius*.89f,a),major?3:1.5f,p.Paper,.3f);
        }
        ArcadeSurfaceGraphic.Disc(vh,c+new Vector2(3,-4),radius*.50f,p.Ink);
        ArcadeSurfaceGraphic.Disc(vh,c,radius*.50f,p.Paper);
        float needle=Mathf.Lerp(223.2f,-43.2f,Mathf.Max(0,needleStep)/270f);
        RacingPanelGraphic.Line(vh,ArcadeSurfaceGraphic.Point(c,radius*.75f,needle),ArcadeSurfaceGraphic.Point(c,radius*1.075f,needle),9,p.Ink,.5f);
        RacingPanelGraphic.Line(vh,ArcadeSurfaceGraphic.Point(c,radius*.76f,needle),ArcadeSurfaceGraphic.Point(c,radius*1.065f,needle),4,p.Paper,.5f);
        if(!Tachometer)return;
        ArcadeSurfaceGraphic.Arc(vh,c,radius*1.14f,223.2f,-43.2f,8,p.Ink);
        for(int i=0;i<40;i++){float a=Mathf.Lerp(223.2f,-43.2f,i/40f);ArcadeSurfaceGraphic.Arc(vh,c,radius*1.14f,a,a-266.4f/40*.74f,5,i<boostSegments?p.Secondary:new Color(.51f,.57f,.61f,.44f));}
    }
}
