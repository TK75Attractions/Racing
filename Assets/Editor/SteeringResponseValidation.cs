using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Production car/tire physics on an isolated road, using fixed 20ms simulation steps.</summary>
public static class SteeringResponseValidation
{
    private const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    private sealed class Input : IDriveInputSource,IDriveSteeringCalibration
    {
        public int PlayerIndex=>0; public string DeviceId=>"STEERING_RESPONSE_QA"; public bool IsConnected=>true;
        public DriveInputState CurrentState {get;set;}
        public float FullSteeringInput=>90f/6.5f;
        public void UpdateInput(float dt){} public void Dispose(){}
    }
    [Serializable] private sealed class Trial
    {
        public float initialSpeed,handleDegrees,firstYaw,heldYaw,heldSpeed,centerYaw,reverseYaw,maxSlip,maxTilt;
        public float timeToTurn=-1,timeToCenter=-1,timeToReverse=-1;
    }
    [Serializable] private sealed class Report
    {
        public string revision,methodology="Actual car2 prefab, production tire/drivetrain/stability code, isolated flat road. Editor Physics.Simulate at 0.02s; not a human or hardware test. Initial pose and velocity are set only between trials; no pose/velocity writes during each driving trial. Physical handle degrees are supplied as handle/6.5 through a player gate. 0.6s turn, 0.6s center, 0.6s opposite lock. Speed may change naturally; yaw is radians/second. Update/camera/network latency are outside this test.";
        public List<Trial> trials=new List<Trial>();
    }
    private static Report lastReport;
    private static readonly MethodInfo Drive=typeof(DebugMover).GetMethod("ApplyDriving",Private);
    private static readonly MethodInfo Stabilize=typeof(CarStabilityController).GetMethod("FixedUpdate",Private);
    public static void RecordBatch()
    {
        if(!Application.isBatchMode)throw new InvalidOperationException("Use an isolated batch editor.");
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);
        string folder=Environment.GetEnvironmentVariable("RACING_STEERING_OUTPUT")??"Artifacts/SteeringResponse/Latest";
        Directory.CreateDirectory(folder);
        var report=new Report {revision=Environment.GetEnvironmentVariable("RACING_STEERING_REVISION")??"working-tree"};
        SimulationMode previous=Physics.simulationMode; Physics.simulationMode=SimulationMode.Script;
        try
        {
            using(var trace=new StreamWriter(Path.Combine(folder,"trace.csv")))
            {
                trace.WriteLine("initialSpeed,handleDegrees,time,command,speed,yaw,slip,tilt,x,z");
                foreach(float speed in new[]{8f,20f,40f})
                foreach(float handle in new[]{-90f,-45f,-15f,15f,45f,90f})
                    report.trials.Add(Run(speed,handle,trace));
            }
            lastReport=report;
            File.WriteAllText(Path.Combine(folder,"report.json"),JsonUtility.ToJson(report,true));
            Debug.Log("STEERING_RESPONSE_RECORDED: "+folder+"; trials="+report.trials.Count);
        }
        finally {Physics.simulationMode=previous;}
    }
    private static Trial Run(float speed,float handle,StreamWriter trace)
    {
        GameObject root=new GameObject("Steering response trial");
        try
        {
            Rigidbody body=CreateCar(root,out DebugMover mover,out Input input,out CarStabilityController stability);
            body.linearVelocity=Vector3.forward*speed;body.angularVelocity=Vector3.zero;
            var trial=new Trial {initialSpeed=speed,handleDegrees=handle};
            for(int i=0;i<90;i++)
            {
                float time=(i+1)*.02f;
                float command=i<30?handle:i<60?0:-handle;
                float forwardSpeed=Vector3.Dot(body.linearVelocity,body.rotation*Vector3.forward);
                // Maintain the initial pace through pedal input; the vehicle keeps its real forces.
                input.CurrentState=new DriveInputState {pedal=Mathf.Clamp((speed-forwardSpeed)*.15f,-1,1),steering=command/6.5f};
                Drive.Invoke(mover,null);Stabilize.Invoke(stability,null);Physics.Simulate(.02f);
                Vector3 velocity=Vector3.ProjectOnPlane(body.linearVelocity,Vector3.up);
                float yaw=Vector3.Dot(body.angularVelocity,Vector3.up);
                float slip=velocity.magnitude>.5f?Vector3.SignedAngle(velocity,body.rotation*Vector3.forward,Vector3.up):0;
                float tilt=Vector3.Angle(body.rotation*Vector3.up,Vector3.up);
                trial.maxSlip=Mathf.Max(trial.maxSlip,Mathf.Abs(slip));trial.maxTilt=Mathf.Max(trial.maxTilt,tilt);
                if(i==0)trial.firstYaw=yaw;
                if(i==29){trial.heldYaw=yaw;trial.heldSpeed=velocity.magnitude;}
                if(i==59)trial.centerYaw=yaw;
                if(i==89)trial.reverseYaw=yaw;
                if(i<30&&trial.timeToTurn<0&&yaw*Mathf.Sign(handle)>.05f)trial.timeToTurn=time;
                if(i>=30&&i<60&&trial.timeToCenter<0&&Mathf.Abs(yaw)<.05f)trial.timeToCenter=time-.6f;
                if(i>=60&&trial.timeToReverse<0&&yaw*Mathf.Sign(handle)<-.05f)trial.timeToReverse=time-1.2f;
                trace.WriteLine(FormattableString.Invariant($"{speed},{handle},{time:F2},{command},{velocity.magnitude:F4},{yaw:F5},{slip:F4},{tilt:F3},{body.position.x:F4},{body.position.z:F4}"));
            }
            return trial;
        }
        finally {UnityEngine.Object.DestroyImmediate(root);}
    }

    public static void ValidateBatch()
    {
        RecordBatch();
        foreach(Trial trial in lastReport.trials)
        {
            Require(trial.firstYaw*Mathf.Sign(trial.handleDegrees)>0,"The first physics step must turn toward the handle.");
            Require(trial.timeToTurn>0&&trial.timeToTurn<=.021f,"Turning must begin within one 20ms step.");
            Require(trial.timeToCenter>0&&trial.timeToCenter<=.12f,"Centering must settle promptly.");
            Require(trial.timeToReverse>0&&trial.timeToReverse<=.12f,"Countersteering must respond promptly.");
            Require(trial.maxTilt<5&&trial.maxSlip<13,"Responsive steering must not introduce roll or uncontrolled slip.");
        }
        foreach(float speed in new[]{8f,20f,40f})
        foreach(float sign in new[]{-1f,1f})
        {
            Trial half=lastReport.trials.Find(t=>t.initialSpeed==speed&&t.handleDegrees==sign*45);
            Trial full=lastReport.trials.Find(t=>t.initialSpeed==speed&&t.handleDegrees==sign*90);
            float ratio=(half.heldYaw/half.heldSpeed)/(full.heldYaw/full.heldSpeed);
            Require(Mathf.Abs(ratio-.5f)<.08f,"The physical half-turn must retain approximately half the curvature.");
        }
        SimulationMode previous=Physics.simulationMode;
        Physics.simulationMode=SimulationMode.Script;
        try
        {
            foreach(string condition in new[]{"ground","air","oil","suppressed","no-grip"})ValidateGuard(condition);
        }
        finally {Physics.simulationMode=previous;}
        SteeringValidation.Run(); DriftValidation.Run(); RoadSeamValidation.Run();
        Debug.Log("RESPONSIVE_STEERING_PASS: 18 physical angle/speed trials, 5 eligibility guards, calibrated input, drift and seam regressions.");
    }
    private static Rigidbody CreateCar(GameObject root,out DebugMover mover,out Input input,out CarStabilityController stability)
    {
            var road=new GameObject("Road",typeof(BoxCollider));road.transform.SetParent(root.transform);
            road.transform.position=Vector3.down*.5f; road.GetComponent<BoxCollider>().size=new Vector3(500,1,500);
            road.GetComponent<BoxCollider>().sharedMaterial=AssetDatabase.LoadAssetAtPath<PhysicsMaterial>("Assets/TestAddForce/New Physics Material.physicMaterial");
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefab/car/car2.prefab");
            GameObject car=UnityEngine.Object.Instantiate(prefab,root.transform);
            car.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);
            Rigidbody body=car.GetComponent<Rigidbody>();body.isKinematic=false;
            stability=car.GetComponent<CarStabilityController>(); Call(stability,"Awake");
            foreach(GroundCheck ground in car.GetComponentsInChildren<GroundCheck>())Call(ground,"Awake");
            foreach(TireForce tire in car.GetComponentsInChildren<TireForce>())Call(tire,"Awake");
            mover=car.GetComponent<DebugMover>(); Call(mover,"Awake");
            input=new Input();mover.SetInputSource(new PlayerDriveInputGate(input));
            Physics.SyncTransforms();
            float bottom=float.MaxValue;
            foreach(TireForce tire in car.GetComponentsInChildren<TireForce>())bottom=Mathf.Min(bottom,tire.GetComponent<SphereCollider>().bounds.min.y);
            body.position+=Vector3.up*(.03f-bottom);Physics.SyncTransforms();
            for(int i=0;i<40;i++){Stabilize.Invoke(stability,null);Physics.Simulate(.02f);}

        return body;
    }
    private static void ValidateGuard(string condition)
    {
        var root=new GameObject("Steering guard "+condition);
        try
        {
            Rigidbody body=CreateCar(root,out DebugMover mover,out Input input,out CarStabilityController stability);
            body.linearVelocity=Vector3.forward*20;
            input.CurrentState=new DriveInputState {steering=90f/6.5f};
            if(condition=="air")
            {
                body.transform.position=body.position+Vector3.up*20;
                Physics.SyncTransforms();
                foreach(GroundCheck ground in body.GetComponentsInChildren<GroundCheck>())
                    Require(!ground.CheckNow(),"The airborne fixture must have no wheel contact.");
            }
            if(condition=="suppressed")mover.SuppressInputAfterRespawn();
            if(condition=="no-grip")typeof(DebugMover).GetField("enableLateralTireForces",Private).SetValue(mover,false);
            if(condition=="oil")
            {
                CarItemEffects effects=body.GetComponent<CarItemEffects>()??body.gameObject.AddComponent<CarItemEffects>();
                Call(effects,"Awake");Require(effects.ApplyOil(),"The guard fixture must actually be oiled.");
            }
            Call(mover,"ReadInput");
            Vector3 before=body.GetAccumulatedTorque();
            typeof(DebugMover).GetMethod("ApplyDriftSpinProtection",Private).Invoke(mover,new object[]{.02f});
            float torque=(body.GetAccumulatedTorque()-before).magnitude;
            Require(condition=="ground"?torque>.01f:torque<.0001f,"Unexpected steering assistance in "+condition);
        }
        finally {UnityEngine.Object.DestroyImmediate(root);}
    }
    private static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}

    private static void Call(object target,string name)=>target.GetType().GetMethod(name,Private).Invoke(target,null);
}
