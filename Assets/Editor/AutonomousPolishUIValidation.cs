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

/// <summary>Production UI renders with explicit fixture values; course views use SampleScene geometry.</summary>
public static class AutonomousPolishUIValidation
{
    private static string Output => Path.GetFullPath(Environment.GetEnvironmentVariable("RACING_POLISH_OUTPUT") ?? "Logs/AutonomousPolish/Visuals");
    private sealed class Input : IDriveInputSource
    {
        public int PlayerIndex { get; set; }
        public string DeviceId => "POLISH_QA";
        public bool IsConnected { get; set; } = true;
        public DriveInputState CurrentState { get; set; }
        public void UpdateInput(float dt) { }
        public void Dispose() { }
    }
    public static void Run()
    {
        RacingHUDFontBuilder.Build();
        ArcadeUIValidation.Run();
        RaceBarrierPathValidation.Run();
        ValidateBindings();
        Capture();
        Debug.Log("AUTONOMOUS_POLISH_UI_PASS: both palettes, readable Japanese states, notice preemption/release, held pedal, barrier UVs, 720p/1080p renders.");
    }
    public static void RunRules()
    {
        SkillGameplayValidation.Run();
        RacingPolishValidation.Run();
        CourseBarrierValidation.Run();
        Debug.Log("AUTONOMOUS_POLISH_RULES_PASS: input/recovery, tutorial prompts, barrier coverage and isolated physics.");
    }
    private static void ValidateBindings()
    {
        var root = new GameObject("Polish UI QA", typeof(RectTransform));
        try
        {
            var play = RacingUITheme.Rect(root.transform,"OnPlay",Vector2.zero,Vector2.one);
            var ui = new OnPlayUIManager(); ui.Init(play,null,0);
            Require(play.Find("ModernHUD/DriverNotice")==null,"Driving guidance must not create a center card.");
            var input = new Input(); var gate = new PlayerDriveInputGate(input);
            ui.UpdateDriverNotice(gate,null,true,coachingNotice:"曲がってためる");
            Require(ui.DriverNotice == "曲がってためる","A driving hint must be visible.");
            input.IsConnected = false; _ = gate.CurrentState;
            ui.UpdateDriverNotice(gate,null,true,coachingNotice:"曲がってためる");
            Require(ui.DriverNotice == "接続を確認してください","Lost input must interrupt a coaching hint immediately.");
            input.IsConnected = true; input.CurrentState = new DriveInputState {pedal=1};
            ui.UpdateDriverNotice(gate,null,true);
            Require(ui.DriverNotice == "ペダルを離して再開","The release latch must replace a resolved connection warning.");
            Require(gate.CurrentState.pedal == 0,"A held pedal cannot restart a reconnected car.");
            input.CurrentState = DriveInputState.Neutral; _ = gate.CurrentState;
            ui.UpdateDriverNotice(gate,null,true);
            Require(ui.DriverNotice != "ペダルを離して再開","Resolved critical instructions must clear immediately.");
            ui.UpdateDriverNotice(gate,null,false);
            Require(ui.DriverNotice.Length == 0,"Finished drivers must not retain coaching hints.");
            ArcadeNoticeRail.Get(root.transform).Set(3,"P2 オイルでスリップ");
            ui.UpdateDriverNotice(gate,null,true,true,coachingNotice:"曲がってためる");
            Require(ui.DriverNotice.Contains("ジャンプ"),"Jump safety must remain visible during rival/item feedback.");
            ui.UpdateDriverNotice(gate,null,false);
            ui.UpdateDriverNotice(gate,null,true,autodrive:true);
            Require(ui.DriverNotice.StartsWith("P1 "),"P1 autodrive must not be labelled as P2.");
            ui.UpdateDriverNotice(gate,null,false);input.PlayerIndex=1;
            ui.UpdateDriverNotice(gate,null,true,autodrive:true);
            Require(ui.DriverNotice.StartsWith("P2 "),"A spectating display must label the viewed car, not its owner.");
            ui.UpdateDriverNotice(gate,null,false);
            var practice = new DrivingTutorialUI(root.transform,0);
            var lesson = new DrivingTutorialProgress(1);
            lesson.Tick(1,true,0,0,0,0);practice.Update(lesson,DriveInputState.Neutral,0,true);
            var card = root.transform.Find("DrivingTutorial/CurrentInstruction");
            Require(((RectTransform)card).anchorMin.y > .75f,"Practice instructions must leave both the road horizon and car clear.");
            Require(card.Find("Message").GetComponent<TMP_Text>().text.Contains("準備待ち"),"Timeout must explain what happens next.");
            practice.Update(new DrivingTutorialProgress(),DriveInputState.Neutral,0,false);
            Require(card.Find("Status").GetComponent<TMP_Text>().text == "接続待ち","Connection status must update even when the lesson stage is unchanged.");
            practice.Dispose();
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
        var wall = new GameObject("Barrier UV QA");
        try
        {
            var path=wall.AddComponent<RaceBarrierPath>();path.AddControlPoint();path.AddControlPoint();path.Rebuild();
            var mesh=wall.GetComponentInChildren<MeshFilter>().sharedMesh;
            Require(mesh.uv2.Length==mesh.vertexCount,"Every barrier vertex needs distances to both edges.");
            Vector2 first=mesh.uv2[0], top=mesh.uv2[32], middle=mesh.uv2[16];
            Require(first.x==0 && top.y==0 && middle.x>.1f && middle.y>.1f,"Only the upper/lower boundaries receive edge glow.");
        }
        finally { UnityEngine.Object.DestroyImmediate(wall); }
    }

    public static void Capture()
    {
        Directory.CreateDirectory(Output);
        foreach(int player in new[]{0,1})
            foreach(int width in new[]{1280,1920})
                foreach(string state in new[]{"steering","waiting","disconnected","reconnect"})
                    CaptureUI(player,width,width*9/16,state);
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        var course=UnityEngine.Object.FindFirstObjectByType<RaceCourse>();course.RebuildCache();course.RebuildRoad();Physics.SyncTransforms();
        var tube=new SerializedObject(course).FindProperty("road.tubeInterior").objectReferenceValue as MeshCollider;
        var capture=typeof(CourseBarrierValidation).GetMethod("CaptureViews",BindingFlags.NonPublic|BindingFlags.Static);
        capture.Invoke(null,new object[]{course,tube,Output});
        var environment=UnityEngine.Object.FindFirstObjectByType<RaceEnvironmentController>();
        if(environment!=null)
        {
            var weather=environment.CurrentWeather;var time=environment.CurrentTimeOfDay;
            string night=Path.Combine(Output,"Night");Directory.CreateDirectory(night);
            try
            {
                environment.SetSituation(RaceEnvironmentController.Weather.Sunny,RaceEnvironmentController.TimeOfDay.Night);
                capture.Invoke(null,new object[]{course,tube,night});
            }
            finally { environment.SetSituation(weather,time); }
        }
        Debug.Log("AUTONOMOUS_POLISH_CAPTURE_PASS: "+Output);
    }

    private static void CaptureUI(int player,int width,int height,string state)
    {
        var scene=EditorSceneManager.NewPreviewScene();
        var pipeline=GraphicsSettings.defaultRenderPipeline;var quality=QualitySettings.renderPipeline;
        var active=RenderTexture.active;var target=new RenderTexture(width,height,24){antiAliasing=4};
        var pixels=new Texture2D(width,height,TextureFormat.RGB24,false);
        try
        {
            GraphicsSettings.defaultRenderPipeline=null;QualitySettings.renderPipeline=null;
            var go=new GameObject("Polish camera",typeof(Camera));SceneManager.MoveGameObjectToScene(go,scene);
            var camera=go.GetComponent<Camera>();camera.scene=scene;camera.orthographic=true;camera.orthographicSize=540;camera.transform.position=new Vector3(0,0,-10);camera.targetTexture=target;camera.enabled=false;
            var host=new GameObject("Polish canvas",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler));SceneManager.MoveGameObjectToScene(host,scene);
            var canvas=host.GetComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=camera;canvas.planeDistance=1;
            var scaler=host.GetComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1920,1080);scaler.matchWidthOrHeight=.5f;
            ArcadeUI.Bind(host.transform,player);
            var background=RacingUITheme.Rect(host.transform,"Backdrop",Vector2.zero,Vector2.one).gameObject.AddComponent<RawImage>();
            background.texture=AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Editor/ArcadeReference/Race.jpg");background.raycastTarget=false;
            if(state=="reconnect")
            {
                var play=RacingUITheme.Rect(host.transform,"OnPlay",Vector2.zero,Vector2.one);var ui=new OnPlayUIManager();ui.Init(play,null,player);
                var input=new Input();var gate=new PlayerDriveInputGate(input);gate.RequirePedalRelease();ui.UpdateDriverNotice(gate,null,true);
            }
            else
            {
                var lesson=new DrivingTutorialProgress();lesson.Tick(.1f,true,0,0,0,0);lesson.Tick(.7f,true,1,0,4,0);
                if(state=="waiting"){lesson.Tick(.7f,true,1,10,4,35);lesson.CollectItem(true);}
                new DrivingTutorialUI(host.transform,player).Update(lesson,DriveInputState.Neutral,0,state!="disconnected");
                // Production screen ownership hides the other screens. This fixture has
                // an always-visible backdrop, so explicitly put practice above it.
                host.transform.Find("DrivingTutorial").SetAsLastSibling();
            }
            Canvas.ForceUpdateCanvases();
            foreach(var label in host.GetComponentsInChildren<TMP_Text>())
            {
                label.ForceMeshUpdate();
                if(label.transform.parent.name=="CurrentInstruction" || label.transform.parent.name=="QuietRaceNotice")
                {
                    Require(!label.isTextOverflowing,"Instruction overflows at "+width+": "+label.text);
                    Require(label.font.HasCharacters(new string(label.text.Where(c=>!char.IsControl(c)).ToArray())),"Static font is missing instruction glyphs: "+label.text);
                }
            }
            camera.Render();RenderTexture.active=target;pixels.ReadPixels(new Rect(0,0,width,height),0,0);pixels.Apply();
            File.WriteAllBytes(Path.Combine(Output,$"{state}-p{player+1}-{width}.png"),pixels.EncodeToPNG());
        }
        finally
        {
            RenderTexture.active=active;GraphicsSettings.defaultRenderPipeline=pipeline;QualitySettings.renderPipeline=quality;
            UnityEngine.Object.DestroyImmediate(pixels);UnityEngine.Object.DestroyImmediate(target);EditorSceneManager.ClosePreviewScene(scene);
        }
    }
    private static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
}
#endif
