using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Threading;
using UnityEngine;

/// <summary>Starts only local dashboard helpers, asynchronously, once per game launch.</summary>
public static class RaceUsbAutoStart
{
    private static int started;
    private static readonly List<Process> children = new List<Process>();
    private static readonly object logLock = new object();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset() { started = 0; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Initialize()
    {
        if (Application.isBatchMode || Environment.GetEnvironmentVariable("RACING_USB_AUTOSTART") == "0" ||
            (Application.platform != RuntimePlatform.OSXEditor && Application.platform != RuntimePlatform.OSXPlayer) ||
            Interlocked.Exchange(ref started, 1) != 0) return;
        string source = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        string archive = Path.Combine(Application.streamingAssetsPath, "DashboardHelpers.zip");
        string cache = Path.Combine(Application.persistentDataPath, "DashboardHelpers");
        bool editorRelay = false;
#if UNITY_EDITOR
        // The existing Editor launcher owns relay builds/Tailscale setup; avoid building it twice.
        editorRelay = UnityEditor.EditorPrefs.GetBool("Racing.Dashboard.AutoStart", true) ||
            UnityEditor.SessionState.GetBool("Racing.Dashboard.OpenOnPlay", false);
        archive = ""; // Play always uses the current helper source, never a stale player payload.
#endif
        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                Directory.CreateDirectory(cache);
                string log = Path.Combine(cache, "startup.log");
                StartHelpers(source, archive, cache, FindDotnet(), url => editorRelay && url.Contains(":8766/") || Probe(url),
                    (working, arguments, wait) => Run(working, arguments, wait, log));
            }
            catch (Exception error)
            {
                try { Directory.CreateDirectory(cache); File.AppendAllText(Path.Combine(cache, "startup.log"), error + Environment.NewLine); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        });
    }

    // Injecting probe/process execution lets tests verify game startup without starting a game or helper.
    internal static void StartHelpers(string source, string archive, string cache, string dotnet,
        Func<string, bool> running, Action<string, string, bool> run)
    {
        bool relay = running("http://127.0.0.1:8766/api/connection");
        bool usb = running("http://127.0.0.1:8767/api/usb");
        if (relay && usb) return;
        string usbDirectory, relayDirectory;
        if (File.Exists(archive))
        {
            string packed = Extract(archive, cache);
            usbDirectory = Path.Combine(packed, "Usb"); relayDirectory = Path.Combine(packed, "Relay");
        }
        else
        {
            usbDirectory = Path.Combine(source, "Tools", "DashboardUsb");
            relayDirectory = Path.Combine(source, "Tools", "DashboardRelay");
            foreach (var entry in new[] { new { Needed = !usb, Directory = usbDirectory, Project = "DashboardUsb" },
                new { Needed = !relay, Directory = relayDirectory, Project = "DashboardRelay" } })
            {
                if (!entry.Needed) continue;
                if (!File.Exists(Path.Combine(entry.Directory, entry.Project + ".csproj")))
                    throw new IOException("USB自動起動用の補助アプリがありません。ゲームを新しいビルドへ更新してください。");
                run(entry.Directory, Quote(dotnet) + " build " + entry.Project + ".csproj --nologo -v:q", true);
            }
            usbDirectory = Path.Combine(usbDirectory, "bin", "Debug", "net10.0");
            relayDirectory = Path.Combine(relayDirectory, "bin", "Debug", "net10.0");
        }
        if (!relay) run(relayDirectory, Quote(dotnet) + " " + Quote(Path.Combine(relayDirectory, "DashboardRelay.dll")) + " --no-browser --local-startup", false);
        if (!usb) run(usbDirectory, Quote(dotnet) + " " + Quote(Path.Combine(usbDirectory, "DashboardUsb.dll")) + " --iphone --no-browser", false);
    }

    internal static string Extract(string archive, string cache)
    {
        string digest;
        using (var sha = SHA256.Create()) using (var input = File.OpenRead(archive))
            digest = BitConverter.ToString(sha.ComputeHash(input)).Replace("-", "").ToLowerInvariant();
        string target = Path.Combine(cache, digest);
        if (!Directory.Exists(target))
        {
            string temporary = target + "." + Guid.NewGuid().ToString("N");
            try { ZipFile.ExtractToDirectory(archive, temporary); Directory.Move(temporary, target); }
            catch (IOException) { if (!Directory.Exists(target)) throw; }
            finally { if (Directory.Exists(temporary)) Directory.Delete(temporary, true); }
        }
        return target;
    }
    private static string FindDotnet()
    {
        foreach (string path in new[] { "/usr/local/share/dotnet/dotnet", "/opt/homebrew/bin/dotnet", "/usr/local/bin/dotnet" })
            if (File.Exists(path)) return path;
        throw new IOException("USB補助アプリには.NET 10とASP.NET Core Runtimeが必要です（SDKには両方含まれます）。");
    }
    private static bool Probe(string url)
    {
        try
        {
            var request = (HttpWebRequest)WebRequest.Create(url); request.Proxy = null; request.Timeout = 750; request.ReadWriteTimeout = 750;
            using (var response = request.GetResponse()) using (var reader = new StreamReader(response.GetResponseStream()))
            {
                string json = reader.ReadToEnd();
                return url.Contains(":8766/") ? json.Contains("\"racing-dashboard\"") :
                    json.Contains("\"racing-usb-dashboard\"") && json.Contains("\"iphone\"") && json.Contains("\"racing-usb-v2\"");
            }
        }
        catch (WebException) { return false; }
    }
    private static string Quote(string value) { return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\""; }
    private static void Run(string working, string command, bool wait, string log)
    {
        // The executable is separate from arguments so source/project paths never reach a shell.
        int end = command.IndexOf('"', 1);
        var info = new ProcessStartInfo(command.Substring(1, end - 1), command.Substring(end + 1).TrimStart())
        { WorkingDirectory = working, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        info.EnvironmentVariables["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        var process = new Process { StartInfo = info };
        DataReceivedEventHandler write = (_, data) =>
        {
            if (data.Data == null) return;
            try { lock (logLock) File.AppendAllText(log, data.Data + Environment.NewLine); }
            catch (IOException) { } catch (UnauthorizedAccessException) { }
        };
        process.OutputDataReceived += write; process.ErrorDataReceived += write;
        process.Start(); process.BeginOutputReadLine(); process.BeginErrorReadLine();
        if (wait)
        {
            if (!process.WaitForExit(120000)) { process.Kill(); process.Dispose(); throw new IOException("USB補助アプリの準備がタイムアウトしました。"); }
            process.WaitForExit(); int code = process.ExitCode; process.Dispose();
            if (code != 0) throw new IOException("USB補助アプリをビルドできません。startup.logを確認してください。");
        }
        else lock (children) { children.RemoveAll(child => { if (!child.HasExited) return false; child.Dispose(); return true; }); children.Add(process); }
    }
}
