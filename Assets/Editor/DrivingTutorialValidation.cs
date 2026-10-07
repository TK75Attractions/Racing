using System;
using System.Collections;
using System.IO;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Preview and automated QA use the actual scene, production car, cameras and tutorial UI.</summary>
[InitializeOnLoad]
public static class DrivingTutorialValidation
{
    private const string PreviewKey = "Racing.TutorialPreview", ValidationKey = "Racing.TutorialValidation";
    private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private static float began, maximumVerticalSpeed;
    private static bool bound, capturedTurn, capturedStop, capturedDriving;
    private static StubInput[] inputs;
    private static IList practices;

    private sealed class StubInput : IDriveInputSource
    {
        public int PlayerIndex { get; set; }
        public string DeviceId => "TUTORIAL_QA";
        public bool IsConnected => true;
        public DriveInputState CurrentState { get; set; }
        public void UpdateInput(float dt) { }
        public void Dispose() { }
    }
    static DrivingTutorialValidation() { EditorApplication.update += UpdatePreview; }

    [MenuItem("Racing/Tutorial/Play Preview")]
    public static void StartPreview() => Start(false);
    [MenuItem("Racing/Tutorial/Validate and Capture")]
    public static void Run() => Start(true);

    private static void Start(bool validate)
    {
        if (EditorApplication.isPlaying) return;
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        InputManager input = UnityEngine.Object.FindFirstObjectByType<InputManager>();
        input.isDebugMode = true; input.isP1SerialP2KeyboardDebugMode = false;
        SessionState.SetBool(PreviewKey, true); SessionState.SetBool(ValidationKey, validate);
        Debug.Log("TUTORIAL_QA_START");
        EditorApplication.isPlaying = true;
    }

    private static void UpdatePreview()
    {
        if (!EditorApplication.isPlaying) return;
        Gmanager manager = Gmanager.Control;
        if (manager == null) return;
        try
        {
            if (SessionState.GetBool(PreviewKey, false) && manager.state == Gmanager.State.Title)
            {
                SessionState.SetBool(PreviewKey, false);
                manager.StartGame(); return;
            }
            if (!SessionState.GetBool(ValidationKey, false)) return;
            if (!bound)
            {
                if (manager.state != Gmanager.State.Tutorial || !manager.IsDrivingEnabled) return;
                DrivingTutorialController tutorial = manager.GetComponent<DrivingTutorialController>();
                practices = (IList)typeof(DrivingTutorialController).GetField("players", Fields).GetValue(tutorial);
                inputs = new StubInput[practices.Count];
                for (int i = 0; i < practices.Count; i++)
                {
                    inputs[i] = new StubInput { PlayerIndex = i };
                    Set(practices[i], "input", inputs[i]);
                    Get<DebugMover>(practices[i], "mover").SetInputSource(inputs[i]);
                }
                ValidateRoad();
                bound = true; began = Time.time;
                Capture(manager, 0, "01-accelerate-p1.png"); Capture(manager, 1, "01-accelerate-p2.png");
            }
            if (Time.time - began > 55f) throw new Exception("The real practice drive did not reach the race countdown within 55 seconds.");
            if (manager.state == Gmanager.State.Countdown || manager.state == Gmanager.State.Game)
            {
                Debug.Log($"TUTORIAL_QA_PASSED: both private courses, actual accelerate/turn/stop, countdown. Maximum vertical speed after settling: {maximumVerticalSpeed:0.000} m/s");
                SessionState.SetBool(ValidationKey, false);
                if (Application.isBatchMode) EditorApplication.Exit(0);
                return;
            }
            for (int i = 0; i < practices.Count; i++)
            {
                object practice = practices[i];
                var lesson = Get<DrivingTutorialProgress>(practice, "lesson");
                Rigidbody body = Get<Rigidbody>(practice, "body");
                Vector3 local = body.position - Get<GameObject>(practice, "world").transform.position;
                if (Time.time - began > 2f && local.z < -15f && lesson.CurrentStage == DrivingTutorialProgress.Stage.Accelerate)
                    maximumVerticalSpeed = Mathf.Max(maximumVerticalSpeed, Mathf.Abs(body.linearVelocity.y));
                if (i == 0 && !capturedDriving && Time.time - began > .9f && lesson.CurrentStage == DrivingTutorialProgress.Stage.Accelerate)
                { capturedDriving = true; Capture(manager, i, "05-accelerating.png"); }
                float pedal = 0f, steering = 0f;
                if (lesson.CurrentStage == DrivingTutorialProgress.Stage.Accelerate && Time.time - began > .6f + i * 2f) pedal = .65f;
                if (lesson.CurrentStage == DrivingTutorialProgress.Stage.Steer)
                {
                    pedal = .55f; steering = local.z >= 3f ? 10f : 0f;
                    if (i == 0 && !capturedTurn) { capturedTurn = true; Capture(manager, i, "02-steer.png"); }
                }
                if (lesson.CurrentStage == DrivingTutorialProgress.Stage.Stop && i == 0 && !capturedStop)
                { capturedStop = true; Capture(manager, i, "03-stop.png"); }
                inputs[i].CurrentState = new DriveInputState { pedal = pedal, steering = steering };
                if (i == 0 && lesson.IsComplete && !SessionState.GetBool("Racing.TutorialWaitCaptured", false))
                { SessionState.SetBool("Racing.TutorialWaitCaptured", true); Capture(manager, i, "04-wait.png"); }
            }
        }
        catch (Exception error)
        {
            Debug.LogException(error); SessionState.SetBool(ValidationKey, false);
            if (Application.isBatchMode) EditorApplication.Exit(1);
        }
    }

    private static void ValidateRoad()
    {
        foreach (DrivingTutorialRoad road in UnityEngine.Object.FindObjectsByType<DrivingTutorialRoad>(FindObjectsSortMode.None))
        {
            Require(road.transform.Find("Asphalt").GetComponent<Collider>() == null, "The visible asphalt must not introduce separate contact seams.");
            Transform course = road.transform.parent;
            BoxCollider floor = course.Find("Safety floor").GetComponent<BoxCollider>();
            Require(floor != null, "Each course needs one continuous drivable floor.");
            for (float distance = 0f; distance <= 195f; distance += .5f)
            {
                DrivingTutorialRoad.Sample(distance, out Vector3 center, out _);
                Vector3 origin = course.position + center + Vector3.up * 4f;
                Require(Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 8f), "Missing road contact.");
                Require(hit.collider == floor, "Road centerline has an unexpected contact seam.");
                Require(Mathf.Abs(hit.point.y - course.position.y) < .001f, "Uneven road height.");
            }
        }
        DrivingTutorialRoad.Sample(70f - .001f, out _, out Vector3 before);
        DrivingTutorialRoad.Sample(70f + .001f, out _, out Vector3 after);
        Require(Vector3.Angle(before, after) < .01f, "The turn must join the straight with a continuous tangent.");
    }
    private static void Capture(Gmanager manager, int player, string filename)
    {
        string directory = Path.Combine(Directory.GetCurrentDirectory(), "Artifacts/Tutorial"); Directory.CreateDirectory(directory);
        PlayerDisplayRig[] rigs = (PlayerDisplayRig[])typeof(Gmanager).GetField("displayRigs", Fields).GetValue(manager);
        PlayerDisplayRig rig = rigs[player];
        Camera camera = rig.BackImageCamera;
        Camera[] cameras = { rig.BackImageCamera, rig.MainCamera, rig.FrontCamera, rig.UiCamera };
        var oldTargets = new RenderTexture[cameras.Length]; var oldRects = new Rect[cameras.Length];
        int oldDisplay = rig.Canvas.targetDisplay;
        RenderMode oldMode = rig.Canvas.renderMode;
        RenderTexture previous = RenderTexture.active;
        RenderTexture texture = new RenderTexture(1672, 940, 24, RenderTextureFormat.ARGB32);
        Texture2D image = new Texture2D(1672, 940, TextureFormat.RGB24, false);
        try
        {
            for (int i = 0; i < cameras.Length; i++)
            {
                oldTargets[i] = cameras[i].targetTexture; oldRects[i] = cameras[i].rect;
                cameras[i].targetTexture = texture; cameras[i].rect = new Rect(0f, 0f, 1f, 1f);
            }
            rig.Canvas.targetDisplay = 0;
            rig.Canvas.worldCamera = rig.UiCamera;
            foreach (UnityEngine.UI.Graphic graphic in rig.Canvas.GetComponentsInChildren<UnityEngine.UI.Graphic>())
                Require(graphic.GetComponent<CanvasRenderer>() != null, "UI graphics must have a CanvasRenderer: " + graphic.name);
            Canvas.ForceUpdateCanvases();
            foreach (TMP_Text label in rig.Canvas.GetComponentsInChildren<TMP_Text>()) label.ForceMeshUpdate();
            Debug.Log($"TUTORIAL_QA_CANVAS: scale={rig.Canvas.transform.localScale}, size={((RectTransform)rig.Canvas.transform).rect.size}, cameraPixels={rig.UiCamera.pixelRect}");
            // Screen-space UI is emitted by the native game-view pass, which Camera.Render does
            // not run in batch mode. Freeze the same canvas plane as world-space geometry for
            // this synchronous snapshot; positions, typography and the UI camera stay identical.
            RectTransform canvasRect = (RectTransform)rig.Canvas.transform;
            Vector3 position = canvasRect.position, scale = canvasRect.localScale;
            Quaternion rotation = canvasRect.rotation; Vector2 size = canvasRect.rect.size;
            rig.Canvas.renderMode = RenderMode.WorldSpace;
            rig.Canvas.worldCamera = rig.UiCamera;
            canvasRect.SetPositionAndRotation(position, rotation); canvasRect.localScale = scale; canvasRect.sizeDelta = size;
            foreach (UnityEngine.UI.Graphic graphic in rig.Canvas.GetComponentsInChildren<UnityEngine.UI.Graphic>()) graphic.SetVerticesDirty();
            Canvas.ForceUpdateCanvases();
            ScriptableRenderContext.EmitGeometryForCamera(rig.UiCamera);
            RenderPipeline.SubmitRenderRequest(camera, new RenderPipeline.StandardRequest { destination = texture });
            RenderTexture.active = texture;
            image.ReadPixels(new Rect(0, 0, 1672, 940), 0, 0); image.Apply();
            File.WriteAllBytes(Path.Combine(directory, filename), image.EncodeToPNG());
            Debug.Log("TUTORIAL_QA_CAPTURE: " + filename);
        }
        finally
        {
            for (int i = 0; i < cameras.Length; i++) { cameras[i].targetTexture = oldTargets[i]; cameras[i].rect = oldRects[i]; }
            rig.Canvas.renderMode = oldMode; rig.Canvas.targetDisplay = oldDisplay;
            Canvas.ForceUpdateCanvases(); RenderTexture.active = previous;
            UnityEngine.Object.DestroyImmediate(image); texture.Release(); UnityEngine.Object.DestroyImmediate(texture);
        }
    }
    private static T Get<T>(object target, string field) => (T)target.GetType().GetField(field, Fields).GetValue(target);
    private static void Set(object target, string field, object value) => target.GetType().GetField(field, Fields).SetValue(target, value);
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
}
