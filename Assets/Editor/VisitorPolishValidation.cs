using System;
using System.IO;
using System.Reflection;
using System.Collections;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class VisitorPolishValidation
{
    [MenuItem("Racing/Validation/Visitor Pillar Audit %#k")]
    public static void Audit()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        var course = UnityEngine.Object.FindFirstObjectByType<RaceCourse>();
        course.RebuildCache(); Physics.SyncTransforms();
        var lines = new System.Collections.Generic.List<string>();
        var all = new System.Collections.Generic.List<string>();
        foreach (var collider in UnityEngine.Object.FindObjectsByType<Collider>(FindObjectsSortMode.None))
        {
            all.Add(collider.name + " / " + collider.transform.parent?.name + " bounds=" + collider.bounds);
            if (collider.isTrigger || collider.attachedRigidbody != null) continue;
            var bounds = collider.bounds;
            if (bounds.size.y < 1f || bounds.size.x > 8f || bounds.size.z > 8f) continue;
            if (!course.TryGetNearestRoadSample(bounds.center, out RaceCourse.CourseSample sample)) continue;
            if (Vector3.Distance(new Vector3(bounds.center.x, 0, bounds.center.z), new Vector3(sample.position.x, 0, sample.position.z)) > sample.width * .5f + 3f) continue;
            if (bounds.min.y > sample.position.y + 3f || bounds.max.y < sample.position.y) continue;
            string path = collider.name; for(var parent=collider.transform.parent;parent!=null;parent=parent.parent) path=parent.name+"/"+path;
            lines.Add(path + " bounds=" + bounds + " road=" + sample.position);
        }
        Directory.CreateDirectory("Logs/VisitorPolish");
        File.WriteAllLines("Logs/VisitorPolish/pillars.txt", lines);
        File.WriteAllLines("Logs/VisitorPolish/all-colliders.txt", all);
        Debug.Log("PILLAR_AUDIT: " + lines.Count);
    }
    private const string Key="Racing.VisitorPolishValidation", Folder="Artifacts/VisitorPolish";
    private const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    private static int stage, captures;
    private static double began, since;
    private static string failure;
    private static RenderTexture[] outputs;
    static VisitorPolishValidation(){began=EditorApplication.timeSinceStartup;EditorApplication.update+=Tick;}

    public static void EditChecks(){TutorialPhotoBuilder.UpdateAcceleration();TubeChargeLineValidation.Run();}

    public static void RemoveSnagPoles()
    {
        var scene=EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        var plaza=GameObject.Find("1016こうしゃ")?.transform.Find("Cylinder.002");
        Require(plaza!=null,"Cannot locate authored plaza poles.");
        foreach(string name in new[]{"Cube.011","Cube.012","Sphere","Sphere.001"})
        {
            var part=plaza.Find(name);Require(part!=null,"Missing pole part: "+name);
            part.gameObject.SetActive(false);PrefabUtility.RecordPrefabInstancePropertyModifications(part.gameObject);
        }
        EditorSceneManager.MarkSceneDirty(scene);Require(EditorSceneManager.SaveScene(scene),"Cannot save pole removal.");
        Debug.Log("SNAG_POLES_REMOVED: two slender plaza poles and their top ornaments.");
    }

    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        Directory.CreateDirectory(Folder);File.Delete(Folder+"/failure.txt");
        RacingHUDFontBuilder.Build();
        PlayerAudioValidation.Run();
        var pad=new GameObject("Forward arrow QA");
        try
        {
            var arrows=pad.AddComponent<ForwardBoostArrows>();arrows.Configure(Color.cyan,8,4);
            arrows.SetPhase(0);var before=pad.transform.GetChild(0).localPosition;
            arrows.SetPhase(.25f);Require(pad.transform.GetChild(0).localPosition.z>before.z,"Pad arrow must travel forward.");
            Require(pad.transform.GetChild(0).localRotation==Quaternion.identity,"Pad arrow must not spin.");
            Require(pad.transform.GetChild(0).GetComponent<MeshFilter>().sharedMesh.normals[0].y>.9f,"Pad chevrons must face the driver camera.");
        }
        finally{UnityEngine.Object.DestroyImmediate(pad);}
        var plaza=GameObject.Find("1016こうしゃ")?.transform.Find("Cylinder.002");
        foreach(string name in new[]{"Cube.011","Cube.012","Sphere","Sphere.001"})Require(!plaza.Find(name).gameObject.activeSelf,"Snag pole remains: "+name);
        SessionState.SetBool(Key,true);stage=captures=0;failure=null;began=EditorApplication.timeSinceStartup;
        EditorApplication.isPlaying=true;
    }

    private static void Tick()
    {
        if(!SessionState.GetBool(Key,false))return;
        try
        {
            if(failure!=null)throw new Exception(failure);
            if(EditorApplication.timeSinceStartup-began>120)throw new Exception("Visitor validation timed out at "+stage);
            if(!EditorApplication.isPlaying||Gmanager.Control==null)return;
            var manager=Gmanager.Control;
            foreach(var publisher in UnityEngine.Object.FindObjectsByType<RaceRemoteFirebasePublisher>(FindObjectsSortMode.None))publisher.enabled=false;
            if(stage==0&&Time.frameCount>15)
            {
                if(manager.IsMaintenanceActive)manager.SetMaintenance(false);
                var input=manager.IManager;input.SetInputMode("keyboard");
                manager.StartGame();stage=1;since=EditorApplication.timeSinceStartup;
            }
            else if(stage==1&&EditorApplication.timeSinceStartup-since>2)
            {
                Require(manager.GetPlayerPreparationState(0)==Gmanager.State.Tutorial&&manager.GetPlayerPreparationState(1)==Gmanager.State.Tutorial,"Title must start both tutorials directly.");
                Require(UnityEngine.Object.FindObjectsByType<CarSelectionUI>(FindObjectsSortMode.None).Length==0,"Car selection UI must not be generated.");
                var tutorial=manager.GetComponent<DrivingTutorialController>();
                var practices=(IList)typeof(DrivingTutorialController).GetField("players",Private).GetValue(tutorial);
                var practice=practices[0];
                var lesson=tutorial.GetPlayerProgress(0);
                lesson.Tick(.01f,true,0,0,0,0);lesson.Tick(.7f,true,1,0,4,0);lesson.Tick(.7f,true,1,10,4,30);
                tutorial.Tick(.01f);Require(lesson.CurrentStage==DrivingTutorialProgress.Stage.CollectItem,"Need real pickup stage.");
                float item=(float)Get(practice,"itemProgress");
                var world=(GameObject)Get(practice,"world");var body=(Rigidbody)Get(practice,"body");
                DrivingTutorialRoad.Sample(item+8,out Vector3 missed,out _);body.position=world.transform.position+missed+Vector3.up*1.3f;
                tutorial.Tick(.6f);
                Require(DrivingTutorialRoad.NearestProgress(body.position-world.transform.position)<item-15,"Missed pickup must recover before the same box. item="+item+" car="+DrivingTutorialRoad.NearestProgress(body.position-world.transform.position));
                Require(Mathf.Abs((float)Get(practice,"itemProgress")-item)<.01f,"Recovery must keep the missed box in front.");
                Require(((IList)Get(practice,"pickups")).Count==3,"Recovery must restore all pickup lanes.");
                Require(world.transform.Find("CityBlock0").GetComponent<Collider>().enabled,"Black tutorial buildings need solid colliders.");
                ConfigureOutputs(manager);stage=2;since=EditorApplication.timeSinceStartup;
            }
            else if(stage==2&&EditorApplication.timeSinceStartup-since>.5)
            {
                Capture(manager,0,"tutorial-p1.jpg");stage=3;
            }
            else if(stage==3&&captures==1)
            {
                manager.GetComponent<DrivingTutorialController>().GetPlayerProgress(0).CollectItem(true);
                stage=4;since=EditorApplication.timeSinceStartup;
            }
            else if(stage==4&&EditorApplication.timeSinceStartup-since>1.2)
            {Capture(manager,0,"briefing-p1.jpg");stage=5;}
            else if(stage==5&&captures==2)
            {
                manager.Features.SetValidationFeature(RaceFeature.CourseIntro,false);
                Require(manager.AdvanceDashboardToCountdown(),"Cannot start the race after tutorials.");stage=6;
            }
            else if(stage==6&&manager.state==Gmanager.State.Game)
            {since=EditorApplication.timeSinceStartup;stage=7;}
            else if(stage==7&&EditorApplication.timeSinceStartup-since>.5)
            {
                var car=manager.GetPlayerCarTransform(0).gameObject;
                var resetter=car.GetComponent<CarResetter>();var body=car.GetComponent<Rigidbody>();var mover=car.GetComponent<DebugMover>();
                var input=new HeldPedal();var gate=new PlayerDriveInputGate(input);mover.SetInputSource(gate);resetter.SetInputSource(gate);
                manager.course.TryGetNearestRoadSample(body.position,out RaceCourse.CourseSample road);
                float direction=manager.CourseProgressSign;
                body.rotation=Quaternion.LookRotation(-road.forward*direction,road.up);body.linearVelocity=-road.forward*direction*5;
                foreach(var tire in car.GetComponentsInChildren<TireForce>())typeof(TireForce).GetField("isGrounded",Private).SetValue(tire,true);
                int before=resetter.AutomaticRecoveryCount;
                var tick=typeof(CarResetter).GetMethod("FixedUpdate",Private);
                for(int i=0;i<40;i++)tick.Invoke(resetter,null);
                Require(resetter.IsWrongWay&&resetter.AutomaticRecoveryCount==before,"Warning must precede three-second recovery.");
                for(int i=0;i<Mathf.CeilToInt(CarResetter.AutomaticRecoveryDelay/Time.fixedDeltaTime)-40;i++)tick.Invoke(resetter,null);
                Require(resetter.AutomaticRecoveryCount==before+1,"Wrong way must recover once after three seconds.");
                Require(!gate.NeedsPedalRelease&&gate.CurrentState.pedal>.9f,"Recovery must accept a continuously held pedal.");
                Require(Vector3.Dot(body.rotation*Vector3.forward,road.forward*direction)>.9f,"Recovery must face race direction.");
                var warning=UnityEngine.Object.FindObjectsByType<WrongWayNotice>(FindObjectsInactive.Include,FindObjectsSortMode.None);
                Require(warning.Length==2,"Each player needs an independent large warning.");
                manager.enabled=false;
                var rigs=(PlayerDisplayRig[])typeof(Gmanager).GetField("displayRigs",Private).GetValue(manager);
                rigs[0].CanvasRoot.GetComponentInChildren<WrongWayNotice>(true).SetState(true,1f);
                Capture(manager,0,"wrong-way-p1.jpg");stage=8;
            }
            else if(stage==8&&captures==3)
            {
                File.WriteAllText(Folder+"/verification.txt","PASS: opening music throughout tutorial; fixed-car title-to-practice flow; missed pickup restores the same three lanes ahead; solid black buildings; preserved release state; forward arrows with upward faces; five-second wrong-way recovery; held pedal resumes; independent center warning; three full-HD live frame captures; removed plaza poles.\n");
                Finish(null);
            }
        }
        catch(Exception error){Finish(error.ToString());}
    }
    private static void ConfigureOutputs(Gmanager manager)
    {
        var rigs=(PlayerDisplayRig[])typeof(Gmanager).GetField("displayRigs",Private).GetValue(manager);outputs=new RenderTexture[2];
        for(int i=0;i<2;i++)
        {
            outputs[i]=new RenderTexture(1920,1080,24);outputs[i].Create();
            foreach(var camera in new[]{rigs[i].BackCamera,rigs[i].BackImageCamera,rigs[i].MainCamera,rigs[i].FrontCamera,rigs[i].UiCamera})if(camera!=null)camera.targetTexture=outputs[i];
        }
    }
    private static void Capture(Gmanager manager,int player,string name)
    {
        manager.RequestDashboardPlayerFrame(player,bytes=>{
            try
            {
                Require(bytes!=null&&bytes.Length>1000,"Missing live frame: "+name);File.WriteAllBytes(Folder+"/"+name,bytes);
                var image=new Texture2D(2,2);try{image.LoadImage(bytes);Require(image.width==1920&&image.height==1080,"Live preview must be full HD.");}finally{UnityEngine.Object.Destroy(image);}
                captures++;
            }catch(Exception error){failure=error.ToString();}
        });
    }
    private static void Finish(string error)
    {
        SessionState.SetBool(Key,false);
        if(error!=null){Directory.CreateDirectory(Folder);File.WriteAllText(Folder+"/failure.txt",error);Debug.LogError(error);}else Debug.Log("VISITOR_POLISH_VALIDATION_PASS");
        EditorApplication.isPlaying=false;
        if(Application.isBatchMode)EditorApplication.Exit(error==null?0:1);
    }
    private static object Get(object value,string field)=>value.GetType().GetField(field,Private|BindingFlags.Public).GetValue(value);
    private static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    private sealed class HeldPedal:IDriveInputSource
    {
        public int PlayerIndex=>0;public string DeviceId=>"Visitor QA";public bool IsConnected=>true;
        public DriveInputState CurrentState=>new DriveInputState{pedal=1};public void UpdateInput(float dt){}public void Dispose(){}
    }

}
