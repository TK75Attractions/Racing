using System;
using System.Collections;
using System.IO;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object=UnityEngine.Object;

/// <summary>Production tutorial pages at both output sizes, plus completion/timeout gating.</summary>
public static class TutorialGimmickCapture
{
    public static void Run()
    {
        string[] settings={"ProjectSettings/GraphicsSettings.asset","ProjectSettings/QualitySettings.asset"};
        byte[][] previous=Array.ConvertAll(settings,File.ReadAllBytes);
        try
        {
            RacingHUDFontBuilder.Build();
            ValidateCompletionGate();
            foreach(int player in new[]{0,1})foreach(int width in new[]{1280,1920})for(int page=0;page<TutorialGimmickBriefing.PageCount;page++)Capture(player,width,page);
            Debug.Log("TUTORIAL_GIMMICK_UI_PASS: four simultaneous photographic hints in one overview, both players, 720p/1080p; glyphs, overflow, input transparency; independent completion and timed-out practice still receive the briefing.");
        }
        finally { for(int i=0;i<settings.Length;i++)File.WriteAllBytes(settings[i],previous[i]); }
    }
    private static void ValidateCompletionGate()
    {
        var host=new GameObject("Briefing completion gate");
        try
        {
            var controller=host.AddComponent<DrivingTutorialController>();
            var fields=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
            var type=typeof(DrivingTutorialController).GetNestedType("Practice",BindingFlags.NonPublic);
            var players=(IList)typeof(DrivingTutorialController).GetField("players",fields).GetValue(controller);
            var guides=new TutorialGimmickBriefing[2];
            for(int i=0;i<2;i++)
            {
                object practice=Activator.CreateInstance(type,true);
                var lesson=new DrivingTutorialProgress(.01f);lesson.Tick(.1f,false,0,0,0,0);
                Require(lesson.HasTimedOut,"Timeout fixture");
                type.GetField("lesson",fields).SetValue(practice,lesson);
                guides[i]=(TutorialGimmickBriefing)type.GetField("briefing",fields).GetValue(practice);players.Add(practice);
            }
            typeof(DrivingTutorialController).GetField("readyTimer",fields).SetValue(controller,2f);
            Require(!controller.AllComplete,"Practice timeout cannot skip the briefing");
            for(int i=0;i<450;i++)guides[0].Tick(.1f,false,0f);
            Require(!controller.AllComplete,"Both players must finish the briefing");
            for(int i=0;i<450;i++)guides[1].Tick(.1f,false,0f);
            Require(controller.AllComplete,"Automatic progression must remain bounded without input");
        }
        finally { Object.DestroyImmediate(host); }
    }
    private static void Capture(int player,int width,int page)
    {
        var scene=EditorSceneManager.NewPreviewScene();
        var pipeline=GraphicsSettings.defaultRenderPipeline;var quality=QualitySettings.renderPipeline;
        RenderTexture target=null;Texture2D image=null;Camera camera=null;
        try
        {
            GraphicsSettings.defaultRenderPipeline=null;QualitySettings.renderPipeline=null;
            var go=new GameObject("Guide preview camera",typeof(Camera));SceneManager.MoveGameObjectToScene(go,scene);
            camera=go.GetComponent<Camera>();camera.scene=scene;camera.orthographic=true;camera.transform.position=new Vector3(0,0,-10);
            var host=new GameObject("Tutorial canvas",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler));SceneManager.MoveGameObjectToScene(host,scene);
            var canvas=host.GetComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=camera;canvas.planeDistance=1f;
            var scaler=host.GetComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1920,1080);scaler.matchWidthOrHeight=.5f;
            ArcadeUI.Bind(host.transform,player);
            var background=RacingUITheme.Rect(host.transform,"Backdrop",Vector2.zero,Vector2.one).gameObject.AddComponent<RawImage>();background.raycastTarget=false;
            background.texture=AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Editor/ArcadeReference/Race.jpg");
            var ui=new DrivingTutorialUI(host.transform,player);
            host.transform.Find("DrivingTutorial").SetAsLastSibling();
            var lesson=new DrivingTutorialProgress();lesson.Tick(.1f,true,0,0,0,0);lesson.Tick(.7f,true,1,0,4,0);lesson.Tick(.7f,true,1,10,4,35);lesson.CollectItem(true);
            var briefing=new TutorialGimmickBriefing();
            for(int i=0;i<page;i++)briefing.Tick(TutorialGimmickBriefing.PageSeconds,false,0f);
            Require(briefing.Page==page,"Capture page setup");
            ui.Update(lesson,DriveInputState.Neutral,0,true,null,briefing);
            target=new RenderTexture(width,width*9/16,24);target.Create();camera.targetTexture=target;camera.Render();Canvas.ForceUpdateCanvases();
            var guide=host.transform.Find("DrivingTutorial/GimmickGuide");
            Require(guide.gameObject.activeInHierarchy,"Production tutorial must display the briefing");
            Require(guide.Find("Page0").childCount==4,"All four hints must be visible together.");
            for(int i=0;i<4;i++)Require(guide.Find("Page0/Card"+i).gameObject.activeInHierarchy,"Hidden hint "+i);
            Require(!host.transform.Find("DrivingTutorial/ItemGuide").gameObject.activeSelf,"Old waiting guide must not cover the new pages");
            foreach(var text in guide.GetComponentsInChildren<TMP_Text>())
            {
                text.ForceMeshUpdate();Require(!text.isTextOverflowing,"Overflow: "+width+" / "+page+" / "+text.transform.parent.name+"/"+text.name+" / "+text.text);
                Require(text.font.HasCharacters(text.text),"Missing glyphs: "+text.text);
            }
            foreach(var graphic in guide.GetComponentsInChildren<Graphic>())
            {
                Require(!graphic.raycastTarget,"Guide cannot intercept pedal/menu input");
                Require(graphic.GetComponent<CanvasRenderer>()!=null,"Every guide illustration needs a CanvasRenderer");
            }
            Require(!host.transform.Find("DrivingTutorial/PreparationTimer").gameObject.activeSelf,"A finished practice timer must not distract from reading");
            foreach(var photo in guide.GetComponentsInChildren<RawImage>())Require(photo.texture!=null&&photo.texture.width>=512,"Missing real gameplay photograph");
            var flow=guide.GetComponentInChildren<TutorialItemPickupFlow>();
            if(flow!=null){flow.SetPhase(.1f);Require(flow.HighlightedStep==0,"Box highlight");flow.SetPhase(1f);Require(flow.HighlightedStep==1,"Pickup highlight");flow.SetPhase(2f);Require(flow.HighlightedStep==2,"Automatic activation highlight");}
            if(flow!=null)
            {
                foreach(float time in new[]{2f,4.5f,7f,9.5f})
                { flow.SetPhase(time);foreach(var text in flow.GetComponentsInChildren<TMP_Text>()){text.ForceMeshUpdate();Require(!text.isTextOverflowing,"Pickup caption overflow: "+text.text);} }
                flow.SetPhase(2f);
            }
            camera.Render();var active=RenderTexture.active;
            try { RenderTexture.active=target;image=new Texture2D(width,width*9/16,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,width,width*9/16),0,0);image.Apply(); }
            finally { RenderTexture.active=active; }
            string directory="Artifacts/TutorialGimmicks/Preview";Directory.CreateDirectory(directory);
            // Keep both full-size review images; all four player/size combinations are validated.

            if(width==1920)File.WriteAllBytes($"{directory}/page-{page+1}-p{player+1}.png",image.EncodeToPNG());
        }
        finally
        {
            if(camera!=null)camera.targetTexture=null;
            if(target!=null)Object.DestroyImmediate(target);if(image!=null)Object.DestroyImmediate(image);
            GraphicsSettings.defaultRenderPipeline=pipeline;QualitySettings.renderPipeline=quality;EditorSceneManager.ClosePreviewScene(scene);
        }
    }
    private static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
}
