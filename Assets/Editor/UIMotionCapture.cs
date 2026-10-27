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

/// <summary>Deterministic previews made from the production components, not a second UI implementation.</summary>
public static class UIMotionCapture
{
    public const string Frames="/private/tmp/racing-ui-motion";
    public static void Run()
    {
        string[] settings={"ProjectSettings/GraphicsSettings.asset","ProjectSettings/QualitySettings.asset"};var original=Array.ConvertAll(settings,File.ReadAllBytes);
        try
        {
            Directory.CreateDirectory("Artifacts/UIMotion/stills");
            foreach(string clip in new[]{"hud-blue","transition-blue","buttons-pink","result-pink"})Capture(clip);
            Debug.Log("UI_MOTION_CAPTURE_PASS: four production-component animation sequences at 960x540 / 25 fps.");
        }
        finally{for(int i=0;i<settings.Length;i++)File.WriteAllBytes(settings[i],original[i]);}
    }
    private static void Capture(string clip)
    {
        var scene=EditorSceneManager.NewPreviewScene();var pipeline=GraphicsSettings.defaultRenderPipeline;var quality=QualitySettings.renderPipeline;var active=RenderTexture.active;
        var target=new RenderTexture(960,540,24){antiAliasing=4};var pixels=new Texture2D(960,540,TextureFormat.RGB24,false);
        string output=Path.Combine(Frames,clip);Directory.CreateDirectory(output);
        try
        {
            GraphicsSettings.defaultRenderPipeline=null;QualitySettings.renderPipeline=null;
            var cameraObject=new GameObject("Motion preview camera",typeof(Camera));SceneManager.MoveGameObjectToScene(cameraObject,scene);
            var camera=cameraObject.GetComponent<Camera>();camera.scene=scene;camera.orthographic=true;camera.orthographicSize=540;camera.transform.position=new Vector3(0,0,-10);camera.targetTexture=target;camera.enabled=false;
            var host=new GameObject("Motion preview canvas",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler));SceneManager.MoveGameObjectToScene(host,scene);
            var canvas=host.GetComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=camera;canvas.planeDistance=1;
            var scaler=host.GetComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1920,1080);scaler.matchWidthOrHeight=.5f;
            int player=clip.EndsWith("pink")?1:0;ArcadeUI.Bind(host.transform,player);var p=ArcadeUI.For(player);
            var backdrop=RacingUITheme.Rect(host.transform,"Backdrop",Vector2.zero,Vector2.one).gameObject.AddComponent<RawImage>();backdrop.raycastTarget=false;
            backdrop.texture=AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Editor/ArcadeReference/"+(clip=="hud-blue"?"Race.jpg":"Campus.jpg"));
            backdrop.uvRect=new Rect(.143f,.143f,.714f,.714f);
            UIPosition position=null;ArcadeDialGraphic speed=null,tach=null;TMP_Text speedText=null,rpm=null,boost=null;
            ScreenTransitionController transition=null;RacingTransitionGraphic wipe=null;CanvasGroup fade=null;PedalButtonFeedback button=null;UIRevealSequence reveal=null;UIValuePulse resultPulse=null;
            RectTransform play=null;Transform hud=null;
            if(clip=="hud-blue"||clip=="transition-blue")
            {
                play=RacingUITheme.Rect(host.transform,"OnPlay",Vector2.zero,Vector2.one);hud=RacingHUDBuilder.Build(play,player,2);
                position=new UIPosition();position.Init(hud.Find("Position"));position.SetPosition(2);
                var lap=new UILap();lap.Init(hud.Find("Lap"));lap.SetLap(2);var timer=new UITime();timer.Init(hud.Find("Time"));timer.SetTotalTime(72.34f);
                speed=hud.Find("Speed/Gauge").GetComponent<ArcadeDialGraphic>();tach=hud.Find("Tachometer/Gauge").GetComponent<ArcadeDialGraphic>();speed.SetReading(85);tach.SetReading(4800);tach.SetBoost(.3f);
                speedText=hud.Find("Speed/Txt").GetComponent<TMP_Text>();rpm=hud.Find("Tachometer/Txt").GetComponent<TMP_Text>();boost=hud.Find("Tachometer/Boost/Value").GetComponent<TMP_Text>();
                hud.Find("SteeringIndicator/Track").GetComponent<RacingSteeringIndicator>().SetState(-.25f,-.25f,.67f);
            }
            if(clip=="transition-blue"||clip=="buttons-pink")
            {
                var title=RacingUITheme.Rect(host.transform,"Title",Vector2.zero,Vector2.one);
                if(play==null)play=RacingUITheme.Rect(host.transform,"OnPlay",Vector2.zero,Vector2.one);
                var result=RacingUITheme.Rect(host.transform,"Result",Vector2.zero,Vector2.one);
                transition=host.AddComponent<ScreenTransitionController>();transition.Initialize(title,play,result,"","",player);transition.ApplyStateImmediate(Gmanager.State.Title);
                wipe=host.transform.Find("ScreenFade/RacingShutters").GetComponent<RacingTransitionGraphic>();fade=host.transform.Find("ScreenFade").GetComponent<CanvasGroup>();
                if(clip=="buttons-pink")button=title.Find("Player2Pedal").GetComponent<PedalButtonFeedback>();
            }
            if(clip=="result-pink")
            {
                var result=RacingUITheme.Rect(host.transform,"Result",Vector2.zero,Vector2.one);var ui=new ResultUIManager();ui.Init(result,player+1);
                var session=new RaceSessionResult(4);
                session.SetPlayerResult(0,new RaceResultRecord{playerNumber=1,finishPosition=2,didFinish=true,totalRaceTime=94.022f,goalLap=2,completedLaps=2});
                session.SetPlayerResult(1,new RaceResultRecord{playerNumber=2,finishPosition=1,didFinish=true,totalRaceTime=92.461f,goalLap=2,completedLaps=2});
                session.SetPlayerResult(2,new RaceResultRecord{playerNumber=3,isCpu=true,finishPosition=3,didFinish=true,totalRaceTime=95.133f});
                session.SetPlayerResult(3,new RaceResultRecord{playerNumber=4,isCpu=true,finishPosition=4,didFinish=true,totalRaceTime=96.874f});ui.ShowResults(session);
                var card=result.Find("ResultPresentation/ResultCard");reveal=card.GetComponent<UIRevealSequence>();resultPulse=card.Find("Finish/Rank").gameObject.AddComponent<UIValuePulse>();reveal.Play();
            }
            var pulses=host.GetComponentsInChildren<UIValuePulse>(true);var sequences=host.GetComponentsInChildren<UIRevealSequence>(true);var feedback=host.GetComponentsInChildren<PedalButtonFeedback>(true);
            for(int frame=0;frame<42;frame++)
            {
                float t=frame/25f;
                if(clip=="hud-blue")
                {
                    float reading=t<.8f?Mathf.Lerp(85,175,t/.8f):Mathf.Lerp(175,65,(t-.8f)/.8f);speedText.SetText("{0}",Mathf.RoundToInt(reading));speed.SetTargetReading(reading);
                    float revolutions=3200+reading*22;int hundreds=Mathf.RoundToInt(revolutions/100);rpm.SetText("{0:0}.{1:0}",hundreds/10,hundreds%10);tach.SetTargetReading(revolutions);
                    float charge=frame<8?.3f:Mathf.Clamp01(1-(frame-8)/40f);tach.SetTargetBoost(charge);boost.SetText("BOOST  {0}%",Mathf.RoundToInt(charge*100));
                    if(frame==8){position.SetPosition(1);hud.Find("Position/Txt").GetComponent<UIValuePulse>().PlayRank(new Color(.08f,.49f,.39f),1);tach.FlashBoost();}
                    if(frame==24){position.SetPosition(3);hud.Find("Position/Txt").GetComponent<UIValuePulse>().PlayRank(new Color(.82f,.22f,.12f),-1);}
                    speed.Tick(1f/25);tach.Tick(1f/25);
                }
                else if(clip=="transition-blue")
                {
                    if(frame==16){transition.ApplyStateImmediate(Gmanager.State.Countdown);transition.ShowCountdown(3);play.GetComponent<UIRevealSequence>().Play();}
                    float coverage=frame<6?0:frame<16?(frame-6)/10f:Mathf.Clamp01(1-(frame-16)/10f);
                    wipe.SetCoverage(coverage,frame>=16);fade.alpha=coverage>0?1:0;
                }
                else if(clip=="buttons-pink")
                {
                    float amount=frame<7?0:frame<13?(frame-7)/15f:frame<17?0:Mathf.Clamp01((frame-17)/16f);
                    button.SetState(true,amount,p.Primary);button.SetPointerState(true,amount>0,true);
                    if(frame==33)button.SetConfirmed(true);
                }
                else if(clip=="result-pink"&&frame==15)resultPulse.Play(p.Accent,0);
                if(frame>0){foreach(var s in sequences)s.Tick(1f/25);foreach(var pulse in pulses)pulse.Tick(1f/25);foreach(var f in feedback)f.Tick(1f/25);}
                foreach(var label in host.GetComponentsInChildren<TMP_Text>(false))label.ForceMeshUpdate();
                Canvas.ForceUpdateCanvases();camera.Render();RenderTexture.active=target;pixels.ReadPixels(new Rect(0,0,960,540),0,0);pixels.Apply();byte[] png=pixels.EncodeToPNG();
                File.WriteAllBytes(Path.Combine(output,$"frame-{frame:000}.png"),png);
                if(frame==12||frame==30)File.WriteAllBytes($"Artifacts/UIMotion/stills/{clip}-{frame:000}.png",png);
            }
        }
        finally
        {
            RenderTexture.active=active;GraphicsSettings.defaultRenderPipeline=pipeline;QualitySettings.renderPipeline=quality;
            UnityEngine.Object.DestroyImmediate(pixels);UnityEngine.Object.DestroyImmediate(target);EditorSceneManager.ClosePreviewScene(scene);
        }
    }
}
#endif
