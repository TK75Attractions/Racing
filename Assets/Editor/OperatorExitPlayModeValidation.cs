#if UNITY_EDITOR
using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Real result/thank-you transitions, independent service sheets and cancellation.</summary>
[InitializeOnLoad]
public static class OperatorExitPlayModeValidation
{
    private const string Key="Racing.OperatorExit.Validation";
    private const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    private static int stage;private static double began;
    static OperatorExitPlayModeValidation(){EditorApplication.update+=Tick;}
    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        var input=UnityEngine.Object.FindFirstObjectByType<InputManager>();input.isDebugMode=true;input.isP1SerialP2KeyboardDebugMode=false;
        stage=0;began=0;SessionState.SetBool(Key,true);EditorApplication.isPlaying=true;
    }
    private static void Tick()
    {
        if(!SessionState.GetBool(Key,false)||!EditorApplication.isPlaying||Gmanager.Control==null)return;
        try
        {
            if(began==0)began=EditorApplication.timeSinceStartup;
            Require(EditorApplication.timeSinceStartup-began<120,"Exit validation timeout at stage "+stage);
            var race=Gmanager.Control;
            foreach(var publisher in UnityEngine.Object.FindObjectsByType<RaceRemoteFirebasePublisher>(FindObjectsSortMode.None))publisher.enabled=false;
            if(stage<=2)
                foreach(var transition in UnityEngine.Object.FindObjectsByType<ScreenTransitionController>(FindObjectsSortMode.None))if(transition.IsTransitioning)return;
            if(stage==0 && Time.frameCount>12)
            {
                race.Maintenance.SetActive(false,persist:false);
                race.Features.SetValidationFeature(RaceFeature.FinishRun,false);race.Features.SelectPreset(RacePreset.Quick);
                race.StartGame();stage++;
            }
            else if(stage==1 && race.state==Gmanager.State.Game)
            {
                Require(race.BeginUpdateExit()&&!race.UpdateExitReady&&!race.IsMaintenanceActive,"Build completion must keep the race playing.");
                Call(race,"AdvanceDebugLap",0,false);stage++;
            }
            else if(stage==2 && Field<bool>(Player(race,0),"resultPresented"))
            {
                race.SelectResultOption(0,0);
                Require(!race.IsPlayerWaitingForUpdate(0)&&race.IsPlayerStillRacing(1),"P1's completed result still spectates P2.");
                Call(race,"AdvanceDebugLap",1,false);stage++;
            }
            else if(stage==3 && Ending(race,0).IsVisible)
            {
                Require(!race.UpdateExitReady,"Thank-you presentation cannot be skipped by the updater.");
                Ending(race,0).TickTime(ArcadeEndingUI.AutoReturnSeconds);stage++;
            }
            else if(stage==4 && race.IsPlayerWaitingForUpdate(0))
            {
                Require(!race.UpdateExitReady&&!race.IsPlayerWaitingForUpdate(1)&&Time.timeScale>0,"P1 maintenance must not pause P2's presentation.");
                Require(Screen(race,0).activeSelf&&!Screen(race,1).activeSelf,"Only P1's service sheet is visible.");
                if(!Field<bool>(Player(race,1),"resultPresented") || Rig(race,1).CanvasRoot.GetComponent<ScreenTransitionController>()?.IsTransitioning==true)return;
                race.SelectResultOption(1,0);
                if(!Ending(race,1).IsVisible)return;
                var state=race.state;race.RetryGame();Require(race.state==state,"A new race cannot bypass the pending update.");
                Ending(race,1).TickTime(ArcadeEndingUI.AutoReturnSeconds);stage++;
            }
            else if(stage==5 && race.UpdateExitReady)
            {
                Require(Screen(race,0).activeSelf&&Screen(race,1).activeSelf,"Both service sheets precede readiness.");
                var sheet=race.Broadcast.PresentationCanvas.transform.Find(RaceMaintenanceScreen.RootName);
                Require(sheet!=null&&sheet.gameObject.activeSelf,"Broadcast shows maintenance once both players leave.");
                race.CancelUpdateExit();
                Require(race.state==Gmanager.State.Title&&!race.UpdateExitPending&&!Screen(race,0).activeSelf&&!Screen(race,1).activeSelf,"Cancellation restores the next group's title.");
                race.Maintenance.SetActive(true,persist:false);Require(race.BeginUpdateExit()&&!race.UpdateExitReady,"Idle title still displays service for at least one frame.");stage++;
            }
            else if(stage==6 && race.UpdateExitReady)
            {
                race.CancelUpdateExit();Require(race.IsMaintenanceActive,"Cancellation must preserve operator-owned maintenance.");
                race.Maintenance.SetActive(false,persist:false);
                Require(Time.timeScale>0&&!Screen(race,0).activeSelf&&!Screen(race,1).activeSelf,"Normal service resume restores time and sheets.");
                Debug.Log("OPERATOR_EXIT_PLAYMODE_PASS: P1 thanks then local maintenance; P2 results/thanks continue; both sheets precede release; cancellation/title/manual-maintenance preservation.");Finish(0);
            }
        }
        catch(Exception error){Debug.LogException(error);Finish(1);}
    }
    private static object Player(Gmanager race,int index)=>((Array)typeof(Gmanager).GetField("players",Private).GetValue(race)).GetValue(index);
    private static T Field<T>(object target,string name)=>(T)target.GetType().GetField(name,BindingFlags.Instance|BindingFlags.Public).GetValue(target);
    private static PlayerDisplayRig Rig(Gmanager race,int index)=>Field<PlayerDisplayRig>(Player(race,index),"displayRig");
    private static ArcadeEndingUI Ending(Gmanager race,int index)=>ArcadeEndingUI.Get(Rig(race,index).CanvasRoot.transform);
    private static GameObject Screen(Gmanager race,int index)=>Rig(race,index).Canvas.transform.Find(RaceMaintenanceScreen.RootName).gameObject;
    private static void Call(object target,string method,params object[] args)=>target.GetType().GetMethod(method,Private).Invoke(target,args);
    private static void Require(bool ok,string message){if(!ok)throw new InvalidOperationException(message);}
    private static void Finish(int code){SessionState.SetBool(Key,false);EditorApplication.isPlaying=false;if(Application.isBatchMode)EditorApplication.Exit(code);}
}
#endif
