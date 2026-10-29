#if UNITY_EDITOR
using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Capture the new ending after real rendered frames, and verify per-display return gates.</summary>
[InitializeOnLoad]
public static class ArcadeEndingPlayModeValidation
{
    private const string Key="Racing.ArcadeEndingValidation";
    private static int stage,changedFrame;
    private static double started;
    static ArcadeEndingPlayModeValidation(){EditorApplication.update+=Tick;}
    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        var input=UnityEngine.Object.FindFirstObjectByType<InputManager>();input.isDebugMode=true;input.isP1SerialP2KeyboardDebugMode=false;
        SessionState.SetBool(Key,true);EditorApplication.isPlaying=true;
    }
    private static void Tick()
    {
        if(!SessionState.GetBool(Key,false)||!EditorApplication.isPlaying||Gmanager.Control==null)return;
        try
        {
            if(started==0)started=EditorApplication.timeSinceStartup;
            if(EditorApplication.timeSinceStartup-started>60)throw new InvalidOperationException("Ending validation timed out at "+stage);
            var manager=Gmanager.Control;
            foreach(var transition in UnityEngine.Object.FindObjectsByType<ScreenTransitionController>(FindObjectsSortMode.None))if(transition.IsTransitioning)return;
            if(stage==0&&Time.frameCount>8){manager.DebugPreviewResult();Next();}
            else if(stage==1){manager.SelectResultOption(1,0);Next();}
            else if(stage==2&&Time.frameCount-changedFrame>2)
            {
                var p2=Root(1).GetComponent<ArcadeEndingUI>();Require(p2.IsVisible&&!Root(1).Find("Result").gameObject.activeSelf,"P2 ending replaces its result.");
                Require(Root(0).Find("Result").gameObject.activeSelf,"P1 result remains visible.");Capture(manager,1);p2.TickInput(1,2);Require(p2.IsVisible,"A held finish pedal must not skip ending.");p2.TickInput(0,.1f);p2.TickInput(1,1);Next();
            }
            else if(stage==3&&Root(1).Find("Title").gameObject.activeSelf){manager.SelectResultOption(0,0);Next();}
            else if(stage==4&&Time.frameCount-changedFrame>2)
            {
                var p1=Root(0).GetComponent<ArcadeEndingUI>();Require(p1.IsVisible,"P1 ending remains independent.");Capture(manager,0);p1.TickInput(0,.1f);p1.TickInput(1,1);Next();
            }
            else if(stage==5&&manager.state==Gmanager.State.Title){Debug.Log("ARCADE_ENDING_PLAYMODE_PASS: real rendered P1/P2 endings, held-pedal safety, independent title returns.");Finish(0);}
        }
        catch(Exception error){Debug.LogException(error);Finish(1);}
    }
    private static Transform Root(int player)=>GameObject.Find("GameManagers").transform.Find(player==0?"MainCanvas":"MainCanvas_P2");
    private static void Next(){stage++;changedFrame=Time.frameCount;}
    private static void Capture(Gmanager manager,int player)
    {
        string output=Path.GetFullPath("Artifacts/ArcadeSportUI/Live");Directory.CreateDirectory(output);
        typeof(DrivingTutorialValidation).GetMethod("Capture",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,new object[]{manager,player,Path.Combine(output,"ending-p"+(player+1)+".png")});
    }
    private static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    private static void Finish(int code){SessionState.SetBool(Key,false);EditorApplication.isPlaying=false;if(Application.isBatchMode)EditorApplication.Exit(code);}
}
#endif
