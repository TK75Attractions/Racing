using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

/// <summary>One shared world feed for the broadcast window and both result replay panes.</summary>
[DefaultExecutionOrder(100)]
public sealed class RaceBroadcastDirector : MonoBehaviour
{
    private const int UiLayer=28;
    private Gmanager manager;
    private Camera world,presenter;
    private GameObject presentationRoot;
    private RenderGraphDisplayOutput output;
    private RaceBroadcastHUD hud;
    private RaceReplayAssets assets;
    private RaceReplayRecorder recorder;
    private readonly RaceReplayPlayback playback=new RaceReplayPlayback();
    private readonly List<RaceReplayTake> history=new List<RaceReplayTake>();
    private readonly List<(Renderer renderer,bool original)> hidden=new List<(Renderer,bool)>();
    private readonly List<Renderer> identityLabels=new List<Renderer>();
    private TracksideCameraRig rig;
    private Vector3[] anchors;
    private int archive,clip,focus;
    private float cursor,focusRemaining;
    private bool recordingWasGame,showWindow,replaying;
    private float nextStandings;
    private string cachedStandings="";
    private string folder;
    private int broadcastDisplay;
    public RenderTexture Feed { get; private set; }
    public Camera WorldCamera => world;
    public Camera PresentationCamera => presenter;
    public Canvas PresentationCanvas { get; private set; }
    public string Mode { get; private set; }="COURSE PV";
    public string Caption { get; private set; }="TSUKUKOMA CIRCUIT";
    public int RecordedFrames=>recorder?.Take.Count??0;
    public int StoredRaces=>history.Count;
    public int ReplayActors=>playback.ActorCount;
    public int CameraCuts=>rig?.Cuts??0;
    public RaceReplayTake LatestReplay=>history.Count>0?history[0]:null;
    public bool IsReplaying=>replaying;
    public int PresentedFrames=>output!=null?output.PresentedFrames:0;
    public Vector2Int WindowPixels=>output?.Target!=null?new Vector2Int(output.Target.width,output.Target.height):Vector2Int.zero;
    public void RestartLatestHighlights(){archive=clip=0;cursor=0;rig?.Cut();}

    public void Configure(Gmanager race,PlayerDisplayRig[] displays)
    {
        manager=race;assets=new RaceReplayAssets(manager.carPrefab);rig=new TracksideCameraRig(manager);
        broadcastDisplay=RaceDisplayRouting.BroadcastDisplay(displays[0].DisplayIndex,displays[1].DisplayIndex);
        anchors=TracksideCameraRig.BuildAnchors(manager.course);
        folder=Environment.GetEnvironmentVariable("RACING_BROADCAST_HISTORY")??Path.Combine(Application.persistentDataPath,"BroadcastHighlights");
        if(Directory.Exists(folder))foreach(var file in new DirectoryInfo(folder).GetFiles("race-*.rpl").OrderByDescending(x=>x.LastWriteTimeUtc).Take(3))
            try{history.Add(RaceReplayArchive.Load(file.FullName,assets));}catch(Exception error){Debug.LogWarning("Broadcast replay skipped: "+error.Message);}
        Feed=new RenderTexture(1280,720,24,RenderTextureFormat.Default){name="Race broadcast / shared highlights",antiAliasing=1};Feed.Create();
        var go=new GameObject("Broadcast world camera");go.transform.SetParent(transform,false);world=go.AddComponent<Camera>();
        world.CopyFrom(displays[0].MainCamera);world.targetTexture=Feed;world.depth=-100;world.targetDisplay=0;
        world.rect=new Rect(0,0,1,1);world.aspect=16f/9f;
        world.nearClipPlane=.15f;world.farClipPlane=2200;world.clearFlags=CameraClearFlags.Skybox;
        world.cullingMask&=~((1<<UiLayer)|(1<<5)|(1<<10)|(1<<11)|(1<<12)|(1<<13));world.cullingMask|=1<<RaceReplayPlayback.Layer;
        var worldData=world.GetUniversalAdditionalCameraData();worldData.renderType=CameraRenderType.Base;worldData.renderPostProcessing=false;
        foreach(var display in displays)
        {
            foreach(var camera in display.CameraRoot.GetComponentsInChildren<Camera>(true))camera.cullingMask&=~((1<<UiLayer)|(1<<RaceReplayPlayback.Layer));
            if(display.BackCamera!=null)display.BackCamera.cullingMask&=~((1<<UiLayer)|(1<<RaceReplayPlayback.Layer));
        }
        presentationRoot=new GameObject("Broadcast display 3");presentationRoot.transform.SetParent(transform,false);
        presenter=presentationRoot.AddComponent<Camera>();presenter.clearFlags=CameraClearFlags.SolidColor;presenter.backgroundColor=Color.black;
        presenter.orthographic=true;presenter.orthographicSize=5;presenter.nearClipPlane=.1f;presenter.farClipPlane=20;presenter.cullingMask=1<<UiLayer;presenter.depth=100;presenter.targetDisplay=broadcastDisplay;
        var canvasObject=new GameObject("Broadcast canvas",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler));canvasObject.transform.SetParent(presentationRoot.transform,false);
        var canvas=canvasObject.GetComponent<Canvas>();PresentationCanvas=canvas;canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=presenter;canvas.planeDistance=1;canvas.targetDisplay=broadcastDisplay;
        var scaler=canvasObject.GetComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1920,1080);scaler.matchWidthOrHeight=.5f;
        hud=new RaceBroadcastHUD(canvasObject.transform,Feed);
        foreach(Transform child in canvasObject.GetComponentsInChildren<Transform>(true))child.gameObject.layer=UiLayer;
        output=presentationRoot.AddComponent<RenderGraphDisplayOutput>();output.enabled=false;
        RenderPipelineManager.beginCameraRendering+=BeforeCamera;RenderPipelineManager.endCameraRendering+=AfterCamera;
        RefreshWindow();
    }
    private bool Feature(RaceFeature feature)=>manager?.Features?.Settings.IsEnabled(feature)==true;
    private void RefreshWindow()
    {
        bool wanted=manager.IsMaintenanceActive||Feature(RaceFeature.BroadcastScreen);
        if(wanted==showWindow)return;showWindow=wanted;
        presenter.enabled=wanted;
        if(!wanted){output.enabled=false;return;}
        if(RacePlayerWindows.UsesNativeWindow){output.enabled=true;if(!output.ConfigureBroadcast(presenter))output.enabled=false;}
        else if(!Application.isEditor&&Display.displays.Length>broadcastDisplay)
        {Display.displays[broadcastDisplay].Activate();output.enabled=true;output.Configure(new[]{presenter},broadcastDisplay,presenter);}
        else if(!Application.isEditor)Debug.LogWarning("The broadcast feed is available, but a floating broadcast window requires the macOS Metal plugin.");
    }
    private void LateUpdate()
    {
        if(manager==null)return;
        RefreshWindow();
        if(manager.IsMaintenanceActive){world.enabled=false;return;}
        float dt=Time.unscaledDeltaTime;
        bool game=manager.state==Gmanager.State.Game;
        if(game&&!recordingWasGame)
        {
            identityLabels.Clear();
            foreach(var entry in manager.Participants)
            {
                var label=entry.Car!=null?entry.Car.transform.Find("CPU identity")?.GetComponent<Renderer>():null;
                if(label!=null)identityLabels.Add(label);
            }
        }
        if(!Feature(RaceFeature.RaceReplays)&&recorder!=null){recorder.Take.Dispose();recorder=null;}
        if(game&&recorder==null&&Feature(RaceFeature.RaceReplays))recorder=new RaceReplayRecorder(manager,assets,anchors);
        if(game&&recorder!=null)recorder.Advance(Time.deltaTime);
        if(!game&&recorder!=null)FinishRecording();
        recordingWasGame=game;
        bool result=manager.state==Gmanager.State.Result&&Feature(RaceFeature.RaceReplays);
        bool needed=(showWindow&&(!RacePlayerWindows.UsesNativeWindow||output.enabled))||result;
        world.enabled=needed;
        if(!needed){replaying=false;playback.SetVisible(false);return;}
        if(game){Live(dt);return;}
        if(Feature(RaceFeature.RaceReplays)&&history.Count>0)Replay(dt,result);
        else Preview(dt);
        hud.Update(Mode,Caption,game?Standings():"TSUKUKOMA CIRCUIT / CAMPUS MOTORSPORT");
    }
    private void Live(float dt)
    {
        replaying=false;playback.SetVisible(false);world.cullingMask&=~(1<<RaceReplayPlayback.Layer);
        focusRemaining-=dt;
        var entry=manager.GetParticipant(focus);
        if(focusRemaining<=0||entry?.IsRacing!=true)
        {
            var candidates=manager.Participants.Where(x=>!x.IsCpu&&x.IsRacing).ToArray();
            if(candidates.Length==0)candidates=manager.Participants.Where(x=>x.Body!=null).ToArray();
            if(candidates.Length==0){Preview(dt);return;}
            entry=candidates[Array.FindIndex(candidates,x=>x.Index==focus)>=0&&candidates.Length>1?(Array.FindIndex(candidates,x=>x.Index==focus)+1)%candidates.Length:0];
            focus=entry.Index;focusRemaining=9;rig.Cut();
        }
        Vector3 companion=entry.Body.position+Vector3.forward*100;
        float distance=22;
        foreach(var other in manager.Participants)if(other.Index!=entry.Index&&other.Body!=null)
        {float d=Vector3.Distance(other.Body.position,entry.Body.position);if(d<distance){distance=d;companion=other.Body.position;}}
        rig.Apply(world,new Pose(entry.Body.position,entry.Body.rotation),companion,entry.Body.linearVelocity.magnitude,anchors,dt);
        Mode="LIVE";Caption=entry.Label+" / "+Mathf.RoundToInt(entry.Body.linearVelocity.magnitude*3.6f)+" km/h";
        hud.Update(Mode,Caption,Standings());
    }
    private string Standings()
    {
        if(Time.unscaledTime>=nextStandings){nextStandings=Time.unscaledTime+.25f;cachedStandings=string.Join("    /    ",manager.Participants.OrderBy(x=>x.Position).Select(x=>x.Position+" "+x.Label));}
        return cachedStandings;
    }
    private void FinishRecording()
    {
        var take=recorder.Finish();recorder=null;
        if(take.Cars.Length==0||take.Clips.Count==0){take.Dispose();return;}
        try
        {
            string path=RaceReplayArchive.Save(folder,take);
            var compact=RaceReplayArchive.Load(path,assets);take.Dispose();take=compact;
        }
        catch(Exception error){Debug.LogWarning("Broadcast highlights retained in memory; archive unavailable: "+error.Message);}
        playback.Clear();history.Insert(0,take);
        while(history.Count>3){history[history.Count-1].Dispose();history.RemoveAt(history.Count-1);}
        archive=clip=0;cursor=0;rig.Cut();
    }
    private void Replay(float dt,bool latestOnly)
    {
        if(latestOnly&&archive!=0){archive=clip=0;cursor=0;}
        var take=history[archive];
        if(playback.Take!=take){playback.Bind(take,transform);clip=0;cursor=0;rig.Cut();}
        if(take.Clips.Count==0){Preview(dt);return;}
        var highlight=take.Clips[clip];
        if(cursor<highlight.Start)cursor=highlight.Start;
        cursor+=dt*.85f;
        if(cursor>highlight.End)
        {
            clip++;if(clip>=take.Clips.Count){clip=0;if(!latestOnly)archive=(archive+1)%history.Count;}
            if(history[archive]!=take){cursor=0;rig.Cut();Replay(0,latestOnly);return;}
            cursor=0;rig.Cut();highlight=take.Clips[clip];cursor=highlight.Start;
        }
        playback.SetVisible(true);replaying=true;world.cullingMask|=1<<RaceReplayPlayback.Layer;
        playback.Apply(cursor,out int a,out int b,out float blend);
        int index=Array.FindIndex(take.Cars,x=>x.Driver==highlight.Driver);if(index<0)index=0;
        Pose subject=playback.Root(index,a,b,blend);Vector3 companion=subject.position+Vector3.forward*100;float distance=22;
        for(int i=0;i<take.Cars.Length;i++)if(i!=index){Vector3 position=playback.Root(i,a,b,blend).position;float d=Vector3.Distance(position,subject.position);if(d<distance){distance=d;companion=position;}}
        rig.Apply(world,subject,companion,take.Cars[index].Speeds[a],take.Anchors,dt);
        Mode="REPLAY";Caption=highlight.Kind.ToString().ToUpperInvariant()+" / "+take.Cars[index].Label;
    }
    private void Preview(float dt)
    {
        replaying=false;playback.SetVisible(false);world.cullingMask&=~(1<<RaceReplayPlayback.Layer);
        Pose pose=MenuSceneryDirector.EvaluatePose(Mathf.FloorToInt(Time.unscaledTime/18)%2==1,Time.unscaledTime);
        world.transform.SetPositionAndRotation(pose.position,pose.rotation);world.fieldOfView=50;
        Mode="COURSE PV";Caption="TSUKUKOMA CIRCUIT";
    }
    private void BeforeCamera(ScriptableRenderContext context,Camera camera)
    {
        if(camera!=world)return;
        RestoreRenderers();
        if(replaying)
        {
            foreach(var entry in manager.Participants)if(entry.Car!=null)foreach(var renderer in entry.Car.GetComponentsInChildren<Renderer>())
            {hidden.Add((renderer,renderer.forceRenderingOff));renderer.forceRenderingOff=true;}
        }
        else foreach(var label in identityLabels)if(label!=null){hidden.Add((label,label.forceRenderingOff));label.forceRenderingOff=true;}
    }
    private void AfterCamera(ScriptableRenderContext context,Camera camera){if(camera==world)RestoreRenderers();}
    private void RestoreRenderers(){foreach(var item in hidden)if(item.renderer!=null)item.renderer.forceRenderingOff=item.original;hidden.Clear();}
    private void OnDisable()
    {
        RestoreRenderers();playback.SetVisible(false);replaying=false;
        if(world!=null)world.enabled=false;
        if(presenter!=null)presenter.enabled=false;
        if(output!=null)output.enabled=false;
        showWindow=false;
        if(recorder!=null){recorder.Take.Dispose();recorder=null;}
    }
    private void OnDestroy()
    {
        RenderPipelineManager.beginCameraRendering-=BeforeCamera;RenderPipelineManager.endCameraRendering-=AfterCamera;RestoreRenderers();playback.Clear();
        recorder?.Take.Dispose();foreach(var take in history)take.Dispose();history.Clear();
        if(world!=null)Destroy(world.gameObject);
        if(Feed!=null){Feed.Release();Destroy(Feed);}if(presentationRoot!=null)Destroy(presentationRoot);
    }
}
