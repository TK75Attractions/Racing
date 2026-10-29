#if UNITY_EDITOR
using System;
using System.Collections;
using System.Reflection;
using UnityEngine;

public sealed class TubeAccelerationPlayProbe:MonoBehaviour
{
    private const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    private sealed class Input:IDriveInputSource
    {public bool Connected=true;public int PlayerIndex=>0;public string DeviceId=>"real-pad-playthrough";public bool IsConnected=>Connected;public DriveInputState CurrentState=>new DriveInputState{pedal=.35f};public void UpdateInput(float dt){}public void Dispose(){}}
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
        UnityEditor.EditorApplication.Exit(0);
    }
    private static void Fail(string reason){Debug.LogError("TUBE_ACCELERATION_PLAY_FAIL: "+reason);UnityEditor.EditorApplication.Exit(1);}
}
#endif
