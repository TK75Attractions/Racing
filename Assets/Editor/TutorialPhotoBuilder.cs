using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object=UnityEngine.Object;

/// <summary>Photographs the real course, car, pickups and effect assets. No drawn stand-ins.</summary>
public static class TutorialPhotoBuilder
{
    private const string Output="Assets/Resources/UI/TutorialPhotos";
    private const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    private static readonly List<GameObject> temporary=new List<GameObject>();
    private static GameObject prefab;
    private static RaceCourse course;
    public static void UpdateAcceleration()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        course=Object.FindFirstObjectByType<RaceCourse>();prefab=Object.FindFirstObjectByType<Gmanager>().carPrefab;
        course.RebuildCache();course.RebuildRoad();Physics.SyncTransforms();
        try
        {
            var light=New("Photography fill");var lamp=light.AddComponent<Light>();lamp.type=LightType.Directional;lamp.intensity=1.2f;light.transform.rotation=Quaternion.Euler(40,-25,0);
            var layout=course.GetComponent<TubeChargeLineLayout>()??course.gameObject.AddComponent<TubeChargeLineLayout>();layout.Configure(course,-1f);
            Require(layout.Available&&layout.Pads.Count==2,"Need two real tube pads.");
            course.transform.Find("Tube acceleration pads").gameObject.SetActive(true);
            var pad=layout.Pads[0];var forward=pad.transform.forward;var up=pad.transform.up;
            Car(pad.transform.position-forward*3.5f+up*.65f,Quaternion.LookRotation(forward,up));
            Capture("Acceleration",pad.transform.position-forward*12f+up*6f,pad.transform.position+forward*3f+up*.65f);
            AssetDatabase.ImportAsset(Output+"/Acceleration.png",ImportAssetOptions.ForceSynchronousImport);
        }
        finally {foreach(var item in temporary)if(item!=null)Object.DestroyImmediate(item);temporary.Clear();}
    }
    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        course=Object.FindFirstObjectByType<RaceCourse>();prefab=Object.FindFirstObjectByType<Gmanager>().carPrefab;
        course.RebuildCache();course.RebuildRoad();Physics.SyncTransforms();Directory.CreateDirectory(Output);
        try
        {
            var light=New("Photography fill");var lamp=light.AddComponent<Light>();lamp.type=LightType.Directional;lamp.intensity=1.2f;light.transform.rotation=Quaternion.Euler(40,-25,0);
            var layout=course.GetComponent<TubeChargeLineLayout>()??course.gameObject.AddComponent<TubeChargeLineLayout>();layout.Configure(course,-1f);
            Require(layout.Available&&layout.Pads.Count==2,"Tube must have two safe acceleration pads");
            course.transform.Find("Tube acceleration pads").gameObject.SetActive(true);
            var pad=layout.Pads[0];var forward=pad.transform.forward;var up=pad.transform.up;
            var car=Car(pad.transform.position-forward*3.5f+up*.65f,Quaternion.LookRotation(forward,up));
            Capture("Acceleration",pad.transform.position-forward*12f+up*6f,pad.transform.position+forward*3f+up*.65f);
            car.SetActive(false);
            var jumps=new List<RaceCourse.JumpSection>();course.CopyJumpSections(jumps,-1f);Require(jumps.Count>0,"A real jump is required");
            course.TryGetSampleAtProgress(jumps[0].takeoff+7f,out var ramp);
            var guidance=course.GetComponentInChildren<RaceJumpGuidance>();guidance.SetDirection(-1f);
            car=Car(ramp.position+Vector3.up*.6f,Quaternion.LookRotation(-ramp.forward,Vector3.up));
            Capture("Jump",ramp.position+ramp.forward*16f+Vector3.up*7f,ramp.position-ramp.forward*9f+Vector3.up);
            car.SetActive(false);
            RaceCourse.CourseSample here=default;
            for(float raw=40;raw<course.TotalLength-40;raw+=5f)
            { course.TryGetSampleAtProgress(raw,out here);if(!course.IsPointInsideTube(here.position+Vector3.up)&&!course.IsJumpAhead(here.position,-1,60f))break; }
            Vector3 f=-here.forward;
            car=Car(here.position+Vector3.up*.65f,Quaternion.AngleAxis(25f,Vector3.up)*Quaternion.LookRotation(f,Vector3.up));
            var sparks=car.GetComponent<DriftChargeVisual>()??car.AddComponent<DriftChargeVisual>();Invoke(sparks,"Awake");
            typeof(DriftChargeVisual).GetMethod("EmitBurst",Private).Invoke(sparks,new object[]{100,new Color(1,.65f,.1f),1.2f});
            foreach(var particle in car.GetComponentsInChildren<ParticleSystem>())particle.Simulate(.12f,true,false,true);
            Capture("Drift",here.position-f*7f+here.right*6f+Vector3.up*4f,here.position+Vector3.up*.8f);
            car.SetActive(false);
            car=Car(here.position+Vector3.up*.65f,Quaternion.LookRotation(f,Vector3.up));
            var front=Car(here.position+f*8f+Vector3.up*.65f,Quaternion.LookRotation(f,Vector3.up));front.GetComponent<PlayerCarPaint>()?.SetPlayerIndex(1);
            Capture("Slipstream",here.position-f*11f+here.right*3f+Vector3.up*4.5f,here.position+f*4f+Vector3.up*.6f);
            front.SetActive(false);
            var box=New("Photographed mystery box");box.transform.position=here.position+f*3f;box.AddComponent<ItemBox>();
            car.SetActive(false);Capture("MysteryBox",box.transform.position-f*4f+here.right*2.2f+Vector3.up*2.2f,box.transform.position+Vector3.up*1.1f);
            car.SetActive(true);car.transform.position=here.position+f*1.7f+Vector3.up*.65f;
            Capture("Pickup",here.position-f*5f+here.right*4f+Vector3.up*3.5f,box.transform.position+Vector3.up*.7f);
            box.SetActive(false);
            Capture("PickupAfter",here.position-f*5f+here.right*4f+Vector3.up*3.5f,box.transform.position+Vector3.up*.7f);
            car.transform.position=here.position+Vector3.up*.65f;
            var effects=car.GetComponent<CarItemEffects>()??car.AddComponent<CarItemEffects>();Invoke(effects,"Awake");
            effects.UseItem(RaceItemType.Shield,null);Invoke(effects,"UpdateShieldVisual");
            Capture("Shield",here.position-f*6f+here.right*5f+Vector3.up*3.2f,here.position+Vector3.up*.8f);effects.ClearAll();
            effects.UseItem(RaceItemType.Rocket,null);
            Capture("Rocket",here.position-f*6f+here.right*4f+Vector3.up*2.8f,here.position+Vector3.up*.7f,effects,RaceItemType.Rocket);effects.ClearAll();
            var oil=OilSlick.Spawn(here.position-f*4.5f,Vector3.up,-f,effects);temporary.Add(oil.gameObject);
            Invoke(oil,"Awake");typeof(OilSlick).GetField("age",Private).SetValue(oil,.5f);Invoke(oil,"Update");
            Require(oil.GetComponentInChildren<MeshRenderer>()!=null,"The oil photograph needs the production puddle mesh");
            Capture("Oil",here.position-f*9f+here.right*3f+Vector3.up*5f,here.position-f*2f+Vector3.up*.3f);oil.gameObject.SetActive(false);
            // Capture the actual reversed-steering HUD below in the UI capture pass; this image is its real car backdrop.
            effects.ApplyConfuse();Capture("Confuse",here.position-f*6f+here.right*4f+Vector3.up*2.8f,here.position+Vector3.up*.7f,effects,RaceItemType.Confuse);effects.ClearAll();
            car.SetActive(false);
            var crystal=New("Photographed charge crystal");crystal.transform.position=here.position;crystal.AddComponent<ChargeCrystal>();
            Capture("Crystal",here.position-f*4f+here.right*2f+Vector3.up*2f,here.position+Vector3.up*1.1f);
            Debug.Log("TUTORIAL_PHOTOS_PASS: actual jump, installed tube pad, car with drift sparks, two cars, real box/pickup/shield/oil/crystal.");
        }
        finally{foreach(var item in temporary)if(item!=null)Object.DestroyImmediate(item);temporary.Clear();}
        AssetDatabase.Refresh();
        foreach(string file in Directory.GetFiles(Output,"*.png"))
        {
            var importer=(TextureImporter)AssetImporter.GetAtPath(file);importer.mipmapEnabled=false;importer.npotScale=TextureImporterNPOTScale.None;
            importer.textureCompression=TextureImporterCompression.Uncompressed;importer.maxTextureSize=1024;importer.SaveAndReimport();
        }
    }
    private static GameObject New(string name){var host=new GameObject(name);temporary.Add(host);return host;}
    private static GameObject Car(Vector3 position,Quaternion rotation)
    {
        var car=Object.Instantiate(prefab,position,rotation);temporary.Add(car);
        var paint=car.GetComponent<PlayerCarPaint>()??car.AddComponent<PlayerCarPaint>();paint.SetPlayerIndex(0);
        var body=car.GetComponent<Rigidbody>();if(body!=null)body.isKinematic=true;
        return car;
    }
    private static void Capture(string name,Vector3 eye,Vector3 target,CarItemEffects effects=null,RaceItemType item=RaceItemType.None)
    {
        foreach(var arrow in Object.FindObjectsByType<RotatingBoostArrows>(FindObjectsSortMode.None))arrow.SetPhase(.4f);
        foreach(var arrow in Object.FindObjectsByType<ForwardBoostArrows>(FindObjectsSortMode.None))arrow.SetPhase(.4f);
        var host=new GameObject("Tutorial photograph camera");var camera=host.AddComponent<Camera>();camera.enabled=false;
        camera.transform.SetPositionAndRotation(eye,Quaternion.LookRotation(target-eye));camera.nearClipPlane=.1f;camera.farClipPlane=1200;camera.fieldOfView=55f;
        GameObject overlay=null;
        if(effects!=null)
        {
            overlay=new GameObject("Actual item HUD",typeof(RectTransform),typeof(Canvas));
            var canvas=overlay.GetComponent<Canvas>();canvas.renderMode=RenderMode.WorldSpace;canvas.worldCamera=camera;
            var rect=(RectTransform)overlay.transform;rect.sizeDelta=new Vector2(1920,1080);rect.position=eye+camera.transform.forward*2f;rect.rotation=camera.transform.rotation;
            rect.localScale=Vector3.one*(4f*Mathf.Tan(camera.fieldOfView*.5f*Mathf.Deg2Rad)/1080f);
            var hud=PlayerItemHUD.Create(overlay.transform);hud.Bind(effects);
            string method=item==RaceItemType.Confuse?"HandleAfflicted":"HandleItemUsed";
            typeof(PlayerItemHUD).GetMethod(method,Private).Invoke(hud,new object[]{item});
            typeof(PlayerItemHUD).GetMethod("UpdateToast",Private).Invoke(hud,new object[]{.2f});
            if(item==RaceItemType.Confuse)typeof(PlayerItemHUD).GetMethod("UpdateConfuse",Private).Invoke(hud,null);
            Canvas.ForceUpdateCanvases();
        }
        var output=new RenderTexture(768,432,24);Texture2D image=null;var previous=RenderTexture.active;
        try
        {
            camera.targetTexture=output;camera.Render();image=new Texture2D(768,432,TextureFormat.RGB24,false);RenderTexture.active=output;
            image.ReadPixels(new Rect(0,0,768,432),0,0);image.Apply();File.WriteAllBytes(Output+"/"+name+".png",image.EncodeToPNG());
        }
        finally{if(overlay!=null)Object.DestroyImmediate(overlay);camera.targetTexture=null;RenderTexture.active=previous;Object.DestroyImmediate(host);Object.DestroyImmediate(output);if(image!=null)Object.DestroyImmediate(image);}
    }
    private static void Invoke(object target,string name)=>target.GetType().GetMethod(name,Private).Invoke(target,null);
    private static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
}
