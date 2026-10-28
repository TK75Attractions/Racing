using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>Windows x64 packaging and checks shared by the menu and Build Profiles.</summary>
public sealed class WindowsPlayerBuild : IPreprocessBuildWithReport
{
    public int callbackOrder => 0;
    private const string PluginPath = "Assets/Plugins/RaceAudio/Windows/RaceAudio.dll";
    private static readonly string[] RequiredAudioApis = {
        "ra_refresh_devices", "ra_device_name", "ra_device_key", "ra_open", "ra_is_started",
        "ra_close", "ra_shutdown", "ra_push", "ra_set_min_buffer_milliseconds", "ra_get_underruns"
    };

    public void OnPreprocessBuild(BuildReport report)
    {
        if (report.summary.platform != BuildTarget.StandaloneWindows64) return;
        ValidateAudioPlugin(PluginPath);
        var importer = AssetImporter.GetAtPath(PluginPath) as PluginImporter;
        if (importer == null || !importer.GetCompatibleWithPlatform(BuildTarget.StandaloneWindows64))
            throw new BuildFailedException("RaceAudio.dll の Import Settings で Windows x86_64 を有効にしてください。");
    }

    internal static void ValidateAudioPlugin(string path)
    {
        const string repair = "Unityを終了し、Windowsで Native~\\RaceAudio\\build-windows.cmd を実行してから再度開いてください。";
        if (!File.Exists(path)) throw new BuildFailedException("Windows音声DLLがありません。" + repair);
        byte[] binary = File.ReadAllBytes(path);
        if (binary.Length < 64 || binary[0] != 'M' || binary[1] != 'Z')
            throw new BuildFailedException("Windows音声DLLが不正です。" + repair);
        int pe = BitConverter.ToInt32(binary, 60);
        if (pe < 64 || pe > binary.Length - 6 || BitConverter.ToUInt32(binary, pe) != 0x4550 ||
            BitConverter.ToUInt16(binary, pe + 4) != 0x8664)
            throw new BuildFailedException("Windows音声DLLはx64版が必要です。" + repair);
        // Export names are NUL-terminated ASCII in the PE image. Read without loading
        // the DLL so cross-builds work and the Editor never locks the Windows plugin.
        string image = Encoding.ASCII.GetString(binary);
        string[] missing = RequiredAudioApis.Where(api => !image.Contains("\0" + api + "\0")).ToArray();
        if (missing.Length > 0)
            throw new BuildFailedException("Windows音声DLLが古く、必要なAPIがありません: " + string.Join(", ", missing) + "。" + repair);
    }

    [MenuItem("Racing/Build/Windows x64")]
    public static void Build()
    {
        if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Standalone, BuildTarget.StandaloneWindows64))
            throw new BuildFailedException("Unity Hubで6000.3.9f1のWindows Build Support (Mono)を追加してください。");
        ValidateAudioPlugin(PluginPath);
        if (!Application.isBatchMode && !UnityEditor.SceneManagement.EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);
        PlayerSettings.SetApiCompatibilityLevel(NamedBuildTarget.Standalone, ApiCompatibilityLevel.NET_Unity_4_8);
        string path = Path.GetFullPath("Builds/Windows/Race.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
            scenes = new[] { "Assets/Scenes/SampleScene.unity" },
            locationPathName = path, target = BuildTarget.StandaloneWindows64, options = BuildOptions.None
        });
        if (report.summary.result != BuildResult.Succeeded)
            throw new BuildFailedException("Windowsビルドに失敗しました。Console / Editor.logを確認してください。");
        Debug.Log("WINDOWS_BUILD_PASS: " + path);
    }
}
