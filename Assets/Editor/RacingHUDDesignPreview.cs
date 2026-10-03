#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>Exports the real HUD builders on a reference racing background, without saving the scene.</summary>
public static class RacingHUDDesignPreview
{
    [MenuItem("Racing/UI/Capture Bilingual HUD Design")]
    public static void Capture()
    {
        RaceCourse course = UnityEngine.Object.FindFirstObjectByType<RaceCourse>();
        if (course == null) throw new InvalidOperationException("Open the race scene before capturing the HUD.");
        string output = Path.GetFullPath("Artifacts/HUDDesign");
        Directory.CreateDirectory(output);
        string[] settings = { "ProjectSettings/GraphicsSettings.asset", "ProjectSettings/QualitySettings.asset" };
        byte[][] originals = Array.ConvertAll(settings, File.ReadAllBytes);
        try
        {
            CaptureView(course, output, 1920, 1080, 0, "race");
            CaptureView(course, output, 1920, 1080, 1, "boost");
            CaptureView(course, output, 1920, 1080, 0, "countdown");
            CaptureView(course, output, 1920, 1080, 0, "finish-warning");
            CaptureView(course, output, 1920, 1080, 0, "spectator");
            CaptureView(course, Path.GetTempPath(), 1280, 720, 1, "race");
            CaptureView(course, Path.GetTempPath(), 3840, 2160, 0, "race");
        }
        finally
        {
            // Unity may upgrade serialized quality settings when assigning a temporary render pipeline.
            // Keep previews from changing the project's persistent graphics configuration.
            for (int i = 0; i < settings.Length; i++) File.WriteAllBytes(settings[i], originals[i]);
        }
        Debug.Log("HUD_DESIGN_CAPTURE_PASS: " + output);
    }

    public static void RunBatch()
    {
        RacingHUDFontBuilder.Build();
        ModernUIValidation.Run();
        MiniMapValidation.RunBatch();
        Capture();
    }

    private static void CaptureView(RaceCourse course, string directory, int width, int height, int player, string state)
    {
        Scene scene = EditorSceneManager.NewPreviewScene();
        RenderPipelineAsset pipeline = GraphicsSettings.defaultRenderPipeline;
        RenderPipelineAsset qualityPipeline = QualitySettings.renderPipeline;
        RenderTexture previous = RenderTexture.active;
        RenderTexture target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
        Texture2D png = new Texture2D(width, height, TextureFormat.RGB24, false);
        try
        {
            // Built-in rendering makes UI previews independent of HDRP lighting and post processing.
            GraphicsSettings.defaultRenderPipeline = null;
            QualitySettings.renderPipeline = null;
            GameObject cameraObject = new GameObject("HUD preview camera");
            SceneManager.MoveGameObjectToScene(cameraObject, scene);
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.scene = scene;
            camera.orthographic = true;
            camera.orthographicSize = 540f;
            camera.transform.position = new Vector3(0f, 0f, -10f);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            camera.targetTexture = target;
            camera.allowHDR = false;
            camera.enabled = false;
            GameObject canvasObject = new GameObject("HUD preview canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            SceneManager.MoveGameObjectToScene(canvasObject, scene);
            Canvas canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;
            canvas.planeDistance = 1f;
            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = .5f;
            RectTransform background = RacingUITheme.Rect(canvas.transform, "ReferenceBackground", Vector2.zero, Vector2.one);
            RawImage backdrop = background.gameObject.AddComponent<RawImage>();
            backdrop.texture = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Managers/UI/Elements/hud/race_background.png");
            backdrop.raycastTarget = false;
            RectTransform play = RacingUITheme.Rect(canvas.transform, "OnPlay", Vector2.zero, Vector2.one);
            Transform hud = RacingHUDBuilder.Build(play, player, 3);
            UIPosition position = new UIPosition(); position.Init(hud.Find("Position")); position.SetPosition(player + 1);
            UILap lap = new UILap(); lap.Init(hud.Find("Lap")); lap.SetLap(2);
            UITime time = new UITime(); time.Init(hud.Find("Time")); time.SetTotalTime(72.345f); time.SetLapTime(19.876f);
            UISpeed speed = new UISpeed(); speed.Init(hud.Find("Speed")); speed.UpdateSpeedMeter(state == "boost" ? 168f : 127f, 0f);
            Canvas.ForceUpdateCanvases();
            UIMiniMap map = new UIMiniMap(); map.Init(play, course, player);
            var path = new List<Vector3>(); course.CopyCenterPathWorld(path);
            GameObject[] cars = { new GameObject("Preview P1"), new GameObject("Preview P2") };
            for (int i = 0; i < cars.Length; i++)
            {
                SceneManager.MoveGameObjectToScene(cars[i], scene);
                int index = path.Count / 3 - i * Mathf.Max(1, path.Count / 20);
                cars[i].transform.position = path[index];
                Vector3 direction = path[(index + 1) % path.Count] - path[index];
                cars[i].transform.rotation = Quaternion.LookRotation(direction);
            }
            map.SetCars(cars[0].transform, cars[1].transform);
            ScreenTransitionController transition = canvasObject.AddComponent<ScreenTransitionController>();
            transition.Initialize(null, play, null, string.Empty, string.Empty, player);
            transition.ApplyStateImmediate(Gmanager.State.Game);
            if (state == "countdown") transition.ShowCountdown(3);
            else if (state == "finish-warning") transition.ShowFinishWarning("P1", 8.4f);
            else if (state == "spectator") transition.ShowSpectator(1);
            else transition.ClearRaceStatus();
            if (state == "boost")
            {
                speed.UpdateBoostGauge(2.4f, 3f);
                PlayerItemHUD.Create(canvas.transform);
                Transform item = canvas.transform.Find("ItemHUD");
                item.gameObject.SetActive(true);
                item.Find("Toast").GetComponent<CanvasGroup>().alpha = 1f;
                item.Find("Toast/Title").GetComponent<TMP_Text>().text = "チャージ MAX";
                item.Find("Toast/Title").GetComponent<TMP_Text>().color = RacingHUDStyle.Teal;
                item.Find("Toast/Caption").GetComponent<TMP_Text>().text = "DRIFT CHARGE / ドリフトを解放して加速";
            }
            Canvas.ForceUpdateCanvases();
            foreach (string instrument in new[] { "Position", "Lap" })
            {
                TMP_Text number = hud.Find(instrument + "/Txt").GetComponent<TMP_Text>();
                number.transform.localScale = Vector3.one;
                number.transform.localRotation = Quaternion.identity;
                number.color = RacingHUDStyle.Text;
            }
            foreach (TMP_Text label in canvas.GetComponentsInChildren<TMP_Text>(false))
            {
                if (!label.font.HasCharacters(label.text))
                    throw new InvalidOperationException($"Missing HUD glyph: {label.name} / {label.text}");
                label.ForceMeshUpdate();
                if (label.isTextTruncated || label.isTextOverflowing)
                    throw new InvalidOperationException($"HUD text overflow at {width}x{height}: {label.name} / {label.text}");
            }
            Canvas.ForceUpdateCanvases();
            camera.Render();
            RenderTexture.active = target;
            png.ReadPixels(new Rect(0f, 0f, width, height), 0, 0);
            png.Apply();
            File.WriteAllBytes(Path.Combine(directory, $"hud-{state}-{width}x{height}.png"), png.EncodeToPNG());
        }
        finally
        {
            RenderTexture.active = previous;
            GraphicsSettings.defaultRenderPipeline = pipeline;
            QualitySettings.renderPipeline = qualityPipeline;
            EditorSceneManager.ClosePreviewScene(scene);
            UnityEngine.Object.DestroyImmediate(png);
            target.Release();
            UnityEngine.Object.DestroyImmediate(target);
        }
    }
}
#endif
