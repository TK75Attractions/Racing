using System;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

// The runtime payload is prepared before Unity copies StreamingAssets/signs the player.
public sealed class RaceUsbPlayerPackaging : IPreprocessBuildWithReport
{
    public int callbackOrder { get { return 0; } }
    public void OnPreprocessBuild(BuildReport report)
    {
        if (report.summary.platform != BuildTarget.StandaloneOSX) return;
        string root = Path.GetFullPath(Path.Combine(UnityEngine.Application.dataPath, ".."));
        var info = new ProcessStartInfo("/bin/sh", "\"" + Path.Combine(root, "Tools/DashboardUsb/prepare-player.sh") + "\"")
        { WorkingDirectory = root, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        using (var process = new Process { StartInfo = info })
        {
            process.OutputDataReceived += (_, data) => { if (data.Data != null) UnityEngine.Debug.Log(data.Data); };
            process.ErrorDataReceived += (_, data) => { if (data.Data != null) UnityEngine.Debug.LogWarning(data.Data); };
            process.Start(); process.BeginOutputReadLine(); process.BeginErrorReadLine();
            if (!process.WaitForExit(120000)) { process.Kill(); throw new BuildFailedException("USB helper packaging timed out."); }
            process.WaitForExit(); if (process.ExitCode != 0) throw new BuildFailedException("USB helper packaging failed.");
        }
        AssetDatabase.Refresh();
    }
}
