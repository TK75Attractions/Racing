#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

[DefaultExecutionOrder(9991)]
public sealed class CampusAtmospherePlaytestProbe : MonoBehaviour
{
    [Serializable] private sealed class Report
    {
        public bool passed; public string error, mode;
        public int replacedTreeVertices, generatedVertices, trees, houses;
        public float originalShadowDistance, introShadowDistance, restoredShadowDistance;
        public int originalShadowResolution, introShadowResolution, restoredShadowResolution;
        public bool liveOffOn, rainNightDay, disableEnable, collidersUnchanged;
    }
    private readonly Report report=new Report();
    private readonly Dictionary<Collider,(bool enabled,Mesh mesh)> courseColliders=new Dictionary<Collider,(bool,Mesh)>();
    private UniversalRenderPipelineAsset pipeline;
    private bool seen,done;private int stage;
    private RaceEnvironmentController.Weather weather;
    private RaceEnvironmentController.TimeOfDay timeOfDay;
    private string Folder=>Environment.GetEnvironmentVariable("RACING_BALANCE_OUTPUT")??"Logs/CampusAtmosphere";
    private void Awake()
    {
        report.mode=Environment.GetEnvironmentVariable("RACING_CAMPUS_ATMOSPHERE_MODE")??"normal";
        pipeline=GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
        report.originalShadowDistance=pipeline.shadowDistance;report.originalShadowResolution=pipeline.mainLightShadowmapResolution;

    }
    private void LateUpdate()
    {
        if(done)return;
        try {Observe();}
        catch(Exception error){report.error=error.ToString();Save();done=true;Debug.LogException(error);if(Application.isBatchMode)UnityEditor.EditorApplication.Exit(1);}
    }
    private void Observe()
    {
        var manager=Gmanager.Control;if(manager==null)return;
        var campus=manager.GetComponent<CampusAtmosphereController>();if(campus==null)return;
        if(manager.IsCourseIntroPlaying)
        {
            if(!seen)
            {
                seen=true;
                // Terrain coexistence meshes finish their normal startup in Start.
                // Compare live switches against the ready grid, not pre-Start import meshes.
                foreach(var c in GameObject.Find("1016こうしゃ").GetComponentsInChildren<Collider>())
                    if(!c.name.StartsWith("tree",StringComparison.OrdinalIgnoreCase))courseColliders.Add(c,(c.enabled,(c as MeshCollider)?.sharedMesh));
                report.replacedTreeVertices=campus.ReplacedTreeVertices;report.generatedVertices=campus.GeneratedVertices;report.trees=campus.TreeCount;report.houses=campus.HouseCount;
                weather=RaceEnvironmentController.Active.CurrentWeather;timeOfDay=RaceEnvironmentController.Active.CurrentTimeOfDay;
            }
            if(stage==0)
            {
                Require(campus.IsApplied&&campus.IntroShadowQualityActive,"The styled campus and aerial shadow quality must be active.");
                report.introShadowDistance=pipeline.shadowDistance;report.introShadowResolution=pipeline.mainLightShadowmapResolution;
                Require(report.introShadowDistance>=650&&report.introShadowResolution>=4096,"Aerial shots need campus-wide shadows.");
                Require(report.generatedVertices<150000&&report.generatedVertices<report.replacedTreeVertices*.1f,"The replacement trees and town must remain a small shared mesh batch.");
                Require(GameObject.Find("Campus atmosphere scenery").GetComponentsInChildren<Collider>().Length==0,"Decorations must not add invisible collision walls.");
            }
            float elapsed=manager.GetComponent<CourseIntroDirector>().ElapsedSeconds;
            if(report.mode=="checks")
            {
                if(stage==0&&elapsed>1.5f)
                {
                    manager.Features.SetFeature(RaceFeature.CampusAtmosphere,false);
                    Require(!campus.IsApplied&&Mathf.Abs(pipeline.shadowDistance-report.originalShadowDistance)<.01f,"Live OFF must restore the road-level shadow distance.");
                    Require(GameObject.Find("Campus atmosphere scenery")==null,"OFF must hide generated scenery.");
                    stage=1;
                }
                else if(stage==1)
                {
                    manager.Features.SetFeature(RaceFeature.CampusAtmosphere,true);
                    Require(campus.IsApplied&&campus.IntroShadowQualityActive,"Live ON must restore the new look.");report.liveOffOn=true;stage=2;
                }
                else if(stage==2&&elapsed>3)
                {RaceEnvironmentController.Active.SetSituation(RaceEnvironmentController.Weather.Rainy,RaceEnvironmentController.TimeOfDay.Night);stage=3;}
                else if(stage==3&&elapsed>3.3f)
                {
                    Require(RenderSettings.ambientMode==AmbientMode.Trilight&&RaceEnvironmentController.Active.CurrentWeather==RaceEnvironmentController.Weather.Rainy,"Weather changes must retain the styled lighting.");
                    bool glow=false;foreach(var m in GameObject.Find("Campus atmosphere scenery").GetComponent<Renderer>().sharedMaterials)
                        if(m.HasProperty("_EmissionColor")&&m.GetColor("_EmissionColor").maxColorComponent>1)glow=true;
                    Require(glow,"Night windows must light up.");
                    RaceEnvironmentController.Active.SetSituation(weather,timeOfDay);report.rainNightDay=true;stage=4;
                }
                else if(stage==4&&elapsed>4.5f)
                {
                    campus.enabled=false;Require(!campus.IsApplied&&!campus.IntroShadowQualityActive,"Disabling must restore scenery and shadow settings.");
                    campus.enabled=true;Require(campus.IsApplied&&campus.IntroShadowQualityActive,"Re-enabling must rebuild no duplicate scenery.");
                    report.disableEnable=true;stage=5;
                }
            }
        }
        else if(seen&&manager.state==Gmanager.State.Game)
        {
            report.restoredShadowDistance=pipeline.shadowDistance;report.restoredShadowResolution=pipeline.mainLightShadowmapResolution;
            Require(!campus.IntroShadowQualityActive&&Mathf.Abs(pipeline.shadowDistance-report.originalShadowDistance)<.01f&&pipeline.mainLightShadowmapResolution==report.originalShadowResolution,"Aerial quality must restore the gameplay pipeline settings.");
            foreach(var pair in courseColliders)Require(pair.Key.enabled==pair.Value.enabled&&((pair.Key as MeshCollider)?.sharedMesh)==pair.Value.mesh,"School/road collision geometry must not change: "+pair.Key.name+" was "+pair.Value.mesh?.name+" now "+(pair.Key as MeshCollider)?.sharedMesh?.name);
            report.collidersUnchanged=true;
            if(report.mode=="checks")Require(report.liveOffOn&&report.rainNightDay&&report.disableEnable,"All live atmosphere checks must run.");
            report.passed=true;done=true;Save();Debug.Log("CAMPUS_ATMOSPHERE_PLAYTEST_PASS: "+report.mode+", batched scenery, aerial shadow range, collider preservation, gameplay shadow restore.");
        }
    }
    private void Save(){Directory.CreateDirectory(Folder);File.WriteAllText(Path.Combine(Folder,"campus-checks.json"),JsonUtility.ToJson(report,true));}
    private static void Require(bool ok,string reason){if(!ok)throw new InvalidOperationException(reason);}
}
#endif
