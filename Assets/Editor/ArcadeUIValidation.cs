#if UNITY_EDITOR
using System;
using System.IO;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>Builds the production UI, validates bindings and exports actual Unity renders.</summary>
public static class ArcadeUIValidation
{
    private static readonly string Output="Artifacts/ArcadeSportUI";
    [MenuItem("Racing/UI/Validate Arcade Sport UI")]
    public static void Run()
    {
        for(int player=0;player<2;player++)
        {
            var root=new GameObject("Arcade UI validation",typeof(RectTransform));
            try
            {
                ArcadeUI.Bind(root.transform,player);var play=RacingUITheme.Rect(root.transform,"OnPlay",Vector2.zero,Vector2.one);
                Transform hud=RacingHUDBuilder.Build(play,player,2);
                foreach(var graphic in hud.GetComponentsInChildren<Graphic>(true))Require(graphic.canvasRenderer!=null,"Native UI graphics require a CanvasRenderer.");
                Require(RacingHUDBuilder.Build(play,player,2)==hud,"HUD builder must be idempotent.");
                Require(hud.GetComponentsInChildren<RacingSteeringIndicator>(true).Length==1,"Exactly one steering display is required.");
                Require(hud.GetComponentsInChildren<RacingAccelerationGauge>(true).Length==0,"Obsolete acceleration gauge must not be built.");
                var steering=hud.Find("SteeringIndicator/Track").GetComponent<RacingSteeringIndicator>();steering.SetState(.9f,.8f,.35f);
                Require(Mathf.Abs(steering.AppliedFraction-.35f)<.001f,"Steering cursor must use the actual limited angle.");
                var telemetry=hud.GetComponent<ArcadeHUDTelemetry>();telemetry.SetTelemetry(6200,.72f);
                Require(Text(hud,"Tachometer/Txt")=="6.2"&&Text(hud,"Tachometer/Boost/Value")=="BOOST  72%","RPM and boost must use live bindings.");
                var rail=ArcadeNoticeRail.Get(root.transform);rail.Set(0,"この先ジャンプ");rail.Set(2,"P2 後方から接近");rail.Set(3,"P2 オイルでスリップ");
                Require(rail.Text=="P2 オイルでスリップ","Quiet notices must arbitrate competing sources.");rail.Set(3,"");Require(rail.Text=="P2 後方から接近","A remaining notice must survive a higher priority expiry.");rail.Clear();Require(rail.Text=="","Leaving a race must clear every quiet notice source.");
                var title=RacingUITheme.Rect(root.transform,"Title",Vector2.zero,Vector2.one);var result=RacingUITheme.Rect(root.transform,"Result",Vector2.zero,Vector2.one);
                var transition=root.AddComponent<ScreenTransitionController>();transition.Initialize(title,play,result,"","準備",player);transition.ShowCountdown(3);
                Require(hud.gameObject.activeSelf&&play.Find("CountdownStatus").gameObject.activeSelf,"Countdown must overlay the race HUD.");
                Require(!play.Find("CountdownCourse").gameObject.activeSelf,"Countdown must not duplicate course/lap cards.");
                Require(title.Find("News")==null||!title.Find("News").gameObject.activeSelf,"Title must omit the news strip.");
                var speedLabel=hud.Find("Speed/Txt").GetComponent<TMP_Text>();var tachLabel=hud.Find("Tachometer/Txt").GetComponent<TMP_Text>();
                Require(speedLabel.font==ArcadeUI.SpeedDigits&&tachLabel.font==speedLabel.font&&Mathf.Approximately(speedLabel.fontSize,tachLabel.fontSize)&&speedLabel.fontSize>108&&speedLabel.fontSize<154&&!speedLabel.enableAutoSizing&&!tachLabel.enableAutoSizing,"Both instruments use the same intermediate ExtraBold size without shrinking.");
                foreach(string name in new[]{"Speed","Tachometer"})foreach(string label in new[]{"Heading","Unit"})Require(hud.Find(name+"/"+label).GetComponent<TMP_Text>().fontSize>20,"Meter headings and units must be larger.");
                var lap=new UILap();lap.Init(hud.Find("Lap"));var left=hud.Find("Lap/Ticks/Left").GetComponent<Image>();var right=hud.Find("Lap/Ticks/Right").GetComponent<Image>();
                Require(left.color.a==1&&right.color.a<.3f,"First lap lights only the left segment.");lap.SetLap(2);Require(left.color.a==1&&right.color.a==1,"Second lap lights both segments.");lap.SetLap(1);Require(left.color.a==1&&right.color.a<.3f,"A new race resets the second segment.");
                Require(hud.Find("Position").GetComponent<ArcadeSurfaceGraphic>()!=null&&hud.Find("Position").GetComponent<ArcadeSurfaceGraphic>().ShadowOffset.x>0,"Rank needs the shared plate and shadow.");
                var itemHud=PreviewItemNotice(root.transform,"ブースト発動！","BOOST ON");
                var toast=root.transform.Find("ItemHUD/Toast") as RectTransform;var status=root.transform.Find("ItemHUD/StatusPlate") as RectTransform;
                Require(toast.anchorMin.x>=.7f&&status.anchorMin.x>=.7f&&toast.anchorMin.y>hud.Find("Tachometer").GetComponent<RectTransform>().anchorMax.y,"Toast and active effect must leave the central driving view clear.");
                Require(toast.anchorMax.y<.683f&&status.anchorMax.y<toast.anchorMin.y&&status.anchorMin.y>.5f,"Notice, quiet rail, effect status and tachometer must not overlap.");
                Require(toast.localScale==Vector3.one&&!toast.GetComponent<CanvasGroup>().blocksRaycasts,"Notices must not zoom or intercept controls.");
                TickItemNotice(itemHud,1.7f);Require(toast.GetComponent<CanvasGroup>().alpha==0,"Expired notices must clear.");
                var shadow=hud.Find("Time").GetComponent<ArcadeSurfaceGraphic>();Require(shadow.ShadowOffset.x>0&&shadow.ShadowOffset.y<0,"HUD shadows point down and right.");
                var speedDial=hud.Find("Speed/Gauge").GetComponent<ArcadeDialGraphic>();speedDial.SetReading(0);var cold=speedDial.NeedleColor;speedDial.SetReading(240);Require(cold!=speedDial.NeedleColor,"Speed changes the needle colour.");
                Require(title.Find("CircuitLogo/Artwork").GetComponent<RawImage>().texture!=null,"Supplied transparent logo must load.");
                var ui=new ResultUIManager();ui.Init(result,player+1);var session=Session();ui.ShowResults(session);
                var card=result.Find("ResultPresentation/ResultCard");Require(card.Find("Classification").gameObject.activeSelf,"Classification must be initially visible.");
                Require(card.Find("RankingDisclosure")==null&&card.Find("DailyRanking/DailyRow0")==null,"Daily rank must not expand a full ranking.");
                Require(Text(card,"DailyRanking/OwnRank").StartsWith("本日")&&Text(card,"DailyRanking/OwnRank").EndsWith("位！"),"Each player sees only their own daily place.");
                session.GetPlayerResult(player).didFinish=false;ui.ShowResults(session);Require(Text(card,"Finish/Rank")=="DNF"&&Text(card,"Finish/Time")=="未完走","DNF must not award a finish place/time.");
                var practice=new DrivingTutorialUI(root.transform,player);var lesson=new DrivingTutorialProgress();practice.Update(lesson,new DriveInputState{steering=-24,pedal=.65f},6,true);
                var tutorial=root.transform.Find("DrivingTutorial/ModernHUD");Require(tutorial.GetComponentsInChildren<ArcadeDialGraphic>(true).Length==2,"Practice must reuse both race dials.");Require(!tutorial.Find("Position").gameObject.activeSelf&&!tutorial.Find("Time").gameObject.activeSelf,"Practice must omit race rank/timer.");Require(tutorial.Find("SteeringIndicator/Track").GetComponent<RacingSteeringIndicator>().enabled,"Practice steering must remain visible.");practice.Dispose();
                bool completed=false;var ending=ArcadeEndingUI.Get(root.transform);ending.Show(()=>completed=true);ending.TickInput(1,2);Require(!completed,"Finishing with a held pedal must not skip thanks.");ending.TickInput(0,.1f);ending.TickInput(1,1);Require(completed&&!ending.IsVisible,"Ending must accept a deliberate pedal confirmation.");
                int dirty=0;var dial=hud.Find("Tachometer/Gauge").GetComponent<ArcadeDialGraphic>();dial.RegisterDirtyVerticesCallback(()=>dirty++);for(int i=0;i<120;i++)telemetry.SetTelemetry(6200,.72f);Require(dirty==0,"Unchanged telemetry must not rebuild meshes.");
            }
            finally{UnityEngine.Object.DestroyImmediate(root);}
        }
        Debug.Log("ARCADE_UI_VALIDATION_PASS: both palettes, live instruments, shared practice/countdown, notice priority, daily place summary, matched ExtraBold instruments, two lap segments, peripheral item notices, HUD shadows, speed colours, DNF, ending input gate, stable meshes.");
    }
    [MenuItem("Racing/UI/Capture Arcade Sport UI")]
    public static void Capture()
    {
        string[] paths={"ProjectSettings/GraphicsSettings.asset","ProjectSettings/QualitySettings.asset"};var bytes=Array.ConvertAll(paths,File.ReadAllBytes);
        try{Directory.CreateDirectory(Output);
        foreach(int player in new[]{0,1})foreach(string state in new[]{"title","tutorial","countdown","race","spectator","quiet","boost","item","pause","settings","result","ending","broadcast","replay","pv"})CaptureView(player,state,1920,1080);
        foreach(int player in new[]{0,1}){CaptureView(player,"race",1280,720);CaptureView(player,"race",3840,2160);}
        Debug.Log("ARCADE_UI_CAPTURE_PASS: "+Path.GetFullPath(Output));
        }finally{for(int i=0;i<paths.Length;i++)File.WriteAllBytes(paths[i],bytes[i]);}
    }
    public static void RunAndCapture(){RacingHUDFontBuilder.Build();Run();Capture();}
    public static void RunRegressionChecks()
    {
        string oldDaily=Environment.GetEnvironmentVariable("RACING_DAILY_RANKING_OUTPUT"),oldHighlight=Environment.GetEnvironmentVariable("RACING_HIGHLIGHT_VALIDATION_OUTPUT");
        try
        {
            Environment.SetEnvironmentVariable("RACING_DAILY_RANKING_OUTPUT",Path.Combine(Path.GetTempPath(),"arcade-daily-ranking"));
            Environment.SetEnvironmentVariable("RACING_HIGHLIGHT_VALIDATION_OUTPUT",Path.Combine(Path.GetTempPath(),"arcade-result-highlights"));
            DailyRankingValidation.Run();ResultHighlightValidation.Run();RunAndCapture();
        }
        finally
        {
            Environment.SetEnvironmentVariable("RACING_DAILY_RANKING_OUTPUT",oldDaily);Environment.SetEnvironmentVariable("RACING_HIGHLIGHT_VALIDATION_OUTPUT",oldHighlight);
        }
    }
    private static RaceSessionResult Session()
    {
        var session=new RaceSessionResult(4);
        session.SetPlayerResult(0,new RaceResultRecord{playerNumber=1,finishPosition=2,totalRaceTime=109.933f,didFinish=true,goalLap=2,completedLaps=2});
        session.SetPlayerResult(1,new RaceResultRecord{playerNumber=2,finishPosition=4,totalRaceTime=132.896f,didFinish=true,goalLap=2,completedLaps=2});
        session.SetPlayerResult(2,new RaceResultRecord{playerNumber=3,isCpu=true,finishPosition=1,totalRaceTime=108.865f,didFinish=true,goalLap=2,completedLaps=2});
        session.SetPlayerResult(3,new RaceResultRecord{playerNumber=4,isCpu=true,finishPosition=3,totalRaceTime=132.647f,didFinish=true,goalLap=2,completedLaps=2});
        var book=new DailyRankingBook{day="2026-10-24"};var now=new DateTime(2026,10,24,12,0,0);
        book.Record("previous1","arcade",new RaceResultRecord{playerNumber=1,didFinish=true,goalLap=2,completedLaps=2,totalRaceTime=90},now);
        book.Record("previous2","arcade",new RaceResultRecord{playerNumber=1,didFinish=true,goalLap=2,completedLaps=2,totalRaceTime=100},now);
        book.Record("current","arcade",session.GetPlayerResult(0),now);book.Record("current","arcade",session.GetPlayerResult(1),now);
        session.dailyRanking=DailyRankingView.Build(book,"arcade","current");return session;
    }
    private static void CaptureView(int player,string state,int width,int height)
    {
        var scene=EditorSceneManager.NewPreviewScene();RenderPipelineAsset pipeline=GraphicsSettings.defaultRenderPipeline,quality=QualitySettings.renderPipeline;RenderTexture previous=RenderTexture.active;
        var target=new RenderTexture(width,height,24,RenderTextureFormat.ARGB32){antiAliasing=4};var pixels=new Texture2D(width,height,TextureFormat.RGB24,false);
        try
        {
            GraphicsSettings.defaultRenderPipeline=null;QualitySettings.renderPipeline=null;
            var go=new GameObject("Arcade camera",typeof(Camera));SceneManager.MoveGameObjectToScene(go,scene);var camera=go.GetComponent<Camera>();camera.scene=scene;camera.orthographic=true;camera.orthographicSize=540;camera.transform.position=new Vector3(0,0,-10);camera.targetTexture=target;camera.enabled=false;
            var host=new GameObject("Arcade canvas",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler));SceneManager.MoveGameObjectToScene(host,scene);var canvas=host.GetComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=camera;canvas.planeDistance=1;
            var scaler=host.GetComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1920,1080);scaler.matchWidthOrHeight=.5f;ArcadeUI.Bind(host.transform,player);
            var backdrop=RacingUITheme.Rect(host.transform,"Backdrop",Vector2.zero,Vector2.one).gameObject.AddComponent<RawImage>();backdrop.texture=AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Editor/ArcadeReference/"+(state=="title"||state=="result"||state=="ending"?"Campus.jpg":"Race.jpg"));backdrop.uvRect=new Rect(.143f,.143f,.714f,.714f);backdrop.raycastTarget=false;
            var play=RacingUITheme.Rect(host.transform,"OnPlay",Vector2.zero,Vector2.one);var hud=RacingHUDBuilder.Build(play,player,2);
            hud.Find("Leaderboard").GetComponent<RacingLeaderboardUI>().SetPosition(2,8);
            var pos=new UIPosition();pos.Init(hud.Find("Position"));pos.SetPosition(2);var lap=new UILap();lap.Init(hud.Find("Lap"));lap.SetLap(2);var time=new UITime();time.Init(hud.Find("Time"));time.SetTotalTime(72.34f);
            var speed=new UISpeed();speed.Init(hud.Find("Speed"));speed.UpdateSpeedMeter(124,0);hud.GetComponent<ArcadeHUDTelemetry>().SetTelemetry(6200,.72f);hud.GetComponent<ArcadeHUDTelemetry>().SetLapDelta(-.42f);hud.Find("SteeringIndicator/Track").GetComponent<RacingSteeringIndicator>().SetState(-24f/90,-24f/90,60f/90);
            var title=RacingUITheme.Rect(host.transform,"Title",Vector2.zero,Vector2.one);var result=RacingUITheme.Rect(host.transform,"Result",Vector2.zero,Vector2.one);var results=new ResultUIManager();results.Init(result,player+1);results.ShowResults(Session());
            var transition=host.AddComponent<ScreenTransitionController>();transition.Initialize(title,play,result,"","ペダルを離して準備してください",player);transition.ApplyStateImmediate(state=="title"?Gmanager.State.Title:state=="result"?Gmanager.State.Result:Gmanager.State.Game);
            if(state=="tutorial"){play.gameObject.SetActive(false);var practice=new DrivingTutorialUI(host.transform,player);var lesson=new DrivingTutorialProgress();practice.Update(lesson,new DriveInputState{steering=-24,pedal=.65f},12,true);host.transform.Find("DrivingTutorial").SetAsLastSibling();}
            if(state=="countdown"){transition.ShowCountdown(3);speed.UpdateSpeedMeter(0,0);time.SetTotalTime(0);lap.SetLap(1);hud.GetComponent<ArcadeHUDTelemetry>().SetTelemetry(900,0);}
            if(state=="spectator")SpectatorOverlayUI.Create(host.transform,RacingUIFontCatalog.GetHUD(true)).Show(player==0?1:0);
            if(state=="quiet")ArcadeNoticeRail.Get(host.transform).Set(2,"P"+(player==0?2:1)+" 後方から接近");
            if(state=="boost")
            {
                PreviewItemNotice(host.transform,"ブースト発動！","BOOST ON");
            }
            if(state=="item")
            {
                PreviewItemNotice(host.transform,"ハンドル反転!","左右の操作が逆になります");
                var status=host.transform.Find("ItemHUD/StatusPlate");status.gameObject.SetActive(true);status.Find("Status").GetComponent<TMP_Text>().text="ハンドル反転 / CONFUSED   3.2秒";
                ArcadeNoticeRail.Get(host.transform).Set(2,"P"+(player==0?2:1)+" 後方から接近");
            }
            if(state=="pause"||state=="settings"){var menu=InterruptionMenuUI.Create(host.transform,null,null,null);menu.Show(false);if(state=="settings")host.transform.Find("InterruptionMenu/MenuCard/Options").GetComponent<Button>().onClick.Invoke();}
            if(state=="ending")ArcadeEndingUI.Get(host.transform).Show(null);
            if(state=="broadcast"||state=="replay"||state=="pv"){play.gameObject.SetActive(false);var feed=new RaceBroadcastHUD(host.transform,null);host.transform.Find("BroadcastVideo").gameObject.SetActive(false);feed.Update(state=="pv"?"COURSE PV":state=="replay"?"REPLAY":"LIVE",state=="pv"?"CAMPUS MOTORSPORT":state=="replay"?"OVERTAKE / CPU 2":"P2 / 111 km/h",state=="pv"?"":"1 P1     2 P2     3 CPU 2     4 CPU 1");}
            foreach(TMP_Text label in host.GetComponentsInChildren<TMP_Text>(false))label.ForceMeshUpdate();
            Canvas.ForceUpdateCanvases();camera.Render();RenderTexture.active=target;pixels.ReadPixels(new Rect(0,0,width,height),0,0);pixels.Apply();File.WriteAllBytes(Path.Combine(Output,$"{state}-p{player+1}"+(width==1920?"":$"-{width}x{height}")+".png"),pixels.EncodeToPNG());
        }
        finally{RenderTexture.active=previous;GraphicsSettings.defaultRenderPipeline=pipeline;QualitySettings.renderPipeline=quality;UnityEngine.Object.DestroyImmediate(pixels);UnityEngine.Object.DestroyImmediate(target);EditorSceneManager.ClosePreviewScene(scene);}
    }
    // Exercise the production toast, including its animation, rather than a separate mock layout.
    private static PlayerItemHUD PreviewItemNotice(Transform parent,string title,string caption)
    {
        var hud=PlayerItemHUD.Create(parent);parent.Find("ItemHUD").gameObject.SetActive(true);
        typeof(PlayerItemHUD).GetMethod("ShowToast",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(hud,new object[]{title,caption,Color.white});
        TickItemNotice(hud,.1f);return hud;
    }
    private static void TickItemNotice(PlayerItemHUD hud,float seconds)=>typeof(PlayerItemHUD).GetMethod("UpdateToast",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(hud,new object[]{seconds});
    private static string Text(Transform parent,string path)=>parent.Find(path).GetComponent<TMP_Text>().text;
    private static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
}
#endif
