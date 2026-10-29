using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class StartRoofAndLiveValidation
{
    private const string Folder = "Artifacts/StartRoofAndLive", Key = "Racing.StartRoofLiveValidation";
    private static double began;
    static StartRoofAndLiveValidation() { began=EditorApplication.timeSinceStartup; EditorApplication.update+=Tick; }
    private static void Tick()
    {
        if (!SessionState.GetBool(Key,false)) return;
        if (EditorApplication.timeSinceStartup-began>90) { Finish("Validation timed out."); return; }
        if (!EditorApplication.isPlaying || Gmanager.Control==null) return;
        SessionState.SetBool(Key,false);
        foreach(var publisher in Object.FindObjectsByType<RaceRemoteFirebasePublisher>(FindObjectsSortMode.None)) publisher.enabled=false;
        try { RunChecks(); Finish(null); } catch(Exception error) { Finish(error.ToString()); }
    }
    private static void Finish(string error)
    {
        SessionState.SetBool(Key,false);
        if(error!=null) { Directory.CreateDirectory(Folder);File.WriteAllText(Folder+"/failure.txt",error);Debug.LogError(error); }
        EditorApplication.isPlaying=false;
        if(Application.isBatchMode) EditorApplication.Exit(error==null?0:1);
    }
    [MenuItem("Racing/Validation/Start Roof and Live Display")]
    public static void Run()
    {
        if (SceneManager.GetActiveScene().path != "Assets/Scenes/SampleScene.unity")
            UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");

        SessionState.SetBool(Key,true); began=EditorApplication.timeSinceStartup;
        EditorApplication.isPlaying=true;
    }
    private static void RunChecks()
    {
        Physics.SyncTransforms();
        var original=SceneManager.GetActiveScene();
        var school=GameObject.Find("1016こうしゃ");
        Require(school!=null,"Open the race scene first.");
        var authored=school.transform.Find("Cube.026").GetComponent<MeshCollider>();
        Require(authored!=null,"Need the authored roof collider.");
        var actual=StartAreaCeilingCollision.EnsureForScene(original);
        Require(actual!=null&&!authored.enabled,"Production startup must repair the roof.");
        var targetBounds=actual.bounds;
        var isolated=SceneManager.CreateScene("Roof collision validation",new CreateSceneParameters(LocalPhysicsMode.Physics3D));
        var oldPipeline=GraphicsSettings.defaultRenderPipeline; var oldQuality=QualitySettings.renderPipeline;
        try
        {
            SceneManager.SetActiveScene(isolated);
            var root=new GameObject("1016こうしゃ");
            var roof=new GameObject("Cube.026");roof.transform.SetParent(root.transform);
            roof.transform.SetPositionAndRotation(authored.transform.position,authored.transform.rotation);
            roof.transform.localScale=authored.transform.lossyScale;
            var mesh=roof.AddComponent<MeshCollider>();mesh.sharedMesh=authored.sharedMesh;
            Physics.SyncTransforms();
            var solid=StartAreaCeilingCollision.EnsureForScene(isolated);
            Require(solid!=null&&!mesh.enabled,"Roof must use a positive-scale solid slab.");
            Physics.SyncTransforms();
            Require(Vector3.Distance(solid.bounds.center,targetBounds.center)<.01f&&Vector3.Distance(solid.bounds.size,targetBounds.size)<.01f,"Repair must match the visible roof bounds.");
            Require(StartAreaCeilingCollision.EnsureForScene(isolated)==solid,"Two players must not duplicate the roof collider.");
            var physics=isolated.GetPhysicsScene();
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefab/car/car2.prefab");
            var chassis=prefab.GetComponent<BoxCollider>();
            int trials=0;
            foreach(float x in new[]{targetBounds.center.x,targetBounds.max.x-1f})
            foreach(float speed in new[]{10f,30f,80f})
            foreach(float direction in new[]{-1f,1f})
            {
                var car=new GameObject("Production chassis collision probe");car.layer=prefab.layer;
                var box=car.AddComponent<BoxCollider>();box.size=chassis.size;box.center=chassis.center;
                car.transform.localScale=prefab.transform.localScale;
                float y=direction>0?targetBounds.min.y-4f:targetBounds.max.y+4f;
                car.transform.position=new Vector3(x,y,targetBounds.center.z);
                var body=car.AddComponent<Rigidbody>();body.useGravity=false;body.mass=prefab.GetComponent<Rigidbody>().mass;
                body.collisionDetectionMode=CollisionDetectionMode.ContinuousDynamic;body.constraints=RigidbodyConstraints.FreezeRotation;
                body.linearVelocity=Vector3.up*speed*direction;
                Physics.SyncTransforms();
                for(int i=0;i<50;i++)physics.Simulate(.02f);
                Require(direction>0?box.bounds.max.y<=targetBounds.min.y+.1f:box.bounds.min.y>=targetBounds.max.y-.1f,"Chassis passed through the roof: "+speed+" m/s, direction "+direction);
                Object.DestroyImmediate(car); trials++;
            }
            Directory.CreateDirectory(Folder);File.Delete(Folder+"/failure.txt");
            File.WriteAllText(Folder+"/verification.txt","PASS: actual start-side roof bounds; positive-scale slab; no duplicates; "+trials+" production chassis/layer upward/downward collision trials at 10/30/80 m/s and 0.02s physics steps, including roof edge.\n");
            GraphicsSettings.defaultRenderPipeline=null; QualitySettings.renderPipeline=null;
            CaptureHUD();
            File.AppendAllText(Folder+"/verification.txt","PASS: prominent LIVE badge on current footage only; hidden for REPLAY and COURSE PV; noninteractive overlay; spectator identity retained. UI captures use a recorded race reference as background.\n");
            Debug.Log("START_ROOF_AND_LIVE_VALIDATION_PASS: "+trials+" collision trials and live/replay/PV presentation.");
        }
        finally
        {
            GraphicsSettings.defaultRenderPipeline=oldPipeline; QualitySettings.renderPipeline=oldQuality;
            SceneManager.SetActiveScene(original);
            SceneManager.UnloadSceneAsync(isolated);
        }
    }
    private static void CaptureHUD()
    {
        var host=new GameObject("Live badge validation",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler));
        var cam=new GameObject("UI camera").AddComponent<Camera>();cam.clearFlags=CameraClearFlags.SolidColor;cam.backgroundColor=ArcadeUI.Blue.Ink;
        cam.cullingMask=1<<30;
        cam.transform.position=new Vector3(0,0,-10);cam.nearClipPlane=.1f;cam.farClipPlane=100;
        var output=new RenderTexture(1920,1080,24);output.Create();cam.targetTexture=output;
        var feed=new RenderTexture(1920,1080,0);feed.Create();Graphics.Blit(AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Editor/ArcadeReference/Race.jpg"),feed);
        var canvas=host.GetComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=cam;canvas.planeDistance=1;
        var scaler=host.GetComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1920,1080);
        var hud=new RaceBroadcastHUD(host.transform,feed);
        hud.Update("LIVE","P1 / 128 km/h","1 P1     2 P2     3 CPU1     4 CPU2");
        var badge=host.transform.Find("BroadcastLive");Require(badge.gameObject.activeSelf,"Live footage needs a LIVE badge.");
        foreach(var graphic in badge.GetComponentsInChildren<Graphic>()){Require(!graphic.raycastTarget,"LIVE must not intercept input.");Require(graphic.GetComponent<CanvasRenderer>()!=null,"LIVE graphics need a CanvasRenderer.");}
        foreach(var reveal in host.GetComponentsInChildren<UIRevealSequence>())reveal.Cancel();
        foreach(var child in host.GetComponentsInChildren<Transform>(true))child.gameObject.layer=30;
        Save(cam,output,"live.jpg");
        hud.Update("REPLAY","FINISH / P1","REPLAY");Require(!badge.gameObject.activeSelf,"Replay must never be marked live.");Save(cam,output,"replay.jpg");
        hud.Update("COURSE PV","TSUKUKOMA CIRCUIT","");Require(!badge.gameObject.activeSelf,"Course PV must never be marked live.");
        Object.DestroyImmediate(host);
        host=new GameObject("Spectator validation",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler));
        canvas=host.GetComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=cam;canvas.planeDistance=1;
        scaler=host.GetComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1920,1080);
        var background=RacingUITheme.Rect(host.transform,"Background",Vector2.zero,Vector2.one).gameObject.AddComponent<RawImage>();background.texture=feed;background.raycastTarget=false;
        var play=RacingUITheme.Rect(host.transform,"OnPlay",Vector2.zero,Vector2.one);
        var instruments=RacingHUDBuilder.Build(play,0,2);
        var map=new UIMiniMap();map.Init(instruments,Gmanager.Control.course,0);
        var spectator=SpectatorOverlayUI.Create(host.transform,null);spectator.Show(1);
        foreach(var reveal in host.GetComponentsInChildren<UIRevealSequence>())reveal.Cancel();
        Canvas.ForceUpdateCanvases();
        var liveBounds=RectTransformUtility.CalculateRelativeRectTransformBounds(host.transform,spectator.transform.Find("SpectatorLive"));
        foreach(var target in new[]{instruments.Find("MiniMap/MapFrame"),instruments.Find("Lap"),instruments.Find("Time")})
        {
            var other=RectTransformUtility.CalculateRelativeRectTransformBounds(host.transform,target);
            Require(!(liveBounds.min.x<other.max.x&&other.min.x<liveBounds.max.x&&liveBounds.min.y<other.max.y&&other.min.y<liveBounds.max.y),"LIVE must not cover race instruments.");
        }
        foreach(var text in spectator.GetComponentsInChildren<TMPro.TMP_Text>())
        { text.ForceMeshUpdate();Require(!text.isTextOverflowing,"LIVE text must fit: "+text.text);Require(text.font.HasCharacters(text.text),"LIVE glyphs: "+text.text); }
        foreach(var child in host.GetComponentsInChildren<Transform>(true))child.gameObject.layer=30;
        Save(cam,output,"spectator.jpg");
        Object.DestroyImmediate(host);
        cam.targetTexture=null;Object.DestroyImmediate(cam.gameObject);Object.DestroyImmediate(output);Object.DestroyImmediate(feed);
    }
    private static void Save(Camera camera,RenderTexture target,string name)
    {
        Canvas.ForceUpdateCanvases();camera.Render();var previous=RenderTexture.active;
        var pixels=new Texture2D(target.width,target.height,TextureFormat.RGB24,false);
        try { RenderTexture.active=target;pixels.ReadPixels(new Rect(0,0,target.width,target.height),0,0);pixels.Apply();File.WriteAllBytes(Folder+"/"+name,pixels.EncodeToJPG(90)); }
        finally { RenderTexture.active=previous;Object.DestroyImmediate(pixels); }
    }
    private static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
}
