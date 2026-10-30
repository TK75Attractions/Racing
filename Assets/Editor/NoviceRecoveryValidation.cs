using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object=UnityEngine.Object;

/// <summary>Controlled recovery-state regressions; full-race input-only evidence is recorded separately.</summary>
public static class NoviceRecoveryValidation
{
    private const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    private sealed class Input:IDriveInputSource
    {
        public int PlayerIndex=>0;public string DeviceId=>"NOVICE_RECOVERY_QA";
        public bool IsConnected {get;set;}=true;public DriveInputState CurrentState {get;set;}
        public void UpdateInput(float dt){}public void Dispose(){}
    }
    [Serializable] private sealed class Check {public string name;public bool passed;public int recoveries;}
    [Serializable] private sealed class Report {public bool passed;public List<Check> checks=new List<Check>();}
    public static void Run()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);
        var root=new GameObject("Novice recovery QA");var previous=Gmanager.Control;
        var report=new Report();var input=new Input();
        try
        {
            var managerHost=new GameObject("Inactive manager");managerHost.transform.SetParent(root.transform);managerHost.SetActive(false);
            var race=managerHost.AddComponent<Gmanager>();race.state=Gmanager.State.Game;Gmanager.Control=race;
            var course=root.AddComponent<RaceCourse>();race.course=course;
            JsonUtility.FromJsonOverwrite("{\"closedLoop\":false,\"generateRoad\":false,\"cornerRoundingDistance\":0,\"slopeBlendDistance\":0,\"waypoints\":[{\"position\":{\"x\":10000,\"y\":10000},\"height\":100,\"width\":24},{\"position\":{\"x\":10000,\"y\":10200},\"height\":100,\"width\":24}]}",course);course.RebuildCache();
            var car=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefab/car/car2.prefab"),root.transform);
            var body=car.GetComponent<Rigidbody>();body.useGravity=false;
            var mover=car.GetComponent<DebugMover>();Call(mover,"Awake");mover.SetInputSource(input);
            var resetter=car.GetComponent<CarResetter>();Call(resetter,"Awake");resetter.SetInputSource(input);
            var gate=new PlayerDriveInputGate(input);mover.SetInputSource(gate);resetter.SetInputSource(gate);
            course.TryGetSampleAtProgress(70,out var sample);
            int frames=Mathf.CeilToInt((CarResetter.AutomaticRecoveryDelay-.0001f)/Time.fixedDeltaTime);
            void Prepare(float pedal=0,float heading=0,float speed=0)
            {
                Call(resetter,"OnDisable");Set(resetter,"resetHeld",false);Set(resetter,"manualPending",false);Set(resetter,"<IsRecoveryPending>k__BackingField",false);Set(mover,"inputSuppressedUntil",float.NegativeInfinity);gate.Reset();input.IsConnected=true;
                input.CurrentState=new DriveInputState{pedal=pedal};body.position=sample.position+sample.up*1.3f;body.rotation=Quaternion.Euler(0,heading,0);
                car.transform.SetPositionAndRotation(body.position,body.rotation);body.linearVelocity=body.rotation*Vector3.forward*speed;body.angularVelocity=Vector3.zero;
                foreach(var tire in car.GetComponentsInChildren<TireForce>())Set(tire,"isGrounded",true);
                resetter.SetCourse(course,null);Physics.SyncTransforms();
            }
            void Tick(int n){for(int i=0;i<n;i++)Call(resetter,"FixedUpdate");}
            void Record(string name,bool passed,int before)=>report.checks.Add(new Check{name=name,passed=passed,recoveries=resetter.AutomaticRecoveryCount-before});
            Prepare();int before=resetter.AutomaticRecoveryCount;Vector3 start=body.position;Tick(frames*4);
            Record("neutral-stop-does-not-teleport",resetter.AutomaticRecoveryCount==before&&body.position==start,before);
            Prepare(speed:.2f);before=resetter.AutomaticRecoveryCount;Tick(frames*2);
            Record("slow-coast-does-not-teleport",resetter.AutomaticRecoveryCount==before,before);
            Prepare(pedal:1);before=resetter.AutomaticRecoveryCount;Tick(frames-1);
            bool boundary=resetter.AutomaticRecoveryCount==before;Tick(1);
            Record("held-pedal-jam-recovers-at-three-seconds",boundary&&resetter.AutomaticRecoveryCount==before+1,before);
            foreach(string escape in new[]{"healthy-motion","disconnection","blocked-input"})
            {
                Prepare(pedal:1);before=resetter.AutomaticRecoveryCount;
                var blocker=new GameObject("Occupied recovery lanes",typeof(BoxCollider));blocker.transform.position=sample.position+Vector3.up*3;
                blocker.GetComponent<BoxCollider>().size=new Vector3(50,8,20);Physics.SyncTransforms();Tick(frames+5);
                bool pending=resetter.IsRecoveryPending&&resetter.AutomaticRecoveryCount==before;
                if(escape=="healthy-motion")
                {body.position+=sample.forward*20;car.transform.position=body.position;body.linearVelocity=sample.forward*10;Physics.SyncTransforms();}
                else if(escape=="disconnection")input.IsConnected=false;
                else gate.SetBlocked(true);
                Tick(1);Record("pending-cancels-on-"+escape,pending&&!resetter.IsRecoveryPending&&resetter.AutomaticRecoveryCount==before,before);
                Object.DestroyImmediate(blocker);
            }
            foreach(bool disconnect in new[]{true,false})
            {
                Prepare();int count=resetter.RecoveryCount;
                var blocker=new GameObject("Occupied manual recovery",typeof(BoxCollider));blocker.transform.position=sample.position+Vector3.up*3;
                blocker.GetComponent<BoxCollider>().size=new Vector3(50,8,20);Physics.SyncTransforms();
                input.CurrentState=new DriveInputState{resetPressed=true};Call(resetter,"Update");bool pending=resetter.IsRecoveryPending;
                if(disconnect)input.IsConnected=false;else gate.SetBlocked(true);
                Object.DestroyImmediate(blocker);Physics.SyncTransforms();Call(resetter,"Update");
                Record("manual-pending-cancels-on-"+(disconnect?"disconnect":"blocked-input"),pending&&!resetter.IsRecoveryPending&&resetter.RecoveryCount==count,resetter.AutomaticRecoveryCount);
            }
            Prepare();int manualBefore=resetter.RecoveryCount;
            input.CurrentState=new DriveInputState{resetPressed=true};
            for(int i=0;i<20;i++){Set(mover,"inputSuppressedUntil",float.NegativeInfinity);Call(resetter,"Update");}
            bool once=resetter.RecoveryCount==manualBefore+1;
            input.CurrentState=DriveInputState.Neutral;Call(resetter,"Update");input.CurrentState=new DriveInputState{resetPressed=true};Set(mover,"inputSuppressedUntil",float.NegativeInfinity);Call(resetter,"Update");
            Record("held-reset-is-one-press-and-fresh-press-works",once&&resetter.RecoveryCount==manualBefore+2,resetter.AutomaticRecoveryCount);
            report.passed=report.checks.TrueForAll(c=>c.passed);
        }
        finally
        {
            Object.DestroyImmediate(root);Gmanager.Control=previous;
            string output=Environment.GetEnvironmentVariable("RACING_NOVICE_OUTPUT")??"Artifacts/NoviceAudit";Directory.CreateDirectory(output);
            File.WriteAllText(output+"/recovery.json",JsonUtility.ToJson(report,true));
        }
        if(!report.passed)throw new InvalidOperationException("NOVICE_RECOVERY_FAIL: "+JsonUtility.ToJson(report));
        Debug.Log("NOVICE_RECOVERY_PASS: neutral/coasting, three-second demand, occupied recovery cancellation, disconnection/blocking, held reset and fresh press.");
    }
    private static void Call(object target,string method)=>target.GetType().GetMethod(method,Private).Invoke(target,null);
    private static void Set(object target,string field,object value)=>target.GetType().GetField(field,Private).SetValue(target,value);
}
