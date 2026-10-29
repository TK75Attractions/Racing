using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

/// <summary>Wall framing and passive dashboard copies from real rendered P1/P2 frames.</summary>
[InitializeOnLoad]
public static class OperationsFixValidation
{
    private const string Key = "Racing.OperationsFixValidation";
    private const string Folder = "Artifacts/OperationsFixes";
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static int stage, completed, copies;
    private static double began, nextCopy;
    private static string failure;
    private static PlayerDisplayRig[] rigs;
    private static Vector2[] canvasSizes;
    private static float[] canvasScales;
    private static RenderTexture[] outputs;

    static OperationsFixValidation()
    {
        EditorApplication.update += Tick;
        EditorApplication.playModeStateChanged += state =>
        {
            if (state != PlayModeStateChange.EnteredEditMode || !SessionState.GetBool(Key + ".RestoreInput", false)) return;
            SessionState.SetBool(Key + ".RestoreInput", false);
            var input = UnityEngine.Object.FindFirstObjectByType<InputManager>();
            if (input == null) return;
            input.isDebugMode = SessionState.GetBool(Key + ".Debug", false);
            input.isP1SerialP2KeyboardDebugMode = SessionState.GetBool(Key + ".Mixed", false);
        };
    }

    [MenuItem("Racing/Validation/Operations Fixes %#o")]
    public static void Run()
    {
        Require(!EditorApplication.isPlaying, "Stop Play mode before validation.");
        Directory.CreateDirectory(Folder);
        foreach (var target in UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsSortMode.None))
            if (target.name == "RaceCameraTarget" || target.name == "RaceCameraLookTarget") UnityEngine.Object.DestroyImmediate(target.gameObject);
        ValidateWallCamera();
        typeof(SlopeTubeDrivingValidation).GetMethod("ValidateCamera", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, null);
        for (int player = 0; player < 2; player++)
        {
            var resultRoot = new GameObject("Next action QA", typeof(RectTransform));
            try
            {
                var resultUi = new ResultUIManager(); resultUi.Init(resultRoot.transform, player + 1);
                typeof(UIResultMotionValidation).GetMethod("ValidateSelection", BindingFlags.NonPublic | BindingFlags.Static)
                    .Invoke(null, new object[] { resultUi, resultRoot.transform, player });
            }
            finally { UnityEngine.Object.DestroyImmediate(resultRoot); }
        }
        stage = completed = copies = 0; failure = null; rigs = null; canvasSizes = null; began = 0;
        var input = UnityEngine.Object.FindFirstObjectByType<InputManager>();
        SessionState.SetBool(Key + ".Debug", input.isDebugMode);
        SessionState.SetBool(Key + ".Mixed", input.isP1SerialP2KeyboardDebugMode);
        SessionState.SetBool(Key + ".RestoreInput", true);
        input.isDebugMode = true; input.isP1SerialP2KeyboardDebugMode = false;
        SessionState.SetBool(Key, true);
        EditorApplication.isPlaying = true;
    }

    private static void ValidateWallCamera()
    {
        var origin = new Vector3(-5000, 5000, -5000);
        var car = new GameObject("Bank camera QA", typeof(Rigidbody));
        var surface = GameObject.CreatePrimitive(PrimitiveType.Cube);
        UnityEngine.Object.DestroyImmediate(surface.GetComponent<BoxCollider>());
        var surfaceCollider = surface.AddComponent<MeshCollider>();
        surfaceCollider.sharedMesh = surface.GetComponent<MeshFilter>().sharedMesh;
        TubeDriveSurface.Ensure(surfaceCollider);
        var rig = new GameObject("Bank camera rig QA");
        try
        {
            car.GetComponent<Rigidbody>().useGravity = false;
            var checks = new GroundCheck[4];
            for (int i = 0; i < checks.Length; i++)
            {
                var wheel = new GameObject("Wheel", typeof(SphereCollider));
                wheel.transform.SetParent(car.transform, false);
                wheel.transform.localPosition = new Vector3(i % 2 == 0 ? -.6f : .6f, 0, i < 2 ? 1 : -1);
                wheel.GetComponent<SphereCollider>().radius = .3f;
                wheel.AddComponent<TireForce>();
                checks[i] = wheel.GetComponent<GroundCheck>();
                typeof(GroundCheck).GetMethod("Awake", Private).Invoke(checks[i], null);
            }
            var camera = rig.AddComponent<RaceDirectionCameraController>();
            surface.transform.localScale = new Vector3(100, 1, 100);
            foreach (float bank in new[] { -90f, -80f, -60f, 0f, 60f, 80f, 90f, 120f, 180f })
            {
                Quaternion rotation = Quaternion.Euler(0, 0, bank);
                Vector3 up = rotation * Vector3.up;
                surface.transform.SetPositionAndRotation(origin - up * .5f, rotation);
                car.transform.SetPositionAndRotation(origin + up * .3f, rotation);
                Physics.SyncTransforms();
                foreach (var wheel in checks) Require(wheel.CheckNow(), "Real contact missing on bank " + bank);
                camera.SetCar(car.transform);
                Vector3 offset = camera.CameraTarget.position - car.transform.position;
                Require(offset.magnitude > 5f, "Camera collapsed toward the car on bank " + bank + ": " + offset);
                Require(Vector3.Dot(offset, up) > 1.5f, "Lens must stay inside the driving surface on bank " + bank);
                Require(Mathf.Abs(camera.CameraTarget.right.y) < .001f, "Bank must not roll the horizon.");
                // A separate obstacle must still shorten the camera arm.
                var obstacle = GameObject.CreatePrimitive(PrimitiveType.Cube);
                try
                {
                    obstacle.transform.position = car.transform.position + up * 1.4f - Vector3.forward * 3;
                    obstacle.transform.localScale = Vector3.one * 1.2f;
                    Physics.SyncTransforms();
                    Vector3 constrained = camera.ConstrainCameraPosition(camera.CameraTarget.position);
                    Require(Vector3.Distance(constrained, car.transform.position) < offset.magnitude - .5f,
                        "Independent obstacle must still occlude the camera.");
                }
                finally { UnityEngine.Object.DestroyImmediate(obstacle); }
            }
            File.WriteAllText(Folder + "/wall-camera.txt", "PASS: banks -90,-80,-60,0,60,80,90,120,180; distance >5m; inside clearance >1.5m; level horizon; independent obstruction retained.\n");
        }
        finally { UnityEngine.Object.DestroyImmediate(car); UnityEngine.Object.DestroyImmediate(surface); UnityEngine.Object.DestroyImmediate(rig); }
    }

    private static void Tick()
    {
        if (!SessionState.GetBool(Key, false) || !EditorApplication.isPlaying || Gmanager.Control == null) return;
        try
        {
            if (began == 0) began = EditorApplication.timeSinceStartup;
            if (failure != null) throw new InvalidOperationException(failure);
            if (EditorApplication.timeSinceStartup - began > 90)
            {
                string detail = "";
                if (rigs != null) foreach (var rig in rigs)
                {
                    var preview = rig.CameraRoot.GetComponent<NativeResolutionUI>();
                    detail += " P" + (rig.PlayerIndex + 1) + " pending=" +
                        ((System.Collections.ICollection)typeof(NativeResolutionUI).GetField("previewRequests", Private).GetValue(preview)).Count +
                        " readback=" + typeof(NativeResolutionUI).GetField("previewReadback", Private).GetValue(preview);
                }
                throw new InvalidOperationException("Preview validation timed out at " + stage + detail);
            }
            var race = Gmanager.Control;
            if (stage == 0 && Time.frameCount > 15)
            {
                race.DebugPreviewResult();
                rigs = (PlayerDisplayRig[])typeof(Gmanager).GetField("displayRigs", Private).GetValue(race);
                // Batch mode cannot advance WaitForEndOfFrame; render both players to explicit outputs.
                // Interactive validation keeps P1 on the real Game-view backbuffer.
                outputs = new RenderTexture[2];
                for (int i = Application.isBatchMode ? 0 : 1; i < 2; i++)
                {
                    outputs[i] = new RenderTexture(1280, 720, 24); outputs[i].Create();
                    foreach (var camera in new[] { rigs[i].BackCamera, rigs[i].BackImageCamera,
                        rigs[i].MainCamera, rigs[i].FrontCamera, rigs[i].UiCamera })
                        if (camera != null) camera.targetTexture = outputs[i];
                }
                stage = 1; nextCopy = EditorApplication.timeSinceStartup + 2;
            }
            else if (stage == 1 && EditorApplication.timeSinceStartup >= nextCopy && !race.IsTransitioning())
            {
                if (canvasSizes == null)
                {
                    canvasSizes = new Vector2[2]; canvasScales = new float[2];
                    for (int i = 0; i < 2; i++)
                    {
                        canvasSizes[i] = ((RectTransform)rigs[i].Canvas.transform).sizeDelta;
                        canvasScales[i] = rigs[i].Canvas.scaleFactor;
                    }
                }
                for (int i = 0; i < 2; i++)
                {
                    int player = i;
                    race.RequestDashboardPlayerFrame(i, jpeg =>
                    {
                        if (jpeg == null || jpeg.Length < 3000) failure = "Missing passive preview P" + (player + 1);
                        else File.WriteAllBytes(Folder + "/preview-p" + (player + 1) + ".jpg", jpeg);
                        completed++;
                    });
                }
                copies++; nextCopy = EditorApplication.timeSinceStartup + 1.2;
                if (copies >= 5) stage = 2;
            }
            else if (stage == 2 && completed == copies * 2)
            {
                for (int i = 0; i < 2; i++)
                {
                    Require(rigs[i].Canvas.renderMode == RenderMode.ScreenSpaceCamera, "Preview changed the Canvas mode.");
                    Require(Vector2.Distance(((RectTransform)rigs[i].Canvas.transform).sizeDelta, canvasSizes[i]) < .01f &&
                        Mathf.Abs(rigs[i].Canvas.scaleFactor - canvasScales[i]) < .001f, "Preview resized the game HUD.");
                    var menu = rigs[i].CanvasRoot.transform.Find("Result/ResultPresentation/ResultCard/ResultMenu");
                    Require(menu.Find("Option2") == null, "Results must have one action.");
                }
                race.SelectResultOption(1, 0);
                Require(rigs[1].CanvasRoot.GetComponent<ArcadeEndingUI>().IsVisible &&
                    rigs[0].CanvasRoot.transform.Find("Result").gameObject.activeSelf, "Next must follow the player-local title path.");
                File.WriteAllText(Folder + "/verification.txt", "PASS: 10 asynchronous P1/P2 previews; stable Canvas mode, dimensions and scale; single Next action; independent ending.\n");
                Debug.Log("OPERATIONS_FIX_VALIDATION_PASS"); Finish();
            }
        }
        catch (Exception error) { failure = error.ToString(); Debug.LogException(error); File.WriteAllText(Folder + "/failure.txt", failure); Finish(); }
    }
    private static void Finish()
    {
        SessionState.SetBool(Key, false); EditorApplication.isPlaying = false; canvasSizes = null;
        if (outputs != null)
        {
            if (rigs != null) foreach (var rig in rigs)
                foreach (var camera in new[] { rig.BackCamera, rig.BackImageCamera, rig.MainCamera, rig.FrontCamera, rig.UiCamera })
                    if (camera != null && Array.IndexOf(outputs, camera.targetTexture) >= 0) camera.targetTexture = null;
            foreach (var target in outputs) if (target != null) UnityEngine.Object.Destroy(target);
        }
        outputs = null;
        if (Application.isBatchMode)
            EditorApplication.delayCall += () => EditorApplication.Exit(failure == null ? 0 : 1);
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
