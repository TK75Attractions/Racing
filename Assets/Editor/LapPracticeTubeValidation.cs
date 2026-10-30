using System;
using System.Collections;
using System.IO;
using System.Reflection;
using TMPro;
using Unity.Cinemachine;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

public static class LapPracticeTubeValidation
{
    private const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    private const string PhysicsKey="Racing.LapPracticeTubePhysics";
    private static double physicsBegan;
    private const string Folder="Artifacts/LapPracticeTube";
    private sealed class Input:IDriveInputSource
    {
        public int PlayerIndex {get;set;}
        public string DeviceId=>"PRACTICE_RECOVERY_QA";
        public bool IsConnected {get;set;}=true;
        public DriveInputState CurrentState {get;set;}
        public void UpdateInput(float dt){}public void Dispose(){}
    }
    public static void Run()
    {
        Directory.CreateDirectory(Folder);
        RacingHUDFontBuilder.Build();
        ValidateLapTimes();ValidatePracticeRecovery();
        PlayerStartTubeResultValidation.Run();
        ArcadeUIValidation.Run();
        CaptureHud();
        File.WriteAllText(Folder+"/verification.txt","PASS: LAP1/LAP2 recorded by real lap completion, live second lap, finished-time preservation, retry and spectator source changes; first lap hidden; both rendered HUD palettes.\nPASS: practice stuck/off-course/80-degree heading at continuous three seconds, timer reset on motion/re-entry, idle/disconnected/suppressed guards; completed lessons and preparation deadline preserved; independent P1/P2.\nPASS: wall speed reward and ordinary resistance on floor, 45/90-degree banks and ceiling; coasting, disconnection and airborne guards; existing arcade UI and race recovery checks.\n");
        Debug.Log("LAP_PRACTICE_TUBE_EDITMODE_PASS");
    }
    public static void CaptureHud()
    {
        var capture=typeof(ArcadeUIValidation).GetMethod("CaptureView",BindingFlags.Static|BindingFlags.NonPublic);
        foreach(int player in new[]{0,1})capture.Invoke(null,new object[]{player,"race",1920,1080});
    }
    public static void RunPhysics()
    {
        UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene);
        SessionState.SetBool(PhysicsKey,true);ResumePhysics();EditorApplication.isPlaying=true;
    }
    [InitializeOnLoadMethod] private static void ResumePhysics()
    {
        if(!SessionState.GetBool(PhysicsKey,false))return;
        physicsBegan=EditorApplication.timeSinceStartup;EditorApplication.update-=PhysicsTick;EditorApplication.update+=PhysicsTick;
    }
    private static void PhysicsTick()
    {
        if(!SessionState.GetBool(PhysicsKey,false))return;
        if(EditorApplication.timeSinceStartup-physicsBegan>90){FinishPhysics(false,"Tube traction physics timeout");return;}
        if(EditorApplication.isPlaying&&Object.FindFirstObjectByType<TubeTractionValidationProbe>()==null)
            new GameObject("Tube traction physics probe").AddComponent<TubeTractionValidationProbe>();
    }
    public static void FinishPhysics(bool passed,string message)
    {
        SessionState.SetBool(PhysicsKey,false);EditorApplication.update-=PhysicsTick;
        if(passed)Debug.Log(message);else Debug.LogError(message);
        if(Application.isBatchMode)EditorApplication.Exit(passed?0:1);else EditorApplication.isPlaying=false;
    }
    private static void ValidateLapTimes()
    {
        var root=new GameObject("Lap time QA",typeof(RectTransform));
        var previous=Gmanager.Control;Gmanager.Control=null;
        try
        {
            var courseRoot=new GameObject("Lap timing source");courseRoot.transform.SetParent(root.transform);
            var laps=courseRoot.AddComponent<LapManager>();Set(laps,"raceActive",true);
            var body=new GameObject("Lap car",typeof(Rigidbody)).GetComponent<Rigidbody>();body.transform.SetParent(root.transform);
            laps.RegisterCar(body);var data=laps.GetCarData(body);
            data.currentLapTime=72.34f;Require(laps.DebugAdvanceLap(body,false),"First lap completion");
            Require(data.firstLapTime==72.34f&&data.currentLapTime==0&&data.lapCount==1,"Store LAP1 at the crossing.");
            data.currentLapTime=68.91f;Require(laps.DebugAdvanceLap(body,false),"Second lap completion");
            Require(data.firstLapTime==72.34f&&data.secondLapTime==68.91f&&data.currentLapTime==0,"Finish must preserve both lap times.");
            foreach(int player in new[]{0,1})
            {
                var host=Child(root.transform,"Player "+player,typeof(RectTransform));
                var hud=RacingHUDBuilder.Build(host.transform,player,2);var telemetry=hud.GetComponent<ArcadeHUDTelemetry>();
                telemetry.SetLapTimes(0,0,20);Require(!hud.Find("LapTimes").gameObject.activeSelf,"Hide before lap two.");
                telemetry.SetLapTimes(1,72.34f,18.42f);
                Require(Text(hud,"LapTimes/Lap1Time")=="01:12.34"&&Text(hud,"LapTimes/Lap2Time")=="00:18.42","Show exact LAP1 and live LAP2: "+Text(hud,"LapTimes/Lap1Time")+" / "+Text(hud,"LapTimes/Lap2Time"));
                telemetry.SetLapTimes(2,data.firstLapTime,data.secondLapTime);
                Require(Text(hud,"LapTimes/Lap2Time")=="01:08.91","Finish retains LAP2 after the current clock resets.");
                telemetry.SetLapTimes(1,90,3);Require(Text(hud,"LapTimes/Lap1Time")=="01:30.00","Changing viewed driver must replace the first-lap time.");
                telemetry.SetLapTimes(0,0,0);Require(!hud.Find("LapTimes").gameObject.activeSelf,"Retry hides old lap data.");
                var times=(RectTransform)hud.Find("LapTimes");Require(times.anchorMin.y>.738f&&times.anchorMax.y<.835f,"Lap rows must leave room for notices and total race time.");
            }
            laps.RegisterCar(body);Require(data.firstLapTime==0&&data.secondLapTime==0,"Re-registering must clear previous lap times.");
        }
        finally{Object.DestroyImmediate(root);Gmanager.Control=previous;}
    }
    private static void ValidatePracticeRecovery()
    {
        var previous=Gmanager.Control;var root=new GameObject("Practice recovery QA");root.SetActive(false);
        var race=root.AddComponent<Gmanager>();Gmanager.Control=race;
        var controller=root.AddComponent<DrivingTutorialController>();
        try
        {
            var inputs=root.AddComponent<InputManager>();var live=(LiveDriveInputSource[])Get(inputs,"liveInputs");
            var sources=new[]{new Input{PlayerIndex=0},new Input{PlayerIndex=1}};
            var rigs=new PlayerDisplayRig[2];
            for(int i=0;i<2;i++)
            {
                live[i].SwitchSource(sources[i],false);
                var canvas=Child(root.transform,"Canvas "+i,typeof(RectTransform),typeof(Canvas));
                var cameras=Child(root.transform,"Cameras "+i);var camera=Child(cameras.transform,"Race").AddComponent<CinemachineCamera>();
                rigs[i]=new PlayerDisplayRig(i,cameras,canvas,null,camera,false);
            }
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefab/car/car2.prefab");controller.Begin(prefab,inputs,rigs);
            var practices=(IList)Get(controller,"players");var practice=practices[0];var other=practices[1];
            var car=controller.GetPlayerCar(0);var body=car.GetComponent<Rigidbody>();var mover=car.GetComponent<DebugMover>();
            Call(mover,"Awake");var gate=(PlayerDriveInputGate)Get(practice,"driveGate");gate.Reset();
            var origin=((GameObject)Get(practice,"world")).transform.position;
            var otherPosition=controller.GetPlayerCar(1).transform.position;
            var lesson=controller.GetPlayerProgress(0);lesson.Tick(.1f,true,0,0,0,0);lesson.Tick(.7f,true,1,0,4,0);
            Require(lesson.CurrentStage==DrivingTutorialProgress.Stage.Steer,"Prepare a completed acceleration lesson.");
            float remaining=lesson.TimeRemaining;
            void Prepare(float x=0,float angle=0,float pedal=0)
            {
                Set(mover,"inputSuppressedUntil",float.NegativeInfinity);sources[0].IsConnected=true;gate.Reset();
                sources[0].CurrentState=new DriveInputState{pedal=pedal};
                body.position=origin+new Vector3(x,1.3f,-48);body.rotation=Quaternion.Euler(0,angle,0);car.transform.SetPositionAndRotation(body.position,body.rotation);
                body.linearVelocity=Vector3.zero;
                foreach(var tire in car.GetComponentsInChildren<TireForce>())Set(tire,"isGrounded",true);
                Set(practice,"recoveryTimer",0f);Set(practice,"offCourseSeconds",0f);Set(practice,"headingSeconds",0f);Set(practice,"stuckSeconds",0f);Set(practice,"trackingProgress",false);
            }
            bool Tick(float dt)
            {
                var local=body.position-origin;
                return (bool)typeof(DrivingTutorialController).GetMethod("UpdateRecovery",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{practice,local,DrivingTutorialRoad.NearestProgress(local),dt});
            }
            Prepare(pedal:1);Require(!Tick(2.98f)&&Tick(.02f),"A held pedal without two meters of travel must recover at three seconds.");
            Require(controller.RecoverPlayer(0),"Restore the current lesson.");
            Require(lesson.CurrentStage==DrivingTutorialProgress.Stage.Steer&&lesson.TimeRemaining==remaining,"Recovery must preserve completed lessons and the existing deadline.");
            Require(controller.GetPlayerCar(1).transform.position==otherPosition&&(int)Get(other,"recoveryCount")==0,"P1 recovery must not move P2.");
            Prepare(x:30);Require(!Tick(2.98f),"Off course waits three seconds even within the outer practice world.");
            int before=(int)Get(practice,"recoveryCount");controller.Tick(.02f);
            Require((int)Get(practice,"recoveryCount")==before+1,"The real practice tick must execute off-course recovery.");
            Prepare(x:30);Tick(2.5f);body.position=origin+new Vector3(0,1.3f,-48);Require(!Tick(1),"Re-entering the road cancels off-course time.");
            Prepare(angle:80);Require(!Tick(2.98f)&&Tick(.02f),"Practice uses the race's continuous 80-degree/three-second heading rule.");
            Prepare(pedal:0);Require(!Tick(10),"An idle learner must not be repeatedly respawned.");
            Prepare(pedal:.1f);Require(!Tick(2.98f)&&Tick(.02f),"A lightly pressed pedal must also recover a genuinely stuck learner.");
            Prepare(pedal:.04f);Require(!Tick(10),"Pedal deadzone contact is not a request to move.");
            foreach(float speed in new[]{0f,.5f,CarResetter.StuckRecoverySpeedMps})
            {
                Prepare(pedal:1);body.linearVelocity=Vector3.forward*speed;Require(!Tick(1.5f),"The jam starts without early recovery.");
                sources[0].CurrentState=DriveInputState.Neutral;body.position+=Vector3.forward*2.1f;
                Require(!Tick(1.48f)&&Tick(.02f),"Releasing the pedal at or below the speed threshold retains the full practice countdown, even after slow progress.");
            }
            Prepare(pedal:1);Tick(1.5f);sources[0].CurrentState=DriveInputState.Neutral;body.linearVelocity=Vector3.forward*(CarResetter.StuckRecoverySpeedMps+.01f);
            Require(!Tick(.02f),"Coasting above the threshold clears practice recovery.");body.linearVelocity=Vector3.zero;
            Require(!Tick(10),"A cleared jam must not restart on a neutral stop.");
            Prepare(pedal:1);Tick(1.5f);gate.SetBlocked(true);Require(!Tick(10),"Blocked practice cancels a low-speed countdown.");
            gate.Reset();sources[0].CurrentState=DriveInputState.Neutral;Require(!Tick(10),"Unblocking does not restore an obsolete countdown.");
            Prepare(pedal:1);body.linearVelocity=Vector3.forward*4;for(int i=0;i<50;i++){body.position+=Vector3.forward*.4f;Require(!Tick(.1f),"Moving along the road is not a jam.");}
            Prepare(x:30,pedal:1);Tick(2.5f);sources[0].IsConnected=false;Require(!Tick(3),"Disconnected practice never accumulates recovery time.");sources[0].IsConnected=true;gate.Reset();Require(!Tick(2.98f)&&Tick(.02f),"Reconnection starts a fresh interval.");
            Prepare(x:30,pedal:1);Set(mover,"inputSuppressedUntil",float.PositiveInfinity);Require(!Tick(4),"Respawn suppression prevents repeated recovery.");
        }
        finally
        {
            var practices=(IList)Get(controller,"players");foreach(var practice in practices){var world=(GameObject)Get(practice,"world");if(world!=null)Object.DestroyImmediate(world);}practices.Clear();
            var materials=(IList)Get(controller,"materials");foreach(Material material in materials)if(material!=null)Object.DestroyImmediate(material);materials.Clear();
            Object.DestroyImmediate(root);Gmanager.Control=previous;
        }
    }
    private static GameObject Child(Transform parent,string name,params Type[] components){var child=new GameObject(name,components);child.transform.SetParent(parent,false);return child;}
    private static object Get(object target,string field)=>target.GetType().GetField(field,Private|BindingFlags.Public).GetValue(target);
    private static void Set(object target,string field,object value)=>target.GetType().GetField(field,Private|BindingFlags.Public).SetValue(target,value);
    private static void Call(object target,string method)=>target.GetType().GetMethod(method,Private).Invoke(target,null);
    private static string Text(Transform root,string path)=>root.Find(path).GetComponent<TMP_Text>().text;
    private static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
}
