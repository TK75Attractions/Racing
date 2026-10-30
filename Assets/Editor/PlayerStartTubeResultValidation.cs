using System;
using System.Collections;
using System.IO;
using System.Reflection;
using TMPro;
using Unity.Cinemachine;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;
using Object=UnityEngine.Object;

/// <summary>Device-free, edit-mode checks; never saves or replaces the operator's scene.</summary>
public static class PlayerStartTubeResultValidation
{
    private const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    private const string Folder="Artifacts/PlayerStartTubeResult";
    private sealed class Input:IDriveInputSource
    {
        public int PlayerIndex {get;set;}
        public string DeviceId=>"PLAYER_FLOW_VALIDATION";
        public bool IsConnected {get;set;}=true;
        public DriveInputState CurrentState=>DriveInputState.Neutral;
        public void UpdateInput(float dt){}public void Dispose(){}
    }
    [MenuItem("Racing/Validation/Player Start, Tube Assist and Result Timer")]
    public static void Run()
    {
        Directory.CreateDirectory(Folder);File.Delete(Folder+"/failure.txt");
        try
        {
            ValidateIndependentStart();ValidateTube();ValidateResults();
            File.WriteAllText(Folder+"/verification.txt","PASS: each title input starts only that player's practice; repeated start does not duplicate practice; second start preserves the shared deadline.\nPASS: production tube steering on the floor, banked sides and ceiling; driver input, live OFF, disconnect and airborne guards.\nPASS: both result palettes show 15, ceil(7.2)=8 and 0 seconds; new results reset to 15; nonoverlapping timer/Next/daily ranking; fitted glyphs; production UI renders; actual manager clock shows 1 at 14 seconds and hides results at 15.\n");
            Debug.Log("PLAYER_START_TUBE_RESULT_VALIDATION_PASS");
        }
        catch(Exception error){File.WriteAllText(Folder+"/failure.txt",error.ToString());Debug.LogException(error);throw;}
    }
    private static void ValidateIndependentStart()
    {
        var root=new GameObject("Independent start QA");root.SetActive(false);
        Gmanager previous=Gmanager.Control;
        Gmanager race=root.AddComponent<Gmanager>();Gmanager.Control=race;
        try
        {
            race.state=Gmanager.State.Title;race.carPrefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefab/car/car2.prefab");
            race.IManager=root.AddComponent<InputManager>();
            var inputs=(LiveDriveInputSource[])Get(race.IManager,"liveInputs");
            var rigs=new PlayerDisplayRig[2];var players=(Array)Get(race,"players");
            for(int i=0;i<2;i++)
            {
                inputs[i].SwitchSource(new Input{PlayerIndex=i},false);
                var canvas=Child(root.transform,"Canvas"+i,typeof(RectTransform),typeof(Canvas));
                var cameraRoot=Child(root.transform,"Camera"+i);
                var camera=Child(cameraRoot.transform,"Race").AddComponent<CinemachineCamera>();
                rigs[i]=new PlayerDisplayRig(i,cameraRoot,canvas,null,camera,false);
                var runtime=Activator.CreateInstance(players.GetType().GetElementType(),true);
                Set(runtime,"playerIndex",i);Set(runtime,"displayRig",rigs[i]);players.SetValue(runtime,i);
            }
            Set(race,"displayRigs",rigs);
            race.ConfirmTitleStart(0);
            var tutorial=race.GetComponent<DrivingTutorialController>();
            Require(race.GetPlayerPreparationState(0)==Gmanager.State.Tutorial && race.GetPlayerPreparationState(1)==Gmanager.State.Title,"P1 start must leave P2 at title.");
            Require(tutorial.HasStarted(0)&&!tutorial.HasStarted(1),"Only P1 practice may exist.");
            var first=tutorial.GetPlayerCar(0);race.ConfirmTitleStart(0);Require(tutorial.GetPlayerCar(0)==first,"Repeated start must not duplicate practice.");
            ((PreRaceCountdown)Get(race,"preparationClock")).Tick(10);
            race.ConfirmTitleStart(1);
            Require(tutorial.HasStarted(1)&&race.GetPlayerPreparationState(1)==Gmanager.State.Tutorial,"P2's own input starts P2 practice.");
            Require(tutorial.GetPlayerCar(0)==first&&race.PreparationTimeRemaining==50,"P2 start must preserve P1 and the existing deadline.");
        }
        finally
        {
            var tutorial=root.GetComponent<DrivingTutorialController>();
            if(tutorial!=null)
            {
                var practices=(IList)Get(tutorial,"players");
                foreach(object practice in practices) {var world=(GameObject)Get(practice,"world");if(world!=null)Object.DestroyImmediate(world);}
                practices.Clear();var materials=(IList)Get(tutorial,"materials");foreach(Material material in materials)if(material!=null)Object.DestroyImmediate(material);materials.Clear();
            }
            Object.DestroyImmediate(root);Gmanager.Control=previous;
        }
    }
    private static void ValidateTube()
    {
        var root=new GameObject("Tube assist QA");var managerRoot=Child(root.transform,"Inactive manager");managerRoot.SetActive(false);
        var previous=Gmanager.Control;var race=managerRoot.AddComponent<Gmanager>();Gmanager.Control=race;race.state=Gmanager.State.Game;
        Mesh mesh=(Mesh)typeof(TubeWallRecoveryValidation).GetMethod("MakeTube",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,null);
        try
        {
            var features=managerRoot.AddComponent<RaceFeatureController>();features.Configure(race);Set(race,"<Features>k__BackingField",features);
            var origin=new Vector3(10000,100,10000);
            var surface=Child(root.transform,"Tube").AddComponent<MeshCollider>();surface.transform.position=origin;surface.sharedMesh=mesh;TubeDriveSurface.Ensure(surface);
            var course=Child(root.transform,"Course").AddComponent<RaceCourse>();race.course=course;
            var settings=new SerializedObject(course);settings.FindProperty("closedLoop").boolValue=false;settings.FindProperty("generateRoad").boolValue=false;
            settings.FindProperty("cornerRoundingDistance").floatValue=0;settings.FindProperty("slopeBlendDistance").floatValue=0;settings.FindProperty("road.tubeInterior").objectReferenceValue=surface;
            var points=settings.FindProperty("waypoints");points.arraySize=2;
            for(int i=0;i<2;i++){var point=points.GetArrayElementAtIndex(i);point.FindPropertyRelative("position").vector2Value=new Vector2(origin.x,origin.z+i*100);point.FindPropertyRelative("height").floatValue=origin.y;point.FindPropertyRelative("width").floatValue=10;}
            settings.ApplyModifiedPropertiesWithoutUndo();course.RebuildCache();
            var car=Child(root.transform,"Car");var body=car.AddComponent<Rigidbody>();body.useGravity=false;
            for(int i=0;i<4;i++)
            {
                var wheel=Child(car.transform,"Wheel"+i);wheel.transform.localPosition=new Vector3(i%2==0?-.35f:.35f,0,i<2?.7f:-.7f);
                wheel.AddComponent<SphereCollider>().radius=.35f;var tire=wheel.AddComponent<TireForce>();Call(wheel.GetComponent<GroundCheck>(),"Awake");Call(tire,"Awake");
            }
            var mover=car.AddComponent<DebugMover>();Call(mover,"Awake");var input=new Input();mover.SetInputSource(input);
            var entry=new RaceParticipant{Index=0,Car=car,Body=body};var assist=car.AddComponent<CourseSteeringAssist>();assist.Configure(race,entry);
            foreach(float bank in new[]{0f,45f,-45f,90f,-90f,180f})
            {
                Vector3 up=Quaternion.AngleAxis(bank,Vector3.forward)*Vector3.up;
                Vector3 position=origin+new Vector3(0,12,50)-up*11.65f;
                Quaternion rotation=Quaternion.AngleAxis(10,up)*Quaternion.LookRotation(Vector3.forward,up);
                car.transform.SetPositionAndRotation(position,rotation);body.position=position;body.rotation=rotation;body.linearVelocity=rotation*Vector3.forward*20;body.angularVelocity=Vector3.zero;Physics.SyncTransforms();
                Require(course.IsPointInsideTube(position),"Tube fixture must be inside at bank "+bank);
                assist.Clear();float corrected=0;
                for(int i=0;i<20;i++)corrected=assist.Assist(0,30,up,.02f);
                Require(corrected<-.5f,"Tube axis aid must work at bank "+bank+", got "+corrected);
                assist.Clear();for(int i=0;i<20;i++)corrected=assist.Assist(30,30,up,.02f);
                Require(corrected>15,"Strong handle input must retain control.");
                features.SetValidationFeature(RaceFeature.SteeringAssist,false);Require(assist.Assist(5,30,up,.02f)==5,"Live OFF disables tube aid.");features.SetValidationFeature(RaceFeature.SteeringAssist,true);
                input.IsConnected=false;Require(assist.Assist(5,30,up,.02f)==5,"Disconnected input receives no aid.");input.IsConnected=true;
                body.position=position+up*3;car.transform.position=body.position;Physics.SyncTransforms();Require(assist.Assist(5,30,up,.02f)==5,"Airborne tube cars receive no aid.");
            }
        }
        finally{Object.DestroyImmediate(root);Object.DestroyImmediate(mesh);Gmanager.Control=previous;}
    }
    private static void ValidateResults()
    {
        var oldPipeline=GraphicsSettings.defaultRenderPipeline;var oldQuality=QualitySettings.renderPipeline;
        var camera=new GameObject("Result QA camera").AddComponent<Camera>();camera.cullingMask=1<<30;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=ArcadeUI.Blue.Ink;camera.transform.position=new Vector3(0,0,-10);
        var output=new RenderTexture(1920,1080,24);output.Create();camera.targetTexture=output;
        try
        {
            GraphicsSettings.defaultRenderPipeline=null;QualitySettings.renderPipeline=null;
            for(int player=0;player<2;player++)
            {
                var host=new GameObject("Result timer QA",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler));
                try
                {
                    var canvas=host.GetComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=camera;canvas.planeDistance=1;
                    var scaler=host.GetComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1920,1080);
                    var resultRoot=RacingUITheme.Rect(host.transform,"Result",Vector2.zero,Vector2.one);
                    var record=new RaceResultRecord{playerNumber=player+1,didFinish=true,finishPosition=1,totalRaceTime=92.461f};
                    var ui=new ResultUIManager();ui.Init(resultRoot,player+1);ui.ShowResults(record);
                    Require(ui.CountdownText=="00:15","New result timer starts at 15.");ui.SetCountdown(7.2f);Require(ui.CountdownText=="00:08","Countdown rounds up.");ui.SetCountdown(-1);Require(ui.CountdownText=="00:00","Countdown cannot go negative.");ui.ShowResults();Require(ui.CountdownText=="00:15","Each new result resets the timer.");ui.ShowResults(record);
                    foreach(var reveal in host.GetComponentsInChildren<UIRevealSequence>())reveal.Cancel();
                    Canvas.ForceUpdateCanvases();var card=resultRoot.Find("ResultPresentation/ResultCard");
                    var bounds=RectTransformUtility.CalculateRelativeRectTransformBounds(host.transform,card.Find("ResultCountdown"));
                    foreach(string path in new[]{"ResultMenu","DailyRanking"}){var other=RectTransformUtility.CalculateRelativeRectTransformBounds(host.transform,card.Find(path));Require(!(bounds.min.x<other.max.x&&other.min.x<bounds.max.x&&bounds.min.y<other.max.y&&other.min.y<bounds.max.y),"Timer must not overlap "+path);}
                    foreach(var text in card.Find("ResultCountdown").GetComponentsInChildren<TMP_Text>()){text.ForceMeshUpdate();Require(!text.isTextOverflowing&&text.font.HasCharacters(text.text),"Timer text fits with complete glyphs.");}
                    foreach(var child in host.GetComponentsInChildren<Transform>(true))child.gameObject.layer=30;
                    Canvas.ForceUpdateCanvases();camera.Render();var previous=RenderTexture.active;var pixels=new Texture2D(1920,1080,TextureFormat.RGB24,false);
                    try{RenderTexture.active=output;pixels.ReadPixels(new Rect(0,0,1920,1080),0,0);pixels.Apply();File.WriteAllBytes(Folder+"/result-p"+(player+1)+".jpg",pixels.EncodeToJPG(90));}finally{RenderTexture.active=previous;Object.DestroyImmediate(pixels);}
                    var managerRoot=Child(host.transform,"Inactive timer manager");managerRoot.SetActive(false);var manager=managerRoot.AddComponent<Gmanager>();manager.state=Gmanager.State.Result;
                    var runtimes=(Array)Get(manager,"players");var runtime=Activator.CreateInstance(runtimes.GetType().GetElementType(),true);
                    var cameraRoot=Child(managerRoot.transform,"Camera");var raceCamera=Child(cameraRoot.transform,"Race").AddComponent<CinemachineCamera>();
                    var rig=new PlayerDisplayRig(player,cameraRoot,host,null,raceCamera,false);
                    Set(runtime,"displayRig",rig);Set(runtime,"titleCamera",Child(cameraRoot.transform,"Title").AddComponent<CinemachineCamera>());runtimes.SetValue(runtime,player);
                    ((ResultUIManager[])Get(manager,"resultUIManagers"))[player]=ui;
                    Call(manager,"UpdateResultDisplay",14f);Require(ui.CountdownText=="00:01"&&resultRoot.gameObject.activeSelf,"Actual result clock must show one second before expiry.");
                    Call(manager,"UpdateResultDisplay",1f);Require(ui.CountdownText=="00:00"&&!resultRoot.gameObject.activeSelf&&host.GetComponent<ArcadeEndingUI>().IsVisible,"At 15 seconds, the actual clock and exit transition must agree.");
                }
                finally{Object.DestroyImmediate(host);}
            }
        }
        finally{GraphicsSettings.defaultRenderPipeline=oldPipeline;QualitySettings.renderPipeline=oldQuality;camera.targetTexture=null;Object.DestroyImmediate(camera.gameObject);Object.DestroyImmediate(output);}
    }
    private static GameObject Child(Transform parent,string name,params Type[] components){var child=new GameObject(name,components);child.transform.SetParent(parent,false);return child;}
    private static object Get(object target,string field)=>target.GetType().GetField(field,Private|BindingFlags.Public).GetValue(target);
    private static void Set(object target,string field,object value)=>target.GetType().GetField(field,Private|BindingFlags.Public).SetValue(target,value);
    private static void Call(object target,string method,params object[] args)=>target.GetType().GetMethod(method,Private).Invoke(target,args);
    private static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
}
