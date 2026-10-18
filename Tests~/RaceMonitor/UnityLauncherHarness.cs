// Runs the real Editor launcher against a fake Python child; no webhooks or Unity required.
using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
namespace UnityEngine
{
    public enum RuntimePlatform { WindowsEditor, OSXEditor }
    public static class Application { public static bool isBatchMode; public static string dataPath; public static RuntimePlatform platform = RuntimePlatform.OSXEditor; }
    public static class Debug { public static void Log(object value) { } public static void LogWarning(object value) { } }
}
namespace UnityEditor
{
    public sealed class InitializeOnLoadAttribute : Attribute { }
    public sealed class MenuItemAttribute : Attribute { public MenuItemAttribute(string name) { } }
    public enum PlayModeStateChange { EnteredPlayMode, ExitingPlayMode }
    public static class EditorApplication
    {
        public static Action update, delayCall, quitting;
        public static event Action<PlayModeStateChange> playModeStateChanged;
        private static readonly Stopwatch clock = Stopwatch.StartNew();
        public static double timeSinceStartup => clock.Elapsed.TotalSeconds;
        public static void Change(PlayModeStateChange state) => playModeStateChanged?.Invoke(state);
    }
    public static class AssemblyReloadEvents { public static Action beforeAssemblyReload; }
}
public static class Harness
{
    private static readonly FieldInfo ProcessField = typeof(RaceMonitorLauncher).GetField("process", BindingFlags.Static | BindingFlags.NonPublic);
    private static Process Child => ProcessField.GetValue(null) as Process;
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void Until(Func<bool> predicate)
    {
        var clock = Stopwatch.StartNew();
        while (clock.Elapsed.TotalSeconds < 8) { UnityEditor.EditorApplication.update?.Invoke(); if (predicate()) return; Thread.Sleep(10); }
        throw new Exception("Launcher timed out: " + RaceMonitorLauncher.Message);
    }
    public static void Main(string[] args)
    {
        UnityEngine.Application.dataPath = Path.Combine(args[0], "Assets");
        RuntimeHelpers.RunClassConstructor(typeof(RaceMonitorLauncher).TypeHandle);
        try
        {
            UnityEditor.EditorApplication.delayCall();
            Until(() => RaceMonitorLauncher.Message.Contains("5秒"));
            int pid = Child.Id;
            RaceMonitorLauncher.Start();
            Require(Child.Id == pid, "Duplicate child on repeated Start");
            UnityEditor.EditorApplication.Change(UnityEditor.PlayModeStateChange.EnteredPlayMode);
            Require(Child.Id == pid, "Duplicate child on Play");
            string flags = Child.StartInfo.Arguments;
            Require(flags.Contains("--managed --hold-seconds 5 --poll-seconds 1"), "Timing overrides missing");
            UnityEditor.EditorApplication.Change(UnityEditor.PlayModeStateChange.ExitingPlayMode);
            Until(() => Child == null);
            UnityEditor.EditorApplication.Change(UnityEditor.PlayModeStateChange.EnteredPlayMode);
            Until(() => Child != null && RaceMonitorLauncher.Message.Contains("5秒"));
            UnityEditor.AssemblyReloadEvents.beforeAssemblyReload();
            Require(Child == null, "Owned child survived assembly reload");
            RaceMonitorLauncher.Start();
            Until(() => Child != null && RaceMonitorLauncher.Message.Contains("5秒"));
            UnityEditor.EditorApplication.quitting();
            Require(Child == null, "Owned child survived Editor exit");
            UnityEngine.Application.isBatchMode = true;
            RaceMonitorLauncher.Start();
            Require(Child == null, "Batch mode started a monitor");
            Console.WriteLine("UNITY_MONITOR_LAUNCHER_PASS");
        }
        finally { UnityEditor.EditorApplication.quitting?.Invoke(); }
    }
}
