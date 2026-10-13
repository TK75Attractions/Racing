using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Matched two/four-car rendering samples; this is not hardware input-latency certification.</summary>
public static class NpcRacePerformanceValidation
{
    private const string Key = "Racing.NpcPerformance";
    private static Gmanager manager;
    private static bool initialized;
    private static int phase, lastFrame = -1, warmFrames;
    private static double start, phaseStart, sampleStart;
    private static float oldScale;
    private static int oldVsync, oldTarget;
    private static readonly List<float> frames = new List<float>();
    private static readonly List<string> report = new List<string>();
    private static readonly RenderTexture[] outputs = new RenderTexture[2];
    private static readonly Camera[] cameras = new Camera[2];
    private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
    [MenuItem("Racing/Measure Two And Four Car Rendering")]
    public static void RunBatch()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play Mode first");
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        SessionState.SetBool(Key, true); Attach(); EditorApplication.isPlaying = true;
    }
    [InitializeOnLoadMethod] private static void Resume() { if (SessionState.GetBool(Key, false)) Attach(); }
    private static void Attach()
    {
        EditorApplication.playModeStateChanged -= DisablePublisher; EditorApplication.playModeStateChanged += DisablePublisher;
        start = EditorApplication.timeSinceStartup; initialized = false; phase = -1;
        EditorApplication.update -= Tick; EditorApplication.update += Tick;
    }
    private static void DisablePublisher(PlayModeStateChange state)
    {
        if(state!=PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Key,false))return;
        foreach(RaceRemoteFirebasePublisher publisher in UnityEngine.Object.FindObjectsByType<RaceRemoteFirebasePublisher>(FindObjectsSortMode.None))publisher.enabled=false;
    }
    private static void Tick()
    {
        try
        {
            if (EditorApplication.timeSinceStartup-start>180) throw new InvalidOperationException("Rendering measurement timed out");
            if (!EditorApplication.isPlaying || Gmanager.Control==null) return;
            manager=Gmanager.Control;
            if (!initialized)
            {
                initialized=true; oldScale=Time.timeScale; oldVsync=QualitySettings.vSyncCount; oldTarget=Application.targetFrameRate;
                Time.timeScale=1; QualitySettings.vSyncCount=0; Application.targetFrameRate=-1;
                foreach(RaceRemoteFirebasePublisher publisher in UnityEngine.Object.FindObjectsByType<RaceRemoteFirebasePublisher>(FindObjectsSortMode.None))publisher.enabled=false;
                for(int i=0;i<2;i++)
                {
                    cameras[i]=manager.GetPlayerCaptureCamera(i); outputs[i]=new RenderTexture(1920,1080,24); outputs[i].Create(); cameras[i].targetTexture=outputs[i];

                }
                report.Clear(); report.Add("Unity "+Application.unityVersion+" / "+SystemInfo.graphicsDeviceName+" / "+SystemInfo.processorType);
                report.Add("Editor Play Mode; two 1920x1080 camera stacks; VSync off; fixed staged pose showing all cars; CPUs run physical drivers; 4s and 60 rendered frames warmup + 6s and at least 120 frames sampling. Render-texture capture in Batch Mode; verify HUD visibility in saved views. No serial or external audio hardware latency measured.");
                Begin(0); return;
            }
            if(Time.frameCount==lastFrame)return; lastFrame=Time.frameCount;
            double elapsed=EditorApplication.timeSinceStartup-phaseStart;
            if (elapsed<4 || warmFrames++<60)return;
            if(sampleStart<0)sampleStart=EditorApplication.timeSinceStartup;
            frames.Add(Time.unscaledDeltaTime*1000);
            if(EditorApplication.timeSinceStartup-sampleStart<6 || frames.Count<120)return;
            frames.Sort(); if(frames.Count==0)throw new InvalidOperationException("No rendered frames sampled");
            float median=frames[frames.Count/2], p95=frames[Mathf.Min(frames.Count-1,Mathf.FloorToInt(frames.Count*.95f))];
            double total=0;foreach(float frame in frames)total+=frame;
            string line=(phase==0?"2-cars":phase==1?"4-cars":"2-cars-repeat")+": frames="+frames.Count+"; median="+median.ToString("F2")+"ms; p95="+p95.ToString("F2")+"ms; mean="+(total/frames.Count).ToString("F2")+"ms; max="+frames[frames.Count-1].ToString("F2")+"ms";
            report.Add(line); Debug.Log("NPC_RENDERING_SAMPLE: "+line);
            if(phase==1)SaveViews();
            if(phase<2)Begin(phase+1);else Finish(true);
        }
        catch(Exception error){Debug.LogException(error);Finish(false);}
    }
    private static void Begin(int next)
    {
        phase=next; frames.Clear(); lastFrame=-1; warmFrames=0; sampleStart=-1;
        typeof(Gmanager).GetMethod("ClearCurrentRaceObjects",Private).Invoke(manager,null);
        typeof(Gmanager).GetField("npcCount",Private).SetValue(manager,next==1?2:0);
        typeof(Gmanager).GetMethod("StartGameWhenScreenCovered",Private).Invoke(manager,null);
        typeof(Gmanager).GetMethod("CompleteGameStart",Private).Invoke(manager,null);
        typeof(Gmanager).GetMethod("StartRaceAfterCountdown",Private).Invoke(manager,null);
        // Put the CPU row ahead of the humans so both normal player views include four cars.
        typeof(Gmanager).GetMethod("ApplyStateImmediate",Private).Invoke(manager,new object[]{Gmanager.State.Game});
        foreach(ScreenTransitionController transition in UnityEngine.Object.FindObjectsByType<ScreenTransitionController>(FindObjectsSortMode.None))transition.ClearRaceStatus();
        float raw=manager.course.GetProgressDistance(manager.GetParticipant(0).Body.position);
        foreach(RaceParticipant entry in manager.Participants)
        {
            manager.course.TryGetSampleAtProgress(raw+(entry.IsCpu?manager.CourseProgressSign*14:0),out RaceCourse.CourseSample sample);
            entry.Body.interpolation=RigidbodyInterpolation.None;
            Vector3 position=sample.position+sample.up*.5f+sample.right*(entry.Index%2==0?-2:2);
            Quaternion rotation=Quaternion.LookRotation(sample.forward*manager.CourseProgressSign,sample.up);
            entry.Car.transform.SetPositionAndRotation(position,rotation);
            entry.Body.position=position; entry.Body.rotation=rotation;
            entry.Body.constraints=RigidbodyConstraints.FreezeAll;
        }
        Physics.SyncTransforms();
        phaseStart=EditorApplication.timeSinceStartup;
    }
    private static void SaveViews()
    {
        RenderTexture previous=RenderTexture.active;
        foreach(Canvas canvas in UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None))
            if(canvas.name=="MainCanvas" || canvas.name=="MainCanvas_P2")
                report.Add(canvas.name+": active="+canvas.gameObject.activeInHierarchy+"; HUD active="+canvas.transform.Find("OnPlay/ModernHUD").gameObject.activeInHierarchy+"; scale="+canvas.transform.lossyScale);
        for(int i=0;i<2;i++)
        {
            foreach(RaceParticipant entry in manager.Participants)
            {
                Vector3 view=manager.GetPlayerViewCamera(i).WorldToViewportPoint(entry.Body.position);
                if(view.z<=0 || view.x<0 || view.x>1 || view.y<0 || view.y>1)
                    throw new InvalidOperationException("Rendering sample omitted "+entry.Label+" from P"+(i+1)+" view: "+view+" world="+entry.Body.position);
            }
            RenderTexture.active=outputs[i];Texture2D image=new Texture2D(outputs[i].width,outputs[i].height,TextureFormat.RGB24,false);
            image.ReadPixels(new Rect(0,0,outputs[i].width,outputs[i].height),0,0);image.Apply();
            File.WriteAllBytes(Path.Combine(Path.GetTempPath(),"racing-npc-four-cars-p"+(i+1)+".png"),image.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(image);
        }
        RenderTexture.active=previous;
    }
    private static void Finish(bool success)
    {
        EditorApplication.update-=Tick;EditorApplication.playModeStateChanged-=DisablePublisher;SessionState.SetBool(Key,false);
        if(initialized){Time.timeScale=oldScale;QualitySettings.vSyncCount=oldVsync;Application.targetFrameRate=oldTarget;}
        for(int i=0;i<2;i++)
        { if(cameras[i]!=null)cameras[i].targetTexture=null; if(outputs[i]!=null){outputs[i].Release();UnityEngine.Object.DestroyImmediate(outputs[i]);} }
        string path=Environment.GetEnvironmentVariable("RACING_NPC_PERFORMANCE_OUTPUT")??Path.Combine(Path.GetTempPath(),"racing-npc-performance.txt");
        File.WriteAllLines(path,report);
        if(success)Debug.Log("NPC_RENDERING_MEASUREMENT_PASS: "+path);
        if(Application.isBatchMode)EditorApplication.Exit(success?0:1);else EditorApplication.isPlaying=false;
    }
}
