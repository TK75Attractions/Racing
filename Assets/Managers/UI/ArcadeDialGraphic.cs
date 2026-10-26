using UnityEngine;
using UnityEngine.UI;

/// <summary>270-degree segmented instrument, with a separate outer boost track on the tachometer.</summary>
[RequireComponent(typeof(CanvasRenderer))]
public class ArcadeDialGraphic : MaskableGraphic
{
    public bool Tachometer { get; set; }
    private int segments, boostSegments, needleStep=-1;
    public float Reading { get; private set; }
    public Color NeedleColor=>Tachometer?ArcadeUI.For(transform).Paper:SpeedColor(Reading,ArcadeUI.For(transform));
    public static Color SpeedColor(float speed,ArcadeUI.Palette palette)
    {
        float t=Mathf.Clamp01(speed/240f);
        if(t<.45f)return Color.Lerp(palette.Primary,ArcadeUI.Hex("67d8c5"),t/.45f);
        if(t<.75f)return Color.Lerp(ArcadeUI.Hex("67d8c5"),ArcadeUI.Hex("f5d474"),(t-.45f)/.30f);
        return Color.Lerp(ArcadeUI.Hex("f5d474"),ArcadeUI.Hex("ff682f"),(t-.75f)/.25f);
    }
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
        Vector2 shadow=c+new Vector2(7,-7);Color shade=Color.Lerp(p.Ink,Color.black,.38f);
        ArcadeSurfaceGraphic.Arc(vh,shadow,radius*.89f,225,-45,radius*.38f,shade);
        ArcadeSurfaceGraphic.Arc(vh,c,radius*.89f,225,-45,radius*.38f,p.Ink);
        for(int i=0;i<48;i++)
        {
            bool red=Tachometer&&(i+.5f)/48>=7500f/9000;
            Color tint=i<segments?(red?ArcadeUI.Hex("ff5b21"):Tachometer?p.Primary:SpeedColor((i+.5f)/48*240,p)):(red?ArcadeUI.Hex("d95724"):new Color(.58f,.67f,.73f,.44f));
            float a=Mathf.Lerp(223.2f,-43.2f,i/48f);ArcadeSurfaceGraphic.Arc(vh,c,radius*.98f,a,a-266.4f/48*.72f,Mathf.Max(12,r.width*.041f),tint);
            if(red)
            {
                // A separate orange band and pale hatch remain distinct from P2 pink.
                RacingPanelGraphic.Line(vh,ArcadeSurfaceGraphic.Point(c,radius*.947f,a-1),ArcadeSurfaceGraphic.Point(c,radius*1.018f,a-2.8f),2,p.Paper,1);
            }
        }
        for(int i=0;i<=48;i++)
        {
            float a=Mathf.Lerp(223.2f,-43.2f,i/48f);bool major=i%12==0;
            RacingPanelGraphic.Line(vh,ArcadeSurfaceGraphic.Point(c,radius*(major?.82f:.85f),a),ArcadeSurfaceGraphic.Point(c,radius*.89f,a),major?2:1f,p.Paper,1f);
        }
        ArcadeSurfaceGraphic.Disc(vh,c+new Vector2(7,-7),radius*.50f,shade);
        ArcadeSurfaceGraphic.Disc(vh,c,radius*.50f,p.Paper);
        float needle=Mathf.Lerp(223.2f,-43.2f,Mathf.Max(0,needleStep)/270f);
        RacingPanelGraphic.Line(vh,ArcadeSurfaceGraphic.Point(c,radius*.75f,needle),ArcadeSurfaceGraphic.Point(c,radius*1.075f,needle),9,p.Ink,.5f);
        RacingPanelGraphic.Line(vh,ArcadeSurfaceGraphic.Point(c,radius*.76f,needle),ArcadeSurfaceGraphic.Point(c,radius*1.065f,needle),5,NeedleColor,1f);
        if(!Tachometer)return;
        ArcadeSurfaceGraphic.Arc(vh,c,radius*1.14f,223.2f,-43.2f,12,p.Ink);
        for(int i=0;i<40;i++){float a=Mathf.Lerp(223.2f,-43.2f,i/40f);ArcadeSurfaceGraphic.Arc(vh,c,radius*1.14f,a,a-266.4f/40*.74f,9,i<boostSegments?p.Secondary:new Color(.51f,.57f,.61f,.44f));}
    }
}
