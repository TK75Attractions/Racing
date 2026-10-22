#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Observe the input-driven race, archive, render isolation and actual live/replay frames.</summary>
[DefaultExecutionOrder(11000)]
public sealed class BroadcastPlaytestProbe : MonoBehaviour
{
    [Serializable] private sealed class FrameStamp {public string file;public float seconds;}
    [Serializable] private sealed class Segment {public List<FrameStamp> frames=new List<FrameStamp>();}
    [Serializable] private sealed class Report
    {
        public bool passed,liveOnly;public string error;
        public int liveFrames,cuts,recordedFrames,cars,clips,overpasses,corners,archiveBytes;
        public bool pvSeen,liveSeen,replaySeen,archiveRoundTrip,corruptRejected,renderOnlyActors,physicsUnchanged;
    }
    private readonly Report report=new Report();
    private readonly Segment live=new Segment(),replay=new Segment();
    private RaceBroadcastDirector director;
    private Gmanager manager;
    private RenderTexture target;
    private Texture2D pixels;
    private float began,liveStarted=-1,resultStarted=-1,nextFrame,scale;
    private bool resultCaptured;
    public bool Done {get;private set;}
    public bool Passed=>report.passed;
    private string Folder=>Environment.GetEnvironmentVariable("RACING_BALANCE_OUTPUT");
    private void Start()
    {
        manager=Gmanager.Control;director=manager.Broadcast;began=Time.realtimeSinceStartup;scale=Time.timeScale;
        target=new RenderTexture(1280,720,24);target.Create();director.PresentationCamera.targetTexture=target;
        pixels=new Texture2D(1280,720,TextureFormat.RGB24,false);
    }
    private void LateUpdate()
    {
        if(Done||director==null)return;
        try{Observe();}catch(Exception error){report.error=error.ToString();Complete(false);}
    }
    private void Observe()
    {
        float now=Time.realtimeSinceStartup;
        if(now-began>600)throw new TimeoutException("Broadcast playthrough did not finish.");
        if(director.Mode=="COURSE PV"&&!report.pvSeen&&now-began>1)
        {Capture("pv.jpg",target);report.pvSeen=true;}
        if(manager.state==Gmanager.State.Game)
        {
            report.liveFrames++;report.liveSeen|=director.Mode=="LIVE";
            report.recordedFrames=Mathf.Max(report.recordedFrames,director.RecordedFrames);report.cuts=director.CameraCuts;
            if(manager.time>6&&liveStarted<0){liveStarted=now;Time.timeScale=1;Capture("live.jpg",target);}
            if(liveStarted>=0&&now-liveStarted<8)MovieFrame("LiveFrames",live,now-liveStarted,now);
            else if(liveStarted>=0)Time.timeScale=scale;
            if(Environment.GetEnvironmentVariable("RACING_BROADCAST_LIVE_ONLY")=="1"&&liveStarted>=0&&now-liveStarted>=8)
            {
                report.liveOnly=true;
                var labels=manager.Participants.Where(x=>x.Car!=null).Select(x=>x.Car.transform.Find("CPU identity")?.GetComponent<Renderer>()).Where(x=>x!=null).ToArray();
                bool hiddenDuringRender=false;
                Action<ScriptableRenderContext,Camera> observe=(context,camera)=>{if(camera==director.WorldCamera)hiddenDuringRender=labels.All(x=>x.forceRenderingOff);};
                RenderPipelineManager.beginCameraRendering+=observe;
                try{RenderPipeline.SubmitRenderRequest(director.WorldCamera,new RenderPipeline.StandardRequest{destination=director.Feed});}
                finally{RenderPipelineManager.beginCameraRendering-=observe;}
                Require(hiddenDuringRender&&labels.All(x=>!x.forceRenderingOff),"Broadcast-only identity suppression must restore normal player labels.");
                Capture("live-final.jpg",target);Complete(report.liveSeen&&report.liveFrames>20);return;
            }
        }
        if(manager.state!=Gmanager.State.Result||director.LatestReplay==null)return;
        if(resultStarted<0){resultStarted=now;nextFrame=0;Time.timeScale=1;}
        report.replaySeen|=director.IsReplaying;
        if(now-resultStarted>2)MovieFrame("ReplayFrames",replay,now-resultStarted-2,now);
        if(now-resultStarted>4&&!resultCaptured)
        {
            resultCaptured=true;
            var take=director.LatestReplay;report.cars=take.Cars.Length;report.clips=take.Clips.Count;
            report.overpasses=take.Clips.Count(x=>x.Kind==RaceHighlightKind.Overtake);report.corners=take.Clips.Count(x=>x.Kind==RaceHighlightKind.Corner);
            Require(report.liveSeen&&report.liveFrames>20&&report.recordedFrames>100&&report.cuts>1,"Live tracking and visual recording must run throughout the real race.");
            Require(report.cars>=2&&report.clips>0&&report.replaySeen,"Actual race footage must feed results.");
            GameObject actors=GameObject.Find("Broadcast replay actors");
            Require(actors!=null&&actors.GetComponentsInChildren<Rigidbody>().Length==0&&actors.GetComponentsInChildren<Collider>().Length==0,"Replay actors must be render-only.");report.renderOnlyActors=true;
            var original=manager.Participants.Where(x=>x.Body!=null).Select(x=>x.Body.position).ToArray();
            var renderers=manager.Participants.Where(x=>x.Car!=null).SelectMany(x=>x.Car.GetComponentsInChildren<Renderer>()).ToArray();
            var flags=renderers.Select(x=>x.forceRenderingOff).ToArray();
            RenderPipeline.SubmitRenderRequest(director.WorldCamera,new RenderPipeline.StandardRequest{destination=director.Feed});
            Require(renderers.Select(x=>x.forceRenderingOff).SequenceEqual(flags),"Replay rendering must restore every live renderer flag.");
            report.physicsUnchanged=original.SequenceEqual(manager.Participants.Where(x=>x.Body!=null).Select(x=>x.Body.position));Require(report.physicsUnchanged,"Rendering must never alter race bodies.");
            Capture("replay.jpg",target);Capture("replay-feed.jpg",director.Feed);
            var displays=(PlayerDisplayRig[])typeof(Gmanager).GetField("displayRigs",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(manager);
            for(int p=0;p<2;p++)File.WriteAllBytes(Path.Combine(Folder,"result-p"+(p+1)+".jpg"),displays[p].CaptureJpeg(1280,720));
            var files=new DirectoryInfo(Environment.GetEnvironmentVariable("RACING_BROADCAST_HISTORY")).GetFiles("race-*.rpl");Require(files.Length>0,"A race archive must survive the session.");
            var file=files.OrderByDescending(x=>x.LastWriteTimeUtc).First();report.archiveBytes=(int)file.Length;
            using(var restored=RaceReplayArchive.Load(file.FullName,new RaceReplayAssets(manager.carPrefab)))
            {Require(restored.Cars.Length==take.Cars.Length&&restored.Clips.Count==take.Clips.Count&&Vector3.Distance(restored.Cars[0].Roots[0].position,take.Cars[0].Roots[0].position)<.001f,"Saved replay must restore vehicle assets and exact recorded positions.");report.archiveRoundTrip=true;}
            string corrupt=Path.Combine(Folder,"corrupt-replay.rpl");File.WriteAllBytes(corrupt,new byte[]{1,2,3,4});
            try{RaceReplayArchive.Load(corrupt,new RaceReplayAssets(manager.carPrefab));}catch(InvalidDataException){report.corruptRejected=true;}finally{File.Delete(corrupt);}
            Require(report.corruptRejected,"Corrupt history must be rejected safely.");
        }
        if(now-resultStarted>12)Complete(true);
    }
    private void MovieFrame(string name,Segment segment,float seconds,float now)
    {
        if(now<nextFrame)return;nextFrame=now+1f/12;
        string directory=Path.Combine(Folder,name);Directory.CreateDirectory(directory);
        string file="frame-"+segment.frames.Count.ToString("D5")+".jpg";Capture(Path.Combine(name,file),target);
        segment.frames.Add(new FrameStamp{file=file,seconds=seconds});
    }
    private void Capture(string name,RenderTexture source)
    {
        var previous=RenderTexture.active;RenderTexture.active=source;pixels.ReadPixels(new Rect(0,0,1280,720),0,0);pixels.Apply();RenderTexture.active=previous;
        File.WriteAllBytes(Path.Combine(Folder,name),pixels.EncodeToJPG(85));
    }
    private void Complete(bool passed)
    {
        Done=true;report.passed=passed;
        File.WriteAllText(Path.Combine(Folder,"broadcast-checks.json"),JsonUtility.ToJson(report,true));
        foreach(var pair in new[]{("LiveFrames",live),("ReplayFrames",replay)})
            if(pair.Item2.frames.Count>0)File.WriteAllText(Path.Combine(Folder,pair.Item1,"timeline.json"),JsonUtility.ToJson(pair.Item2,true));
        if(passed)Debug.Log(report.liveOnly
            ? "BROADCAST_CAMERA_SAMPLE_PASS: real input-driven live camera sample; broadcast identity suppression and player-label restoration."
            : "BROADCAST_PLAYTEST_PASS: real input-driven race, tracking camera cuts, archived mesh replay, both result panes, render/physics isolation.");
        else Debug.LogError(report.error);
    }
    private static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    private void OnDestroy()
    {
        if(director!=null&&director.PresentationCamera!=null)director.PresentationCamera.targetTexture=null;
        if(target!=null){target.Release();Destroy(target);}if(pixels!=null)Destroy(pixels);
    }
}
#endif
