using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

/// <summary>Checks independent display routing and UI scale across unequal output sizes.</summary>
public static class MultiDisplayValidation
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    [MenuItem("Racing/Validation/Enable Desktop Display Compatibility")]
    public static void PrepareDesktop()
    {
        string defines = PlayerSettings.GetScriptingDefineSymbols(NamedBuildTarget.Standalone);
        if (!defines.Contains("URP_COMPATIBILITY_MODE"))
            PlayerSettings.SetScriptingDefineSymbols(NamedBuildTarget.Standalone,
                string.IsNullOrEmpty(defines) ? "URP_COMPATIBILITY_MODE" : defines + ";URP_COMPATIBILITY_MODE");
        AssetDatabase.ImportAsset("Assets/Settings/UniversalRenderPipelineGlobalSettings.asset", ImportAssetOptions.ForceUpdate);
        AssetDatabase.ImportAsset("Assets/Settings/PC_Renderer.asset", ImportAssetOptions.ForceUpdate);
#if URP_COMPATIBILITY_MODE
        GraphicsSettings.GetRenderPipelineSettings<RenderGraphSettings>().enableRenderCompatibilityMode = true;
#endif
        AssetDatabase.SaveAssets();
        Debug.Log("Desktop multi-display compatibility settings loaded. Wait for script compilation, then run Display Outputs.");
    }

    [MenuItem("Racing/Validation/Display Outputs")]
    public static void Run()
    {
        Require(!EditorApplication.isPlaying, "Stop Play Mode before validating display outputs.");
        Require(GraphicsSettings.GetRenderPipelineSettings<RenderGraphSettings>().enableRenderCompatibilityMode,
            "URP compatibility mode must bypass the Screen-sized multi-display backbuffer import.");
        Require(PlayerSettings.GetScriptingDefineSymbols(NamedBuildTarget.Standalone).Contains("URP_COMPATIBILITY_MODE"),
            "Unity 6.3 needs URP_COMPATIBILITY_MODE for desktop builds.");
        GameObject source = GameObject.Find("GameManagers");
        Require(source != null, "Open SampleScene before validating display outputs.");
        GameObject fixture = Object.Instantiate(source);
        fixture.name = "Display Validation";
        var textures = new RenderTexture[2];
        try
        {
            PlayerDisplayRig[] rigs = TwoPlayerDisplayFactory.Create(fixture.transform, .65f, 2);
            Require(rigs.Length == 2, "Both display rigs must be created.");
            Require(rigs[0].DisplayIndex == 0 && rigs[1].DisplayIndex == 2,
                "P2 must be able to skip Display 2 and use Display 3.");
            foreach (PlayerDisplayRig rig in rigs)
            {
                Require(rig.Canvas.worldCamera == rig.UiCamera && rig.Canvas.worldCamera.targetDisplay == rig.DisplayIndex,
                    "Screen-space camera Canvas must use its player's UI camera and output.");
                foreach (Camera camera in Cameras(rig))
                    Require(camera.targetDisplay == rig.DisplayIndex, "Every stacked camera must use the selected output.");
            }
            Vector2Int[] sizes = { new Vector2Int(2880, 1800), new Vector2Int(1920, 1080),
                new Vector2Int(1024, 768), new Vector2Int(720, 1280) };
            for (int size = 0; size < sizes.Length; size++)
            {
                for (int player = 0; player < 2; player++)
                {
                    if (textures[player] != null) Object.DestroyImmediate(textures[player]);
                    Vector2Int dimensions = sizes[(size + player) % sizes.Length];
                    textures[player] = new RenderTexture(dimensions.x, dimensions.y, 24);
                    foreach (Camera camera in Cameras(rigs[player])) camera.targetTexture = textures[player];
                    Refresh(rigs[player]);
                }
                float[] scales = { rigs[0].Canvas.scaleFactor, rigs[1].Canvas.scaleFactor };
                for (int callback = 0; callback < 20; callback++)
                {
                    // Reproduce an overlay camera reporting another output's pixels:
                    // scaling must remain tied to this rig's base target, independently.
                    for (int player = 0; player < 2; player++)
                    {
                        rigs[player].UiCamera.targetTexture = textures[1 - player];
                        Refresh(rigs[player]);
                        Require(Mathf.Approximately(rigs[player].Canvas.scaleFactor, scales[player]),
                            "UI scale must not oscillate when the active overlay output changes.");
                        rigs[player].UiCamera.targetTexture = textures[player];
                    }
                    Canvas.ForceUpdateCanvases();
                    for (int player = 0; player < 2; player++)
                    {
                        Refresh(rigs[player]);
                        float expected = Mathf.Min(textures[player].width / 1920f, textures[player].height / 1080f);
                        Require(Mathf.Approximately(rigs[player].Canvas.scaleFactor, expected), "UI must fit its own letterboxed output.");
                        Require(Mathf.Approximately(rigs[player].MainCamera.aspect, 16f / 9f), "Camera aspect must remain 16:9.");
                    }
                }
            }
            string result = "MULTI_DISPLAY_VALIDATION_PASS: Display 1/3 routing; four unequal-size output pairs; stable UI through repeated render callbacks and swapped overlay pixel sizes; desktop compatibility settings.";
            Directory.CreateDirectory("Logs/DisplayValidation");
            File.WriteAllText("Logs/DisplayValidation/validation.txt", result + Environment.NewLine);
            Debug.Log(result);
        }
        finally
        {
            Object.DestroyImmediate(fixture);
            foreach (RenderTexture texture in textures) if (texture != null) Object.DestroyImmediate(texture);
        }
    }

    [MenuItem("Racing/Build/Mac Display Validation")]
    public static void BuildMac()
    {
        Run();
        string path = Path.Combine(Directory.GetCurrentDirectory(), "Builds/DisplayValidation/Race.app");
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
            scenes = new[] { "Assets/Scenes/SampleScene.unity" }, locationPathName = path,
            target = BuildTarget.StandaloneOSX, options = BuildOptions.Development
        });
        Require(report.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded, "Display validation build failed.");
        Debug.Log("MULTI_DISPLAY_BUILD_PASS: " + path);
    }

    private static Camera[] Cameras(PlayerDisplayRig rig) => new[] {
        rig.BackImageCamera, rig.MainCamera, rig.FrontCamera, rig.UiCamera, rig.BackCamera
    };
    private static void Refresh(PlayerDisplayRig rig) => typeof(FreezeAspectRate)
        .GetMethod("RefreshViewport", Private).Invoke(rig.MainCamera.GetComponent<FreezeAspectRate>(), null);
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
