using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class SkillGameplayValidation
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    public static void Run()
    {
        if (!Application.isBatchMode) throw new InvalidOperationException("Run in an isolated batch editor.");
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);
        ValidateStartingLine();
        ValidateOperatorStop();
        ValidateReconnectGate();
        ValidateRecoveryOccupancy();
        ValidateHeldReset();
        ValidateRecoveryProtection();
        ValidateRecoveryInsideGoal();
        ValidateDriverNotice();
        ValidateJumpAdvice();
        Debug.Log("SKILL_GAMEPLAY_RULES_PASS");
    }
    public static void RunAll()
    {
        Run();
        ModernUIValidation.Run();
        RacingPolishValidation.Run();
        RoadSeamValidation.RunAllFixes();
        Debug.Log("SKILL_GAMEPLAY_ALL_PASS");
    }
    public static void RunAllAndRecovery()
    {
        RunAll();
        Environment.SetEnvironmentVariable("RACING_NPC_RECOVERY_ONLY","1");
        NpcRaceValidation.RunBatch();
    }
    private static void ValidateDriverNotice()
    {
        GameObject root=new GameObject("Notice QA",typeof(RectTransform),typeof(Canvas));
        try
        {
            var ui=new OnPlayUIManager(); ui.Init(root.transform,null,0);
            var source=new Input { IsConnected=false }; var gate=new PlayerDriveInputGate(source);
            ui.UpdateDriverNotice(gate,null,true);
            var notice=root.transform.Find("QuietRaceNotice");
            var label=notice.Find("Text").GetComponent<TMPro.TMP_Text>();
            Require(notice.gameObject.activeSelf&&label.text=="接続を確認してください","Lost input needs an actionable notice.");
            _=gate.CurrentState; source.IsConnected=true; ui.UpdateDriverNotice(gate,null,true);
            Require(label.text=="ペダルを離して再開","Resolved connection instructions must immediately yield to the release latch.");
            ui.UpdateDriverNotice(gate,null,false);
            Require(!notice.gameObject.activeSelf,"Finished drivers must not see stale control notices.");
            ui.UpdateDriverNotice(gate,null,true);
            Require(label.text=="ペダルを離して再開","Reconnection must explain the pedal release latch.");
            ui.UpdateDriverNotice(gate,null,false);
            ui.UpdateDriverNotice(null,null,true,true);
            Require(notice.gameObject.activeSelf&&label.text.Contains("ジャンプ"),"A gap needs an advance driving cue.");

            var queue = new DriverNoticeQueue();
            queue.Submit("success", "着地成功！", DriverNoticePriority.Success, 0f);
            Require(queue.Tick(0f).Key == "success", "Success must enter the shared queue.");
            queue.Submit("jump", "ジャンプ", DriverNoticePriority.Safety, .1f, condition:true);
            Require(queue.Tick(.1f).Key == "jump", "Safety must immediately preempt success.");
            queue.Submit("line", "ライン成功！", DriverNoticePriority.Success, .2f);
            queue.Release("jump");
            Require(queue.Tick(.89f).Key == "jump", "A cleared warning must remain for at least 0.8 seconds.");
            Require(queue.Tick(.91f).Key == "line", "Queued success must follow the warning.");
            queue.Submit("repeat", "ライン成功！", DriverNoticePriority.Success, 1f);
            Require(queue.Tick(2.5f) == null, "The same success must not flash again within the cooldown.");
            queue.Submit("old", "古い成功", DriverNoticePriority.Success, 3f);
            Require(queue.Tick(7.1f) == null, "Stale pending success must be discarded.");
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }
    private static void ValidateJumpAdvice()
    {
        GameObject root=new GameObject("Jump advice QA");
        try
        {
            RaceCourse course=MakeCourse(root);
            Require(!course.IsJumpAhead(new Vector3(0,0,70),1),"Solid roads must not show a jump warning.");
            var settings=new SerializedObject(course);
            settings.FindProperty("waypoints").GetArrayElementAtIndex(1).FindPropertyRelative("jumpToNext").boolValue=true;
            settings.ApplyModifiedPropertiesWithoutUndo(); course.RebuildCache();
            Require(course.IsJumpAhead(new Vector3(0,0,70),1),"A gap 30m ahead needs a warning.");
            Require(!course.IsJumpAhead(new Vector3(0,0,70),-1),"Warnings must follow the race direction.");
            Require(!course.IsJumpAhead(new Vector3(0,0,40),1,35),"Recovery needs at least 35m of solid run-up.");
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }
    public static void ValidateStartingLine()
    {
        GameObject root = new GameObject("Starting line QA");
        try
        {
            RaceCourse course = MakeCourse(root);
            LapManager laps = root.AddComponent<LapManager>(); Set(laps,"raceCourse",course);
            GameObject goal = new GameObject("Goal",typeof(GoalSensor)); goal.transform.SetParent(root.transform);
            GameObject start = new GameObject("Grid"); start.transform.SetParent(root.transform); start.transform.position=Vector3.back*20;
            GameObject car = new GameObject("Car",typeof(Rigidbody)); car.transform.SetParent(root.transform);
            Rigidbody body=car.GetComponent<Rigidbody>(); body.isKinematic=false;body.useGravity=false; body.position=start.transform.position;
            laps.RegisterCar(body,start.transform); var data=laps.GetCarData(body); data.currentLapTime=8;
            body.position=Vector3.forward;body.linearVelocity=Vector3.forward*5f;
            Require(!laps.OnCarPassGoal(body,goal.transform) && data.lapCount==0 && data.currentLapTime==8,
                "Initial grid-to-line crossing must not complete a lap or discard race time.");
            for(int i=0;i<20;i++)
            {
                body.position=Vector3.back*2;body.linearVelocity=Vector3.back*5;laps.GetRaceProgressDistance(body);
                Require(!laps.OnCarPassGoal(body,goal.transform),"Reverse goal passage must not count.");
                body.position=Vector3.forward*2;body.linearVelocity=Vector3.forward*5;laps.GetRaceProgressDistance(body);
                Require(!laps.OnCarPassGoal(body,goal.transform)&&data.lapCount==0,"Rocking over the finish cannot complete a lap.");
            }
            float startRaw=course.GetProgressDistance(goal.transform.position);
            for(int round=0;round<2;round++)
            {
                for(int i=1;i<200;i++)
                {
                    course.TryGetSampleAtProgress(startRaw+course.TotalLength*i/200f,out var sample);
                    body.position=sample.position+Vector3.up*.5f;body.linearVelocity=sample.forward*5;
                    laps.GetRaceProgressDistance(body);
                }
                body.linearVelocity=-body.linearVelocity;
                Require(!laps.OnCarPassGoal(body,goal.transform),"A reverse crossing must not consume an armed lap.");
                body.linearVelocity=-body.linearVelocity;
                Require(laps.OnCarPassGoal(body,goal.transform)&&data.lapCount==round+1,"Each full, ordered circuit counts exactly one lap. round="+round+" travel="+data.traversal.Travel+" armed="+data.traversal.IsArmed+" progress="+laps.GetRaceProgressDistance(body)+" crossed="+data.hasCrossedGoal+" lap="+data.lapCount+" velocity="+body.linearVelocity);
                Require(!laps.OnCarPassGoal(body,goal.transform),"A second collider cannot complete another lap.");
            }
            Require(data.isFinished,"Two full circuits must finish a two-lap race.");
            Debug.Log("ORDERED_LAP_RULES_PASS: grid, repeated finish crossings, direction, two full circuits, duplicate entry.");
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }
    private static RaceCourse MakeCourse(GameObject root)
    {
        RaceCourse course=root.AddComponent<RaceCourse>();
        var settings=new SerializedObject(course); settings.FindProperty("cornerRoundingDistance").floatValue=0;
        settings.FindProperty("slopeBlendDistance").floatValue=0; settings.FindProperty("closedLoop").boolValue=true;
        var points=settings.FindProperty("waypoints"); points.arraySize=4;
        Vector3[] positions={new Vector3(0,0,-100),new Vector3(0,0,100),new Vector3(100,0,100),new Vector3(100,0,-100)};
        for(int i=0;i<4;i++)
        {
            var point=points.GetArrayElementAtIndex(i); point.FindPropertyRelative("position").vector2Value=new Vector2(positions[i].x,positions[i].z);
            point.FindPropertyRelative("height").floatValue=0; point.FindPropertyRelative("width").floatValue=20;
            point.FindPropertyRelative("curve").floatValue=0; point.FindPropertyRelative("jumpToNext").boolValue=false;
        }
        settings.ApplyModifiedPropertiesWithoutUndo(); course.RebuildCache(); return course;
    }
    private static void ValidateOperatorStop()
    {
        GameObject root=new GameObject("Operator stop QA"); root.SetActive(false);
        try
        {
            Gmanager manager=root.AddComponent<Gmanager>(); manager.state=Gmanager.State.Game;
            manager.ShowResult();
            Require(manager.state==Gmanager.State.Result,"An operator stop must reach results.");
            for(int i=0;i<2;i++)Require(manager.LatestSessionResult.GetPlayerResult(i)?.didFinish==false,
                "Stopping an unfinished race must not manufacture a winner.");
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }
    private sealed class Input : IDriveInputSource
    {
        public int PlayerIndex=>0; public string DeviceId=>"SKILL_QA";
        public bool IsConnected { get; set; }=true;
        public DriveInputState CurrentState { get; set; }
        public void UpdateInput(float dt){} public void Dispose(){}
    }
    private static void ValidateRecoveryOccupancy()
    {
        GameObject root=new GameObject("Recovery QA");
        try
        {
            RaceCourse course=MakeCourse(root); LapManager laps=root.AddComponent<LapManager>(); Set(laps,"raceCourse",course);
            GameObject car=new GameObject("Car",typeof(Rigidbody),typeof(BoxCollider),typeof(CarResetter)); car.transform.SetParent(root.transform);
            Rigidbody body=car.GetComponent<Rigidbody>(); body.isKinematic=true; body.position=Vector3.back*20+Vector3.up;
            CarResetter resetter=car.GetComponent<CarResetter>(); Call(resetter,"Awake"); laps.RegisterCar(body);
            laps.GetCarData(body).lapCount=1; resetter.SetCourse(course,laps);
            GameObject blocker=new GameObject("Occupied road",typeof(Rigidbody),typeof(BoxCollider)); blocker.transform.SetParent(root.transform);
            blocker.GetComponent<Rigidbody>().isKinematic=true; blocker.transform.position=Vector3.back*20+Vector3.up;
            blocker.GetComponent<BoxCollider>().size=new Vector3(40,4,20); Physics.SyncTransforms();
            body.position=new Vector3(20,-10,0);
            Require(!resetter.TryResetCar(true),"Recovery must wait when other cars occupy every candidate.");
            blocker.GetComponent<BoxCollider>().size=new Vector3(3,4,12); Physics.SyncTransforms();
            Require(resetter.TryResetCar(true)&&Mathf.Abs(body.position.x)>5f,
                "Recovery must use a clear side lane when only the center is occupied.");
            UnityEngine.Object.DestroyImmediate(blocker); Physics.SyncTransforms();
            Require(resetter.TryResetCar(true)&&body.position.z< -19,"A clear recovery must return to the last safe road.");
            Require(laps.GetCarData(body).lapCount==1&&resetter.RecoveryCount==2,"Recovery must preserve laps and count only successful moves.");
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }
    private static void ValidateHeldReset()
    {
        GameObject root=new GameObject("Held reset QA");
        try
        {
            RaceCourse course=MakeCourse(root);
            GameObject car=new GameObject("Car",typeof(Rigidbody),typeof(BoxCollider),typeof(CarResetter)); car.transform.SetParent(root.transform);
            car.GetComponent<Rigidbody>().isKinematic=true;
            var resetter=car.GetComponent<CarResetter>(); Call(resetter,"Awake"); resetter.SetCourse(course,null);
            var source=new Input {CurrentState=new DriveInputState {resetPressed=true}};
            var gate=new PlayerDriveInputGate(source); resetter.SetInputSource(gate);
            for(int i=0;i<5;i++)Call(resetter,"Update");
            Require(resetter.RecoveryCount==1,"A held reset must not repeat when the recovery gate emits neutral input.");
            source.CurrentState=DriveInputState.Neutral; Call(resetter,"Update");
            source.CurrentState=new DriveInputState {resetPressed=true}; Call(resetter,"Update");
            Require(resetter.RecoveryCount==2,"Releasing and pressing reset must allow another recovery.");
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }
    private static void ValidateRecoveryProtection()
    {
        GameObject root=new GameObject("Recovery protection QA",typeof(Rigidbody),typeof(CarItemEffects));
        try
        {
            CarItemEffects effects=root.GetComponent<CarItemEffects>(); Call(effects,"Awake");
            Require(effects.ApplyOil(),"Oil should affect an unprotected driver.");
            effects.GrantRecoveryProtection(2);
            Require(!effects.IsOiled&&effects.IsRecoveryProtected,"Recovery must clear lingering afflictions.");
            Require(!effects.ApplyOil()&&!effects.ApplyConfuse()&&!effects.ApplySpin(Vector3.right),
                "Recovered drivers need a short grace period against chained items.");
            Set(effects,"recoveryProtectionRemaining",0f);
            Require(effects.ApplyConfuse(),"Recovery protection must not be permanent.");
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }
    private static void ValidateRecoveryInsideGoal()
    {
        GameObject root=new GameObject("Goal recovery QA",typeof(LapManager),typeof(GoalSensor),typeof(BoxCollider));
        try
        {
            root.GetComponent<BoxCollider>().isTrigger=true; root.GetComponent<BoxCollider>().size=new Vector3(20,4,1);
            LapManager laps=root.GetComponent<LapManager>(); GoalSensor goal=root.GetComponent<GoalSensor>(); Call(goal,"Awake");
            GameObject car=new GameObject("Car",typeof(Rigidbody),typeof(BoxCollider)); car.transform.SetParent(root.transform);
            car.tag="Player"; car.layer=LayerMask.NameToLayer("CarBody"); Rigidbody body=car.GetComponent<Rigidbody>(); body.isKinematic=true;
            laps.RegisterCar(body); laps.NotifyManualRespawn(body); goal.OnTriggerEnter(car.GetComponent<BoxCollider>());
            Require(laps.GetCarData(body).lapCount==0,"Respawning inside a goal must not award a lap.");
            goal.OnTriggerExit(car.GetComponent<BoxCollider>()); goal.OnTriggerEnter(car.GetComponent<BoxCollider>());
            Require(laps.GetCarData(body).lapCount==0,"Leaving and re-entering after recovery still requires a full circuit.");
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }
    private static void ValidateReconnectGate()
    {
        var source=new Input {CurrentState=new DriveInputState {pedal=1,steering=20,resetPressed=true}};
        var gate=new PlayerDriveInputGate(source);
        Require(gate.CurrentState.pedal==1,"Initial connected input must remain available.");
        source.IsConnected=false;
        Require(gate.CurrentState.pedal==0&&!gate.CurrentState.resetPressed,"Disconnected stale input must be neutral.");
        source.IsConnected=true;
        Require(gate.CurrentState.pedal==0&&gate.NeedsPedalRelease,"Reconnection with a held pedal must not accelerate.");
        source.CurrentState=DriveInputState.Neutral; _=gate.CurrentState;
        source.CurrentState=new DriveInputState {pedal=1};
        Require(gate.CurrentState.pedal==1&&!gate.NeedsPedalRelease,"Release then press must resume driving.");
    }
    private static void Set(object target,string name,object value)=>target.GetType().GetField(name,Private).SetValue(target,value);
    private static object Get(object target,string name)=>target.GetType().GetField(name,Private).GetValue(target);
    private static void Call(object target,string name)=>target.GetType().GetMethod(name,Private).Invoke(target,null);
    private static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
}
