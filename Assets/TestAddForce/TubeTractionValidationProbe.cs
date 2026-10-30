#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEditor;

/// <summary>Real wheel/mesh collisions with only longitudinal translation free, to isolate seam traction.</summary>
[DefaultExecutionOrder(10000)]
public sealed class TubeTractionValidationProbe : MonoBehaviour
{
    private sealed class Input:IDriveInputSource
    {
        public int PlayerIndex=>0;public string DeviceId=>"TUBE_TRACTION_QA";
        public bool IsConnected {get;set;}=true;public DriveInputState CurrentState {get;set;}
        public void UpdateInput(float dt){}public void Dispose(){}
    }
    [Serializable] private sealed class Trial
    {
        public string name;public bool passed;public float idleTravel,forwardTravel,forwardSpeed,maximumWallForce;
        public int physicalWallWheels,rayGroundedWheels;
    }
    [Serializable] private sealed class Report{public bool passed;public List<Trial> trials=new List<Trial>();}
    private readonly Report report=new Report();
    private readonly Input input=new Input();
    private DebugMover mover;private Trial trial;
    private IEnumerator Start()
    {
        Application.runInBackground=true;Time.timeScale=1f;
        var previous=Gmanager.Control;var managerHost=new GameObject("Inactive fixture manager");managerHost.SetActive(false);
        var race=managerHost.AddComponent<Gmanager>();race.state=Gmanager.State.Game;Gmanager.Control=race;
        foreach(bool floor in new[]{false,true})foreach(int side in new[]{-1,1})
        {
            var fixture=new GameObject("Tube seam fixture");
            trial=new Trial{name=(side<0?"left":"right")+(floor?"-road-seam":"-wall-only")};report.trials.Add(trial);
            input.IsConnected=true;input.CurrentState=DriveInputState.Neutral;
            var car=Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefab/car/car2.prefab"),new Vector3(0,100,0),Quaternion.identity,fixture.transform);
            foreach(var component in car.GetComponentsInChildren<MonoBehaviour>(true))
                if(!(component is DebugMover||component is TireForce||component is GroundCheck||component is CarItemEffects||component is CarWallCollisionResponse))component.enabled=false;
            foreach(var audio in car.GetComponentsInChildren<AudioSource>())audio.enabled=false;
            mover=car.GetComponent<DebugMover>();mover.SetInputSource(input);
            var body=car.GetComponent<Rigidbody>();body.useGravity=false;
            body.constraints=RigidbodyConstraints.FreezePositionX|RigidbodyConstraints.FreezePositionY|RigidbodyConstraints.FreezeRotation;
            var tires=car.GetComponentsInChildren<TireForce>();
            TireForce edge=tires[0];
            foreach(var tire in tires)if(tire.transform.position.x*side>edge.transform.position.x*side)edge=tire;
            var sphere=edge.GetComponent<SphereCollider>();float radius=sphere.radius*Mathf.Abs(sphere.transform.lossyScale.x);
            Vector3 center=sphere.transform.TransformPoint(sphere.center);
            float inward=-side;
            body.position+=Vector3.right*(inward*(radius-.015f)-center.x);
            float floorY=center.y-radius+.01f;
            var wallObject=new GameObject("Tube collision wall",typeof(MeshCollider));wallObject.transform.SetParent(fixture.transform);
            Mesh mesh=new Mesh();mesh.vertices=new[]{new Vector3(0,95,-100),new Vector3(0,110,-100),new Vector3(0,110,100),new Vector3(0,95,100)};
            mesh.triangles=new[]{0,1,2,0,2,3,2,1,0,3,2,0};mesh.RecalculateNormals();mesh.RecalculateBounds();
            var wall=wallObject.GetComponent<MeshCollider>();wall.sharedMesh=mesh;TubeDriveSurface.Ensure(wall);
            if(floor)
            {
                var road=GameObject.CreatePrimitive(PrimitiveType.Cube);road.name="Road edge with one unsupported wheel pair";road.transform.SetParent(fixture.transform);
                road.transform.position=new Vector3(inward*11.1f,floorY-.25f,0);road.transform.localScale=new Vector3(20,.5f,200);
            }
            Physics.SyncTransforms();Vector3 initial=body.position;
            yield return new WaitForSeconds(.5f);
            trial.idleTravel=Vector3.Distance(initial,body.position);
            input.CurrentState=new DriveInputState{pedal=1};Vector3 start=body.position;
            yield return new WaitForSeconds(1.2f);
            trial.forwardTravel=body.position.z-start.z;trial.forwardSpeed=body.linearVelocity.z;
            foreach(var tire in tires)
            {
                if(tire.TryGetTubeWallContact(out _))trial.physicalWallWheels++;
                if(tire.GetComponent<GroundCheck>().CheckNow())trial.rayGroundedWheels++;
            }
            bool passed=trial.idleTravel<.05f&&trial.forwardTravel>2f&&trial.forwardSpeed>3f&&trial.maximumWallForce>=15f&&trial.physicalWallWheels>=2&&
                trial.rayGroundedWheels==(floor?2:0);
            input.CurrentState=DriveInputState.Neutral;yield return new WaitForFixedUpdate();yield return new WaitForFixedUpdate();
            passed&=mover.TubeWallDriveForce==0f;
            input.CurrentState=new DriveInputState{pedal=1};input.IsConnected=false;
            yield return new WaitForFixedUpdate();yield return new WaitForFixedUpdate();passed&=mover.TubeWallDriveForce==0f;
            input.IsConnected=true;input.CurrentState=new DriveInputState{pedal=1};
            body.position+=Vector3.right*inward*3+Vector3.up*3;body.linearVelocity=Vector3.forward*5;Physics.SyncTransforms();
            yield return new WaitForFixedUpdate();yield return new WaitForFixedUpdate();
            passed&=mover.TubeWallDriveForce==0f&&body.linearVelocity.z>4;
            trial.passed=passed;mover=null;Destroy(fixture);yield return null;Destroy(mesh);
        }
        report.passed=report.trials.TrueForAll(t=>t.passed);
        Directory.CreateDirectory("Artifacts/LapPracticeTube");File.WriteAllText("Artifacts/LapPracticeTube/physics.json",JsonUtility.ToJson(report,true));
        Gmanager.Control=previous;Destroy(managerHost);
        Type.GetType("LapPracticeTubeValidation, Assembly-CSharp-Editor").GetMethod("FinishPhysics").Invoke(null,
            new object[]{report.passed,"TUBE_TRACTION_PHYSICS_"+(report.passed?"PASS":"FAIL")+": four real collision fixtures, sleeping/coasting/disconnect/airborne guards."});
    }
    private void FixedUpdate(){if(mover!=null&&trial!=null)trial.maximumWallForce=Mathf.Max(trial.maximumWallForce,mover.TubeWallDriveForce);}
}
#endif
