using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Production autonomous drivers race both laps; no vehicle poses or results are injected.</summary>
public static class AutonomousRaceValidation
{
    private const string Key="Racing.AutonomousRevisionValidation";
    private static readonly BindingFlags Fields=BindingFlags.Instance|BindingFlags.NonPublic;
    private static double began;
    private static bool started,goalSeen,mirrored,driven;
    private static StreamWriter trace;
    private static float nextTrace;
    private static string Output=>Path.GetFullPath("Logs/October23AutonomousRace");
    public static void Run()
    {
        Environment.SetEnvironmentVariable("RACING_REVISION_QA","1");
        Environment.SetEnvironmentVariable("RACING_BROADCAST_HISTORY",Path.Combine(Output,"History"));
        Directory.CreateDirectory(Output);SessionState.SetBool(Key,true);Resume();
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        var input=UnityEngine.Object.FindFirstObjectByType<InputManager>();input.isDebugMode=true;input.isP1SerialP2KeyboardDebugMode=false;
        EditorApplication.isPlaying=true;
    }
    [InitializeOnLoadMethod] private static void Resume()
    {
        if(Environment.GetEnvironmentVariable("RACING_REVISION_QA")=="1")
        {SceneManager.sceneLoaded-=OnScene;SceneManager.sceneLoaded+=OnScene;}
        if(!SessionState.GetBool(Key,false))return;
        began=EditorApplication.timeSinceStartup;EditorApplication.update-=Tick;EditorApplication.update+=Tick;
    }
    private static void OnScene(Scene scene,LoadSceneMode mode)
    {
        foreach(var bridge in UnityEngine.Object.FindObjectsByType<RaceDashboardBridge>(FindObjectsSortMode.None))bridge.enabled=false;
        foreach(var publisher in UnityEngine.Object.FindObjectsByType<RaceRemoteFirebasePublisher>(FindObjectsSortMode.None))publisher.enabled=false;
        foreach(var audio in UnityEngine.Object.FindObjectsByType<RaceAudioOutput>(FindObjectsSortMode.None))audio.enabled=false;
    }
    private static void Tick()
    {
        if(!SessionState.GetBool(Key,false))return;
        if(EditorApplication.timeSinceStartup-began>420){Finish(false,"Autonomous race timed out.");return;}
        if(!EditorApplication.isPlaying||Gmanager.Control==null)return;
        var manager=Gmanager.Control;
        try
        {
            if(!started)
            {
                if(manager.state!=Gmanager.State.Title||!manager.CanUseDashboardControls)return;
                var rigs=(PlayerDisplayRig[])typeof(Gmanager).GetField("displayRigs",Fields).GetValue(manager);
                if(manager.Broadcast.PresentationCamera.targetDisplay!=RaceDisplayRouting.BroadcastDisplay(rigs[0].DisplayIndex,rigs[1].DisplayIndex))throw new Exception("Broadcast/player output collision.");
                Time.timeScale=4;manager.StartAutodriveTestRace(true);started=true;
                trace=new StreamWriter(Path.Combine(Output,"trace.csv"));trace.WriteLine("time,driver,lap,progress,speed,pedal,steer,finished,recoveries");
            }
            if(manager.state==Gmanager.State.Game)
            {
                for(int i=0;i<2;i++)
                {
                    var car=manager.GetParticipant(i);var auto=car.Car.GetComponent<PlayerAutodrive>();
                    if(car.IsRacing&&!auto.IsActive)throw new Exception("Both player test drivers must be active.");
                    driven|=auto.CurrentState.pedal>.2f&&Mathf.Abs(auto.CurrentState.steering)>.5f;
                }
                for(int a=0;a<manager.Participants.Count;a++)for(int b=a+1;b<manager.Participants.Count;b++)
                {
                    var first=manager.GetParticipant(a);var second=manager.GetParticipant(b);if(!first.IsRacing||!second.IsRacing)continue;
                    foreach(var x in first.Car.GetComponentsInChildren<Collider>())foreach(var y in second.Car.GetComponentsInChildren<Collider>())
                        if(!x.isTrigger&&!y.isTrigger&&x.enabled&&y.enabled&&Physics.GetIgnoreCollision(x,y))throw new Exception("An active race pair became non-solid.");
                }
            }
            if(manager.time>=nextTrace&&manager.state==Gmanager.State.Game)
            {
                nextTrace=manager.time+.25f;
                foreach(var car in manager.Participants){var auto=car.Car.GetComponent<PlayerAutodrive>();var controls=auto!=null?auto.CurrentState:car.Driver.CurrentState;
                    trace.WriteLine(FormattableString.Invariant($"{manager.time:F3},{car.Label},{manager.GetParticipantLap(car)},{car.Progress:F2},{car.Body.linearVelocity.magnitude:F2},{controls.pedal:F3},{controls.steering:F3},{car.Result?.didFinish==true},{car.Car.GetComponent<CarResetter>()?.AutomaticRecoveryCount??0}"));}
            }
            if(manager.state==Gmanager.State.Goal)
            {
                goalSeen=true;
                var rigs=(PlayerDisplayRig[])typeof(Gmanager).GetField("displayRigs",Fields).GetValue(manager);
                var labels=rigs.Select(rig=>rig.CanvasRoot.transform.Find("Goal")).ToArray();
                if(labels.All(x=>x!=null&&x.gameObject.activeSelf))
                {
                    string[] text=labels.Select(x=>string.Join(" ",x.GetComponentsInChildren<TMPro.TMP_Text>().Select(t=>t.text))).ToArray();
                    mirrored=text[0]==text[1]&&text[0].Contains("FINISHED");
                }
            }
            if(manager.state==Gmanager.State.Result)
            {
                var first=manager.GetParticipant(0).Result;var second=manager.GetParticipant(1).Result;
                bool passed=driven&&goalSeen&&mirrored&&first?.didFinish==true&&second?.didFinish==true&&first.completedLaps==2&&second.completedLaps==2;
                var rigs=(PlayerDisplayRig[])typeof(Gmanager).GetField("displayRigs",Fields).GetValue(manager);
                for(int i=0;i<2;i++)File.WriteAllBytes(Path.Combine(Output,$"result-p{i+1}.jpg"),rigs[i].CaptureJpeg(1280,720));
                File.WriteAllText(Path.Combine(Output,"report.json"),"{\"passed\":"+passed.ToString().ToLowerInvariant()+",\"mirroredFinish\":"+mirrored.ToString().ToLowerInvariant()+",\"steeringInputSeen\":"+driven.ToString().ToLowerInvariant()+",\"p1Seconds\":"+(first?.totalRaceTime??0).ToString(System.Globalization.CultureInfo.InvariantCulture)+",\"p2Seconds\":"+(second?.totalRaceTime??0).ToString(System.Globalization.CultureInfo.InvariantCulture)+"}");
                Finish(passed,"AUTONOMOUS_RACE_"+(passed?"PASS":"FAIL")+": production input driving, two verified laps, solid racing pairs, both-screen final finish.");
            }
        }
        catch(Exception error){Finish(false,error.ToString());}
    }
    private static void Finish(bool passed,string message)
    {
        trace?.Dispose();trace=null;SessionState.SetBool(Key,false);EditorApplication.update-=Tick;Time.timeScale=1;
        if(passed)Debug.Log(message);else Debug.LogError(message);
        if(Application.isBatchMode)EditorApplication.Exit(passed?0:1);else EditorApplication.isPlaying=false;
    }
}
