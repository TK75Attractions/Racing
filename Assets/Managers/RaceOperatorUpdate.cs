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
    private string exitRequestId;
    private Gmanager race;
    public bool Busy => worker != null;
    public string Status { get; private set; } = "最新mainを別フォルダーでビルド・起動します。Ctrl/Cmd + Alt + U";
    public bool Available => !Busy && FindProject() != null && File.Exists(FindUnity());
    private bool CanLeaveRace
    {
        get
        {
#if UNITY_EDITOR
            if (!UnityEditor.EditorApplication.isPlaying) return true;
#endif
            return race != null && race.UpdateExitReady;
        }
    }

    private void Awake() => race = GetComponent<Gmanager>();
    public void Configure(Gmanager manager) => race = manager;

    public bool Request()
    {
        if (Busy) return false;
        if (FindProject() == null) { Status = "更新元プロジェクトが見つかりません。RACING_PROJECT_PATHを設定してください。"; return false; }
        if (!File.Exists(FindUnity())) { Status = "Unity Editorが見つかりません。RACING_UNITY_PATHを設定してください。"; return false; }
        try
        {
            string project = FindProject();
            bool windows = Application.platform == RuntimePlatform.WindowsPlayer || Application.platform == RuntimePlatform.WindowsEditor;
            string name = windows ? "update-and-run.ps1" : "update-and-run.sh";
            requestFolder = Path.Combine(project, "Logs", "OperatorUpdate", DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(requestFolder);
            File.WriteAllText(Path.Combine(requestFolder, "handoff.wait"), "runtime");
            string runner = Path.Combine(requestFolder, name);
            File.Copy(Path.Combine(project, "Tools", "OperatorUpdate", name), runner);
            int parentPid = Process.GetCurrentProcess().Id;
#if UNITY_EDITOR
            parentPid = 0; // Leave the working Editor and unsaved scenes open.
#endif
            string args = Quote(runner) + " " + Quote(project) + " " + Quote(FindUnity()) + " " + parentPid + " " + Quote(requestFolder);
            bool detached = !windows && File.Exists("/usr/bin/perl");
            if (detached) args = "-MPOSIX -e " + Quote("POSIX::setsid() >= 0 or die $!; exec '/bin/bash', @ARGV or die $!;") + " " + args;
            worker = Process.Start(new ProcessStartInfo {
                FileName = windows ? "powershell.exe" : detached ? "/usr/bin/perl" : "/bin/bash",
                Arguments = windows ? "-NoProfile -ExecutionPolicy Bypass -File " + args : args,
                WorkingDirectory = project, UseShellExecute = false, CreateNoWindow = true
            }) ?? throw new IOException("更新プログラムを起動できませんでした。");
            Status = "更新を開始しました。ログ: " + requestFolder;
            return true;
        }
        catch (Exception error) { worker = null; RestoreMaintenance(); Status = "更新開始に失敗: " + error.Message; return false; }
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
            Status = (File.Exists(Path.Combine(requestFolder, "status.txt")) ? File.ReadAllText(Path.Combine(requestFolder, "status.txt")).Trim() : "更新を停止しました。") + " ログ: " + requestFolder;
            if (!File.Exists(Path.Combine(requestFolder, "succeeded"))) RestoreMaintenance();
            worker.Dispose(); worker = null;
            return;
        }
        if (File.Exists(Path.Combine(requestFolder,"built")) && !File.Exists(Path.Combine(requestFolder,"launch.allowed")))
        {
#if UNITY_EDITOR
            if (!UnityEditor.EditorApplication.isPlaying)
                File.WriteAllText(Path.Combine(requestFolder,"launch.allowed"),"edit-mode");
            else
#endif
            if(PrepareUpdateExit(Path.GetFileName(requestFolder)) && CanLeaveRace)
                File.WriteAllText(Path.Combine(requestFolder,"launch.allowed"),"safe");
        }
        if (File.Exists(Path.Combine(requestFolder, "ready")))
        {
            if (!CanLeaveRace) return;
            // The launcher publishes ready only after a successful build and the
            // atomic launch decision. A cancelled/failed build never closes this game.
            File.WriteAllText(Path.Combine(requestFolder, "released"), "runtime");
#if UNITY_EDITOR
            // Leave scenes open, but release the running game's devices before launching the new player.
            if (UnityEditor.EditorApplication.isPlaying) UnityEditor.EditorApplication.isPlaying = false;
            return;
#else
            Application.Quit();
#endif
        }
        else if (File.Exists(Path.Combine(requestFolder, "status.txt")))
            Status = File.ReadAllText(Path.Combine(requestFolder, "status.txt")).Trim() + " ログ: " + requestFolder;
    }

    private void RestoreMaintenance()
    {
        if(exitRequestId!=null)race?.CancelUpdateExit();
        exitRequestId=null;
    }
    private string VerifiedFolder(string requestId)
    {
        if(string.IsNullOrEmpty(requestId) || requestId.Length>80)return null;
        foreach(char c in requestId)
            if(!(c>='a'&&c<='z'||c>='A'&&c<='Z'||c>='0'&&c<='9'||c=='-'||c=='_'))return null;
        string project=FindProject();if(project==null)return null;
        string folder=Path.Combine(project,"Logs","OperatorUpdate",requestId);
        string active=Path.Combine(project,"Logs","OperatorUpdate.lock","request.txt");
        if(!File.Exists(active)||Path.GetFullPath(File.ReadAllText(active).Trim().TrimStart('\uFEFF'))!=Path.GetFullPath(folder)||
            File.Exists(Path.Combine(folder,"failed"))||File.Exists(Path.Combine(folder,"cancelled"))||File.Exists(Path.Combine(folder,"succeeded")))return null;
        if(File.Exists(Path.Combine(folder,"decision"))&&File.ReadAllText(Path.Combine(folder,"decision")).Trim()!="launch")return null;
        return folder;
    }
    public bool PrepareUpdateExit(string requestId)
    {
        string folder=VerifiedFolder(requestId);
        if(folder==null || !File.Exists(Path.Combine(folder,"built")) || race==null || exitRequestId!=null&&exitRequestId!=requestId)return false;
        if(!race.BeginUpdateExit())return false;
        exitRequestId=requestId;return true;
    }
    public bool CancelUpdateExit(string requestId)
    {
        if(string.IsNullOrEmpty(requestId)||exitRequestId!=requestId)return false;
        string project=FindProject();if(project==null)return false;
        string folder=Path.Combine(project,"Logs","OperatorUpdate",requestId);
        if(File.Exists(Path.Combine(folder,"decision")) && File.ReadAllText(Path.Combine(folder,"decision")).Trim()=="launch" &&
            !File.Exists(Path.Combine(folder,"handoff.failed")) && !File.Exists(Path.Combine(folder,"failed")))return false;
        RestoreMaintenance();return true;
    }

    public bool PrepareForLaunch(string requestId)
    {
        if(!CanLeaveRace)return false;
        string folder=VerifiedFolder(requestId);
        if(folder==null || !File.Exists(Path.Combine(folder,"ready")) || !File.Exists(Path.Combine(folder,"decision")) ||
            File.ReadAllText(Path.Combine(folder,"decision")).Trim()!="launch")return false;
#if UNITY_EDITOR
        // Acknowledge the guarded request before Play closes its HTTP bridge.
        UnityEditor.EditorApplication.delayCall += () => UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
        return true;
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
        if (!string.IsNullOrWhiteSpace(configured)) return ResolveEditorExecutable(configured);
#if UNITY_EDITOR
        return ResolveEditorExecutable(UnityEditor.EditorApplication.applicationPath);
#else
        bool windows = Application.platform == RuntimePlatform.WindowsPlayer;
        return windows ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Unity", "Hub", "Editor", Application.unityVersion, "Editor", "Unity.exe")
            : "/Applications/Unity/Hub/Editor/" + Application.unityVersion + "/Unity.app/Contents/MacOS/Unity";
#endif
    }

    private static string ResolveEditorExecutable(string path) =>
        path.EndsWith(".app", StringComparison.OrdinalIgnoreCase) ? Path.Combine(path, "Contents", "MacOS", "Unity") : path;

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
