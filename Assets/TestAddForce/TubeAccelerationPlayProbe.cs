#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Reflection;
using UnityEngine;

public sealed class TubeAccelerationPlayProbe:MonoBehaviour
{
    private const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    private sealed class Input:IDriveInputSource
    {public bool Connected=true;public float Pedal=.35f;public int PlayerIndex=>0;public string DeviceId=>"real-pad-playthrough";public bool IsConnected=>Connected;public DriveInputState CurrentState=>new DriveInputState{pedal=Pedal};public void UpdateInput(float dt){}public void Dispose(){}}
    private IEnumerator Start()
    {
        var race=Gmanager.Control;
        foreach(var publisher in FindObjectsByType<RaceRemoteFirebasePublisher>(FindObjectsSortMode.None))publisher.enabled=false;
        foreach(var bridge in FindObjectsByType<RaceDashboardBridge>(FindObjectsSortMode.None))bridge.enabled=false;
        typeof(Gmanager).GetMethod("StartGameWhenScreenCovered",Private).Invoke(race,null);
        typeof(Gmanager).GetMethod("CompleteGameStart",Private).Invoke(race,null);
        typeof(Gmanager).GetMethod("StartRaceAfterCountdown",Private).Invoke(race,null);race.enabled=false;
        foreach(var entry in race.Participants)if(entry.Index!=0){entry.Car.SetActive(false);}
        var own=race.GetParticipant(0);var body=own.Body;var mover=own.Car.GetComponent<DebugMover>();var input=new Input();mover.SetInputSource(input);
        foreach(var resetter in own.Car.GetComponentsInChildren<CarResetter>())resetter.enabled=false;
        var layout=race.course.GetComponent<TubeChargeLineLayout>();var pad=layout.Pads[0];int hits=0;float entrySpeed=0;
        pad.Activated+=car=>{if(car==mover){hits++;entrySpeed=body.linearVelocity.magnitude;}};
        body.position=pad.transform.position-pad.transform.forward*10f+pad.transform.up*.65f;body.rotation=pad.transform.rotation;body.linearVelocity=pad.transform.forward*12f;Physics.SyncTransforms();
        float began=Time.time;while(hits==0&&Time.time-began<6f)yield return new WaitForFixedUpdate();
        if(hits==0){Fail("Actual colliders never activated the pad");yield break;}
        yield return new WaitForFixedUpdate();float accelerated=body.linearVelocity.magnitude;
        if(!mover.IsAccelerationPadBoosting||accelerated<=entrySpeed+1f){Fail("Pad did not add real speed: "+entrySpeed+" -> "+accelerated);yield break;}
        yield return new WaitForSeconds(.3f);
        if(hits!=1||mover.NormalizedDriftCharge>.001f){Fail("Multiple wheel colliders duplicated activation or added obsolete charge");yield break;}
        input.Connected=false;if(pad.TryActivate(mover)){Fail("Disconnected input accepted a boost");yield break;}input.Connected=true;
        body.linearVelocity=-pad.transform.forward*10f;if(pad.TryActivate(mover)){Fail("Reversing accepted a boost");yield break;}
        body.linearVelocity=pad.transform.forward*10f;mover.SuppressInputAfterRespawn();if(pad.TryActivate(mover)){Fail("Respawn suppression accepted a boost");yield break;}
        Debug.Log("TUBE_ACCELERATION_PLAY_PASS: production car drove through real pad colliders once; speed "+entrySpeed+" -> "+accelerated+" m/s; wheel deduplication, no line charge, disconnected/reverse/respawn rejection.");

        // Isolate duration and speed from the authored course's turns using the same production car,
        // real tires, drive force and resistance on a long flat road. Only longitudinal motion is free.
        race.course=null;input.Pedal=1;
        foreach(var component in own.Car.GetComponentsInChildren<MonoBehaviour>(true))
            if(!(component is DebugMover||component is TireForce||component is GroundCheck))component.enabled=false;
        typeof(DebugMover).GetField("inputSuppressedUntil",Private).SetValue(mover,float.NegativeInfinity);
        body.useGravity=false;body.constraints=RigidbodyConstraints.FreezePositionX|RigidbodyConstraints.FreezePositionY|RigidbodyConstraints.FreezeRotation;
        body.position=new Vector3(0,1000,0);body.rotation=Quaternion.identity;own.Car.transform.SetPositionAndRotation(body.position,body.rotation);Physics.SyncTransforms();
        float floorY=float.PositiveInfinity;
        foreach(var tire in own.Car.GetComponentsInChildren<TireForce>())
        {
            var sphere=tire.GetComponent<SphereCollider>();
            floorY=Mathf.Min(floorY,sphere.transform.TransformPoint(sphere.center).y-sphere.radius*Mathf.Abs(sphere.transform.lossyScale.x));
        }
        var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);floor.name="Longitudinal boost QA road";
        floor.transform.position=new Vector3(0,floorY-.24f,500);floor.transform.localScale=new Vector3(20,.5f,2000);Physics.SyncTransforms();
        var finalSpeed=new float[3];var initialSpeed=new float[3];
        for(int trial=0;trial<3;trial++)
        {
            typeof(DebugMover).GetMethod("ResetBoosts",Private).Invoke(mover,null);
            body.position=new Vector3(0,1000,0);body.linearVelocity=Vector3.forward*50;body.angularVelocity=Vector3.zero;own.Car.transform.SetPositionAndRotation(body.position,body.rotation);Physics.SyncTransforms();
            yield return new WaitForFixedUpdate();
            int grounded=0;foreach(var tire in own.Car.GetComponentsInChildren<TireForce>())if(tire.GetComponent<GroundCheck>().CheckNow())grounded++;
            if(grounded!=4){Fail("Duration fixture must use all four real grounded tires: "+grounded);yield break;}
            if(trial==1)mover.StartAccelerationPadBoost(10f,1.25f,4f,Vector3.forward);
            if(trial==2)mover.StartAccelerationPadBoost(pad.Acceleration,pad.BoostDuration,pad.InstantSpeedBonus,Vector3.forward);
            for(int frame=0;frame<Mathf.CeilToInt(3.12f/Time.fixedDeltaTime);frame++)
            {
                yield return new WaitForFixedUpdate();
                if(frame==0)initialSpeed[trial]=body.linearVelocity.z;
                float elapsed=(frame+1)*Time.fixedDeltaTime;
                if(elapsed>=2.78f&&elapsed<2.82f&&mover.IsAccelerationPadBoosting!=(trial==2))
                {Fail("Three-second boost lifetime at "+elapsed+"s, trial "+trial);yield break;}
            }
            finalSpeed[trial]=body.linearVelocity.z;
            if(mover.IsAccelerationPadBoosting){Fail("Boost continued beyond three seconds.");yield break;}
        }
        if(initialSpeed[2]<59f||finalSpeed[2]<finalSpeed[1]+12f||finalSpeed[2]<finalSpeed[0]+20f)
        {Fail("Stronger boost did not create a clear speed gain: "+string.Join(", ",finalSpeed));yield break;}
        string report="PASS: real production pad trigger, wheel deduplication and input/recovery guards.\n"+
            "PASS: four grounded tires, full pedal, 50 m/s entry, same ordinary forces and drag; speeds after 3.12 seconds: baseline="+finalSpeed[0]+", previous boost="+finalSpeed[1]+", new boost="+finalSpeed[2]+" m/s.\n"+
            "PASS: immediate new speed="+initialSpeed[2]+" m/s; active at 2.8s, expired by 3.12s.\n";
        Directory.CreateDirectory("Artifacts/TutorialOverviewBoost");File.WriteAllText("Artifacts/TutorialOverviewBoost/boost-physics.txt",report);
        Debug.Log("TUBE_BOOST_DURATION_PASS: "+report);
        UnityEditor.EditorApplication.Exit(0);
    }
    private static void Fail(string reason){Debug.LogError("TUBE_ACCELERATION_PLAY_FAIL: "+reason);UnityEditor.EditorApplication.Exit(1);}
}
#endif
