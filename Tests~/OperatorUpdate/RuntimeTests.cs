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
        File.WriteAllText(Path.Combine(root,"Tools","OperatorUpdate","update-and-run.sh"),"mkdir -p \"$4\"\nprintf '%s' \"$3\" > \"$4/parent.txt\"\ntouch \"$4/ready\"\nsleep .3\necho done > \"$4/status.txt\"\n");
        try
        {
            UnityEditor.EditorApplication.isPlaying=true;
            var race=new Gmanager {state=Gmanager.State.Game};var updater=new RaceOperatorUpdate();updater.Configure(race);
            Check(updater.Available,"Update is available during a race");
            Check(updater.Request()&&race.IsMaintenanceActive,"Request pauses a running race");
            WaitUntil(()=>!UnityEditor.EditorApplication.isPlaying,updater.Poll);
            Check(!UnityEditor.EditorApplication.isPlaying,"Editor Play stops after preflight without closing the Editor");
            foreach(string folder in Directory.GetDirectories(Path.Combine(root,"Logs","OperatorUpdate")))Check(File.ReadAllText(Path.Combine(folder,"parent.txt"))=="0","Editor updater never waits for the Editor process to exit");
            WaitUntil(()=>!updater.Busy,updater.Poll);
            var edit=new RaceOperatorUpdate();edit.Configure(null);
            Check(edit.Available&&edit.Request(),"Edit-mode update works without an open race scene");
            WaitUntil(()=>!edit.Busy,edit.Poll);
            Check(!edit.Busy,"Worker completion clears busy state");
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
namespace UnityEditor { public static class EditorApplication {public static bool isPlaying;public static string applicationPath="";} }
