using TMPro;
using UnityEngine;

/// <summary>Production race instruments with only the active lesson and preparation timer added.</summary>
public sealed class DrivingTutorialUI
{
    public static readonly Color Cyan=ArcadeUI.Blue.Primary, Pink=ArcadeUI.Pink.Primary;
    private readonly GameObject root;
    private readonly TMP_Text message,status,step;
    private readonly PreparationTimerUI timer;
    private readonly UISpeed speed=new UISpeed();
    private readonly ArcadeHUDTelemetry telemetry;
    private readonly RacingSteeringIndicator steering;
    private readonly int player;
    private string shownMessage;
    private int shownStage=-1;
    public DrivingTutorialUI(Transform canvas,int playerIndex)
    {
        player=playerIndex;ArcadeUI.Bind(canvas,player);
        var r=RacingUITheme.Rect(canvas,"DrivingTutorial",Vector2.zero,Vector2.one);root=r.gameObject;r.SetAsFirstSibling();
        Transform hud=RacingHUDBuilder.Build(r,player,2);
        ArcadeUI.Hide(hud,"Position","Lap","Time","LapDelta","Leaderboard","Pause");
        speed.Init(hud.Find("Speed"));telemetry=hud.GetComponent<ArcadeHUDTelemetry>();telemetry.enabled=false;
        steering=hud.Find("SteeringIndicator/Track").GetComponent<RacingSteeringIndicator>();steering.AutoBind=false;
        ArcadeUI.Logo(r,new Vector2(.03f,.84f),new Vector2(.27f,.97f));
        var p=ArcadeUI.For(player);
        var tag=ArcadeUI.Panel(r,"Player",new Vector2(.77f,.89f),new Vector2(.81f,.95f),p.Primary);
        ArcadeUI.Label(tag,"Value","P"+(player+1),Vector2.zero,Vector2.one,24,p.Ink,true,TextAlignmentOptions.Center);
        timer=new PreparationTimerUI(r,new Vector2(.82f,.88f),new Vector2(.97f,.96f));
        var lesson=ArcadeUI.Panel(r,"CurrentInstruction",new Vector2(.34f,.51f),new Vector2(.66f,.765f),p.Ink);
        step=ArcadeUI.Label(lesson,"Step","01",new Vector2(.045f,.28f),new Vector2(.28f,.85f),104,p.Soft,true,TextAlignmentOptions.Center);step.fontStyle=FontStyles.Italic;
        ArcadeUI.Label(lesson,"Total","/ 03",new Vector2(.06f,.13f),new Vector2(.26f,.30f),22,p.Paper,true,TextAlignmentOptions.Center);
        RacingUITheme.Rule(lesson,"Divider",new Vector2(.31f,.16f),new Vector2(.315f,.89f),p.Primary);
        status=ArcadeUI.Label(lesson,"Status","練習しよう",new Vector2(.37f,.75f),new Vector2(.96f,.91f),20,p.Soft);
        message=ArcadeUI.Label(lesson,"Message","",new Vector2(.37f,.21f),new Vector2(.97f,.75f),35,p.Paper);
        message.textWrappingMode=TextWrappingModes.Normal;
        for(int i=0;i<3;i++)RacingUITheme.Rule(lesson,"Stage"+i,new Vector2(.37f+i*.19f,.13f),new Vector2(.54f+i*.19f,.16f),p.Soft);
    }
    public void Update(DrivingTutorialProgress lesson,DriveInputState input,float metersPerSecond,bool connected,string connectionNotice=null)
    {
        timer.Update(lesson.TimeRemaining,lesson.TimeLimitSeconds);speed.UpdateSpeedMeter(metersPerSecond*3.6f,0);
        Transform car=Gmanager.Control?.TutorialCar(player);
        var engine=car!=null?car.GetComponentInChildren<CarSoundController>():null;
        var mover=car!=null?car.GetComponent<DebugMover>():null;
        telemetry.SetTelemetry(engine!=null?engine.CurrentRpm:0,mover!=null?mover.NormalizedDriftCharge:0);
        float allowed=mover!=null?mover.SteeringInputLimit/Mathf.Max(.001f,mover.SteeringInputRange):1;
        float raw=input.steering/90f;steering.SetState(raw,Mathf.Clamp(raw,-allowed,allowed),allowed);
        int stage=lesson.HasTimedOut?4:(int)lesson.CurrentStage;
        string prompt=lesson.HasTimedOut?"時間終了 / 相手の準備待ち\nそろうとレースへ":lesson.IsComplete?"練習完了！\nそろうとレースへ":!connected?connectionNotice??"接続を確認してください":!lesson.PedalReleased?"まずペダルから\n足を離そう":stage==0?"ペダルを踏んで\n加速しよう":stage==1?"ハンドルを回して\n角を曲がってみよう":"光る箱に触れて\nアイテムを取ろう";
        if(shownMessage!=prompt){message.text=prompt;shownMessage=prompt;}
        if(shownStage==stage)return;shownStage=stage;
        status.text=lesson.HasTimedOut?"TIME UP":lesson.IsComplete?"COMPLETE":"練習しよう";
        step.text=lesson.HasTimedOut?"!":lesson.IsComplete?"OK":(stage+1).ToString("00");
    }
    public void Dispose(){if(root!=null){root.SetActive(false);if(Application.isPlaying)Object.Destroy(root);else Object.DestroyImmediate(root);}}
}
