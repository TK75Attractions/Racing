#if UNITY_EDITOR
using System;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>Checks real selection, body, paint and handling integration, then captures the actual Unity UI.</summary>
public static class CarSelectionValidation
{
    [MenuItem("Racing/UI/Validate and Capture Car Selection")]
    public static void Run()
    {
        BuildFont();
        CarSelectionCatalog catalog = CarSelectionCatalog.Load();
        Require(catalog != null && catalog.vehicles.Length == 3, "Three vehicle grades are available");
        Require(catalog.wheelModel != null, "Preview wheel reference is available");
        foreach (CarSelectionCatalog.Vehicle vehicle in catalog.vehicles) Require(vehicle.model != null, "Model reference: " + vehicle.id);
        string[] keys = { "Racing.Car.P0", "Racing.Paint.P0", "Racing.Car.P1", "Racing.Paint.P1" };
        bool[] hadKeys = Array.ConvertAll(keys, PlayerPrefs.HasKey);
        string[] strings = { PlayerPrefs.GetString(keys[0]), PlayerPrefs.GetString(keys[2]) };
        int[] paints = { PlayerPrefs.GetInt(keys[1]), PlayerPrefs.GetInt(keys[3]) };
        Scene scene = EditorSceneManager.NewPreviewScene();
        RenderTexture previous = RenderTexture.active;
        RenderTexture output = null;
        Texture2D png = null;
        try
        {
            var root = new GameObject("Car Selection Validation", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            SceneManager.MoveGameObjectToScene(root, scene); root.layer = 5;
            Canvas canvas = root.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceCamera;
            var cameraObject = new GameObject("UI Capture Camera"); SceneManager.MoveGameObjectToScene(cameraObject, scene);
            Camera camera = cameraObject.AddComponent<Camera>(); camera.scene = scene;
            camera.transform.position = new Vector3(0, 0, -10); camera.cullingMask = 1 << 5;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black; camera.enabled = false;
            canvas.worldCamera = camera; canvas.planeDistance = 1f;
            CanvasScaler scaler = root.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080); scaler.matchWidthOrHeight = .5f;
            RectTransform title = RacingUITheme.Rect(root.transform, "Title", Vector2.zero, Vector2.one);
            CarSelectionUI ui = CarSelectionUI.Create(title, 0);
            int initialVehicle = ui.SelectedVehicle, initialPaint = ui.SelectedPaint;
            ui.Open();
            Require(!title.Find("SelectCar").GetComponent<Button>().interactable, "Underlying title controls are blocked");
            ui.SelectVehicle(1); ui.SelectPaint(0);
            Require(ui.SelectedVehicle == initialVehicle && ui.SelectedPaint == initialPaint, "Draft does not change committed selection");
            ui.Cancel();
            Require(title.Find("SelectCar").GetComponent<Button>().interactable, "Closing restores title controls");
            ui.Open();
            Require(ui.DraftVehicle == initialVehicle && ui.DraftPaint == initialPaint, "Cancel restores committed selection");
            ui.SelectVehicle(-1); Require(ui.DraftVehicle == 2, "Previous wraps to last vehicle");
            ui.SelectVehicle(3); Require(ui.DraftVehicle == 0, "Next wraps to first vehicle");
            ui.SelectVehicle(1); ui.SelectPaint(0); ui.Confirm();
            Require(ui.SelectedVehicle == 1 && ui.SelectedPaint == 0 && !ui.IsOpen, "Confirm commits and closes");
            Require(PlayerPrefs.GetString(keys[0]) == "street-rs" && PlayerPrefs.GetInt(keys[1]) == 0, "Confirm persists the selected vehicle and paint");
            ui.Open();
            ui.TickInput(new DriveInputState { pedal = 1f }, true, 1f);
            Require(ui.IsOpen, "Held pedal on entry cannot confirm");
            ui.TickInput(DriveInputState.Neutral, true, .1f);
            ui.TickInput(new DriveInputState { pedal = 1f }, true, .64f);
            Require(ui.IsOpen, "Pedal requires the full confirmation hold");
            ui.TickInput(new DriveInputState { pedal = 1f }, true, .02f);
            Require(!ui.IsOpen, "Released then held pedal confirms once");
            Require(!ui.Showroom.Model.activeInHierarchy, "Closed showroom does not remain active");
            RectTransform titleTwo = RacingUITheme.Rect(root.transform, "Title P2", Vector2.zero, Vector2.one);
            CarSelectionUI second = CarSelectionUI.Create(titleTwo, 1);
            Require(second.SelectedVehicle == Array.FindIndex(catalog.vehicles, v => v.id == PlayerPrefs.GetString(keys[2], catalog.vehicles[0].id)), "Player selections are independent");
            titleTwo.gameObject.SetActive(false);
            GameObject car = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefab/car/car2.prefab"));
            SceneManager.MoveGameObjectToScene(car, scene);
            ui.ApplyToCar(car);
            Require(car.transform.Find("SelectedVehicleBody") != null, "Sedan body reaches gameplay car");
            Require(car.GetComponent<DebugMover>().VehicleMultipliers == new Vector4(1.1f, .9f, 1.15f, .9f), "Selected handling reaches gameplay car");
            int bodies = car.GetComponentsInChildren<Rigidbody>(true).Length;
            catalog.Apply(car, 1, 0); Require(car.GetComponentsInChildren<Rigidbody>(true).Length == bodies, "Body change keeps production physics rig");
            catalog.Apply(car, 0, 2); Require(car.transform.Find("SelectedVehicleBody") == null, "Switch back restores coupe");
            Require(car.GetComponent<DebugMover>().VehicleMultipliers == Vector4.one, "Grade reset does not compound tuning");
            UnityEngine.Object.DestroyImmediate(car);
            Debug.Log("CAR_SELECTION_BEHAVIOR_PASS");
            ui.Open(); ui.SelectVehicle(0); ui.SelectPaint(0);
            Require(ui.Showroom.Model.GetComponentsInChildren<Collider>().Length == 0, "Preview has no colliders");
            Require(ui.Showroom.Model.GetComponentsInChildren<DebugMover>().Length == 0, "Preview has no driving scripts");
            string directory = Path.GetFullPath("Artifacts/CarSelection"); Directory.CreateDirectory(directory);
            foreach (Vector2Int size in new[] { new Vector2Int(1920,1080), new Vector2Int(1280,720), new Vector2Int(2560,1440) })
            {
                output = new RenderTexture(size.x, size.y, 24) { antiAliasing = 4 }; output.Create(); camera.targetTexture = output;
                canvas.pixelPerfect = false;
                scaler.enabled = false; scaler.enabled = true;
                for (int vehicle = 0; vehicle < 3; vehicle++)
                {
                    ui.SelectVehicle(vehicle); ui.SelectPaint(vehicle == 0 ? 0 : vehicle == 1 ? 2 : 3);
                    ui.Showroom.RenderNow(); Canvas.ForceUpdateCanvases();
                    foreach (TMP_Text label in title.GetComponentsInChildren<TMP_Text>(false))
                    {
                        label.ForceMeshUpdate();
                        Require(label.font.HasCharacters(label.text), "Font contains: " + label.text);
                        Require(!label.isTextTruncated && !label.isTextOverflowing, "Text fits: " + label.name + " / " + label.text);
                    }
                    if (GraphicsSettings.currentRenderPipeline != null)
                    {
                        var request = new RenderPipeline.StandardRequest { destination = output };
                        Require(RenderPipeline.SupportsRenderRequest(camera, request), "Render pipeline supports preview capture");
                        RenderPipeline.SubmitRenderRequest(camera, request);
                    }
                    else camera.Render();
                    RenderTexture.active = output;
                    png = new Texture2D(size.x, size.y, TextureFormat.RGB24, false);
                    png.ReadPixels(new Rect(0,0,size.x,size.y),0,0); png.Apply();
                    string captureDirectory = size.x == 1920 ? directory : Path.Combine(Path.GetTempPath(), "RacingCarSelectionCaptures");
                    Directory.CreateDirectory(captureDirectory);
                    Debug.Log($"CAR_SELECTION_CAPTURE: vehicle {vehicle}, {size.x}x{size.y}");
                    File.WriteAllBytes(Path.Combine(captureDirectory, $"car-select-{vehicle}-{size.x}x{size.y}.png"), png.EncodeToPNG());
                    UnityEngine.Object.DestroyImmediate(png); png = null;
                }
                output.Release(); UnityEngine.Object.DestroyImmediate(output); output = null;
            }
            Debug.Log("CAR_SELECTION_VALIDATION_PASS: " + directory);
        }
        finally
        {
            RenderTexture.active = previous;
            if (png != null) UnityEngine.Object.DestroyImmediate(png);
            if (output != null) { output.Release(); UnityEngine.Object.DestroyImmediate(output); }
            EditorSceneManager.ClosePreviewScene(scene);
            for (int i = 0; i < 2; i++)
            {
                if (hadKeys[i * 2]) PlayerPrefs.SetString(keys[i * 2], strings[i]); else PlayerPrefs.DeleteKey(keys[i * 2]);
                if (hadKeys[i * 2 + 1]) PlayerPrefs.SetInt(keys[i * 2 + 1], paints[i]); else PlayerPrefs.DeleteKey(keys[i * 2 + 1]);
            }
            PlayerPrefs.Save();
        }
    }
    private static void BuildFont()
    {
        const string path = "Assets/Resources/UI/CarSelectionFont.asset";
        string corpus = File.ReadAllText("Assets/Resources/UI/CarSelectionCatalog.asset");
        foreach (string source in Directory.GetFiles("Assets/Managers/UI", "CarSelection*.cs")) corpus += File.ReadAllText(source);
        corpus += File.ReadAllText("Assets/Managers/UI/NeonUI.cs");
        string characters = new string(System.Linq.Enumerable.ToArray(System.Linq.Enumerable.OrderBy(
            System.Linq.Enumerable.Distinct(System.Linq.Enumerable.Where(corpus, c => c >= 32)), c => c)));
        TMP_FontAsset existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
        if (existing != null && existing.HasCharacters(characters)) return;
        Font sourceFont = AssetDatabase.LoadAssetAtPath<Font>("Assets/Fonts/MPLUS1-HUD-Bold.ttf");
        TMP_FontAsset font = TMP_FontAsset.CreateFontAsset(sourceFont, 80, 8,
            UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDFAA, 2048, 2048, AtlasPopulationMode.Dynamic, false);
        Require(font.TryAddCharacters(characters, out string missing), "Font coverage: " + missing);
        font.name = "Car Selection Bold"; font.atlasPopulationMode = AtlasPopulationMode.Static;
        font.material.name = "Car Selection Bold Material";
        if (existing != null) AssetDatabase.DeleteAsset(path);
        AssetDatabase.CreateAsset(font, path);
        AssetDatabase.AddObjectToAsset(font.material, font);
        foreach (Texture2D atlas in font.atlasTextures) { atlas.name = font.name + " Atlas"; AssetDatabase.AddObjectToAsset(atlas, font); }
        AssetDatabase.SaveAssets();
    }

    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException("Car selection validation failed: " + message); }
}
#endif
