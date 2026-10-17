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
        ValidateRecoveryProtection();
        ValidateRecoveryInsideGoal();
        Debug.Log("SKILL_GAMEPLAY_RULES_PASS");
    }
    private static void ValidateStartingLine()
    {
        GameObject root = new GameObject("Starting line QA");
        try
        {
            RaceCourse course = MakeCourse(root);
            LapManager laps = root.AddComponent<LapManager>(); Set(laps,"raceCourse",course);
            GameObject goal = new GameObject("Goal",typeof(GoalSensor)); goal.transform.SetParent(root.transform);
            GameObject start = new GameObject("Grid"); start.transform.SetParent(root.transform); start.transform.position=Vector3.back*20;
            GameObject car = new GameObject("Car",typeof(Rigidbody)); car.transform.SetParent(root.transform);
            Rigidbody body=car.GetComponent<Rigidbody>(); body.isKinematic=true; body.position=start.transform.position;
            laps.RegisterCar(body,start.transform); var data=laps.GetCarData(body); data.currentLapTime=8;
            Require(!laps.OnCarPassGoal(body,goal.transform) && data.lapCount==0 && data.currentLapTime==8,
                "Initial grid-to-line crossing must not complete a lap or discard race time.");
            Require(laps.OnCarPassGoal(body,goal.transform) && data.lapCount==1,"A subsequent goal crossing must count without checkpoints.");
            Require(laps.OnCarPassGoal(body,goal.transform) && data.isFinished,"Two full circuits must finish a two-lap race.");
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
            UnityEngine.Object.DestroyImmediate(blocker); Physics.SyncTransforms();
            Require(resetter.TryResetCar(true)&&body.position.z< -19,"A clear recovery must return to the last safe road.");
            Require(laps.GetCarData(body).lapCount==1&&resetter.RecoveryCount==1,"Recovery must preserve laps and count only successful moves.");
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
            Require(laps.GetCarData(body).lapCount==1,"A genuine later passage must still count.");
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
