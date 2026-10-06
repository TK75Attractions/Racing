using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Live instruments arranged like the supplied neon race reference.</summary>
public static class RacingHUDBuilder
{
    public static Transform Build(Transform parent, int playerIndex, int totalLaps)
    {
        foreach(string name in new[]{"Position","Lap","Time","Speed"})
        { Transform legacy=parent.Find(name);if(legacy!=null)legacy.gameObject.SetActive(false); }
        RectTransform root=RacingUITheme.Rect(parent,"ModernHUD",Vector2.zero,Vector2.one);root.SetAsFirstSibling();
        RectTransform overlay=RacingUITheme.Rect(root,"BoostOverlay",Vector2.zero,Vector2.one);overlay.SetAsFirstSibling();
        RacingBoostGraphic screen=overlay.GetComponent<RacingBoostGraphic>() ?? overlay.gameObject.AddComponent<RacingBoostGraphic>();
        screen.Configure(RacingBoostGraphic.DisplayMode.Screen);overlay.gameObject.SetActive(false);

        RectTransform position=RacingHUDStyle.Plate(root,"Position",new Vector2(.02f,.80f),new Vector2(.145f,.95f),RacingHUDPlateGraphic.PlateShape.Position,NeonUI.Cyan);
        Title(position,"順位","POSITION");
        Number(position,"Txt","1",.12f,.05f,.56f,.76f,108f,RacingUITheme.Gold);
        Number(position,"Field","/ 2",.58f,.09f,.90f,.47f,42f,Color.white);
        RacingHUDStyle.Label(position,"PlaceUnit","位",.63f,.48f,.88f,.63f,17f,RacingHUDStyle.Muted);

        RectTransform lap=RacingHUDStyle.Plate(root,"Lap",new Vector2(.152f,.835f),new Vector2(.235f,.95f),RacingHUDPlateGraphic.PlateShape.Lap,NeonUI.Cyan);
        Title(lap,"いまの周","LAP");
        Number(lap,"Txt","1",.12f,.10f,.45f,.70f,70f,Color.white);
        Number(lap,"Total","/ "+Mathf.Max(1,totalLaps),.46f,.12f,.85f,.60f,40f,Color.white);
        RacingHUDStyle.Label(lap,"LapUnit","周",.78f,.05f,.94f,.23f,14f,RacingHUDStyle.Muted);

        RectTransform time=RacingHUDStyle.Plate(root,"Time",new Vector2(.02f,.715f),new Vector2(.235f,.79f),RacingHUDPlateGraphic.PlateShape.Timer,NeonUI.Cyan);
        RacingHUDStyle.Label(time,"Heading","タイム",.06f,.52f,.32f,.94f,18f,Color.white,TextAlignmentOptions.Left);
        RacingHUDStyle.Label(time,"EnglishHeading","TIME",.06f,.16f,.25f,.58f,17f,NeonUI.Cyan);
        Number(time,"Total","00:00",.34f,.35f,.73f,.97f,40f,Color.white);
        Number(time,"TotalFraction",".00",.73f,.39f,.95f,.93f,32f,Color.white);
        RacingHUDStyle.Label(time,"LapHeading","この周",.35f,.05f,.52f,.36f,13f,RacingHUDStyle.Muted);
        RacingHUDStyle.Label(time,"LapEnglish","LAP",.53f,.05f,.62f,.32f,11f,RacingHUDStyle.Muted);
        RacingHUDStyle.Label(time,"Lap","00:00",.64f,.05f,.84f,.33f,17f,Color.white);
        RacingHUDStyle.Label(time,"LapFraction",".00",.84f,.05f,.97f,.33f,14f,RacingHUDStyle.Muted);

        RectTransform board=RacingUITheme.Rect(root,"Leaderboard",new Vector2(.02f,.594f),new Vector2(.235f,.702f));
        RacingLeaderboardUI leaderboard=board.GetComponent<RacingLeaderboardUI>() ?? board.gameObject.AddComponent<RacingLeaderboardUI>();
        leaderboard.Configure(playerIndex);
        IsolateCanvas(board);

        RectTransform speed=RacingHUDStyle.Plate(root,"Speed",new Vector2(.71f,.13f),new Vector2(.925f,.335f),RacingHUDPlateGraphic.PlateShape.Speed,NeonUI.Cyan);
        IsolateCanvas(speed);
        RectTransform gauge=RacingUITheme.Rect(speed,"Gauge",Vector2.zero,Vector2.one);
        if(gauge.GetComponent<RacingSpeedGauge>()==null)gauge.gameObject.AddComponent<RacingSpeedGauge>();
        RacingHUDStyle.Label(speed,"Heading","スピード",.29f,.51f,.72f,.64f,18f,RacingHUDStyle.Muted,TextAlignmentOptions.Center);
        RacingHUDStyle.Label(speed,"EnglishHeading","SPEED",.33f,.43f,.67f,.51f,12f,RacingHUDStyle.Muted,TextAlignmentOptions.Center);
        Number(speed,"Txt","0",.21f,.08f,.78f,.53f,96f,Color.white,TextAlignmentOptions.Center);
        RacingHUDStyle.Label(speed,"Unit","km/h",.32f,.01f,.66f,.12f,22f,Color.white,TextAlignmentOptions.Center);
        for(int i=0;i<=5;i++)
        {
            float a=Mathf.Lerp(180,0,i/5f)*Mathf.Deg2Rad;
            float x=.50f+Mathf.Cos(a)*.35f,y=.035f+Mathf.Sin(a)*.70f;
            Number(speed,"Dial"+i,(i*2).ToString(),x-.055f,y-.04f,x+.055f,y+.05f,19f,Color.white,TextAlignmentOptions.Center);
        }
        RectTransform gear=NeonUI.Panel(speed,"Gear",new Vector2(.72f,.015f),new Vector2(.89f,.21f));
        NeonUI.Text(gear,"Value","D",Vector2.zero,Vector2.one,29f,TextAlignmentOptions.Center,true,NeonUI.Cyan);

        RectTransform boostBadge=RacingUITheme.Rect(root,"BoostBadge",new Vector2(.93f,.13f),new Vector2(.986f,.275f));
        IsolateCanvas(boostBadge);
        NeonRingGraphic badge=boostBadge.GetComponent<NeonRingGraphic>() ?? boostBadge.gameObject.AddComponent<NeonRingGraphic>();badge.display=NeonRingGraphic.Display.Boost;badge.amount=0;
        NeonUI.Icon(boostBadge,"Bolt",RacingIconGraphic.Icon.Bolt,new Vector2(.33f,.52f),new Vector2(.67f,.79f));
        NeonUI.Text(boostBadge,"Name","BOOST",new Vector2(.1f,.33f),new Vector2(.9f,.51f),16f,TextAlignmentOptions.Center,true);
        NeonUI.Text(boostBadge,"Japanese","加速",new Vector2(.16f,.19f),new Vector2(.84f,.33f),14f,TextAlignmentOptions.Center);

        RectTransform boost=RacingHUDStyle.Plate(root,"PadBoost",new Vector2(.71f,.35f),new Vector2(.98f,.405f),RacingHUDPlateGraphic.PlateShape.Boost,NeonUI.Pink);
        RacingHUDStyle.Label(boost,"Heading","加速中",.06f,.33f,.30f,.95f,21f,NeonUI.Pink,bold:true);
        RacingHUDStyle.Label(boost,"EnglishHeading","BOOST",.34f,.42f,.55f,.93f,14f,Color.white);
        RacingHUDStyle.Label(boost,"Remaining","3.0秒",.68f,.35f,.93f,.94f,22f,Color.white,TextAlignmentOptions.Right);
        RacingUITheme.Rule(boost,"Track",new Vector2(.07f,.14f),new Vector2(.93f,.25f),new Color(.07f,.12f,.22f));
        RacingUITheme.Rule(boost,"Glow",new Vector2(.065f,.10f),new Vector2(.935f,.29f),new Color(1,.025f,.39f,.10f));
        RectTransform fill=RacingUITheme.Rect(boost,"Fill",new Vector2(.07f,.14f),new Vector2(.93f,.25f));
        RacingBoostGraphic bar=fill.GetComponent<RacingBoostGraphic>() ?? fill.gameObject.AddComponent<RacingBoostGraphic>();bar.Configure(RacingBoostGraphic.DisplayMode.Bar);boost.gameObject.SetActive(false);
        RectTransform telemetry=NeonUI.Panel(root,"Telemetry",new Vector2(.71f,.045f),new Vector2(.98f,.115f));
        RacingTelemetryUI controls=telemetry.GetComponent<RacingTelemetryUI>() ?? telemetry.gameObject.AddComponent<RacingTelemetryUI>();controls.Configure(playerIndex);
        RacingHUDStyle.Label(root,"Player",$"プレイヤー {playerIndex+1} / PLAYER {playerIndex+1}",.022f,.003f,.32f,.029f,17f,Color.white);
        return root;
    }
    private static void IsolateCanvas(RectTransform instrument)
    {
        // Timer text must not rebatch the thousands of static neon-ring vertices.
        Canvas island = instrument.GetComponent<Canvas>();
        if (island == null) island = instrument.gameObject.AddComponent<Canvas>();
        island.overrideSorting = false;
    }
    private static void Title(Transform p,string japanese,string english)
    {
        RacingHUDStyle.Label(p,"EnglishHeading",english,.11f,.76f,.89f,.96f,23f, new Color(.60f,.85f,1f));
        RacingHUDStyle.Label(p,"Heading",japanese,.53f,.57f,.90f,.76f,16f,RacingHUDStyle.Muted,TextAlignmentOptions.Right);
    }
    private static void Number(Transform p,string name,string text,float x0,float y0,float x1,float y1,float size,Color tint,TextAlignmentOptions align=TextAlignmentOptions.Left)
    { TMP_Text label=RacingHUDStyle.Label(p,name,text,x0,y0,x1,y1,size,tint,align,true);label.fontStyle=FontStyles.Italic;label.fontSizeMin=Mathf.Min(label.fontSizeMin,24f); }
}
