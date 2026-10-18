using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Only processes started here are stopped here. Existing relays are reused.
[InitializeOnLoad]
public static class RaceDashboardLauncher
{
    internal const string AutoKey = "Racing.Dashboard.AutoStart";
    internal const string OperatorsKey = "Racing.Dashboard.Operators";
    private static Process process;
    private static bool building, stopping, reused, openWhenReady;
    private static double stopDeadline;
    private static readonly ConcurrentQueue<string> output = new ConcurrentQueue<string>();
    internal static string Message { get; private set; } = "Play開始時にWebSocketを自動起動します。";
    internal static string LocalUrl { get; private set; }
    internal static string RemoteUrl { get; private set; }
    internal static string ConsentUrl { get; private set; }
    internal static bool Busy => building || (process != null && string.IsNullOrEmpty(LocalUrl)) || stopping;
    [Serializable] private sealed class ReadyInfo { public string localUrl, remoteUrl, notice; public bool reused; }

    static RaceDashboardLauncher()
    {
        EditorApplication.update += Tick;
        EditorApplication.playModeStateChanged += change =>
        {
            if (change == PlayModeStateChange.EnteredPlayMode)
            {
                bool open = SessionState.GetBool("Racing.Dashboard.OpenOnPlay", false);
                SessionState.EraseBool("Racing.Dashboard.OpenOnPlay");
                if (open || (EditorPrefs.GetBool(AutoKey, true) && !Application.isBatchMode)) Start(open);
            }
            if (change == PlayModeStateChange.ExitingPlayMode) Stop();
        };
        AssemblyReloadEvents.beforeAssemblyReload += Shutdown;
        EditorApplication.quitting += Shutdown;
    }
    [MenuItem("Racing/Dashboard")]
    public static void Open() => EditorWindow.GetWindow<RaceDashboardWindow>("レース管理");

    [MenuItem("Racing/Start Dashboard and Play")]
    public static void StartAndPlay()
    {
        if (EditorApplication.isPlaying) { Start(true); return; }
        if (UnityEngine.Object.FindFirstObjectByType<Gmanager>() == null)
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        }
        SessionState.SetBool("Racing.Dashboard.OpenOnPlay", true);
        EditorApplication.isPlaying = true;
    }

    internal static void Start(bool openBrowser)
    {
        if (process != null && !process.HasExited)
        {
            if (openBrowser && !string.IsNullOrEmpty(LocalUrl)) Application.OpenURL(LocalUrl);
            else openWhenReady |= openBrowser;
            return;
        }
        string directory = Path.GetFullPath(Path.Combine(Application.dataPath, "../Tools/DashboardRelay"));
        if (!File.Exists(Path.Combine(directory, "DashboardRelay.csproj")))
        { Message = "補助ブリッジのファイルがありません。Tools/DashboardRelayを確認してください。"; return; }
        LocalUrl = RemoteUrl = ConsentUrl = null;
        stopping = reused = false; openWhenReady = openBrowser;
        Message = "WebSocketの起動準備中…";
        try { building = true; Launch(directory, "build DashboardRelay.csproj --nologo -v:q"); }
        catch (Exception error) { building = false; process = null; Message = ".NET 10 SDKをインストールしてください。\n" + error.Message; }
    }
    internal static void Stop()
    {
        if (process == null || process.HasExited)
        { if (reused) Message = "既に起動していたブリッジを利用中です。起動元で停止してください。"; return; }
        Message = "WebSocketを停止しています…";
        stopping = true; stopDeadline = EditorApplication.timeSinceStartup + 3;
        if (building) { process.Kill(); return; }
        try { process.StandardInput.WriteLine("stop"); process.StandardInput.Flush(); }
        catch (IOException) { }
    }
    private static void Launch(string directory, string arguments)
    {
        string executable = "dotnet";
        foreach (string candidate in new[] { "/usr/local/share/dotnet/dotnet", "/opt/homebrew/bin/dotnet", "/usr/local/bin/dotnet",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet", "dotnet.exe") })
            if (File.Exists(candidate)) { executable = candidate; break; }
        var info = new ProcessStartInfo(executable, arguments)
        {
            WorkingDirectory = directory, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true
        };
        info.EnvironmentVariables["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        string operators = EditorPrefs.GetString(OperatorsKey, "").Trim();
        if (operators.Length > 0) info.EnvironmentVariables["RACING_DASHBOARD_OPERATORS"] = operators;
        process = new Process { StartInfo = info };
        process.OutputDataReceived += (_, entry) => { if (entry.Data != null) output.Enqueue(entry.Data); };
        process.ErrorDataReceived += (_, entry) => { if (entry.Data != null) output.Enqueue(entry.Data); };
        process.Start(); process.BeginOutputReadLine(); process.BeginErrorReadLine();
    }
    private static void Tick()
    {
        while (output.TryDequeue(out string line))
        {
            const string prefix = "RACING_DASHBOARD_READY ";
            if (line.StartsWith(prefix, StringComparison.Ordinal))
            {
                try
                {
                    ReadyInfo ready = JsonUtility.FromJson<ReadyInfo>(line.Substring(prefix.Length));
                    if (ready == null || ready.localUrl != "http://127.0.0.1:8766/") continue;
                    LocalUrl = ready.localUrl;
                    RemoteUrl = Uri.TryCreate(ready.remoteUrl, UriKind.Absolute, out Uri uri) && uri.Scheme == "https" &&
                        uri.Host.EndsWith(".ts.net", StringComparison.OrdinalIgnoreCase) ? ready.remoteUrl : null;
                    reused = ready.reused;
                    Message = string.IsNullOrEmpty(ready.notice) ? "WebSocketの準備ができました。" : ready.notice;
                    Match consent = Regex.Match(Message, @"https://(?:login|control)\.tailscale\.com/[a-zA-Z0-9/_?=&%.\-]+");
                    ConsentUrl = consent.Success ? consent.Value : null;
                    UnityEngine.Debug.Log("Race dashboard: " + LocalUrl + (RemoteUrl == null ? "" : " / 別端末: " + RemoteUrl));
                    if (openWhenReady) { openWhenReady = false; Application.OpenURL(LocalUrl); }
                }
                catch (Exception error) { Message = "起動結果を読み取れませんでした。" + error.Message; }
            }
            else if (!string.IsNullOrWhiteSpace(line) && string.IsNullOrEmpty(LocalUrl)) Message = line;
        }
        if (process == null) return;
        if (stopping && !process.HasExited && EditorApplication.timeSinceStartup >= stopDeadline) process.Kill();
        if (!process.HasExited) return;
        int code = process.ExitCode;
        string directory = process.StartInfo.WorkingDirectory;
        process.Dispose(); process = null;
        if (building && !stopping && code == 0)
        {
            building = false;
            string dll = Path.Combine(directory, "bin/Debug/net10.0/DashboardRelay.dll");
            try { Launch(directory, "\"" + dll.Replace("\"", "\\\"") + "\" --auto --no-browser --managed"); }
            catch (Exception error) { Message = "起動できませんでした。" + error.Message; }
        }
        else
        {
            building = false;
            if (stopping) { LocalUrl = RemoteUrl = null; stopping = false; Message = "WebSocketを停止しました。"; }
            else if (!reused) { LocalUrl = RemoteUrl = null; Message = code == 0 ? "WebSocketを停止しました。" : "起動できませんでした。\n" + Message; }
        }
    }
    private static void Shutdown()
    {
        if (process == null) return;
        try
        {
            if (!process.HasExited)
            {
                if (!building) { process.StandardInput.WriteLine("stop"); process.StandardInput.Flush(); }
                if (!process.WaitForExit(500)) process.Kill();
            }
        }
        catch (Exception) { }
        finally { process.Dispose(); process = null; }
    }
}

public sealed class RaceDashboardWindow : EditorWindow
{
    private void OnEnable() => minSize = new Vector2(470, 310);
    private void OnInspectorUpdate() => Repaint();
    private void OnGUI()
    {
        EditorGUILayout.LabelField("レース管理・シリアル監視", EditorStyles.boldLabel);
        bool automatic = EditorPrefs.GetBool(RaceDashboardLauncher.AutoKey, true);
        bool next = EditorGUILayout.Toggle("Play開始時に自動起動", automatic);
        if (next != automatic) EditorPrefs.SetBool(RaceDashboardLauncher.AutoKey, next);
        string operators = EditorPrefs.GetString(RaceDashboardLauncher.OperatorsKey, "");
        string selected = EditorGUILayout.TextField("操作担当者（任意）", operators);
        if (selected != operators) EditorPrefs.SetString(RaceDashboardLauncher.OperatorsKey, selected);
        EditorGUILayout.LabelField("空欄ではこのPCのTailscaleユーザーを使用。複数人はカンマ区切り。", EditorStyles.wordWrappedMiniLabel);
        EditorGUILayout.HelpBox("UnityをPlayして「ダッシュボードを開く」を押してください。別端末ではTailscaleを接続し、下のURLを開きます。", MessageType.Info);
        using (new EditorGUI.DisabledScope(RaceDashboardLauncher.Busy))
            if (GUILayout.Button("ダッシュボードを開く", GUILayout.Height(34))) RaceDashboardLauncher.Start(true);
        EditorGUILayout.HelpBox(RaceDashboardLauncher.Message, MessageType.Info);
        EditorGUILayout.HelpBox(RaceMonitorLauncher.Message, MessageType.Info);
        if (!string.IsNullOrEmpty(RaceDashboardLauncher.RemoteUrl))
        {
            EditorGUILayout.LabelField("別端末で開くURL");
            EditorGUILayout.SelectableLabel(RaceDashboardLauncher.RemoteUrl, EditorStyles.textField, GUILayout.Height(22));
            if (GUILayout.Button("別端末用URLをコピー")) EditorGUIUtility.systemCopyBuffer = RaceDashboardLauncher.RemoteUrl;
        }
        if (!string.IsNullOrEmpty(RaceDashboardLauncher.ConsentUrl) && GUILayout.Button("Tailscaleの初回HTTPS設定を開く")) Application.OpenURL(RaceDashboardLauncher.ConsentUrl);
        EditorGUILayout.Space();
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Tailscaleを入れる")) Application.OpenURL("https://tailscale.com/download");
            if (GUILayout.Button(".NET 10を入れる")) Application.OpenURL("https://dotnet.microsoft.com/download/dotnet/10.0");
            if (GUILayout.Button("停止")) RaceDashboardLauncher.Stop();
        }
    }
}
