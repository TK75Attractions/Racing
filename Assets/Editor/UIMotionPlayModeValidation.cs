#if UNITY_EDITOR
using System;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Real player-loop probes, including unscaled playback and cancelled callbacks.</summary>
public static class UIMotionPlayModeValidation
{
    private const string Active="Racing.MotionProbe.Active";
    private static int phase,covered,completed;
    private static double started,phaseAt;
    private static ScreenTransitionController transition;
    private static GameObject fixture;
    private static UIPosition position;
    private static UIValuePulse rank;
    private static RectTransform play;
    private static bool all;
    private static int oldGoal,newGoal,buttonClicks,resultShown;
    private static RacingMenuButton button;
    private static Vector2 resultRankPosition;
    private static Vector3 resultRankScale;
    public static void RunScreens()=>Start(false);
    public static void RunAll()=>Start(true);
    private static void Start(bool everything)
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);
        SessionState.SetBool(Active,true);SessionState.SetBool(Active+".All",everything);EditorApplication.isPlaying=true;
    }
    [InitializeOnLoadMethod]
    private static void Resume()
    {
        if(!SessionState.GetBool(Active,false))return;
        phase=covered=completed=0;started=EditorApplication.timeSinceStartup;all=SessionState.GetBool(Active+".All",false);
        EditorApplication.update-=Tick;EditorApplication.update+=Tick;
    }
    private static void Next(int value){phase=value;phaseAt=EditorApplication.timeSinceStartup;}
    private static void Require(bool value,string message)=>UIMotionValidation.Require(value,message);
    private static void Tick()
    {
        if(!EditorApplication.isPlaying)return;
        try
        {
            if(EditorApplication.timeSinceStartup-started>40)throw new InvalidOperationException("Motion probe timed out in phase "+phase);
            double age=EditorApplication.timeSinceStartup-phaseAt;
            if(phase==0)
            {
                Time.timeScale=0;
                fixture=new GameObject("Motion player loop QA",typeof(RectTransform),typeof(Canvas));ArcadeUI.Bind(fixture.transform,0);
                var title=RacingUITheme.Rect(fixture.transform,"Title",Vector2.zero,Vector2.one);
                play=RacingUITheme.Rect(fixture.transform,"OnPlay",Vector2.zero,Vector2.one);var hud=RacingHUDBuilder.Build(play,0,2);
                position=new UIPosition();position.Init(hud.Find("Position"));rank=hud.Find("Position/Txt").GetComponent<UIValuePulse>();
                var result=RacingUITheme.Rect(fixture.transform,"Result",Vector2.zero,Vector2.one);var results=new ResultUIManager();results.Init(result,1);
                results.ShowResults(new RaceResultRecord{playerNumber=1,finishPosition=1,didFinish=true,totalRaceTime=92.461f});
                var resultCard=result.Find("ResultPresentation/ResultCard");resultCard.GetComponent<UIRevealSequence>().Cancel();
                var resultRank=(RectTransform)resultCard.Find("Finish/Rank");resultRankPosition=resultRank.anchoredPosition;resultRankScale=resultRank.localScale;
                transition=fixture.AddComponent<ScreenTransitionController>();transition.Initialize(title,play,result,"","",0);transition.ApplyStateImmediate(Gmanager.State.Title);
                Next(90);
            }
            else if(phase==90&&age>.4)
            {
                Require(transition.TryTransitionTo(Gmanager.State.Countdown,()=>covered++,()=>completed++),"First transition starts.");
                Require(!transition.TryTransitionTo(Gmanager.State.Title,null),"A simultaneous transition must be rejected.");
                fixture.transform.Find("ScreenFade/RacingShutters").GetComponent<RacingTransitionGraphic>().SetCoverage(.4f,false);
                transition.ApplyStateImmediate(Gmanager.State.Title);
                Require(!transition.IsTransitioning&&covered==0&&completed==0,"Cancelling before cover must drop both callbacks.");
                var fade=fixture.transform.Find("ScreenFade").GetComponent<CanvasGroup>();Require(fade.alpha==0&&!fade.blocksRaycasts,"Cancellation must clear the screen and input blocker.");
                transition.TryTransitionTo(Gmanager.State.Countdown,()=>covered++,()=>completed++);Next(2);
            }
            else if(phase==2&&age>.35)
            {
                var wipe=fixture.transform.Find("ScreenFade/RacingShutters").GetComponent<RacingTransitionGraphic>();
                Require(transition.IsTransitioning&&covered==0&&completed==0&&wipe.Coverage>0&&wipe.Coverage<1,"The shutter must still visibly travel before the slower cover completes.");Next(22);
            }
            else if(phase==22&&age>.95)
            {
                Require(covered==1&&completed==1&&!transition.IsTransitioning&&play.gameObject.activeSelf,"Transition completes exactly once at timeScale zero.");
                transition.TryTransitionTo(Gmanager.State.Title,()=>{covered++;transition.ApplyStateImmediate(Gmanager.State.Game);},()=>completed++);Next(21);
            }
            else if(phase==21&&age>.70)
            {
                Require(covered==2&&completed==1&&!transition.IsTransitioning&&play.gameObject.activeSelf,"A cover callback may replace the screen without executing the old reveal/completion.");
                position.SetPosition(3);Next(3);
            }
            else if(phase==3&&age>.08)
            {
                position.SetPosition(1);play.gameObject.SetActive(false);
                Require(!rank.IsPlaying&&rank.transform.localScale==Vector3.one,"Actual OnDisable must restore the rank impulse.");
                play.gameObject.SetActive(true);position.SetPosition(2);Next(4);
            }
            else if(phase==4&&age>.5)
            {
                Require(!rank.IsPlaying&&rank.transform.localScale==Vector3.one,"Rank motion finishes while gameplay time is paused.");
                if(!all){Finish();return;}
                StartAdditionalChecks();
            }
            else if(all)TickAdditionalChecks(age);
        }
        catch(Exception ex){Debug.LogException(ex);Stop(1);}
    }
    private static void StartAdditionalChecks()
    {
        oldGoal=newGoal=buttonClicks=0;
        var rail=ArcadeNoticeRail.Get(fixture.transform);rail.Set(0,"ドリフト加速！");
        var notice=fixture.transform.Find("QuietRaceNotice");var noticeMotion=notice.GetComponent<UIRevealSequence>();noticeMotion.Tick(.08f);
        float visible=notice.Find("Text").GetComponent<CanvasGroup>().alpha;rail.Set(0,"ドリフト加速");
        Require(notice.Find("Text").GetComponent<CanvasGroup>().alpha==visible,"Changing numbers/copy from the same source must not restart a notice fade.");
        Require(transition.TryPlayGoal("P1 / 1位","01:32.461",1.2f,()=>oldGoal++),"Goal starts.");Next(5);
    }
    private static void TickAdditionalChecks(double age)
    {
        if(phase==5&&age>.20)
        {
            Require(transition.TryPlayGoal("P2 / 2位","01:33.000",1.2f,()=>newGoal++),"A close finish replaces the goal motion.");Next(6);
        }
        else if(phase==6&&age>1.55)
        {
            Require(oldGoal==0&&newGoal==1&&!transition.IsTransitioning,"Only the replacement goal callback may run.");
            transition.ApplyStateImmediate(Gmanager.State.Game);
            button=NeonUI.Button(fixture.transform,"ProbeButton","決定","",RacingIconGraphic.Icon.Flag,Vector2.zero,Vector2.one,true,()=>buttonClicks++);button.PlayConfirmationSound=false;
            var pointer=new UnityEngine.EventSystems.PointerEventData(null){button=UnityEngine.EventSystems.PointerEventData.InputButton.Left};
            button.OnPointerClick(pointer);button.gameObject.SetActive(false);button.gameObject.SetActive(true);Next(7);
        }
        else if(phase==7&&age>.2)
        {
            Require(buttonClicks==0,"Real OnDisable must discard a pending confirmation.");
            var pointer=new UnityEngine.EventSystems.PointerEventData(null){button=UnityEngine.EventSystems.PointerEventData.InputButton.Left};button.OnPointerClick(pointer);button.OnPointerClick(pointer);Next(8);
        }
        else if(phase==8&&age>.3)
        {
            Require(buttonClicks==1,"Repeated confirmation must run once at timeScale zero.");
            transition.ShowCountdown(3);transition.ShowCountdown(3);transition.ShowCountdown(2);Next(9);
        }
        else if(phase==9&&age>.5)
        {
            transition.ClearRaceStatus();var counter=play.Find("CountdownStatus/RaceStatus");
            Require(counter.localScale==Vector3.one&&!counter.GetComponent<UIValuePulse>().IsPlaying,"Countdown cancellation must restore the number.");
            resultShown=0;Require(transition.TryShowResultAnimated(()=>resultShown++),"Result reveal starts.");Next(10);
        }
        else if(phase==10&&age>1.20)
        {
            var card=fixture.transform.Find("Result/ResultPresentation/ResultCard");
            Require(resultShown==1&&!transition.IsTransitioning&&card.Find("Classification/ResultRow4").GetComponent<CanvasGroup>().alpha==1,"All result rows must finish once while time is paused.");
            RequireResultRankPose(card);
            Require(transition.TryCloseResultAndTransition(Gmanager.State.Title,()=>covered++,()=>completed++),"Result exit starts.");
            transition.ApplyStateImmediate(Gmanager.State.Result);Next(11);
        }
        else if(phase==11&&age>1.25)
        {
            Require(covered==2&&completed==1&&!transition.IsTransitioning,"Cancelling result exit must discard stale callbacks.");
            var card=fixture.transform.Find("Result/ResultPresentation/ResultCard");
            Require(card.Find("ResultMenu").GetComponent<CanvasGroup>().alpha==1&&((RectTransform)card).anchoredPosition==Vector2.zero,"Cancelled result exit leaves menus visible and panels correctly placed.");
            transition.TryShowResultAnimated(()=>resultShown++);Next(12);
        }
        else if(phase==12&&age>1.20)
        {
            Require(resultShown==2&&!transition.IsTransitioning,"Repeated result entry must complete without drift.");
            RequireResultRankPose(fixture.transform.Find("Result/ResultPresentation/ResultCard"));Finish();
        }
    }
    private static void RequireResultRankPose(Transform card)
    {
        var number=(RectTransform)card.Find("Finish/Rank");
        Require(number.anchoredPosition==resultRankPosition&&number.localScale==resultRankScale,"First and repeated result rank impulses must settle to the original layout, not the reveal's starting pose.");
    }
    private static void Finish(){Debug.Log("UI_MOTION_PLAYMODE_PASS: actual shutter transition, one-shot callbacks, reentry rejection, cancellation, real OnDisable and unscaled rank playback; goal replacement, pending-button disable, repeated confirmation and countdown reset, staged result completion/replay and interrupted result exit when running RunAll.");Stop(0);}
    private static void Stop(int code)
    {
        SessionState.SetBool(Active,false);EditorApplication.update-=Tick;Time.timeScale=1;
        if(fixture!=null)UnityEngine.Object.DestroyImmediate(fixture);
        EditorApplication.isPlaying=false;EditorApplication.delayCall+=()=>EditorApplication.Exit(code);
    }
}
#endif
