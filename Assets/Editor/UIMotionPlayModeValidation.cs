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
            else if(phase==2&&age>.85)
            {
                Require(covered==1&&completed==1&&!transition.IsTransitioning&&play.gameObject.activeSelf,"Transition completes exactly once at timeScale zero.");
                transition.TryTransitionTo(Gmanager.State.Title,()=>{covered++;transition.ApplyStateImmediate(Gmanager.State.Game);},()=>completed++);Next(21);
            }
            else if(phase==21&&age>.45)
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
    private static void StartAdditionalChecks(){Finish();}
    private static void TickAdditionalChecks(double age){}
    private static void Finish(){Debug.Log("UI_MOTION_PLAYMODE_PASS: actual shutter transition, one-shot callbacks, reentry rejection, cancellation, real OnDisable and unscaled rank playback.");Stop(0);}
    private static void Stop(int code)
    {
        SessionState.SetBool(Active,false);EditorApplication.update-=Tick;Time.timeScale=1;
        if(fixture!=null)UnityEngine.Object.DestroyImmediate(fixture);
        EditorApplication.isPlaying=false;EditorApplication.delayCall+=()=>EditorApplication.Exit(code);
    }
}
#endif
