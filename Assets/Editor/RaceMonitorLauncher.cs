using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEngine;

// Independent of the web relay. Only the child owned by this Editor is stopped.
[InitializeOnLoad]
public static class RaceMonitorLauncher
{
    private static Process process;
    private static readonly ConcurrentQueue<string> output = new ConcurrentQueue<string>();
    private static bool stopping, reused;
    private static double stopDeadline;
    internal static string Message { get; private set; } = "運用監視を準備しています。";

    static RaceMonitorLauncher()
    {
        EditorApplication.update += Tick;
        EditorApplication.delayCall += () => { if (!Application.isBatchMode) Start(); };
        EditorApplication.playModeStateChanged += change =>
        {
            if (change == PlayModeStateChange.EnteredPlayMode && !Application.isBatchMode) Start();
            if (change == PlayModeStateChange.ExitingPlayMode) Stop();
        };
        AssemblyReloadEvents.beforeAssemblyReload += Shutdown;
        EditorApplication.quitting += Shutdown;
    }

    [MenuItem("Racing/Monitor/Start")]
    public static void Start()
    {
        if (Application.isBatchMode || (process != null && !process.HasExited)) return;
        string directory = Path.GetFullPath(Path.Combine(Application.dataPath, "../Tools/RaceMonitor"));
        string script = Path.Combine(directory, "monitor.py");
        string config = Environment.GetEnvironmentVariable("RACING_MONITOR_CONFIG");
        if (string.IsNullOrEmpty(config))
        {
            config = Path.Combine(directory, "monitor.local.json");
            if (!File.Exists(config)) config = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Library/Application Support/RacingMonitor/monitor.local.json");
        }
        if (!File.Exists(script) || !File.Exists(config))
        {
            Message = "運用監視は未設定です。Tools/RaceMonitor/monitor.local.json にWebhook URLを設定してください。";
            UnityEngine.Debug.Log("Race monitor: " + Message);
            return;
        }
        string python = Environment.GetEnvironmentVariable("RACING_MONITOR_PYTHON");
        if (string.IsNullOrEmpty(python))
        {
            python = Application.platform == RuntimePlatform.WindowsEditor ? "python" : "python3";
            foreach (string candidate in new[] { "/opt/homebrew/bin/python3", "/usr/local/bin/python3" })
                if (File.Exists(candidate)) { python = candidate; break; }
        }
        try
        {
            process?.Dispose();
            var info = new ProcessStartInfo(python, "-u " + Quote(script) + " --config " + Quote(Path.GetFullPath(config)) +
                " --managed --hold-seconds 5 --poll-seconds 1")
            {
                WorkingDirectory = directory, UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true
            };
            process = new Process { StartInfo = info };
            process.OutputDataReceived += (_, entry) => { if (entry.Data != null) output.Enqueue(entry.Data); };
            process.ErrorDataReceived += (_, entry) => { if (entry.Data != null) output.Enqueue(entry.Data); };
            process.Start(); process.BeginOutputReadLine(); process.BeginErrorReadLine();
            stopping = reused = false;
            Message = "運用監視を起動中です。";
        }
        catch (Exception)
        {
            process?.Dispose(); process = null;
            Message = "運用監視を起動できません。Python 3.10以降と通知設定を確認してください。";
            UnityEngine.Debug.LogWarning("Race monitor: " + Message);
        }
    }

    [MenuItem("Racing/Monitor/Stop")]
    public static void Stop()
    {
        if (process == null || process.HasExited || stopping) return;
        stopping = true; stopDeadline = EditorApplication.timeSinceStartup + 4;
        try { process.StandardInput.WriteLine("stop"); process.StandardInput.Flush(); }
        catch (IOException) { }
    }

    private static string Quote(string value) => "\"" + value.Replace("\"", "\\\"") + "\"";

    private static void Tick()
    {
        while (output.TryDequeue(out string line))
        {
            if (line == "RACING_MONITOR_READY") Message = "運用監視中：エラーが5秒継続すると通知します。Play前は待機します。";
            else if (line == "RACING_MONITOR_REUSED")
            { reused = true; Message = "既に動作している運用監視を利用します。停止は起動元で行ってください。"; }
            else if (line.StartsWith("Webhook", StringComparison.Ordinal) || line.StartsWith("監視", StringComparison.Ordinal))
                UnityEngine.Debug.LogWarning("Race monitor: " + line);
        }
        if (process == null || !process.HasExited)
        {
            if (process != null && stopping && EditorApplication.timeSinceStartup >= stopDeadline) process.Kill();
            return;
        }
        int code = process.ExitCode;
        process.Dispose(); process = null;
        if (stopping) { stopping = false; Message = "運用監視を停止しました。次のPlay開始時に再開します。"; }
        else if (code != 0)
        {
            Message = "運用監視が終了しました。Python 3.10以降・Webhook設定・監視ログを確認してください。";
            UnityEngine.Debug.LogWarning("Race monitor: " + Message);
        }
        else if (!reused) Message = "運用監視を停止しました。Racing > Monitor > Start または次のPlayで再開します。";
    }

    private static void Shutdown()
    {
        if (process == null) return;
        try
        {
            if (!process.HasExited)
            {
                process.StandardInput.WriteLine("stop"); process.StandardInput.Flush();
                if (!process.WaitForExit(1000)) process.Kill();
            }
        }
        catch (Exception) { }
        finally { process.Dispose(); process = null; }
    }
}
