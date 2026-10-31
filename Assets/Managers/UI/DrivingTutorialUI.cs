using TMPro;
using UnityEngine;

/// <summary>Production race instruments with only the active lesson and preparation timer added.</summary>
public sealed class DrivingTutorialUI
{
    public static readonly Color Cyan=ArcadeUI.Blue.Primary, Pink=ArcadeUI.Pink.Primary;
    private readonly GameObject root;
    private readonly ArcadeNoticeRail recoveryNotice;
    private readonly GameObject itemGuide;
    private readonly TutorialGimmickGuideUI gimmickGuide;
    private readonly TMP_Text message,status,step;
    private readonly PreparationTimerUI timer;
    private readonly GameObject preparationTimer;
    private readonly GameObject instruments;
    private readonly GameObject currentInstruction;
    private readonly UISpeed speed=new UISpeed();
    private readonly ArcadeHUDTelemetry telemetry;
    private readonly RacingSteeringIndicator steering;
    private readonly int player;
    private readonly UIValuePulse stepPulse;
    private readonly UIRevealSequence lessonMotion;
    private string shownMessage;
    private int shownStage=-1;
    public DrivingTutorialUI(Transform canvas,int playerIndex)
    {
        player=playerIndex;ArcadeUI.Bind(canvas,player);
        recoveryNotice=ArcadeNoticeRail.Get(canvas);
        var r=RacingUITheme.Rect(canvas,"DrivingTutorial",Vector2.zero,Vector2.one);root=r.gameObject;r.SetAsFirstSibling();
        Transform hud=RacingHUDBuilder.Build(r,player,2);instruments=hud.gameObject;
        ArcadeUI.Hide(hud,"Position","Lap","Time","LapTimes","Leaderboard","Pause");
        speed.Init(hud.Find("Speed"));telemetry=hud.GetComponent<ArcadeHUDTelemetry>();telemetry.enabled=false;
        steering=hud.Find("SteeringIndicator/Track").GetComponent<RacingSteeringIndicator>();steering.AutoBind=false;
        ArcadeUI.Logo(r,new Vector2(.03f,.84f),new Vector2(.27f,.97f));
        var p=ArcadeUI.For(player);
        var mode=ArcadeUI.Panel(r,"TutorialMode",new Vector2(.29f,.90f),new Vector2(.71f,.985f),p.Primary);
        TutorialText.Label(mode,"Label","チュートリアル/練習",new Vector2(.025f,.05f),new Vector2(.975f,.95f),52,p.Ink,true,TextAlignmentOptions.Center);
        var tag=ArcadeUI.Panel(r,"Player",new Vector2(.77f,.89f),new Vector2(.81f,.95f),p.Primary);
        TutorialText.Label(tag,"Value","P"+(player+1),Vector2.zero,Vector2.one,24,p.Ink,true,TextAlignmentOptions.Center);
        timer=new PreparationTimerUI(r,new Vector2(.82f,.88f),new Vector2(.97f,.96f),tutorial:true);
        preparationTimer=r.Find("PreparationTimer").gameObject;
        var lesson=ArcadeUI.Panel(r,"CurrentInstruction",new Vector2(.31f,.68f),new Vector2(.69f,.88f),p.Ink);
        currentInstruction=lesson.gameObject;
        step=TutorialText.Label(lesson,"Step","01",new Vector2(.045f,.28f),new Vector2(.28f,.85f),104,p.Soft,true,TextAlignmentOptions.Center);step.fontStyle=FontStyles.Italic;
        TutorialText.Label(lesson,"Total","/ 03",new Vector2(.06f,.13f),new Vector2(.26f,.30f),22,p.Paper,true,TextAlignmentOptions.Center);
        RacingUITheme.Rule(lesson,"Divider",new Vector2(.31f,.16f),new Vector2(.315f,.89f),p.Primary);
        status=TutorialText.Label(lesson,"Status","{練習|れんしゅう}しよう",new Vector2(.37f,.77f),new Vector2(.96f,.88f),20,p.Soft);
        message=TutorialText.Label(lesson,"Message","",new Vector2(.37f,.17f),new Vector2(.97f,.66f),35,p.Paper);
        message.textWrappingMode=TextWrappingModes.Normal;
        for(int i=0;i<3;i++)RacingUITheme.Rule(lesson,"Stage"+i,new Vector2(.37f+i*.19f,.13f),new Vector2(.54f+i*.19f,.16f),p.Soft);
        message.fontSizeMin=28f;
        itemGuide = ItemGuideUI.Create(r, player);
        itemGuide.SetActive(false);
        gimmickGuide = new TutorialGimmickGuideUI(r, player);
        stepPulse=step.gameObject.AddComponent<UIValuePulse>();lessonMotion=lesson.gameObject.AddComponent<UIRevealSequence>();lessonMotion.Configure(lesson,new[]{"Status","Message"},new Vector2(12,0),.025f,.22f);
    }
    public void Update(DrivingTutorialProgress lesson,DriveInputState input,float metersPerSecond,bool connected,string connectionNotice=null,TutorialGimmickBriefing briefing=null)
    {
        timer.Update(lesson.TimeRemaining,lesson.TimeLimitSeconds);speed.UpdateSpeedMeter(metersPerSecond*3.6f,0);
        Transform car=Gmanager.Control?.TutorialCar(player);
        var engine=car!=null?car.GetComponentInChildren<CarSoundController>():null;
        var mover=car!=null?car.GetComponent<DebugMover>():null;
        telemetry.SetTelemetry(engine!=null?engine.CurrentRpm:0,mover!=null?mover.NormalizedDriftCharge:0);
        float allowed=mover!=null?mover.SteeringInputLimit/Mathf.Max(.001f,mover.SteeringInputRange):1;
        float raw=input.steering/90f;steering.SetState(raw,Mathf.Clamp(raw,-allowed,allowed),allowed);
        bool explaining = lesson.IsFinished && briefing != null && !briefing.IsComplete;
        instruments.SetActive(!(lesson.IsFinished && briefing != null));
        preparationTimer.SetActive(!lesson.IsFinished);
        currentInstruction.SetActive(!lesson.IsFinished);
        itemGuide.SetActive(connected && lesson.IsFinished && briefing == null);
        gimmickGuide.Update(lesson.IsFinished ? briefing : null, connected);
        int stage=lesson.HasTimedOut?4:(int)lesson.CurrentStage;
        string prompt=explaining?"しかけを{見|み}よう":lesson.IsFinished?"もうひとりを\nまとう":!connected?"{係員|かかりいん}を\nよんでね":stage==0?"ペダルを\nふもう":stage==1?"{道|みち}にそって\n{曲|ま}がろう":"{光|ひか}る{箱|はこ}に\nふれよう";
        if(shownMessage!=prompt){TutorialText.Set(message,prompt);shownMessage=prompt;}
        string heading=explaining?"とまって{読|よ}もう":lesson.HasTimedOut?"{練習|れんしゅう}おわり":lesson.IsComplete?"できた！":!connected?"{接続|せつぞく}まち":"{練習|れんしゅう}しよう";
        if(status.text!=heading)TutorialText.Set(status,heading);
        if(shownStage==stage)return;bool changed=shownStage>=0;shownStage=stage;
        if(changed&&Application.isPlaying){stepPulse.Play(ArcadeUI.For(player).Accent,0);lessonMotion.Play();}
        step.text=lesson.HasTimedOut?"!":lesson.IsComplete?"OK":(stage+1).ToString("00");
        var palette=ArcadeUI.For(player);
        for(int i=0;i<3;i++)
        {
            var bar=step.transform.parent.Find("Stage"+i).GetComponent<UnityEngine.UI.Image>();
            bar.color=i<(int)lesson.CurrentStage?palette.Primary:i==stage?palette.Paper:new Color(palette.Soft.r,palette.Soft.g,palette.Soft.b,.3f);
        }
    }
    public void SetRecoveryNotice(string text)=>recoveryNotice.Set(ArcadeNoticeRail.SafetyInstruction,text);
    public void Dispose(){recoveryNotice.Set(ArcadeNoticeRail.SafetyInstruction,"");if(root!=null){root.SetActive(false);if(Application.isPlaying)Object.Destroy(root);else Object.DestroyImmediate(root);}}
}
