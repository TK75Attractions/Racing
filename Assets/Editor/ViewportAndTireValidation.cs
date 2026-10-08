using System;
using System.IO;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

/// <summary>Play Mode regression for live viewport changes and marks after a retained drift.</summary>
public static class ViewportAndTireValidation
{
    private const string Key = "Racing.ViewportAndTireValidation";
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly Vector2Int[] Sizes = {
        new Vector2Int(1920,1080), new Vector2Int(1280,720), new Vector2Int(1680,720),
        new Vector2Int(1024,768), new Vector2Int(720,1280), new Vector2Int(1920,1080)
    };
    private static Gmanager manager;
    private static PlayerDisplayRig[] rigs;
    private static RenderTexture output;
    private static int step, sizeIndex, changedFrame, marksBefore;
    private static double began, phaseBegan;
    private static string screen;
    private static GameObject fixture, floor;
    private static Rigidbody body;
    private static TireForce[] tires;

    [MenuItem("Racing/Validation/Viewport Resize and Tire Marks")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        InputManager input = Object.FindFirstObjectByType<InputManager>();
        input.isDebugMode = true; input.isP1SerialP2KeyboardDebugMode = false;
        SessionState.SetBool(Key, true);
        EditorApplication.isPlaying = true;
    }

    [InitializeOnLoadMethod]
    private static void Resume()
    {
        if (!SessionState.GetBool(Key, false)) return;
        began = EditorApplication.timeSinceStartup;
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
    }

    private static void Tick()
    {
        if (!EditorApplication.isPlaying) return;
        try
        {
            if (EditorApplication.timeSinceStartup - began > 90d) throw new Exception("Validation timed out at step " + step);
            if (manager == null) manager = Gmanager.Control;
            if (manager == null || Time.frameCount < 5) return;
            if (rigs == null) rigs = (PlayerDisplayRig[])typeof(Gmanager).GetField("displayRigs", Private).GetValue(manager);
            if (tires != null)
                foreach (TireForce tire in tires) tire.ApplyForces(Vector3.forward, Vector3.up, 0f, 0f, 0f, 0f, 0f, false, false);
            switch (step)
            {
                case 0: BeginResizes("Title"); step=1; break;
                case 1:
                    if (!ResizeAndCheck()) break;
                    manager.StartGame(); step=2; break;
                case 2:
                    if (manager.state == Gmanager.State.Tutorial && !Transitioning())
                    {
                        Call("FinishTutorialWhenScreenCovered"); Call("CompleteGameStart");
                        foreach (PlayerDisplayRig rig in rigs) rig.Transition.ApplyStateImmediate(Gmanager.State.Countdown);
                    }
                    if (manager.state != Gmanager.State.Game || Transitioning()) break;
                    BeginResizes("Game"); step=3; break;
                case 3:
                    if (!ResizeAndCheck()) break;
                    CreateTireFixture(); phaseBegan=EditorApplication.timeSinceStartup; step=4; break;
                case 4 when EditorApplication.timeSinceStartup-phaseBegan > .8d:
                    Require(TireMarkRenderer.GetOrCreate().SegmentCount == 0, "Straight driving with retained drift must not emit marks.");
                    body.linearVelocity=new Vector3(8f,0f,10f); phaseBegan=EditorApplication.timeSinceStartup; step=5; break;
                case 5 when EditorApplication.timeSinceStartup-phaseBegan > .8d:
                    Require(TireMarkRenderer.GetOrCreate().SegmentCount > 0, "Actual lateral slip must emit marks even without lateral forces.");
                    body.linearVelocity=Vector3.forward*10f;
                    foreach (TireForce tire in tires) tire.ApplyForces(Vector3.forward,Vector3.up,0f,0f,0f,0f,0f,false,false);
                    marksBefore=TireMarkRenderer.GetOrCreate().SegmentCount; phaseBegan=EditorApplication.timeSinceStartup; step=6; break;
                case 6 when EditorApplication.timeSinceStartup-phaseBegan > .8d:
                    Require(fixture.GetComponent<DebugMover>().IsDrifting, "The fixture must retain drift to reproduce the original issue.");
                    Require(TireMarkRenderer.GetOrCreate().SegmentCount == marksBefore, "New marks must stop once sideways slip has ended.");
                    Object.Destroy(fixture); Object.Destroy(floor); tires=null;
                    manager.DebugPreviewResult(); step=7; break;
                case 7 when manager.state == Gmanager.State.Result && !Transitioning():
                    BeginResizes("Result"); step=8; break;
                case 8:
                    if (!ResizeAndCheck()) break;
                    Finish(true,"VIEWPORT_AND_TIRE_VALIDATION_PASS: both player title/HUD/result canvases survive six live sizes; straight/slide/straight marks with retained drift."); break;
            }
        }
        catch (Exception error) { Finish(false,error.ToString()); }
    }

    private static void BeginResizes(string name) { screen=name; sizeIndex=0; SetOutput(); }
    private static void SetOutput()
    {
        Vector2Int size=Sizes[sizeIndex];
        RenderTexture previous=output;
        output=new RenderTexture(size.x,size.y,24); output.Create();
        foreach (PlayerDisplayRig rig in rigs)
        {
            foreach (Camera camera in new[] {rig.BackImageCamera,rig.MainCamera,rig.FrontCamera,rig.UiCamera,rig.BackCamera}) camera.targetTexture=output;
            rig.Canvas.targetDisplay=0;
        }
        if (previous != null) { previous.Release(); Object.DestroyImmediate(previous); }
        changedFrame=Time.frameCount;
    }

    private static bool ResizeAndCheck()
    {
        if (Time.frameCount-changedFrame < 3) return false;
        Canvas.ForceUpdateCanvases();
        foreach (PlayerDisplayRig rig in rigs)
        {
            RectTransform canvas=(RectTransform)rig.Canvas.transform;
            Require(canvas.rect.width >= 1918f && canvas.rect.height >= 1078f,
                $"{screen} P{rig.PlayerIndex+1} clipped its reference layout at {Sizes[sizeIndex]}: layout={canvas.rect.size}, scale={rig.Canvas.scaleFactor}, cameraPixels={rig.UiCamera.pixelRect}, canvasPixels={rig.Canvas.pixelRect}, display={rig.Canvas.renderingDisplaySize}, screen={Screen.width}x{Screen.height}, aspect={rig.UiCamera.aspect}");
            Rect viewport=rig.UiCamera.pixelRect;
            foreach (TMP_Text label in rig.Canvas.GetComponentsInChildren<TMP_Text>())
            {
                if (string.IsNullOrEmpty(label.text)) continue;
                Vector3[] corners=new Vector3[4]; label.rectTransform.GetWorldCorners(corners);
                foreach (Vector3 corner in corners)
                {
                    Vector2 point=RectTransformUtility.WorldToScreenPoint(rig.UiCamera,corner);
                    Require(point.x >= viewport.xMin-2f && point.x <= viewport.xMax+2f && point.y >= viewport.yMin-2f && point.y <= viewport.yMax+2f,
                        $"{screen} P{rig.PlayerIndex+1} label {label.name} escaped viewport at {Sizes[sizeIndex]}: {point} / {viewport}");
                }
            }
        }
        if (sizeIndex==2 || sizeIndex==4) Capture(screen+"-"+Sizes[sizeIndex].x+"x"+Sizes[sizeIndex].y+".png");
        Debug.Log($"VIEWPORT_RESIZE_PASS: {screen} / {Sizes[sizeIndex]} / both players");
        if (++sizeIndex >= Sizes.Length) return true;
        SetOutput(); return false;
    }

    private static void CreateTireFixture()
    {
        fixture=Object.Instantiate(manager.carPrefab,new Vector3(0f,2000f,0f),Quaternion.identity);
        DebugMover mover=fixture.GetComponent<DebugMover>(); mover.enabled=false;
        foreach (CarStabilityController stability in fixture.GetComponentsInChildren<CarStabilityController>()) stability.enabled=false;
        foreach (CarResetter reset in fixture.GetComponentsInChildren<CarResetter>()) reset.enabled=false;
        body=fixture.GetComponent<Rigidbody>(); body.useGravity=false; body.constraints=RigidbodyConstraints.FreezeRotation|RigidbodyConstraints.FreezePositionY;
        tires=fixture.GetComponentsInChildren<TireForce>();
        float bottom=float.PositiveInfinity;
        foreach (TireForce tire in tires)
        {
            SphereCollider collider=tire.GetComponent<SphereCollider>();
            Vector3 scale=tire.transform.lossyScale;
            bottom=Mathf.Min(bottom,tire.transform.TransformPoint(collider.center).y-collider.radius*Mathf.Max(scale.x,scale.y,scale.z));
        }
        floor=GameObject.CreatePrimitive(PrimitiveType.Cube); floor.name="Tire QA floor";
        floor.transform.position=new Vector3(0f,bottom-.55f,0f); floor.transform.localScale=new Vector3(100f,1f,100f);
        foreach (Collider collider in fixture.GetComponentsInChildren<Collider>()) Physics.IgnoreCollision(collider,floor.GetComponent<Collider>());
        Physics.SyncTransforms();
        foreach (TireForce tire in tires) { tire.Init(body); tire.ApplyForces(Vector3.forward,Vector3.up,0f,0f,0f,0f,0f,false,false); Require(tire.IsGrounded,"The fixture tire must contact its floor."); }
        if (fixture.GetComponent<TireEffectsVisual>() == null) fixture.AddComponent<TireEffectsVisual>();
        typeof(DebugMover).GetField("isDrifting",Private).SetValue(mover,true);
        TireMarkRenderer.ClearAll(); body.linearVelocity=Vector3.forward*10f;
    }

    private static void Capture(string name)
    {
        PlayerDisplayRig rig=rigs[0]; RectTransform rect=(RectTransform)rig.Canvas.transform;
        RenderMode mode=rig.Canvas.renderMode; Vector3 position=rect.position,scale=rect.localScale;
        Quaternion rotation=rect.rotation; Vector2 size=rect.rect.size; RenderTexture active=RenderTexture.active;
        Texture2D snapshot=new Texture2D(output.width,output.height,TextureFormat.RGB24,false);
        try
        {
            rig.Canvas.renderMode=RenderMode.WorldSpace; rect.SetPositionAndRotation(position,rotation); rect.localScale=scale; rect.sizeDelta=size;
            foreach (UnityEngine.UI.Graphic graphic in rig.Canvas.GetComponentsInChildren<UnityEngine.UI.Graphic>()) graphic.SetVerticesDirty();
            Canvas.ForceUpdateCanvases(); RenderTexture.active=output; GL.Clear(true,true,Color.black);
            ScriptableRenderContext.EmitGeometryForCamera(rig.UiCamera);
            RenderPipeline.SubmitRenderRequest(rig.BackImageCamera,new RenderPipeline.StandardRequest { destination=output });
            snapshot.ReadPixels(new Rect(0,0,output.width,output.height),0,0); snapshot.Apply();
            string directory=Path.Combine(Directory.GetCurrentDirectory(),"Logs/ViewportValidation"); Directory.CreateDirectory(directory);
            File.WriteAllBytes(Path.Combine(directory,name),snapshot.EncodeToPNG());
        }
        finally { rig.Canvas.renderMode=mode; RenderTexture.active=active; Object.DestroyImmediate(snapshot); Canvas.ForceUpdateCanvases(); }
    }
    private static bool Transitioning() { foreach (PlayerDisplayRig rig in rigs) if (rig.Transition.IsTransitioning) return true; return false; }
    private static void Call(string name) => typeof(Gmanager).GetMethod(name,Private).Invoke(manager,null);
    private static void Require(bool condition,string message) { if (!condition) throw new Exception(message); }
    private static void Finish(bool passed,string message)
    {
        SessionState.SetBool(Key,false); EditorApplication.update-=Tick;
        if (output != null)
        {
            foreach (PlayerDisplayRig rig in rigs)
                foreach (Camera camera in new[] {rig.BackImageCamera,rig.MainCamera,rig.FrontCamera,rig.UiCamera,rig.BackCamera})
                    if (camera != null && camera.targetTexture == output) camera.targetTexture=null;
            output.Release(); Object.DestroyImmediate(output); output=null;
        }
        if (passed) Debug.Log(message); else Debug.LogError(message);
        if (Application.isBatchMode) EditorApplication.Exit(passed?0:1); else EditorApplication.isPlaying=false;
    }
}
