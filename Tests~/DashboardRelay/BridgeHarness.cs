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
    public class RangeAttribute : Attribute { public RangeAttribute(float minimum, float maximum) { } }
    public static class PlayerPrefs {
        private static readonly Dictionary<string,float> Values = new Dictionary<string,float>();
        public static float GetFloat(string key,float fallback)=>Values.TryGetValue(key,out float v)?v:fallback;
        public static void SetFloat(string key,float value)=>Values[key]=value;
        public static int GetInt(string key,int fallback)=>(int)GetFloat(key,fallback);
        public static void SetInt(string key,int value)=>SetFloat(key,value);
        public static void Save() { }
    }
    public static class AudioListener { public static float volume; }
    public static class Application { public static bool runInBackground; public static string streamingAssetsPath; }
    public static class Time { private static readonly Stopwatch Clock = Stopwatch.StartNew(); public static double realtimeSinceStartupAsDouble => Clock.Elapsed.TotalSeconds; public static float realtimeSinceStartup => (float)realtimeSinceStartupAsDouble; public static float time => realtimeSinceStartup; public static float unscaledTime => realtimeSinceStartup; }
    public static class Debug { public static void Log(object value) => Console.WriteLine(value); public static void LogWarning(object value) => Console.Error.WriteLine(value); public static void LogError(object value) => Console.Error.WriteLine(value); }
    public static class JsonUtility { public static string ToJson(object value) => new JavaScriptSerializer().Serialize(value); public static T FromJson<T>(string value) => new JavaScriptSerializer().Deserialize<T>(value); }
    public static class Mathf { public static float Clamp01(float v)=>Clamp(v,0,1); public static float Clamp(float v,float a,float b)=>Math.Max(a,Math.Min(v,b)); public static float Round(float v)=>(float)Math.Round(v); public static float Max(float a,float b)=>Math.Max(a,b); }
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
    public string InputMode = "serial";
    public P1SteeringTestMode SteeringTestMode;
    public float ConstantTestPedal = 1f;
    public bool SerialDebugMode, ConstantTestPedalPaused;
    public bool IsSerialDebugDisplayVisible => SerialDebugMode;
    public bool SetInputMode(string mode) { if(mode!="serial"&&mode!="keyboard"&&mode!="mixed")return false;if(InputMode!=mode)ClearDashboardInputs();InputMode=mode;return true; }
    public void SetP1SteeringTestMode(P1SteeringTestMode mode)=>SteeringTestMode=mode;
    public void SetP1ConstantPedal(float value)=>ConstantTestPedal=value;
    public void SetSerialDebugDisplayVisible(bool value) { }
    public void PauseP1ConstantPedal()=>ConstantTestPedalPaused=true;
    public void ResumeP1ConstantPedal()=>ConstantTestPedalPaused=false;
    public string InputModeLabel => "Serial P1/P2";
    private readonly DashboardInputLease[] leases = {new DashboardInputLease(),new DashboardInputLease()};
    private DashboardDriveInputSource[] sources;
    public DashboardInputLease GetDashboardInput(int player)=>leases[player];
    public void ClearDashboardInputs(){foreach(var lease in leases)lease.Clear();}
    private void EnsureSources(){if(sources==null)sources=new[]{new DashboardDriveInputSource(new HarnessDrive(this,0),leases[0],()=>UnityEngine.Time.realtimeSinceStartupAsDouble),new DashboardDriveInputSource(new HarnessDrive(this,1),leases[1],()=>UnityEngine.Time.realtimeSinceStartupAsDouble)};}
    public DriveInputState GetInputState(int i){EnsureSources();return sources[i].CurrentState;}
    public bool IsPlayerConnected(int i){EnsureSources();return sources[i].IsConnected;}
    public void Pump() { SharedSerialInputSource?.UpdateInput(.016f);EnsureSources();foreach(var source in sources)source.UpdateInput(.016f);
        for(int i=0;i<2;i++){var state=GetInputState(i);if(state.resetPressed)Harness.Resets[i]++;if(state.readyPressed)Harness.Readies[i]++;}}
    private sealed class HarnessDrive : IDriveInputSource {
        private InputManager input;public int PlayerIndex{get;}public string DeviceId=>"Harness";
        public bool IsConnected=>input.SharedSerialInputSource?.IsConnected(PlayerIndex)??false;
        public DriveInputState CurrentState=>input.SharedSerialInputSource?.GetInputState(PlayerIndex)??DriveInputState.Neutral;
        public HarnessDrive(InputManager input,int player){this.input=input;PlayerIndex=player;}
        public void UpdateInput(float deltaTime){}public void Dispose(){}
    }
    public void Bind() { if(SharedSerialInputSource!=null) SharedSerialInputSource.LineProcessed+=(status,line)=>SerialLineProcessed?.Invoke(status,line); }
}
public class Gmanager
{
    public bool BothPlayersAutodrive { get; private set; }
    public enum State { Title, CarSelection, Tutorial, Countdown, Game, Result }
    public State state=State.Title;
    public RaceFeatureController Features = new RaceFeatureController();
    public bool IsMaintenanceActive { get; private set; }
    public bool MaintenanceAvailable=>true;
    public bool SetMaintenance(bool active){IsMaintenanceActive=active;((InputManager)Harness.Components[typeof(InputManager)]).ClearDashboardInputs();return true;}
    public bool CanUseDashboardControls=>!IsMaintenanceActive;
    private readonly bool[] menus={false,false};
    public bool CanUseDashboardPlayerInput(int i)=>!IsMaintenanceActive&&(state==State.Title||state==State.CarSelection||state==State.Tutorial||state==State.Game||state==State.Result)&&!menus[i];
    public bool CanRecoverDashboardPlayer(int i)=>!IsMaintenanceActive&&(state==State.Game||state==State.Tutorial)&&!menus[i];
    public bool TryDashboardRecovery(int i){if(!CanRecoverDashboardPlayer(i))return false;Harness.MenuActions++;return true;}
    public bool IsDashboardMenuOpen(int i)=>menus[i];
    public bool ToggleDashboardEscape(){if(state!=State.Game)return false;bool close=menus[0]||menus[1];menus[0]=menus[1]=!close;((InputManager)Harness.Components[typeof(InputManager)]).ClearDashboardInputs();return true;}
    public bool TryDashboardMenuAction(int i,string action){if(state!=State.Game||!menus[i]||!(action=="resume"||action=="direction"||action=="restart"||action=="withdraw"))return false;menus[i]=false;Harness.MenuActions++;return true;}
    public bool IsPreparing=>state==State.Title;
    public float time=>UnityEngine.Time.time;
    public float CountdownTimeRemaining=>0;
    public int GoalLap=>Features.Settings.ActivePreset==RacePreset.Experience?1:2;
    public List<RaceParticipant> Participants=new List<RaceParticipant>();
    public void StartAutodriveTestRace(bool both=false){BothPlayersAutodrive=both;Features.SetFeature(RaceFeature.PlayerTwoAutodrive,true);Features.Settings.BeginPreparation();state=State.Countdown;Harness.Applied++;}
    public void StartGame(){Features.Settings.BeginPreparation();state=State.Countdown;Harness.Applied++;}
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
    public byte[] CapturePlayerFrame(int i)=>null;
    public void RequestDashboardPlayerFrame(int i, Action<byte[]> completed)=>completed(null);
}
public enum P1SteeringTestMode { Normal, KeyboardPedal, ConstantPedal }
public class RaceFeatureController {
    public bool PanelVisible;
    public RaceFeatureSettings Settings = new RaceFeatureSettings();
    public void SetFeature(RaceFeature feature, bool enabled)=>Settings.SetFeature(feature, enabled);
    public void SelectPreset(RacePreset preset)=>Settings.SelectPreset(preset);
}
public class RaceAudioOutput {
    public static RaceAudioOutput Instance = new RaceAudioOutput();
    public bool CanRefreshDevices => true;
    public string[] DeviceNames = { "USB P1", "HDMI P2" }, DeviceKeys = { "usb-key", "hdmi-key" };
    private int[] selected = { -1, -1 };
    public string GetDeviceLabel(int player)=>selected[player]<0 ? "出力なし" : DeviceNames[selected[player]];
    public int GetSelectedDevice(int player)=>selected[player];
    public bool CanSelectDevice(int player,int device)=>device<0||device!=selected[1-player];
    public bool SelectDevice(int player,int device) { if(!CanSelectDevice(player,device))return false;selected[player]=device;return true; }
    public void RefreshDevices() { }
}
public class RaceParticipant { public int Index,Position; public bool IsCpu,IsRacing; public float Progress; public NpcRaceDriver Driver; public RaceResultRecord Result; }
public class NpcRaceDriver { public int RivalIndex,RecoveryCount; public bool HasContactGrace; public string DrivingStatus; public float TargetSpeed; }
public class RaceResultRecord { public int finishPosition; public bool didFinish; }
public static class KeyboardDriveInputSource { public static bool IsResetPressed(int i)=>false; }
public static class Harness
{
    public static readonly Dictionary<Type,object> Components=new Dictionary<Type,object>();
    public static int Applied,MenuActions;
    public static readonly int[] Resets={0,0},Readies={0,0};
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
            else if(line=="rules:unlock") race.Features.Settings.UnlockAtTitle();
            else if(line=="rules:lock") race.Features.Settings.BeginPreparation();
            else if(line.StartsWith("feature:")) race.Features.SetFeature((RaceFeature)Enum.Parse(typeof(RaceFeature),line.Substring(8)),false);
            else if(line=="inputCounts") Console.WriteLine("INPUT_COUNTS="+string.Join(",",Resets)+"/"+string.Join(",",Readies));
            else if(line=="count") Console.WriteLine("APPLIED="+Applied);
        }}) {IsBackground=true}.Start();
        Console.WriteLine("BRIDGE_READY");
        while(running) { if(!frozen) { input.Pump();call("Update"); } Thread.Sleep(10); }
        call("OnDestroy");input.SharedSerialInputSource?.Dispose();
    }
}
