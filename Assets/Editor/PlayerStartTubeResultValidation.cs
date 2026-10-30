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
        public DriveInputState CurrentState {get;set;}=DriveInputState.Neutral;
        public void UpdateInput(float dt){}public void Dispose(){}
    }
    [MenuItem("Racing/Validation/Player Start, Tube Assist and Result Timer")]
    public static void Run()
    {
        Directory.CreateDirectory(Folder);File.Delete(Folder+"/failure.txt");
        try
        {
            ValidateIndependentStart();ValidateTube();ValidateResults();
            File.WriteAllText(Folder+"/verification.txt","PASS: each title input starts only that player's practice; repeated start does not duplicate practice; second start preserves the shared deadline.\nPASS: explicit tutorial label persists during driving, briefing and waiting for both palettes; fitted glyphs and 1080p captures.\nPASS: five-second recovery for sideways, reverse, backward rolling, nearly stopped without pedal and outside-course cars; continuous unsafe clock; healthy driving cancels clock; disconnect guard; tube wall driving preserved.\nPASS: wall contact reduces drag for +8% equilibrium speed, floor/coasting/disconnect/air receive none.\nPASS: production tube steering on the floor, banked sides and ceiling; driver input, live OFF, disconnect and airborne guards.\nPASS: both result palettes show 15, ceil(7.2)=8 and 0 seconds; new results reset to 15; nonoverlapping timer/Next/daily ranking; fitted glyphs; production UI renders; actual manager clock shows 1 at 14 seconds and hides results at 15.\n");
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
            var label=rigs[0].CanvasRoot.transform.Find("DrivingTutorial/TutorialMode/Label").GetComponent<TMP_Text>();
            Canvas.ForceUpdateCanvases();label.ForceMeshUpdate();
            Require(label.text=="チュートリアル"&&label.gameObject.activeSelf&&label.transform.parent.gameObject.activeSelf,"Practice must explicitly display a fitted tutorial label.");
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
            var mover=car.AddComponent<DebugMover>();Call(mover,"Awake");var input=new Input{CurrentState=new DriveInputState{pedal=1}};mover.SetInputSource(input);
            var entry=new RaceParticipant{Index=0,Car=car,Body=body};var assist=car.AddComponent<CourseSteeringAssist>();assist.Configure(race,entry);
            foreach(float bank in new[]{0f,45f,-45f,90f,-90f,180f})
            {
                Vector3 up=Quaternion.AngleAxis(bank,Vector3.forward)*Vector3.up;
                Vector3 position=origin+new Vector3(0,12,50)-up*11.65f;
                Quaternion rotation=Quaternion.AngleAxis(10,up)*Quaternion.LookRotation(Vector3.forward,up);
                car.transform.SetPositionAndRotation(position,rotation);body.position=position;body.rotation=rotation;body.linearVelocity=rotation*Vector3.forward*20;body.angularVelocity=Vector3.zero;Physics.SyncTransforms();
                float wallMultiplier=(float)typeof(DebugMover).GetMethod("TubeWallSpeedMultiplier",Private).Invoke(mover,null);
                Require(Mathf.Abs(wallMultiplier-(bank==0?1:1.08f))<.0001f,"Only banked tube contact earns +8% equilibrium speed at bank "+bank);
                Call(mover,"ApplyVelocityResistance");float expectedDrag=Vector3.ProjectOnPlane(body.linearVelocity,up).magnitude*.6f/wallMultiplier;
                Require(Mathf.Abs((float)Get(mover,"resistanceForce")-expectedDrag)<.01f,"Actual drag must apply the bounded wall reward.");
                input.CurrentState=DriveInputState.Neutral;Require((float)typeof(DebugMover).GetMethod("TubeWallSpeedMultiplier",Private).Invoke(mover,null)==1,"Coasting receives no wall reward.");input.CurrentState=new DriveInputState{pedal=1};
                input.IsConnected=false;Require((float)typeof(DebugMover).GetMethod("TubeWallSpeedMultiplier",Private).Invoke(mover,null)==1,"Disconnected input receives no wall reward.");input.IsConnected=true;
                Require(course.IsPointInsideTube(position),"Tube fixture must be inside at bank "+bank);
                assist.Clear();float corrected=0;
                for(int i=0;i<20;i++)corrected=assist.Assist(0,30,up,.02f);
                Require(corrected<-.5f,"Tube axis aid must work at bank "+bank+", got "+corrected);
                assist.Clear();for(int i=0;i<20;i++)corrected=assist.Assist(30,30,up,.02f);
                Require(corrected>15,"Strong handle input must retain control.");
                features.SetValidationFeature(RaceFeature.SteeringAssist,false);Require(assist.Assist(5,30,up,.02f)==5,"Live OFF disables tube aid.");features.SetValidationFeature(RaceFeature.SteeringAssist,true);
                input.IsConnected=false;Require(assist.Assist(5,30,up,.02f)==5,"Disconnected input receives no aid.");input.IsConnected=true;
                body.position=position+up*3;car.transform.position=body.position;Physics.SyncTransforms();Require(assist.Assist(5,30,up,.02f)==5,"Airborne tube cars receive no aid.");Require((float)typeof(DebugMover).GetMethod("TubeWallSpeedMultiplier",Private).Invoke(mover,null)==1,"Airborne cars receive no wall speed reward.");
            }
            ValidateRecovery(car,body,mover,input,course,origin);
        }
        finally{Object.DestroyImmediate(root);Object.DestroyImmediate(mesh);Gmanager.Control=previous;}
    }
    private static void ValidateRecovery(GameObject car,Rigidbody body,DebugMover mover,Input input,RaceCourse course,Vector3 origin)
    {
        var resetter=car.AddComponent<CarResetter>();Call(resetter,"Awake");resetter.SetInputSource(input);
        Vector3 start=origin+new Vector3(0,.35f,50);
        void Prepare(float heading,Vector3 velocity,bool connected=true)
        {
            Call(resetter,"OnDisable");Set(mover,"inputSuppressedUntil",float.NegativeInfinity);
            body.position=start;body.rotation=Quaternion.Euler(0,heading,0);car.transform.SetPositionAndRotation(start,body.rotation);
            body.linearVelocity=velocity;body.angularVelocity=Vector3.zero;input.IsConnected=connected;input.CurrentState=DriveInputState.Neutral;
            foreach(var tire in car.GetComponentsInChildren<TireForce>())Set(tire,"isGrounded",true);
            Physics.SyncTransforms();resetter.SetCourse(course,null);
        }
        void Tick(int count){for(int i=0;i<count;i++)Call(resetter,"FixedUpdate");}
        int frames=Mathf.CeilToInt((CarResetter.AutomaticRecoveryDelay-.0001f)/Time.fixedDeltaTime);
        int before=resetter.AutomaticRecoveryCount;
        foreach(float heading in new[]{90f,180f,0f})
        {
            Prepare(heading,heading==0?Vector3.forward*.4f:Quaternion.Euler(0,heading,0)*Vector3.forward*10);
            Tick(frames-1);Require(resetter.AutomaticRecoveryCount==before,"No recovery before five seconds at heading "+heading+" frames="+frames+" dt="+Time.fixedDeltaTime+" reason="+resetter.RecoveryReason);
            Tick(1);before++;Require(resetter.AutomaticRecoveryCount==before,"Five seconds must recover sideways, reverse or nearly stopped cars without pedal input.");
            Require(Vector3.Dot(body.rotation*Vector3.forward,Vector3.forward)>.99f&&course.IsPointInsideDrivingArea(body.position),"Recovery must face forward inside the course.");
        }
        Prepare(0,-Vector3.forward*5);Tick(frames);before++;Require(resetter.AutomaticRecoveryCount==before,"Backward rolling must recover even when the car faces forward.");
        Prepare(0,Vector3.forward*10);body.position=start+Vector3.right*25;car.transform.position=body.position;Physics.SyncTransforms();Tick(frames-1);
        Require(resetter.AutomaticRecoveryCount==before,"Outside-course recovery must wait five seconds.");Tick(1);before++;Require(resetter.AutomaticRecoveryCount==before,"Outside-course cars recover after five seconds.");
        Prepare(90,Vector3.right*10);Tick(frames/2);body.rotation=Quaternion.identity;car.transform.rotation=body.rotation;body.linearVelocity=Vector3.zero;Tick(frames-frames/2);
        before++;Require(resetter.AutomaticRecoveryCount==before,"Switching unsafe reasons must not restart the shared clock.");
        Prepare(90,Vector3.right*10);Tick(frames-1);body.rotation=Quaternion.identity;car.transform.rotation=body.rotation;body.linearVelocity=Vector3.forward*10;Tick(frames*2);
        Require(resetter.AutomaticRecoveryCount==before,"Resuming healthy forward travel cancels recovery.");body.linearVelocity=Vector3.zero;Tick(frames-1);Require(resetter.AutomaticRecoveryCount==before,"A new stop must get a fresh five seconds.");Tick(1);before++;Require(resetter.AutomaticRecoveryCount==before,"Fresh stopped clock must expire.");
        Prepare(0,Vector3.zero,false);Tick(frames*2);Require(resetter.AutomaticRecoveryCount==before,"Disconnected controllers must not accumulate recovery time.");input.IsConnected=true;
        Prepare(0,Vector3.forward*20);Vector3 up=Vector3.left;body.position=origin+new Vector3(11.65f,12,50);body.rotation=Quaternion.LookRotation(Vector3.forward,up);car.transform.SetPositionAndRotation(body.position,body.rotation);Physics.SyncTransforms();Tick(frames*2);
        Require(resetter.AutomaticRecoveryCount==before,"Healthy tube wall driving must not count as a bad heading or overturn.");
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
                    var tutorialUi=new DrivingTutorialUI(host.transform,player);
                    var lesson=new DrivingTutorialProgress();
                    for(int page=0;page<3;page++)
                    {
                        TutorialGimmickBriefing briefing=null;
                        if(page>0){lesson.Tick(40,true,0,0,0,0);briefing=new TutorialGimmickBriefing();if(page==2)briefing.Tick(10,true,0);}
                        tutorialUi.Update(lesson,DriveInputState.Neutral,0,true,briefing:briefing);
                        foreach(var reveal in host.GetComponentsInChildren<UIRevealSequence>())reveal.Cancel();
                        var badge=host.transform.Find("DrivingTutorial/TutorialMode");var text=badge.Find("Label").GetComponent<TMP_Text>();
                        Canvas.ForceUpdateCanvases();text.ForceMeshUpdate();Require(badge.gameObject.activeInHierarchy&&text.text=="チュートリアル"&&!text.isTextOverflowing,"Tutorial badge must persist during driving, briefing and waiting.");
                        foreach(var child in host.GetComponentsInChildren<Transform>(true))child.gameObject.layer=30;
                        if(page<2)Capture(camera,output,"tutorial-p"+(player+1)+(page==1?"-briefing":"")+".jpg");
                    }
                    tutorialUi.Dispose();
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
                    Capture(camera,output,"result-p"+(player+1)+".jpg");
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
    private static void Capture(Camera camera,RenderTexture output,string name)
    {
        Canvas.ForceUpdateCanvases();camera.Render();var previous=RenderTexture.active;var pixels=new Texture2D(1920,1080,TextureFormat.RGB24,false);
        try{RenderTexture.active=output;pixels.ReadPixels(new Rect(0,0,1920,1080),0,0);pixels.Apply();File.WriteAllBytes(Folder+"/"+name,pixels.EncodeToJPG(90));}
        finally{RenderTexture.active=previous;Object.DestroyImmediate(pixels);}
    }
    private static GameObject Child(Transform parent,string name,params Type[] components){var child=new GameObject(name,components);child.transform.SetParent(parent,false);return child;}
    private static object Get(object target,string field)=>target.GetType().GetField(field,Private|BindingFlags.Public).GetValue(target);
    private static void Set(object target,string field,object value)=>target.GetType().GetField(field,Private|BindingFlags.Public).SetValue(target,value);
    private static void Call(object target,string method,params object[] args)=>target.GetType().GetMethod(method,Private).Invoke(target,args);
    private static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
}
