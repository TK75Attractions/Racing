using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

namespace UnityEngine
{
    public enum RuntimeInitializeLoadType { SubsystemRegistration, AfterSceneLoad }
    public enum RuntimePlatform { OSXPlayer, OSXEditor }
    public sealed class RuntimeInitializeOnLoadMethodAttribute : Attribute { public RuntimeInitializeOnLoadMethodAttribute(RuntimeInitializeLoadType type) { } }
    public static class Application
    {
        public static bool isBatchMode = true;
        public static RuntimePlatform platform;
        public static string dataPath, streamingAssetsPath, persistentDataPath;
    }
    public static class Debug { public static void Log(object value) { } public static void LogWarning(object value) { } }
}
namespace UnityEditor
{
    public enum BuildTarget { StandaloneOSX, StandaloneWindows64 }
    public static class EditorPrefs { public static bool GetBool(string key, bool value) { return value; } }
    public static class SessionState { public static bool GetBool(string key, bool value) { return value; } }
    public static class AssetDatabase { public static void Refresh() { } }
}
namespace UnityEditor.Build
{
    public interface IPreprocessBuildWithReport { int callbackOrder { get; } void OnPreprocessBuild(UnityEditor.Build.Reporting.BuildReport report); }
    public class BuildFailedException : Exception { public BuildFailedException(string value) : base(value) { } }
}
namespace UnityEditor.Build.Reporting
{
    public class BuildSummary { public UnityEditor.BuildTarget platform; }
    public class BuildReport { public BuildSummary summary = new BuildSummary(); }
}
internal static class StartupHarness
{
    static void Require(bool result, string message) { if (!result) throw new Exception(message); }
    public static void Main()
    {
        string root = Path.Combine(Path.GetTempPath(), "racing startup " + Guid.NewGuid().ToString("N"));
        var calls = new List<string>();
        Action<string, string, bool> record = (directory, arguments, wait) => calls.Add((wait ? "build:" : "start:") + arguments);
        Action<string> ready = url => calls.Add("ready:" + url);
        try
        {
            foreach (string project in new[] { "DashboardUsb", "DashboardRelay" })
            {
                string directory = Path.Combine(root, "Tools", project); Directory.CreateDirectory(directory);
                File.WriteAllText(Path.Combine(directory, project + ".csproj"), "fake project, never executed");
            }
            RaceUsbAutoStart.StartHelpers(root, "missing.zip", root, "/fake/dotnet", _ => true, record, ready);
            Require(calls.Count == 0, "Existing services must never be rebuilt or restarted.");
            RaceUsbAutoStart.StartHelpers(root, "missing.zip", root, "/fake/dotnet", url => url.Contains(":8766/"), record, ready);
            Require(calls.Count == 3 && calls[0].Contains("build DashboardUsb.csproj") && calls[1].StartsWith("ready:") && calls[2].Contains("--iphone --no-browser"), "Only the missing USB helper starts after relay readiness is confirmed.");
            calls.Clear();
            RaceUsbAutoStart.StartHelpers(root, "missing.zip", root, "/fake/dotnet", _ => false, record, ready);
            Require(calls.Count == 5 && calls[2].Contains("--local-startup") && calls[3].StartsWith("ready:") && calls[4].Contains("--iphone"), "Fresh source launch waits for the local-only relay before USB.");
            string pack = Path.Combine(root, "payload");
            foreach (string entry in new[] { "Usb/DashboardUsb.dll", "Relay/DashboardRelay.dll" })
            { string path = Path.Combine(pack, entry); Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllText(path, "fake payload"); }
            string archive = Path.Combine(root, "DashboardHelpers.zip"); ZipFile.CreateFromDirectory(pack, archive);
            calls.Clear(); RaceUsbAutoStart.StartHelpers("missing source", archive, root, "/fake/dotnet", _ => false, record, ready);
            Require(calls.Count == 3 && calls[0].StartsWith("start:") && calls[1].StartsWith("ready:") && calls[2].StartsWith("start:"), "A packaged player waits for HTTP readiness and never invokes the SDK.");
            calls.Clear(); bool failed = false;
            try { RaceUsbAutoStart.StartHelpers("missing source", archive, root, "/fake/dotnet", _ => false, record, _ => { throw new IOException("fake relay not ready"); }); }
            catch (IOException) { failed = true; }
            Require(failed && calls.Count == 1 && !calls[0].Contains("--iphone"), "A relay startup failure must not launch a phone UI against an unavailable HTTP server.");
            string extracted = RaceUsbAutoStart.Extract(archive, root);
            Require(File.Exists(Path.Combine(extracted, "Usb/DashboardUsb.dll")) && RaceUsbAutoStart.Extract(archive, root) == extracted, "Packaged helpers extract completely and reuse a content-specific cache.");
            new RaceUsbPlayerPackaging().OnPreprocessBuild(new UnityEditor.Build.Reporting.BuildReport { summary = new UnityEditor.Build.Reporting.BuildSummary { platform = UnityEditor.BuildTarget.StandaloneWindows64 } });
            Console.WriteLine("USB_STARTUP_TEST_PASS: source/packaged launch, reuse, local-only relay, no real processes or Race operations");
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
