using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>Build and verify opt-in standalone rendering measurements.</summary>
public static class StandalonePerformanceValidation
{
    private const string RendererPath = "Assets/Settings/PC_Renderer.asset";
    private const string PipelinePath = "Assets/Settings/PC_RPAsset.asset";

    [MenuItem("Racing/Validation/Standalone Rendering")]
    public static void Validate()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        MultiDisplayValidation.Run();
        var rendererData = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
        RaceAmbientOcclusion occlusion = null;
        foreach (var feature in rendererData.rendererFeatures)
            if (feature is RaceAmbientOcclusion candidate) occlusion = candidate;
        Require(occlusion != null && occlusion.isActive, "World ambient occlusion must remain enabled.");
        var settings = new SerializedObject(occlusion).FindProperty("m_Settings");
        Require(settings.FindPropertyRelative("Downsample").boolValue &&
            settings.FindPropertyRelative("AfterOpaque").boolValue &&
            settings.FindPropertyRelative("Source").intValue == 0,
            "AO must reuse opaque depth, at half resolution, without a normals prepass.");
        int trees = 0;
        foreach (var filter in UnityEngine.Object.FindObjectsByType<MeshFilter>(FindObjectsSortMode.None))
        {
            Mesh mesh = filter.sharedMesh;
            if (mesh == null || !mesh.name.StartsWith("tree")) continue;
            var renderer = filter.GetComponent<MeshRenderer>();
            Require(mesh.lodCount >= 4, "Tree LODs are missing: " + mesh.name);
            Require(renderer != null && renderer.forceMeshLod == -1 &&
                Mathf.Approximately(renderer.meshLodSelectionBias, 2f),
                "Course trees must automatically select lighter meshes: " + mesh.name);
            ulong full = 0, simplified = 0;
            for (int sub = 0; sub < mesh.subMeshCount; sub++)
            {
                full += mesh.GetLod(sub, 0).indexCount;
                simplified += mesh.GetLod(sub, 2).indexCount;
            }
            Require(full > 0 && simplified > 0 && simplified < full / 2,
                "Tree LOD must preserve geometry while substantially reducing indices.");
            trees++;
        }
        Require(trees == 15, "Expected all 15 course trees.");
        Directory.CreateDirectory("Logs/StandalonePerformance");
        File.WriteAllText("Logs/StandalonePerformance/validation.txt",
            "STANDALONE_RENDERING_VALIDATION_PASS: Render Graph/display lifecycle; world-only depth AO; 15 automatic tree LODs, original LOD0 retained.\n");
        Debug.Log("STANDALONE_RENDERING_VALIDATION_PASS");
    }

    [MenuItem("Racing/Build/Standalone Performance Probe")]
    public static void Build()
    {
        Validate();
        BuildPlayer("Builds/StandalonePerformance/Race.app", BuildOptions.Development);
    }

    // No scene/asset saves or PlayerSettings changes, including when the build fails.
    public static void BuildWindows()
    {
        string path = Environment.GetEnvironmentVariable("RACING_PERFORMANCE_BUILD");
        if (string.IsNullOrWhiteSpace(path))
            path = Path.Combine(Path.GetTempPath(), "RacingPerformance", Guid.NewGuid().ToString("N"), "Race.exe");
        else
        {
            path = Path.GetFullPath(path);
            if (!path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) path = Path.Combine(path, "Race.exe");
        }
        BuildPlayer(path, BuildOptions.Development, BuildTarget.StandaloneWindows64);
    }

    public static void BuildRelease()
    {
        Validate();
        BuildPlayer("Builds/StandaloneRelease/Race.app", BuildOptions.None);
    }

    // Build the original rendering configuration for a matched foreground A/B
    // sample. Restore assets even if building fails; never save this configuration.
    public static void BuildBaseline()
    {
        string renderer = File.ReadAllText(RendererPath);
        string pipeline = File.ReadAllText(PipelinePath);
        var target = UnityEditor.Build.NamedBuildTarget.Standalone;
        string identifier = PlayerSettings.GetApplicationIdentifier(target);
        try
        {
            PlayerSettings.SetApplicationIdentifier(target, "com.ennichi.race.performance-baseline");
            string guid = AssetDatabase.AssetPathToGUID("Assets/Managers/Visual/RaceAmbientOcclusion.cs");
            File.WriteAllText(RendererPath, renderer.Replace(guid, "f62c9c65cf3354c93be831c8bc075510")
                .Replace("Downsample: 1", "Downsample: 0").Replace("AfterOpaque: 1", "AfterOpaque: 0")
                .Replace("Source: 0", "Source: 1").Replace("NormalSamples: 2", "NormalSamples: 1")
                .Replace("BlurQuality: 1", "BlurQuality: 0"));
            AssetDatabase.ImportAsset(RendererPath, ImportAssetOptions.ForceUpdate);
            BuildPlayer("Builds/StandalonePerformanceBaseline/Race.app", BuildOptions.Development);
        }
        finally
        {
            PlayerSettings.SetApplicationIdentifier(target, identifier);
            File.WriteAllText(RendererPath, renderer);
            File.WriteAllText(PipelinePath, pipeline);
            AssetDatabase.ImportAsset(RendererPath, ImportAssetOptions.ForceUpdate);
            AssetDatabase.ImportAsset(PipelinePath, ImportAssetOptions.ForceUpdate);
        }
    }

    public static void BuildComparisonAndRelease()
    {
        BuildBaseline();
        Build();
        BuildRelease();
    }

    private static void BuildPlayer(string path, BuildOptions options, BuildTarget target = BuildTarget.StandaloneOSX)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        var result = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
            scenes = new[] { "Assets/Scenes/SampleScene.unity" }, locationPathName = path,
            target = target, options = options
        });
        Require(result.summary.result == BuildResult.Succeeded, "Standalone build failed: " + result.summary.result);
        Debug.Log("STANDALONE_PERFORMANCE_BUILD_PASS: " + path);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
