using System;
using System.Diagnostics;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>Hand off to an external updater, which survives closing this Editor/player.</summary>
[DisallowMultipleComponent]
public sealed class RaceOperatorUpdate : MonoBehaviour
{
    private Process worker;
    private string requestFolder;
    private double requestedAt;
    private Gmanager race;
    public bool Busy => worker != null;
    public string Status { get; private set; } = "最新mainを取得し、Unityを終了してビルド・起動します。Ctrl/Cmd + Alt + U";
    public bool Available => !Busy && CanLeaveRace && FindProject() != null && File.Exists(FindUnity());
    private bool CanLeaveRace => race != null && (race.IsMaintenanceActive || race.state == Gmanager.State.Title || race.state == Gmanager.State.Result);

    private void Awake() => race = GetComponent<Gmanager>();
    public void Configure(Gmanager manager) => race = manager;

    public bool Request()
    {
        if (!Available) { Status = "タイトル・結果・メンテナンス画面で実行してください。プロジェクトとUnity Editorも必要です。"; return false; }
#if UNITY_EDITOR
        for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
            if (UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).isDirty)
            { Status = "未保存のシーンがあります。保存してから更新してください。"; return false; }
#endif
        try
        {
            string project = FindProject();
            bool windows = Application.platform == RuntimePlatform.WindowsPlayer || Application.platform == RuntimePlatform.WindowsEditor;
            string name = windows ? "update-and-run.ps1" : "update-and-run.sh";
            requestFolder = Path.Combine(project, "Logs", "OperatorUpdate", DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(requestFolder);
            string runner = Path.Combine(requestFolder, name);
            File.Copy(Path.Combine(project, "Tools", "OperatorUpdate", name), runner);
            string args = Quote(runner) + " " + Quote(project) + " " + Quote(FindUnity()) + " " + Process.GetCurrentProcess().Id + " " + Quote(requestFolder);
            worker = Process.Start(new ProcessStartInfo {
                FileName = windows ? "powershell.exe" : "/bin/bash",
                Arguments = windows ? "-NoProfile -ExecutionPolicy Bypass -File " + args : args,
                WorkingDirectory = project, UseShellExecute = false, CreateNoWindow = true
            });
            requestedAt = Time.realtimeSinceStartupAsDouble;
            Status = "更新前チェック中。成功するとUnityを終了します。ログ: " + requestFolder;
            return true;
        }
        catch (Exception error) { worker = null; Status = "更新開始に失敗: " + error.Message; return false; }
    }

    private void Update()
    {
#if !UNITY_EDITOR
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null && (keyboard.ctrlKey.isPressed || keyboard.leftMetaKey.isPressed || keyboard.rightMetaKey.isPressed) &&
            keyboard.altKey.isPressed && keyboard.uKey.wasPressedThisFrame) Request();
#endif
        Poll();
    }

    public void Poll()
    {
        if (worker == null) return;
        if (File.Exists(Path.Combine(requestFolder, "failed")) || worker.HasExited)
        {
            Status = "更新を停止しました。未コミット変更・Git・Unityの設定を確認してください。ログ: " + requestFolder;
            worker.Dispose(); worker = null;
            return;
        }
        if (File.Exists(Path.Combine(requestFolder, "ready")))
        {
            // The worker rechecks Git after shutdown, then builds into a fresh directory.
#if UNITY_EDITOR
            UnityEditor.EditorApplication.Exit(0);
#else
            Application.Quit();
#endif
        }
        else if (Time.realtimeSinceStartupAsDouble - requestedAt > 120)
        {
            worker.Kill(); worker.Dispose(); worker = null;
            Status = "更新前チェックがタイムアウトしました。ログ: " + requestFolder;
        }
    }

    private static string FindProject()
    {
        string configured = Environment.GetEnvironmentVariable("RACING_PROJECT_PATH");
        foreach (string start in new[] { configured, Application.dataPath, Environment.CurrentDirectory })
        {
            if (string.IsNullOrWhiteSpace(start)) continue;
            for (var dir = new DirectoryInfo(start); dir != null; dir = dir.Parent)
                if ((Directory.Exists(Path.Combine(dir.FullName, ".git")) || File.Exists(Path.Combine(dir.FullName, ".git"))) &&
                    Directory.Exists(Path.Combine(dir.FullName, "Tools", "OperatorUpdate"))) return dir.FullName;
        }
        return null;
    }

    private static string FindUnity()
    {
        string configured = Environment.GetEnvironmentVariable("RACING_UNITY_PATH");
        if (!string.IsNullOrWhiteSpace(configured)) return configured;
#if UNITY_EDITOR
        return UnityEditor.EditorApplication.applicationPath;
#else
        bool windows = Application.platform == RuntimePlatform.WindowsPlayer;
        return windows ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Unity", "Hub", "Editor", Application.unityVersion, "Editor", "Unity.exe")
            : "/Applications/Unity/Hub/Editor/" + Application.unityVersion + "/Unity.app/Contents/MacOS/Unity";
#endif
    }

    // Windows CRT argument quoting also works for ProcessStartInfo's Unix argv parser.
    private static string Quote(string value)
    {
        var text = new System.Text.StringBuilder("\"");
        int slashes = 0;
        foreach (char c in value)
        {
            if (c == '\\') { slashes++; continue; }
            text.Append('\\', c == '"' ? slashes * 2 + 1 : slashes); slashes = 0; text.Append(c);
        }
        return text.Append('\\', slashes * 2).Append('"').ToString();
    }
}
