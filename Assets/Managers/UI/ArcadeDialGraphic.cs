using UnityEngine;
using UnityEngine.UI;

/// <summary>270-degree segmented instrument, with a separate outer boost track on the tachometer.</summary>
[RequireComponent(typeof(CanvasRenderer))]
public class ArcadeDialGraphic : MaskableGraphic
{
    private static readonly Color Mint=ArcadeUI.Hex("67d8c5"),Amber=ArcadeUI.Hex("f5d474"),Hot=ArcadeUI.Hex("ff682f"),
        Redline=ArcadeUI.Hex("ff5b21"),DimRedline=ArcadeUI.Hex("d95724"),Brake=ArcadeUI.Hex("ff876e");
    public bool Tachometer { get; set; }
    private int segments, boostSegments, needleStep=-1, motionStep, flashStep;
    private float targetReading,targetBoost,boostFlash;
    private bool hasReading,hasBoost;
    public bool IsAnimating=>Reading!=targetReading||Boost!=targetBoost||boostFlash>0;
    private float Maximum=>Tachometer?9000:240;
    private float Clean(float value)=>float.IsNaN(value)||float.IsInfinity(value)?0:Mathf.Clamp(value,0,Maximum);
    public float Reading { get; private set; }
    public Color NeedleColor=>Tachometer?ArcadeUI.For(transform).Paper:SpeedColor(Reading,ArcadeUI.For(transform));
    public static Color SpeedColor(float speed,ArcadeUI.Palette palette)
    {
        float t=Mathf.Clamp01(speed/240f);
        if(t<.45f)return Color.Lerp(palette.Primary,Mint,t/.45f);
        if(t<.75f)return Color.Lerp(Mint,Amber,(t-.45f)/.30f);
        return Color.Lerp(Amber,Hot,(t-.75f)/.25f);
    }
    public float Boost { get; private set; }
    public void SetReading(float value)
    {
        targetReading=Clean(value);hasReading=true;
        if(motionStep!=0){motionStep=0;SetVerticesDirty();}
        ApplyReading(targetReading);
    }
    public void SetTargetReading(float value,bool animate=true)
    {
        if(!animate||!hasReading||!isActiveAndEnabled){SetReading(value);return;}
        targetReading=Clean(value);
    }
    private void ApplyReading(float value)
    {
        Reading=value;
        float fraction=Reading/Maximum;
        int next=Mathf.RoundToInt(fraction*48),needle=Mathf.RoundToInt(fraction*270);
        if(segments==next&&needleStep==needle)return;segments=next;needleStep=needle;SetVerticesDirty();
    }
    public void SetBoost(float fraction)
    {
        targetBoost=float.IsNaN(fraction)||float.IsInfinity(fraction)?0:Mathf.Clamp01(fraction);hasBoost=true;ApplyBoost(targetBoost);
    }
    public void SetTargetBoost(float value,bool animate=true)
    {
        if(!animate||!hasBoost||!isActiveAndEnabled){SetBoost(value);return;}
        targetBoost=float.IsNaN(value)||float.IsInfinity(value)?0:Mathf.Clamp01(value);
    }
    private void ApplyBoost(float fraction)
    {
        Boost=fraction;int next=Mathf.RoundToInt(Boost*40);
        if(next==boostSegments)return;boostSegments=next;SetVerticesDirty();
    }
    public void SnapNextSample(){hasReading=hasBoost=false;boostFlash=0;if(motionStep!=0||flashStep!=0)SetVerticesDirty();motionStep=flashStep=0;}
    public void FlashBoost(){boostFlash=.3f;}
    public void Tick(float dt)
    {
        if(!IsAnimating||dt<=0)return;
        ApplyReading(UIMotion.Follow(Reading,targetReading,dt,Tachometer?.045f:.065f,.01f));
        ApplyBoost(UIMotion.Follow(Boost,targetBoost,dt,.045f,.001f));
        boostFlash=Mathf.Max(0,boostFlash-dt);
        int next=Mathf.RoundToInt(Mathf.Clamp((targetReading-Reading)/(Maximum*.09f),-1,1)*12);
        int flash=Mathf.RoundToInt(Mathf.Sin(boostFlash/.3f*Mathf.PI)*12);
        if(motionStep!=next||flashStep!=flash){motionStep=next;flashStep=flash;SetVerticesDirty();}
    }
    private void Update()=>Tick(UIMotion.DeltaTime);
    protected override void OnEnable(){base.OnEnable();raycastTarget=false;SnapNextSample();}
    protected override void OnDisable(){base.OnDisable();SnapNextSample();}
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
            Color tint=i<segments?(red?Redline:Tachometer?p.Primary:SpeedColor((i+.5f)/48*240,p)):(red?DimRedline:new Color(.58f,.67f,.73f,.44f));
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
        if(motionStep!=0)
        {
            Color trail=motionStep>0?p.Secondary:Brake;trail.a=.48f;
            ArcadeSurfaceGraphic.Arc(vh,c,radius*1.04f,needle,needle+motionStep*1.3f,4,trail);
        }
        RacingPanelGraphic.Line(vh,ArcadeSurfaceGraphic.Point(c,radius*.75f,needle),ArcadeSurfaceGraphic.Point(c,radius*1.075f,needle),9,p.Ink,.5f);
        RacingPanelGraphic.Line(vh,ArcadeSurfaceGraphic.Point(c,radius*.76f,needle),ArcadeSurfaceGraphic.Point(c,radius*1.065f,needle),5,NeedleColor,1f);
        if(!Tachometer)return;
        ArcadeSurfaceGraphic.Arc(vh,c,radius*1.14f,223.2f,-43.2f,12,p.Ink);
        for(int i=0;i<40;i++){float a=Mathf.Lerp(223.2f,-43.2f,i/40f);ArcadeSurfaceGraphic.Arc(vh,c,radius*1.14f,a,a-266.4f/40*.74f,9+flashStep*.22f,i<boostSegments?Color.Lerp(p.Secondary,p.Paper,flashStep/24f):new Color(.51f,.57f,.61f,.44f));}
    }
}
