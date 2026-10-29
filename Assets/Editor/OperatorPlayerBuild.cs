using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

/// <summary>Batch entry point used by the operator's external update process.</summary>
public static class OperatorPlayerBuild
{
    [MenuItem("Racing/Build/Operator Player and Run")]
    public static void Build()
    {
        string path = Environment.GetEnvironmentVariable("RACING_OPERATOR_BUILD");
        if (string.IsNullOrWhiteSpace(path))
        {
            if (UnityEngine.Application.isBatchMode) throw new BuildFailedException("RACING_OPERATOR_BUILD is required.");
#if UNITY_EDITOR_WIN
            path = Path.GetFullPath("Builds/Operator/Local/Race.exe");
#else
            path = Path.GetFullPath("Builds/Operator/Local/Race.app");
#endif
        }
#if UNITY_EDITOR_WIN
        var target = BuildTarget.StandaloneWindows64;
        WindowsPlayerBuild.ValidateAudioPlugin("Assets/Plugins/RaceAudio/Windows/RaceAudio.dll");
#else
        var target = BuildTarget.StandaloneOSX;
#endif
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
            scenes = new[] { "Assets/Scenes/SampleScene.unity" }, locationPathName = path,
            target = target, options = UnityEngine.Application.isBatchMode ? BuildOptions.None : BuildOptions.AutoRunPlayer
        });
        if (report.summary.result != BuildResult.Succeeded) throw new BuildFailedException("Operator build failed: " + report.summary.result);
        UnityEngine.Debug.Log("OPERATOR_BUILD_PASS: " + path);
    }
}
