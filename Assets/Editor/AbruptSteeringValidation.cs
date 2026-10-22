using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Real production car + PhysX, with repeatable abrupt pedal/wheel input after initial placement.</summary>
public static class AbruptSteeringValidation
{
    private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
    private static readonly Vector3 Origin = new Vector3(5000,5000,5000);
    private sealed class Input : IDriveInputSource
    {
        public int PlayerIndex => 0;
        public string DeviceId => "Abrupt steering QA";
        public bool IsConnected { get; set; } = true;
        public DriveInputState CurrentState { get; set; }
        public void UpdateInput(float dt) { }
        public void Dispose() { }
    }
    [Serializable] private sealed class Trial
    {
        public string name; public bool enabled;
        public float bank, initialSpeed, handle, maxSlip, maxYaw, maxTilt, maxNormalSpeed, finalSpeed, travelTurn, finalSlip;
    }
    [Serializable] private sealed class Report
    {
        public bool passed, airbornePreserved, spinPreserved, oilPreserved, liveSwitch, straightPreserved;
        public string error="";
        public List<Trial> trials=new List<Trial>();
    }
    private static RaceFeatureController features;
    private static Report report;
    public static void Run()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);
        var previous=Gmanager.Control; var mode=Physics.simulationMode;
        var root=new GameObject("Abrupt steering QA"); root.SetActive(false);
        report=new Report();
        string output=Environment.GetEnvironmentVariable("RACING_STABILITY_OUTPUT")??"Logs/SteeringStability/Physics";
        Directory.CreateDirectory(output);
        try
        {
            Physics.simulationMode=SimulationMode.Script;
            var race=root.AddComponent<Gmanager>(); Gmanager.Control=race; race.state=Gmanager.State.Game;
            features=root.AddComponent<RaceFeatureController>(); features.Configure(race); Set(race,"<Features>k__BackingField",features);
            foreach(float bank in new[]{0f,-60f,60f})
            foreach(float speed in new[]{20f,40f,60f})
            foreach(float handle in new[]{-6f,6f,-30f,30f})
            {
                Drive(bank,speed,handle,"step",false);
                Trial stable=Drive(bank,speed,handle,"step",true);
                Require(stable.maxSlip<5f,"Abrupt steering must retain road grip: "+stable.name+" slip="+stable.maxSlip);
                Require(stable.maxYaw<2f&&stable.maxTilt<12f&&stable.maxNormalSpeed<.8f,"Keep yaw, banked contact and normal motion stable: "+stable.name);
                Require(stable.travelTurn*Mathf.Sign(handle)>12f,"The trajectory must actually turn towards the wheel: "+stable.name);
            }
            foreach(float speed in new[]{20f,40f,60f})foreach(string pattern in new[]{"flick","fishhook"})
            {
                Drive(0,speed,30,pattern,false);
                Trial stable=Drive(0,speed,30,pattern,true);
                Require(stable.maxSlip<5f&&stable.maxYaw<2f,"Fast reversals must not generate a spin: "+stable.name);
            }
            Trial off=Drive(0,40,0,"step",false),on=Drive(0,40,0,"step",true);
            Require(Mathf.Abs(off.finalSpeed-on.finalSpeed)<.05f&&Mathf.Abs(on.travelTurn)<.1f,"Straight driving must retain acceleration.");
            report.straightPreserved=true;
            ValidateExclusions();
            report.passed=true;
            Debug.Log("ABRUPT_STEERING_PASS: "+report.trials.Count+" real-car trials, small/full abrupt lock, reversals, boost, flat/+/-60 banks, air, oil, spin and live OFF/ON.");
        }
        catch(Exception error){report.error=error.ToString();throw;}
        finally
        {
            File.WriteAllText(Path.Combine(output,"physics.json"),JsonUtility.ToJson(report,true));
            UnityEngine.Object.DestroyImmediate(root);Gmanager.Control=previous;Physics.simulationMode=mode;
        }
    }
    private static Trial Drive(float bank,float speed,float handle,string pattern,bool enabled)
    {
        features.SetValidationFeature(RaceFeature.SteeringStability,enabled);
        Quaternion rotation=Quaternion.AngleAxis(bank,Vector3.forward);
        Vector3 up=rotation*Vector3.up,forward=rotation*Vector3.forward;
        GameObject floor=Floor(rotation),car=null;
        var trial=new Trial{name=$"{pattern} bank={bank} speed={speed} handle={handle}",enabled=enabled,bank=bank,initialSpeed=speed,handle=handle};
        report.trials.Add(trial);
        try
        {
            car=Car(Origin+up*.36f,rotation,out var mover,out var body);
            var input=new Input();mover.SetInputSource(input);
            body.useGravity=false;body.linearVelocity=forward*speed;
            if(speed>=40)Call(mover,"StartDriftBoost",30f);
            Physics.SyncTransforms();
            for(int frame=0;frame<60;frame++)
            {
                float t=frame*.02f;
                float wheel=pattern=="flick"?(t<.12f?handle:0):pattern=="fishhook"?(t<.3f?handle:t<.6f?-handle:0):handle;
                input.CurrentState=new DriveInputState{pedal=1,steering=wheel};
                Call(mover,"ApplyDriving");Call(car.GetComponent<CarStabilityController>(),"FixedUpdate");
                Physics.Simulate(.02f);Physics.SyncTransforms();
                Vector3 velocity=Vector3.ProjectOnPlane(body.linearVelocity,up);
                trial.finalSpeed=velocity.magnitude;
                trial.travelTurn=Vector3.SignedAngle(forward,velocity,up);
                trial.finalSlip=Vector3.Angle(Vector3.ProjectOnPlane(body.rotation*Vector3.forward,up),velocity);
                trial.maxSlip=Mathf.Max(trial.maxSlip,trial.finalSlip);
                trial.maxYaw=Mathf.Max(trial.maxYaw,Mathf.Abs(Vector3.Dot(body.angularVelocity,up)));
                trial.maxTilt=Mathf.Max(trial.maxTilt,Vector3.Angle(body.rotation*Vector3.up,up));
                trial.maxNormalSpeed=Mathf.Max(trial.maxNormalSpeed,Mathf.Abs(Vector3.Dot(body.linearVelocity,up)));
            }
            Debug.Log($"ABRUPT_PHYSICS {trial.name} ON={enabled} slip={trial.maxSlip:F3} yaw={trial.maxYaw:F3} turn={trial.travelTurn:F2} speed={trial.finalSpeed:F2}");
            return trial;
        }
        finally{UnityEngine.Object.DestroyImmediate(car);UnityEngine.Object.DestroyImmediate(floor);}
    }
    private static void ValidateExclusions()
    {
        features.SetValidationFeature(RaceFeature.HitProtection,false);
        features.SetValidationFeature(RaceFeature.ExperienceItems,false);
        foreach(string condition in new[]{"air","spin","oil","disconnected","reverse"})
        {
            Vector3[] off=Exclusion(condition,false),on=Exclusion(condition,true);
            Require(Vector3.Distance(off[0],on[0])<.001f&&Vector3.Distance(off[1],on[1])<.001f,
                "Stability must not alter excluded physics after simulation: "+condition);
        }
        report.airbornePreserved=report.spinPreserved=report.oilPreserved=true;
        GameObject car=Car(Origin+Vector3.up*.36f,Quaternion.identity,out var mover,out var body);
        try
        {
            features.SetValidationFeature(RaceFeature.SteeringStability,false);
            Require(!(bool)Get(mover,"UseSteeringStability"),"Live OFF must select legacy handling.");
            features.SetValidationFeature(RaceFeature.SteeringStability,true);
            Require((bool)Get(mover,"UseSteeringStability"),"Live ON must restore stability without respawn.");
            report.liveSwitch=true;
        }
        finally{UnityEngine.Object.DestroyImmediate(car);}
    }
    private static Vector3[] Exclusion(string condition,bool enabled)
    {
        features.SetValidationFeature(RaceFeature.SteeringStability,enabled);
        GameObject floor=Floor(Quaternion.identity),car=null;
        var random=UnityEngine.Random.state;
        try
        {
            car=Car(Origin+Vector3.up*(condition=="air"?10:.36f),Quaternion.identity,out var mover,out var body);
            mover.SetInputSource(new Input{IsConnected=condition!="disconnected",CurrentState=new DriveInputState{pedal=1,steering=30}});
            body.useGravity=false;body.linearVelocity=new Vector3(8,2,condition=="reverse"?-35:35);
            body.angularVelocity=new Vector3(.2f,2,.3f);
            var effects=car.GetComponent<CarItemEffects>();
            UnityEngine.Random.InitState(501);
            if(condition=="spin") Require(effects.ApplySpin(Vector3.right)&&effects.IsSpinning,"Spin setup must apply.");
            if(condition=="oil") Require(effects.ApplyOil()&&effects.IsOiled,"Oil setup must apply.");
            Physics.SyncTransforms();
            if(enabled)Call(mover,"ApplySteeringStability",.02f);
            Physics.Simulate(.02f);
            return new[]{body.linearVelocity,body.angularVelocity};
        }
        finally
        {
            UnityEngine.Random.state=random;
            UnityEngine.Object.DestroyImmediate(car);UnityEngine.Object.DestroyImmediate(floor);
        }
    }
    private static GameObject Floor(Quaternion rotation)
    {
        Vector3 up=rotation*Vector3.up;
        var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);
        floor.transform.SetPositionAndRotation(Origin-up*.5f,rotation);floor.transform.localScale=new Vector3(1000,1,1000);
        UnityEngine.Object.DestroyImmediate(floor.GetComponent<BoxCollider>());
        var mesh=floor.AddComponent<MeshCollider>();mesh.sharedMesh=floor.GetComponent<MeshFilter>().sharedMesh;TubeDriveSurface.Ensure(mesh);
        return floor;
    }
    private static GameObject Car(Vector3 position,Quaternion rotation,out DebugMover mover,out Rigidbody body)
    {
        var car=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefab/car/car2.prefab"),position,rotation);
        mover=car.GetComponent<DebugMover>();body=car.GetComponent<Rigidbody>();
        foreach(var tire in car.GetComponentsInChildren<TireForce>()){Call(tire.GetComponent<GroundCheck>(),"Awake");Call(tire,"Awake");}
        var effects=car.GetComponent<CarItemEffects>()??car.AddComponent<CarItemEffects>();
        Call(effects,"Awake");Call(mover,"Awake");Call(car.GetComponent<CarStabilityController>(),"Awake");return car;
    }
    private static void Set(object target,string name,object value)=>target.GetType().GetField(name,Private).SetValue(target,value);
    private static object Get(object target,string name)=>target.GetType().GetProperty(name,Private).GetValue(target);
    private static void Call(object target,string name,params object[] args)=>target.GetType().GetMethod(name,Private).Invoke(target,args);
    private static void Require(bool ok,string reason){if(!ok)throw new InvalidOperationException(reason);}
}
