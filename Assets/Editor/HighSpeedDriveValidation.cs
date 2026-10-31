#if UNITY_EDITOR
using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Production car and contact rays, real PhysX, flat-road motor/resistance measurement.</summary>
public static class HighSpeedDriveValidation
{
    private const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    private sealed class Input : IDriveInputSource
    {
        public int PlayerIndex=>0;public string DeviceId=>"High-speed drive QA";
        public bool IsConnected {get;set;}=true;
        public DriveInputState CurrentState {get;set;}=new DriveInputState{pedal=1f};
        public void UpdateInput(float dt){}public void Dispose(){}
    }
    [Serializable] private sealed class Trial {public bool fast;public float speedAtTwoSeconds,finalKmh,offFirstStepKmh,offAfterThreeSecondsKmh;}
    public static void Run()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);
        var previous=Gmanager.Control;var simulation=Physics.simulationMode;
        var root=new GameObject("High-speed drive QA");root.SetActive(false);
        var manager=root.AddComponent<Gmanager>();manager.state=Gmanager.State.Game;Gmanager.Control=manager;
        var features=root.AddComponent<RaceFeatureController>();features.Configure(manager);
        Set(manager,"<Features>k__BackingField",features);
        var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);floor.transform.position=new Vector3(5000,4999.5f,5000);floor.transform.localScale=new Vector3(10000,1,10000);
        UnityEngine.Object.DestroyImmediate(floor.GetComponent<BoxCollider>());
        var surface=floor.AddComponent<MeshCollider>();surface.sharedMesh=floor.GetComponent<MeshFilter>().sharedMesh;
        var material=new PhysicsMaterial("Motor test road"){dynamicFriction=0,staticFriction=0,frictionCombine=PhysicsMaterialCombine.Minimum};surface.sharedMaterial=material;
        try
        {
            Physics.simulationMode=SimulationMode.Script;
            Trial normal=Drive(false,features),fast=Drive(true,features);
            VerifyGauge(features);
            Require(fast.speedAtTwoSeconds>normal.speedAtTwoSeconds+40,"The actual pedal accelerates more strongly.");
            Require(fast.finalKmh>=285 && fast.finalKmh<=305,"Flat-road full pedal must settle near 300 km/h: "+fast.finalKmh);
            Require(fast.offFirstStepKmh>250 && fast.offFirstStepKmh<fast.finalKmh,"Live OFF decelerates physically without setting velocity to the old top speed.");
            Require(fast.offAfterThreeSecondsKmh<225,"Live OFF restores the normal drivetrain.");
            string output=Environment.GetEnvironmentVariable("RACING_HIGH_SPEED_OUTPUT")??"Artifacts/HighSpeedDrive";Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(output,"physics.json"),"["+JsonUtility.ToJson(normal,true)+","+JsonUtility.ToJson(fast,true)+"]");
            Debug.Log("HIGH_SPEED_DRIVE_PHYSICS_PASS: normal="+normal.finalKmh.ToString("F1")+"km/h; fast="+fast.finalKmh.ToString("F1")+"km/h; 2s="+normal.speedAtTwoSeconds.ToString("F1")+"/"+fast.speedAtTwoSeconds.ToString("F1")+"km/h; live OFF="+fast.offAfterThreeSecondsKmh.ToString("F1")+"km/h.");
        }
        finally{Physics.simulationMode=simulation;Gmanager.Control=previous;UnityEngine.Object.DestroyImmediate(root);UnityEngine.Object.DestroyImmediate(floor);UnityEngine.Object.DestroyImmediate(material);}
    }
    private static void VerifyGauge(RaceFeatureController features)
    {
        var root=new GameObject("Speed gauge QA",typeof(RectTransform));
        var gauge=root.AddComponent<RacingSpeedGauge>();
        try
        {
            features.SetFeature(RaceFeature.HighSpeedDrive,true);gauge.SetSpeed(300);
            Require(gauge.SpeedScaleMaximum==360 && Mathf.Approximately(gauge.Reading,300),"High-speed gauge must show the real 300 km/h reading.");
            features.SetFeature(RaceFeature.HighSpeedDrive,false);gauge.SetSpeed(300);
            Require(gauge.SpeedScaleMaximum==240 && Mathf.Approximately(gauge.Reading,240),"Live OFF restores the normal dial range immediately.");
        }
        finally{UnityEngine.Object.DestroyImmediate(root);}
    }
    private static Trial Drive(bool fast,RaceFeatureController features)
    {
        features.SetFeature(RaceFeature.HighSpeedDrive,fast);
        var car=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefab/car/car2.prefab"),new Vector3(5000,5000.36f,5000),Quaternion.identity);
        var mover=car.GetComponent<DebugMover>();var body=car.GetComponent<Rigidbody>();var stability=car.GetComponent<CarStabilityController>();
        var input=new Input();var gate=new PlayerDriveInputGate(input);
        var trial=new Trial{fast=fast};
        try
        {
            foreach(var tire in car.GetComponentsInChildren<TireForce>()){Call(tire.GetComponent<GroundCheck>(),"Awake");Call(tire,"Awake");}
            Call(mover,"Awake");Call(stability,"Awake");mover.SetInputSource(gate);
            Physics.SyncTransforms();
            for(int step=0;step<1000;step++)
            {
                Step(mover,stability);if(step==99)trial.speedAtTwoSeconds=body.linearVelocity.magnitude*3.6f;
            }
            trial.finalKmh=body.linearVelocity.magnitude*3.6f;
            Debug.Log("HIGH_SPEED_DRIVE_MEASURE: fast="+fast+" speed="+trial.finalKmh+" force="+mover.EffectiveDriveForce+" drag="+mover.EffectiveResistance);
            if(fast)
            {
                features.SetFeature(RaceFeature.HighSpeedDrive,false);Step(mover,stability);trial.offFirstStepKmh=body.linearVelocity.magnitude*3.6f;
                for(int i=1;i<150;i++)Step(mover,stability);trial.offAfterThreeSecondsKmh=body.linearVelocity.magnitude*3.6f;
                features.SetFeature(RaceFeature.HighSpeedDrive,true);gate.SetBlocked(true);
                for(int i=0;i<50;i++)Step(mover,stability);
                Require(body.linearVelocity.magnitude*3.6f<trial.offAfterThreeSecondsKmh,"A blocked input gate cannot accelerate in high-speed mode.");
            }
            return trial;
        }
        finally{UnityEngine.Object.DestroyImmediate(car);}
    }
    private static void Step(DebugMover mover,CarStabilityController stability){Call(mover,"ApplyDriving");Call(stability,"FixedUpdate");Physics.Simulate(.02f);Physics.SyncTransforms();}
    private static void Set(object obj,string field,object value)=>obj.GetType().GetField(field,Private).SetValue(obj,value);
    private static void Call(object obj,string method)=>obj.GetType().GetMethod(method,Private).Invoke(obj,null);
    private static void Require(bool value,string text){if(!value)throw new InvalidOperationException(text);}
}
#endif
