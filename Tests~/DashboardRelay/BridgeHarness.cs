// Headless harness: the production bridge, journal and serial receiver are compiled unchanged.
// Only Unity's scene/graphics APIs and race actions are substituted. Unity compilation is checked separately.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Web.Script.Serialization;
namespace UnityEngine
{
    public class Object { public static void Destroy(object value) { } }
    public class MonoBehaviour : Object { public T GetComponent<T>() where T : class => Harness.Components[typeof(T)] as T; }
    public class SerializeField : Attribute { }
    public class TooltipAttribute : Attribute { public TooltipAttribute(string value) { } }
    public class MinAttribute : Attribute { public MinAttribute(float value) { } }
    public static class Application { public static bool runInBackground; public static string streamingAssetsPath; }
    public static class Time { private static readonly Stopwatch Clock = Stopwatch.StartNew(); public static float realtimeSinceStartup => (float)Clock.Elapsed.TotalSeconds; public static float time => realtimeSinceStartup; }
    public static class Debug { public static void Log(object value) => Console.WriteLine(value); public static void LogWarning(object value) => Console.Error.WriteLine(value); public static void LogError(object value) => Console.Error.WriteLine(value); }
    public static class JsonUtility { public static string ToJson(object value) => new JavaScriptSerializer().Serialize(value); public static T FromJson<T>(string value) => new JavaScriptSerializer().Deserialize<T>(value); }
    public static class Mathf { public static float Clamp(float v,float a,float b)=>Math.Max(a,Math.Min(v,b)); public static float Round(float v)=>(float)Math.Round(v); public static float Max(float a,float b)=>Math.Max(a,b); }
    public class Camera { }
    public class RenderTexture { public static RenderTexture active; public static RenderTexture GetTemporary(int w,int h,int d)=>new RenderTexture(); public static void ReleaseTemporary(RenderTexture v) { } }
    public enum TextureFormat { RGB24 }
    public class Texture2D { public Texture2D(int w,int h,TextureFormat f,bool m) { } public void ReadPixels(Rect r,int x,int y) { } public void Apply() { } public byte[] EncodeToJPG(int q)=>null; }
    public struct Rect { public Rect(int a,int b,int c,int d) { } }
}
namespace UnityEngine.Rendering
{
    public class RenderPipeline { public class StandardRequest { public UnityEngine.RenderTexture destination; } public static bool SupportsRenderRequest(UnityEngine.Camera c,StandardRequest r)=>false; public static void SubmitRenderRequest(UnityEngine.Camera c,StandardRequest r) { } }
}
public class InputManager
{
    public event Action<string,string> SerialLineProcessed;
    public TwoPlayerSerialInputSource SharedSerialInputSource;
    public string InputModeLabel => "Serial P1/P2";
    public DriveInputState GetInputState(int i)=>SharedSerialInputSource?.GetInputState(i) ?? DriveInputState.Neutral;
    public bool IsPlayerConnected(int i)=>SharedSerialInputSource?.IsConnected(i) ?? false;
    public void Pump() { SharedSerialInputSource?.UpdateInput(.016f); }
    public void Bind() { if(SharedSerialInputSource!=null) SharedSerialInputSource.LineProcessed+=(status,line)=>SerialLineProcessed?.Invoke(status,line); }
}
public class Gmanager
{
    public enum State { Title, CarSelection, Tutorial, Countdown, Game, Result }
    public State state=State.Title;
    public bool CanUseDashboardControls=>true;
    public bool IsPreparing=>state==State.Title;
    public float time=>UnityEngine.Time.time;
    public float CountdownTimeRemaining=>0;
    public int GoalLap=>2;
    public List<RaceParticipant> Participants=new List<RaceParticipant>();
    public void StartGame(){state=State.Countdown;Harness.Applied++;}
    public bool AdvanceDashboardToTutorial(){if(state!=State.CarSelection)return false;state=State.Tutorial;Harness.Applied++;return true;}
    public bool AdvanceDashboardToCountdown(){if(state!=State.Tutorial)return false;state=State.Countdown;Harness.Applied++;return true;}
    public void ShowResult(){state=State.Result;Harness.Applied++;}
    public void RetryGame(){state=State.Title;Harness.Applied++;}
    public void ResetGame(){state=State.Title;Harness.Applied++;}
    public void GetPlayerDashboardProgress(int i, out string phase, out string carName, out string step, out float remaining, out float progress)
    { phase = state.ToString(); carName = "SPORT CAR"; step = ""; remaining = progress = 0; }
    public int GetPlayerLap(int i)=>0; public float GetPlayerSpeed(int i)=>0;
    public RaceParticipant GetParticipant(int i)=>null;
    public int GetParticipantLap(RaceParticipant p)=>0; public float GetParticipantSpeed(RaceParticipant p)=>0;
    public UnityEngine.Camera GetPlayerCaptureCamera(int i)=>null;
}
public class RaceParticipant { public int Index,Position; public bool IsCpu,IsRacing; public float Progress; public NpcRaceDriver Driver; public RaceResultRecord Result; }
public class NpcRaceDriver { public int RivalIndex,RecoveryCount; public bool HasContactGrace; public string DrivingStatus; public float TargetSpeed; }
public class RaceResultRecord { public int finishPosition; public bool didFinish; }
public static class KeyboardDriveInputSource { public static bool IsResetPressed(int i)=>false; }
public static class Harness
{
    public static readonly Dictionary<Type,object> Components=new Dictionary<Type,object>();
    public static int Applied;
    private static volatile bool running=true, frozen;
    public static void Main(string[] args)
    {
        UnityEngine.Application.streamingAssetsPath=Path.GetFullPath(args[0]);
        var race=new Gmanager();var input=new InputManager();
        if(args.Length>1) input.SharedSerialInputSource=new TwoPlayerSerialInputSource(new Esp32SerialConfiguration().WithSimulatorPort(args[1],6.5f),args[1]);
        input.Bind(); Components[typeof(Gmanager)]=race;Components[typeof(InputManager)]=input;
        var bridge=new RaceDashboardBridge();
        Action<string> call=name=>typeof(RaceDashboardBridge).GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(bridge,null);
        call("Start");
        var journal=input.SharedSerialInputSource?.Monitor ?? (RgpSerialJournal)typeof(RaceDashboardBridge).GetField("liveJournal",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(bridge);
        new Thread(()=> { string line; while((line=Console.ReadLine())!=null) {
            if(line=="quit") {running=false;break;}
            if(line=="freeze") frozen=true;
            else if(line=="unfreeze") frozen=false;
            else if(line.StartsWith("rx:")) journal.RecordWire("RX",line.Substring(3)+"\n");
            else if(line.StartsWith("tx:")) journal.RecordWire("TX",line.Substring(3)+"\n");
            else if(line.StartsWith("event:")) journal.Diagnostic("INFO",line.Substring(6));
            else if(line.StartsWith("state:")) race.state=(Gmanager.State)Enum.Parse(typeof(Gmanager.State),line.Substring(6));
            else if(line=="count") Console.WriteLine("APPLIED="+Applied);
        }}) {IsBackground=true}.Start();
        Console.WriteLine("BRIDGE_READY");
        while(running) { if(!frozen) { input.Pump();call("Update"); } Thread.Sleep(10); }
        call("OnDestroy");input.SharedSerialInputSource?.Dispose();
    }
}
