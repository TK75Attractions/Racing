using System;
using System.IO;
using System.Linq;
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

    [MenuItem("Racing/Validation/Enable Desktop Render Graph")]
    public static void PrepareDesktop()
    {
        string defines = PlayerSettings.GetScriptingDefineSymbols(NamedBuildTarget.Standalone);
        string cleaned = string.Join(";", defines.Split(';').Where(value => value != "URP_COMPATIBILITY_MODE"));
        if (cleaned != defines)
            PlayerSettings.SetScriptingDefineSymbols(NamedBuildTarget.Standalone,
                cleaned);
        var settings = new SerializedObject(AssetDatabase.LoadMainAssetAtPath(
            "Assets/Settings/UniversalRenderPipelineGlobalSettings.asset"));
        SerializedProperty list = settings.FindProperty("m_Settings.m_SettingsList.m_List");
        for (int i = 0; i < list.arraySize; i++)
        {
            SerializedProperty item = list.GetArrayElementAtIndex(i);
            if (item.managedReferenceValue is RenderGraphSettings)
                item.FindPropertyRelative("m_EnableRenderCompatibilityMode").boolValue = false;
        }
        settings.ApplyModifiedPropertiesWithoutUndo();
        AssetDatabase.SaveAssets();
        Debug.Log("Desktop Render Graph enabled. Wait for script compilation, then run Display Outputs.");
    }

    [MenuItem("Racing/Validation/Display Outputs")]
    public static void Run()
    {
        Require(!EditorApplication.isPlaying, "Stop Play Mode before validating display outputs.");
        Require(!GraphicsSettings.GetRenderPipelineSettings<RenderGraphSettings>().enableRenderCompatibilityMode,
            "Desktop rendering must use Render Graph.");
        Require(!PlayerSettings.GetScriptingDefineSymbols(NamedBuildTarget.Standalone).Split(';').Contains("URP_COMPATIBILITY_MODE"),
            "Remove URP_COMPATIBILITY_MODE and wait for script compilation before building.");
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
            var output = fixture.AddComponent<RenderGraphDisplayOutput>();
            output.Configure(Cameras(rigs[1]), 2);
            var resize = typeof(RenderGraphDisplayOutput).GetMethod("ResizeTarget", Private);
            RenderTexture previous = null;
            foreach (Vector2Int dimensions in sizes)
            {
                resize.Invoke(output, new object[] { dimensions.x, dimensions.y });
                RenderTexture current = output.Target;
                Require(current != null && current.IsCreated() && current.width == dimensions.x && current.height == dimensions.y && current.depth >= 24,
                    "Additional display must have a correctly sized color/depth target.");
                if (!ReferenceEquals(previous, null)) Require(previous == null, "Resize must destroy the previous target.");
                foreach (Camera camera in Cameras(rigs[1])) Require(camera.targetTexture == current, "The whole display stack must share one target.");
                Refresh(rigs[1]);
                Require(Mathf.Approximately(rigs[1].Canvas.scaleFactor, Mathf.Min(dimensions.x / 1920f, dimensions.y / 1080f)),
                    "UI scale must follow the explicit Render Graph target after resizing.");
                resize.Invoke(output, new object[] { dimensions.x, dimensions.y });
                Require(output.Target == current, "Unchanged output size must reuse its target.");
                previous = current;
            }
            output.enabled = false;
            Require(output.Target == null, "Disabling output must release its target.");
            foreach (Camera camera in Cameras(rigs[1])) Require(camera.targetTexture == null, "Disabling output must detach owned targets.");
            Require(Resources.Load<Shader>("DisplayOutput") != null && Resources.Load<Shader>("DisplayOutput").isSupported,
                "Display presentation shader must be included and supported.");
            string result = "MULTI_DISPLAY_VALIDATION_PASS: Render Graph enabled, no compatibility define; Display 1/3 routing; unequal-size output pairs; stable UI; explicit stack color/depth target resize, reuse and cleanup; presentation shader.";
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
