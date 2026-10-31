using System;
using System.IO;
using System.Threading;
public static class RuntimeTests
{
    public static void Main()
    {
        string root=Path.Combine(Path.GetTempPath(),"operator runtime "+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root,".git"));Directory.CreateDirectory(Path.Combine(root,"Tools","OperatorUpdate"));
        string unity=Path.Combine(root,"Unity");File.WriteAllText(unity,"");
        Environment.SetEnvironmentVariable("RACING_PROJECT_PATH",root);Environment.SetEnvironmentVariable("RACING_UNITY_PATH",unity);
        File.WriteAllText(Path.Combine(root,"Tools","OperatorUpdate","update-and-run.sh"),
            "mkdir -p \"$4\"\nprintf '%s' \"$3\" > \"$4/parent.txt\"\necho building > \"$4/status.txt\"\nwhile [ ! -f \"$4/finish-build\" ]; do sleep .02; done\necho launch > \"$4/decision\"\ntouch \"$4/ready\"\nwhile [ ! -f \"$4/released\" ]; do sleep .02; done\necho done > \"$4/status.txt\"\ntouch \"$4/succeeded\"\n");
        try
        {
            UnityEditor.EditorApplication.isPlaying=true;
            var race=new Gmanager {state=Gmanager.State.Game};var updater=new RaceOperatorUpdate();updater.Configure(race);
            Check(updater.Available,"Update is available during a race");
            Check(updater.Request()&&race.IsMaintenanceActive,"Request pauses a running race");
            string request=Directory.GetDirectories(Path.Combine(root,"Logs","OperatorUpdate"))[0];
            WaitUntil(()=>File.Exists(Path.Combine(request,"status.txt")),updater.Poll);
            UnityEngine.Time.realtimeSinceStartupAsDouble=1000;
            updater.Poll();
            Check(updater.Busy&&UnityEditor.EditorApplication.isPlaying,"A build longer than 120 seconds keeps the worker and Editor Play alive");
            File.WriteAllText(Path.Combine(request,"finish-build"),"");
            WaitUntil(()=>!UnityEditor.EditorApplication.isPlaying,updater.Poll);
            Check(!UnityEditor.EditorApplication.isPlaying,"Editor Play stops only after the successful build, without closing the Editor");
            Check(!race.IsMaintenanceActive,"Updater-owned maintenance is cleared before the new game starts");
            foreach(string folder in Directory.GetDirectories(Path.Combine(root,"Logs","OperatorUpdate")))Check(File.ReadAllText(Path.Combine(folder,"parent.txt"))=="0","Editor updater never waits for the Editor process to exit");
            WaitUntil(()=>!updater.Busy,updater.Poll);
            var edit=new RaceOperatorUpdate();edit.Configure(null);
            Check(edit.Available&&edit.Request(),"Edit-mode update works without an open race scene");
            foreach(string folder in Directory.GetDirectories(Path.Combine(root,"Logs","OperatorUpdate")))File.WriteAllText(Path.Combine(folder,"finish-build"),"");
            WaitUntil(()=>!edit.Busy,edit.Poll);
            Check(!edit.Busy,"Worker completion clears busy state");
            Check(!edit.PrepareForLaunch("../other")&&!edit.PrepareForLaunch("missing-job"),"Handoff rejects unverified jobs and path traversal");
            UnityEditor.EditorApplication.isPlaying=true;
            Check(!edit.PrepareForLaunch(Path.GetFileName(request)),"A completed stale job cannot shut down a newer game");
            string verified=Path.Combine(root,"Logs","OperatorUpdate","verified-ready-job");Directory.CreateDirectory(verified);
            File.WriteAllText(Path.Combine(verified,"decision"),"launch");File.WriteAllText(Path.Combine(verified,"ready"),"");
            string active=Path.Combine(root,"Logs","OperatorUpdate.lock");Directory.CreateDirectory(active);File.WriteAllText(Path.Combine(active,"request.txt"),verified);
            Check(edit.PrepareForLaunch("verified-ready-job"),"Only the active, launch-committed job may release Editor Play");
            UnityEditor.EditorApplication.delayCall?.Invoke();
            Check(!UnityEditor.EditorApplication.isPlaying,"Graceful handoff releases Play without closing the Editor");
        }
        finally {Directory.Delete(root,true);}
        Console.WriteLine("OPERATOR_RUNTIME_TESTS_PASS");
    }
    private static void WaitUntil(Func<bool> complete,Action poll)
    {
        var deadline=System.Diagnostics.Stopwatch.StartNew();
        while(!complete()&&deadline.Elapsed<TimeSpan.FromSeconds(10)){Thread.Sleep(10);poll();}
    }
    private static void Check(bool value,string label){if(!value)throw new Exception(label);Console.WriteLine("PASS "+label);}
}
public sealed class Gmanager
{
    public enum State {Title,Game,Result}
    public State state;public bool IsMaintenanceActive;
    public bool SetMaintenance(bool active){IsMaintenanceActive=active;return true;}
}
namespace UnityEngine
{
    public class DisallowMultipleComponent:Attribute{}
    public class MonoBehaviour{protected T GetComponent<T>() where T:class{return null;}}
    public enum RuntimePlatform{WindowsPlayer,WindowsEditor,OSXPlayer,OSXEditor}
    public static class Application{public static string dataPath="";public static RuntimePlatform platform=RuntimePlatform.OSXEditor;public static string unityVersion="6000.3.9f1";public static void Quit(){}}
    public static class Time{public static double realtimeSinceStartupAsDouble;}
}
namespace UnityEngine.InputSystem { public class Keyboard{} }
namespace UnityEditor { public static class EditorApplication {public static bool isPlaying;public static Action delayCall;public static string applicationPath="";} }
