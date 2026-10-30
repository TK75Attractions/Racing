using System;
using System.IO;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object=UnityEngine.Object;

/// <summary>Checks the production practice rails from the road side and captures the complete surface.</summary>
public static class PracticeRoadValidation
{
    public static void Run()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);
        var root=new GameObject("Practice road QA");
        var asphalt=Material(new Color(.12f,.15f,.20f));var cyan=Material(new Color(.02f,.75f,.95f));
        var pink=Material(new Color(.95f,.18f,.48f));var white=Material(new Color(.85f,.88f,.90f));
        try
        {
            var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);floor.name="Safety floor";floor.transform.SetParent(root.transform);
            floor.transform.position=new Vector3(38,-.5f,0);floor.transform.localScale=new Vector3(180,1,172);
            floor.GetComponent<MeshRenderer>().sharedMaterial=asphalt;
            DrivingTutorialRoad.Build(root.transform,asphalt,cyan,pink,white);Physics.SyncTransforms();
            var road=root.GetComponentInChildren<DrivingTutorialRoad>();
            Require(road.transform.Find("Asphalt").GetComponent<Collider>()==null,"Keep the single continuous drivable floor.");
            int checkedEdges=0;
            for(float distance=1f;distance<=195f;distance+=.5f)
            {
                DrivingTutorialRoad.Sample(distance,out var center,out var tangent);
                var right=Vector3.Cross(Vector3.up,tangent);
                foreach(int sign in new[]{-1,1})
                {
                    var outward=right*sign;
                    var rail=road.transform.Find(sign<0?"Cyan barrier":"Pink barrier").GetComponent<MeshCollider>();
                    Require(Physics.Raycast(center+outward*(DrivingTutorialRoad.Width*.5f-.5f)+Vector3.up*.45f,outward,out var inner,1f)&&inner.collider==rail&&Vector3.Dot(inner.normal,-outward)>.995f,"Road-facing collision/normal missing at "+distance+" / "+sign);
                    Require(Physics.Raycast(center+outward*(DrivingTutorialRoad.Width*.5f+.2f)+Vector3.up*2f,Vector3.down,out var top,3f)&&top.collider==rail&&top.normal.y>.999f&&Mathf.Abs(top.point.y-.9f)<.001f,"Upward rail top missing at "+distance+" / "+sign);
                    Require(Physics.Raycast(center+outward*(DrivingTutorialRoad.Width*.5f-.02f)+Vector3.up*.2f,Vector3.down,out var ground,1f)&&ground.collider==floor.GetComponent<Collider>()&&Mathf.Abs(ground.point.y)<.001f,"A wheel must have a flush floor beside the rail.");
                    checkedEdges++;
                }
            }
            var surface=road.transform.Find("Asphalt").GetComponent<MeshFilter>().sharedMesh.vertices;
            var left=road.transform.Find("Cyan barrier").GetComponent<MeshFilter>().sharedMesh.vertices;
            var rightRail=road.transform.Find("Pink barrier").GetComponent<MeshFilter>().sharedMesh.vertices;
            for(int i=0;i<surface.Length/2;i++)
            {
                Require(Mathf.Abs(Vector3.ProjectOnPlane(surface[i*2]-left[i*4],Vector3.up).magnitude-.08f)<.001f,"Asphalt must overlap beneath the left rail.");
                Require(Mathf.Abs(Vector3.ProjectOnPlane(surface[i*2+1]-rightRail[i*4],Vector3.up).magnitude-.08f)<.001f,"Asphalt must overlap beneath the right rail.");
            }
            Directory.CreateDirectory("Artifacts/TutorialOverviewBoost");
            Capture("practice-straight",new Vector3(0,7,-55),new Vector3(0,.4f,-29));
            DrivingTutorialRoad.Sample(92,out var turn,out var forward);
            Capture("practice-turn",turn-forward*19+Vector3.up*13,turn+forward*10);
            string result="PASS: "+checkedEdges+" sampled rail edges on straights, turn and exit; inward collision normals, visible upward tops, flush floor, 8cm asphalt overlap; one floor collider.\n";
            File.WriteAllText("Artifacts/TutorialOverviewBoost/road.txt",result);Debug.Log("PRACTICE_ROAD_PASS: "+result);
        }
        finally
        {
            Object.DestroyImmediate(root);Object.DestroyImmediate(asphalt);Object.DestroyImmediate(cyan);Object.DestroyImmediate(pink);Object.DestroyImmediate(white);
        }
    }
    private static Material Material(Color color){var material=new Material(Shader.Find("Unlit/Color"));material.color=color;return material;}
    private static void Capture(string name,Vector3 eye,Vector3 target)
    {
        var pipeline=GraphicsSettings.defaultRenderPipeline;var quality=QualitySettings.renderPipeline;
        var host=new GameObject("Practice capture",typeof(Camera));var camera=host.GetComponent<Camera>();
        var output=new RenderTexture(1280,720,24);Texture2D image=null;var previous=RenderTexture.active;
        try
        {
            GraphicsSettings.defaultRenderPipeline=null;QualitySettings.renderPipeline=null;
            camera.transform.SetPositionAndRotation(eye,Quaternion.LookRotation(target-eye));camera.fieldOfView=65;
            camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.04f,.06f,.09f);
            camera.targetTexture=output;camera.Render();RenderTexture.active=output;
            image=new Texture2D(1280,720,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,1280,720),0,0);image.Apply();
            File.WriteAllBytes("Artifacts/TutorialOverviewBoost/"+name+".png",image.EncodeToPNG());
        }
        finally
        {
            camera.targetTexture=null;RenderTexture.active=previous;GraphicsSettings.defaultRenderPipeline=pipeline;QualitySettings.renderPipeline=quality;
            Object.DestroyImmediate(host);Object.DestroyImmediate(output);if(image!=null)Object.DestroyImmediate(image);
        }
    }
    private static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
}
