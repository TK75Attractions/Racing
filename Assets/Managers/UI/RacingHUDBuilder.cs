using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Approved arcade HUD. The same instruments are reused during practice and countdown.</summary>
public static class RacingHUDBuilder
{
    public static Transform Build(Transform parent,int playerIndex,int totalLaps)
    {
        ArcadeUI.Bind(parent,playerIndex);
        foreach(string name in new[]{"Position","Lap","Time","Speed"}){var old=parent.Find(name);if(old!=null)old.gameObject.SetActive(false);}
        RectTransform root=RacingUITheme.Rect(parent,"ModernHUD",Vector2.zero,Vector2.one);root.SetAsFirstSibling();
        var p=ArcadeUI.For(playerIndex);
        var position=ArcadeUI.Panel(root,"Position",new Vector2(.022f,.815f),new Vector2(.156f,.965f),p.Paper);
        Text(position,"Txt","1",0,.05f,.53f,1,172,p.Ink,true).fontStyle=FontStyles.Italic;
        Text(position,"PlaceUnit","位",.43f,.02f,.64f,.4f,38,p.Ink);
        Text(position,"Field","/ 4",.66f,.45f,1,.90f,50,p.Ink,true);
        var badge=ArcadeUI.Panel(position,"Badge",new Vector2(.66f,.16f),new Vector2(.92f,.40f),p.Primary);
        Text(badge,"Value","P"+(playerIndex+1),0,0,1,1,21,p.Ink,true);
        RacingUITheme.Rule(position,"Underline",Vector2.zero,new Vector2(1,.065f),p.Primary);
        var lap=ArcadeUI.Panel(root,"Lap",new Vector2(.57f,.835f),new Vector2(.702f,.965f),p.Soft);
        Text(lap,"Heading","周回",.12f,.20f,.38f,.88f,22,p.Ink);
        Text(lap,"Txt","1",.40f,.19f,.64f,.93f,78,p.Ink,true);
        Text(lap,"Total","/ "+Mathf.Max(1,totalLaps),.66f,.23f,.96f,.85f,46,p.Ink,true);
        var ticks=RacingUITheme.Rect(lap,"Ticks",new Vector2(.12f,.10f),new Vector2(.85f,.14f));
        RacingUITheme.Rule(ticks,"Left",Vector2.zero,new Vector2(.47f,1),p.Ink);
        Color unlit=p.Ink;unlit.a=.18f;
        RacingUITheme.Rule(ticks,"Right",new Vector2(.53f,0),Vector2.one,unlit);
        var time=ArcadeUI.Panel(root,"Time",new Vector2(.718f,.835f),new Vector2(.92f,.965f),p.Paper);
        Text(time,"Heading","タイム",.08f,.13f,.32f,.87f,22,p.Ink);
        Text(time,"Total","00:00",.32f,.07f,.79f,.94f,73,p.Ink,true);
        Text(time,"TotalFraction",".00",.78f,.10f,.97f,.84f,46,p.Ink,true);
        // The timer adapter still accepts lap time, but it no longer adds a second clock to the HUD.
        Text(time,"Lap","",0,0,0,0,20,p.Ink).gameObject.SetActive(false);
        Text(time,"LapFraction","",0,0,0,0,20,p.Ink).gameObject.SetActive(false);
        var pause=ArcadeUI.Panel(root,"Pause",new Vector2(.934f,.875f),new Vector2(.978f,.957f),p.Paper);
        var pauseFace=pause.GetComponent<ArcadeSurfaceGraphic>();pauseFace.raycastTarget=true;
        var pauseButton=pause.GetComponent<Button>()??pause.gameObject.AddComponent<Button>();pauseButton.targetGraphic=pauseFace;pauseButton.transition=Selectable.Transition.None;pauseButton.onClick.RemoveAllListeners();pauseButton.onClick.AddListener(()=>Gmanager.Control?.OpenInterruptionMenu(playerIndex));
        RacingUITheme.Rule(pause,"LeftBar",new Vector2(.32f,.28f),new Vector2(.43f,.72f),p.Ink);RacingUITheme.Rule(pause,"RightBar",new Vector2(.57f,.28f),new Vector2(.68f,.72f),p.Ink);
        RacingUITheme.Rule(pause,"Accent",Vector2.zero,new Vector2(1,.07f),p.Primary);
        var lapTimes=ArcadeUI.Panel(root,"LapTimes",new Vector2(.718f,.704f),new Vector2(.92f,.825f),p.Ink);
        Text(lapTimes,"Lap1Label","LAP1",.04f,.52f,.30f,.97f,25,p.Paper,true);
        Text(lapTimes,"Lap1Time","00:00.00",.32f,.52f,.96f,.97f,33,p.Secondary,true);
        Text(lapTimes,"Lap2Label","LAP2",.04f,.03f,.30f,.48f,25,p.Paper,true);
        Text(lapTimes,"Lap2Time","00:00.00",.32f,.03f,.96f,.48f,33,p.Paper,true);
        lapTimes.gameObject.SetActive(false);
        var board=RacingUITheme.Rect(root,"Leaderboard",new Vector2(.025f,.55f),new Vector2(.178f,.77f));
        (board.GetComponent<RacingLeaderboardUI>()??board.gameObject.AddComponent<RacingLeaderboardUI>()).Configure(playerIndex);
        BuildDial(root,"Speed",false,new Vector2(.015f,.035f),new Vector2(.29f,.50f),p);
        BuildDial(root,"Tachometer",true,new Vector2(.71f,.035f),new Vector2(.985f,.50f),p);
        var steering=ArcadeUI.Panel(root,"SteeringIndicator",new Vector2(.35f,.035f),new Vector2(.65f,.195f),p.Ink);
        TMP_Text caption=Text(steering,"Angle","0°",.65f,.69f,.95f,.97f,35,p.Paper,true,TextAlignmentOptions.Right);
        Text(steering,"Heading","ハンドル",.05f,.69f,.5f,.97f,21,p.Paper);
        Text(steering,"Left","L",.05f,.05f,.15f,.27f,20,p.Paper,true);
        Text(steering,"Center","0",.45f,.05f,.55f,.27f,20,p.Paper,true);
        Text(steering,"Right","R",.85f,.05f,.95f,.27f,20,p.Paper,true);
        var track=RacingUITheme.Rect(steering,"Track",new Vector2(0,.2f),new Vector2(1,.77f));
        (track.GetComponent<RacingSteeringIndicator>()??track.gameObject.AddComponent<RacingSteeringIndicator>()).Configure(playerIndex,caption);
        var telemetry=root.GetComponent<ArcadeHUDTelemetry>()??root.gameObject.AddComponent<ArcadeHUDTelemetry>();telemetry.Configure(playerIndex);
        IsolateCanvas(root.Find("Speed") as RectTransform);IsolateCanvas(root.Find("Tachometer") as RectTransform);IsolateCanvas(steering);IsolateCanvas(time);
        foreach(string name in new[]{"Position","Lap","Time","Pause","LapTimes","SteeringIndicator","Position/Badge","Tachometer/Boost"})ArcadeUI.HUDShadow(root.Find(name));
        return root;
    }
    private static void BuildDial(Transform root,string name,bool tach,Vector2 min,Vector2 max,ArcadeUI.Palette p)
    {
        var dial=RacingUITheme.Rect(root,name,min,max);
        var face=RacingUITheme.Rect(dial,"Gauge",Vector2.zero,Vector2.one);
        ArcadeDialGraphic graphic=tach?face.GetComponent<ArcadeDialGraphic>()??face.gameObject.AddComponent<ArcadeDialGraphic>():face.GetComponent<RacingSpeedGauge>()??face.gameObject.AddComponent<RacingSpeedGauge>();graphic.Tachometer=tach;
        Text(dial,"Heading",tach?"回転数":"速度",.34f,.65f,.66f,.72f,24,p.Ink);
        var valueLabel=Text(dial,"Txt",tach?"0.9":"0",.275f,.375f,.725f,.64f,130,p.Ink,true);
        valueLabel.font=ArcadeUI.SpeedDigits;valueLabel.fontSharedMaterial=valueLabel.font.material;
        valueLabel.enableAutoSizing=false;valueLabel.overflowMode=TextOverflowModes.Overflow;
        Text(dial,"Unit",tach?"×1000 rpm":"km/h",.29f,.315f,.71f,.385f,24,p.Ink,true);
        float[] values=tach?new[]{0f,2,4,6,8,9}:new[]{0f,60,120,180,240};
        foreach(float value in values){float a=Mathf.Lerp(223.2f,-43.2f,value/(tach?9:240))*Mathf.Deg2Rad;float x=.5f+Mathf.Cos(a)*(tach?.399f:.426f)*.64f,y=.515f+Mathf.Sin(a)*(tach?.399f:.426f)*.64f;Text(dial,"Tick"+value,value.ToString(),x-.045f,y-.03f,x+.045f,y+.03f,20,p.Paper,true);}
        if(tach){var boost=ArcadeUI.Panel(dial,"Boost",new Vector2(.22f,.06f),new Vector2(.78f,.17f),p.Ink);Text(boost,"Value","BOOST  0%",0,0,1,1,29,p.Secondary,true);}
        else Text(dial,"Maximum","MAX 240",.3f,.05f,.7f,.15f,20,p.Paper,true);
    }
    public static TMP_Text Text(Transform parent,string name,string value,float x0,float y0,float x1,float y1,float size,Color tint,bool digits=false,TextAlignmentOptions alignment=TextAlignmentOptions.Center)
        =>ArcadeUI.Label(parent,name,value,new Vector2(x0,y0),new Vector2(x1,y1),size,tint,digits,alignment);
    private static void IsolateCanvas(RectTransform rect){if(rect!=null&&rect.GetComponent<Canvas>()==null)rect.gameObject.AddComponent<Canvas>();}
}
