#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>Production meshes and HUDs, deterministic fixture states; does not save a scene.</summary>
public static class ItemFirstPlayCapture
{
    private static string Output => Path.GetFullPath("Logs/ItemFirstPlay/Captures");
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    public static void Run()
    {
        string[] settings = { "ProjectSettings/GraphicsSettings.asset", "ProjectSettings/QualitySettings.asset" };
        byte[][] previous = Array.ConvertAll(settings, File.ReadAllBytes);
        try { RunCapture(); }
        finally { for (int i = 0; i < settings.Length; i++) File.WriteAllBytes(settings[i], previous[i]); }
    }
    private static void RunCapture()
    {
        RacingHUDFontBuilder.Build();
        ItemReadabilityValidation.Run();
        ValidateMeshAndOwnership();
        Directory.CreateDirectory(Output);
        CaptureBox();
        foreach (int player in new[] { 0, 1 }) foreach (int width in new[] { 1280, 1920 })
        {
            CaptureUI(player, width, "guide");
            foreach (RaceItemType item in new[] { RaceItemType.Shield, RaceItemType.Rocket, RaceItemType.Oil, RaceItemType.Confuse })
                CaptureUI(player, width, item.ToString());
            CaptureUI(player, width, "charge");
            CaptureUI(player, width, "afflicted");
        }
        Debug.Log("ITEM_FIRST_PLAY_CAPTURE_PASS: bevel normals, UVs, texture ownership/rebuild; both palettes at 720p/1080p, all items and guide, no missing glyphs or overflow.");
    }
    private static void ValidateMeshAndOwnership()
    {
        var mesh = ItemVisualFactory.CreateBox(1.4f);
        try
        {
            Require(mesh.uv.Length == mesh.vertexCount, "Every face and bevel needs valid UVs.");
            Require(mesh.triangles.Length / 3 == 44, "The box needs six inset faces, twelve bevels and eight sealed corners.");
            Vector3[] vertices = mesh.vertices, normals = mesh.normals;
            for (int i = 0; i < vertices.Length; i++) Require(Vector3.Dot(vertices[i], normals[i]) > .2f,
                "Every box face must point outward; mirrored faces must not disappear.");
            Require(Vector3.Distance(mesh.bounds.size, Vector3.one * 1.4f) < .001f, "Visual polish must preserve the box envelope.");
        }
        finally { UnityEngine.Object.DestroyImmediate(mesh); }
        var owner = new GameObject("Texture lifetime QA");
        var box = owner.AddComponent<ItemBox>();
        Material material = owner.GetComponentInChildren<MeshRenderer>().sharedMaterial;
        Texture texture = material.mainTexture;
        Require(texture != null && material.GetTexture("_EmissionMap") == texture, "The symbol must be visible in shade and in daylight.");
        box.Refresh(); box.Refresh();
        Require(owner.transform.childCount == 1 && owner.GetComponentInChildren<MeshRenderer>().sharedMaterial == material && material.mainTexture == texture,
            "Inspector refresh must not duplicate visuals or allocate another texture.");
        box.enabled = false; box.enabled = true;
        Require(owner.transform.childCount == 1 && material.mainTexture == texture, "Re-enable must reuse its owned material and texture.");
        UnityEngine.Object.DestroyImmediate(owner);
        Require(texture == null && material == null, "Destroying a pickup must release its generated GPU resources.");
    }
    private static void CaptureBox()
    {
        var scene = EditorSceneManager.NewPreviewScene();
        var go = new GameObject("Mystery box"); SceneManager.MoveGameObjectToScene(go, scene);
        go.AddComponent<ItemBox>();
        var lamp = new GameObject("Soft key", typeof(Light)); SceneManager.MoveGameObjectToScene(lamp, scene);
        lamp.GetComponent<Light>().type = LightType.Directional; lamp.GetComponent<Light>().intensity = 1.2f;
        lamp.transform.rotation = Quaternion.Euler(45, -35, 0);
        var cameraObject = new GameObject("Box camera", typeof(Camera)); SceneManager.MoveGameObjectToScene(cameraObject, scene);
        var camera = cameraObject.GetComponent<Camera>(); camera.scene = scene; camera.enabled = false;
        camera.transform.position = new Vector3(3.4f, 2.5f, -5f);
        camera.transform.LookAt(Vector3.up * 1.2f); camera.fieldOfView = 35;
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.018f,.035f,.07f);
        try { Save(camera, 960, 720, "mystery-box.png"); }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
    }
    private static void CaptureUI(int player, int width, string state)
    {
        var scene = EditorSceneManager.NewPreviewScene();
        var oldPipeline = GraphicsSettings.defaultRenderPipeline; var oldQuality = QualitySettings.renderPipeline;
        try
        {
            GraphicsSettings.defaultRenderPipeline = null; QualitySettings.renderPipeline = null;
            var go = new GameObject("Item UI camera", typeof(Camera)); SceneManager.MoveGameObjectToScene(go, scene);
            var camera = go.GetComponent<Camera>(); camera.scene = scene; camera.enabled = false;
            camera.orthographic = true; camera.orthographicSize = 540; camera.transform.position = new Vector3(0,0,-10);
            var host = new GameObject("Item UI canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler)); SceneManager.MoveGameObjectToScene(host, scene);
            var canvas = host.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 1;
            var scaler = host.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920,1080); scaler.matchWidthOrHeight = .5f;
            ArcadeUI.Bind(host.transform, player);
            var background = RacingUITheme.Rect(host.transform, "Backdrop", Vector2.zero, Vector2.one).gameObject.AddComponent<RawImage>();
            background.texture = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Editor/ArcadeReference/Race.jpg"); background.raycastTarget = false;
            if (state == "guide")
            {
                var lesson = new DrivingTutorialProgress(); lesson.Tick(.01f,true,0,0,0,0); lesson.Tick(.7f,true,1,0,4,0); lesson.Tick(.7f,true,1,10,4,35); lesson.CollectItem(true);
                new DrivingTutorialUI(host.transform,player).Update(lesson,DriveInputState.Neutral,0,true);
                host.transform.Find("DrivingTutorial").SetAsLastSibling();
                Require(host.transform.Find("DrivingTutorial/ItemGuide").gameObject.activeSelf, "The stationary waiting stage must explain automatic activation and every item.");
            }
            else
            {
                var play = RacingUITheme.Rect(host.transform,"OnPlay",Vector2.zero,Vector2.one); new OnPlayUIManager().Init(play,null,player);
                var hud = PlayerItemHUD.Create(host.transform);
                host.transform.Find("ItemHUD").gameObject.SetActive(true);
                if(state=="charge") Call(hud,"HandleChargeCollected");
                else if(state=="afflicted") Call(hud,"HandleAfflicted",RaceItemType.Confuse);
                else Call(hud,"HandleItemUsed",(RaceItemType)Enum.Parse(typeof(RaceItemType),state));
                Call(hud,"UpdateToast",.2f);
            }
            Canvas.ForceUpdateCanvases();
            foreach(var text in host.GetComponentsInChildren<TMP_Text>())
            {
                Transform scope = state == "guide" ? host.transform.Find("DrivingTutorial/ItemGuide") : host.transform.Find("ItemHUD/Toast");
                if (!text.transform.IsChildOf(scope)) continue;
                text.ForceMeshUpdate();
                Require(!text.isTextOverflowing, state+" overflows at "+width+": "+text.text);
                Require(text.font.HasCharacters(new string(text.text.Where(c=>!char.IsControl(c)).ToArray())), "Missing glyphs: "+text.text);
            }
            Save(camera,width,width*9/16,$"{state.ToLowerInvariant()}-p{player+1}-{width}.png");
        }
        finally { GraphicsSettings.defaultRenderPipeline = oldPipeline; QualitySettings.renderPipeline = oldQuality; EditorSceneManager.ClosePreviewScene(scene); }
    }
    private static void Save(Camera camera, int width, int height, string filename)
    {
        var target = new RenderTexture(width,height,24); var image = new Texture2D(width,height,TextureFormat.RGB24,false);
        var previous = RenderTexture.active;
        try
        {
            camera.targetTexture = target; camera.Render(); RenderTexture.active = target;
            image.ReadPixels(new Rect(0,0,width,height),0,0); image.Apply();
            File.WriteAllBytes(Path.Combine(Output,filename),image.EncodeToPNG());
        }
        finally { camera.targetTexture = null; RenderTexture.active = previous; UnityEngine.Object.DestroyImmediate(image); UnityEngine.Object.DestroyImmediate(target); }
    }
    private static void Call(object target, string method, params object[] args) => target.GetType().GetMethod(method,Private).Invoke(target,args);
    private static void Require(bool value, string message) { if(!value) throw new InvalidOperationException(message); }
}
#endif
