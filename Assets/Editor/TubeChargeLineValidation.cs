using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object=UnityEngine.Object;

public static class TubeChargeLineValidation
{
    private const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    private sealed class Input:IDriveInputSource
    {public int PlayerIndex=>0;public string DeviceId=>"pad-validation";public bool IsConnected=>true;public DriveInputState CurrentState=>new DriveInputState{pedal=.4f};public void UpdateInput(float dt){}public void Dispose(){}}
    [MenuItem("Racing/Validation/Tube Acceleration Pads")]
    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");var course=Object.FindFirstObjectByType<RaceCourse>();course.RebuildRoad();Physics.SyncTransforms();
        var previous=Gmanager.Control;var root=new GameObject("Tube acceleration validation");root.SetActive(false);TubeChargeLineLayout layout=null;
        try
        {
            var race=root.AddComponent<Gmanager>();race.course=course;race.state=Gmanager.State.Game;Gmanager.Control=race;
            var features=root.AddComponent<RaceFeatureController>();features.Configure(race);typeof(Gmanager).GetField("<Features>k__BackingField",Private).SetValue(race,features);
            var car=Object.Instantiate(Object.FindFirstObjectByType<Gmanager>().carPrefab??AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefab/car/car2.prefab"),root.transform);
            var body=car.GetComponent<Rigidbody>();body.isKinematic=false;var mover=car.GetComponent<DebugMover>();mover.enabled=true;typeof(DebugMover).GetMethod("Awake",Private).Invoke(mover,null);mover.SetInputSource(new Input());
            var own=new RaceParticipant{Index=0,Car=car,Body=body};((List<RaceParticipant>)race.Participants).Add(own);
            var controller=car.AddComponent<TubeChargeLineController>();controller.Configure(race,own);layout=controller.Layout;root.SetActive(true);Physics.SyncTransforms();
            Require(layout.Available&&layout.VisualsVisible&&layout.Pads.Count==2,"Two safe tube acceleration pads must replace the lines");
            Require(course.transform.Find("Optional tube charge lines")==null,"Retired line visuals must not exist");
            foreach(var pad in layout.Pads)
            {
                var collider=pad.GetComponent<BoxCollider>();Require(collider.isTrigger,"Pads must not introduce a physical step");
                Require(pad.GetComponentsInChildren<Collider>().Length==1,"Arrow effects must be nonphysical");
                Require(course.IsPointInsideTube(pad.transform.position+Vector3.up),"Pad must lie inside the tube");
                Require(Physics.Raycast(pad.transform.position+pad.transform.up*1f,-pad.transform.up,out var hit,2f,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore)&&Mathf.Abs(Vector3.Dot(hit.point-pad.transform.position,pad.transform.up))<.05f,"Pad must be flush with the road");
                var arrow=pad.GetComponentInChildren<RotatingBoostArrows>();var forward=arrow.transform.forward;var rotation=arrow.transform.rotation;arrow.SetPhase(.8f);Require(Quaternion.Angle(rotation,arrow.transform.rotation)>20f,"Arrow effect must visibly rotate");Require(Vector3.Dot(forward,arrow.transform.forward)>.999f,"Rotation must retain the road direction");
                Require(pad.GetComponentInChildren<MeshRenderer>().sharedMaterial.shader.name=="Racing/AccelerationPadFlow","Flowing arrow material is required");
            }
            var first=layout.Pads[0];car.transform.SetPositionAndRotation(first.transform.position+first.transform.up*.7f,first.transform.rotation);body.position=car.transform.position;body.rotation=car.transform.rotation;body.linearVelocity=first.transform.forward*10f;Physics.SyncTransforms();
            bool activated=first.TryActivate(mover);
            Require(activated&&mover.IsAccelerationPadBoosting,"Touching the panel must start the existing acceleration boost: activated="+activated+", pad="+first.isActiveAndEnabled+", mover="+mover.isActiveAndEnabled+", suppression="+mover.IsInputSuppressed+", drive="+race.IsDrivingEnabled+", connected="+mover.InputSource.IsConnected+", dot="+Vector3.Dot(mover.transform.forward,first.transform.forward));
            Require(controller.SuccessfulLines==1&&mover.NormalizedDriftCharge==0f,"Panel gives acceleration and feedback, not line charge");
            features.SetValidationFeature(RaceFeature.TubeChargeLines,false);Require(!layout.VisualsVisible&&!first.TryActivate(mover)&&controller.Notice.Length==0,"OFF hides and disables the pads");
            features.SetValidationFeature(RaceFeature.TubeChargeLines,true);Require(layout.VisualsVisible,"ON restores the pads");
            car.transform.rotation=Quaternion.LookRotation(-first.transform.forward,first.transform.up);body.rotation=car.transform.rotation;Physics.SyncTransforms();Require(!first.TryActivate(mover),"Wrong-way travel cannot collect the tube boost");
            Debug.Log("TUBE_ACCELERATION_VALIDATION_PASS: two real pads, flush triggers, no old lines, existing speed boost not charge, feedback, rotation direction, OFF/ON and wrong-way rejection.");
        }
        finally{Gmanager.Control=previous;Object.DestroyImmediate(root);if(layout!=null){var pads=course.transform.Find("Tube acceleration pads");if(pads!=null)Object.DestroyImmediate(pads.gameObject);Object.DestroyImmediate(layout);}}
    }
    public static void RunPlay()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");var input=Object.FindFirstObjectByType<InputManager>();input.isDebugMode=true;input.isP1SerialP2KeyboardDebugMode=false;
        foreach(var publisher in Object.FindObjectsByType<RaceRemoteFirebasePublisher>(FindObjectsSortMode.None))publisher.enabled=false;
        foreach(var bridge in Object.FindObjectsByType<RaceDashboardBridge>(FindObjectsSortMode.None))bridge.enabled=false;
        SessionState.SetBool("Racing.TubePadPlayTest",true);EditorApplication.isPlaying=true;
    }
    [InitializeOnLoadMethod]private static void Resume(){EditorApplication.update-=Watch;EditorApplication.update+=Watch;}
    private static void Watch()
    {
        if(!SessionState.GetBool("Racing.TubePadPlayTest",false)||!EditorApplication.isPlaying||Gmanager.Control==null)return;
        SessionState.SetBool("Racing.TubePadPlayTest",false);new GameObject("Tube pad play probe").AddComponent<TubeAccelerationPlayProbe>();
    }
    private static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
}
