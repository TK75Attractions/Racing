using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class JumpPortalFollowupValidation
{
    public const string Output = "Artifacts/JumpPortalFollowup";
    public static void RunAll()
    {
        RunJump(); RunPortal(); CircuitDrivingRefinementValidation.RunAll();
        Debug.Log("JUMP_PORTAL_FULL_REGRESSION_PASS");
    }
    public static void RunJump()
    {
        CircuitDrivingRefinementValidation.RunJump();
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        var course = UnityEngine.Object.FindFirstObjectByType<RaceCourse>(); course.RebuildCache(); course.RebuildRoad();
        var guidance = course.GetComponentInChildren<RaceJumpGuidance>();
        var renderer = guidance.transform.Find("Gold animated takeoff").GetComponent<MeshRenderer>();
        var mesh = renderer.GetComponent<MeshFilter>().sharedMesh;
        if (renderer.sharedMaterial.shader.name != "Racing/JumpGoldFlow" || mesh.uv.Length != mesh.vertexCount || mesh.uv2.Length != mesh.vertexCount)
            throw new InvalidOperationException("Takeoff flow requires distance and approach coordinates.");
        foreach (var vertex in mesh.vertices)
        {
            var world = renderer.transform.TransformPoint(vertex);
            float progress = course.GetProgressDistance(world);
            // Edge vertices may lie exactly on the takeoff boundary; examine the quad centre below instead.
            if (!float.IsFinite(progress)) throw new InvalidOperationException("Invalid flow position.");
        }
        for (int i = 0; i < mesh.vertexCount; i += 4)
        {
            Vector3 center = Vector3.zero; for (int k=0;k<4;k++) center += renderer.transform.TransformPoint(mesh.vertices[i+k]) * .25f;
            if (!course.HasRoadAtProgress(course.GetProgressDistance(center))) throw new InvalidOperationException("Gold paint spans the air gap.");
        }
        Directory.CreateDirectory(Output);
        var capture = typeof(CourseBarrierValidation).GetMethod("Capture", BindingFlags.Static|BindingFlags.NonPublic);
        var jumps = new List<RaceCourse.JumpSection>(); course.CopyJumpSections(jumps, -1);
        course.TryGetSampleAtProgress(jumps[0].takeoff + 20, out var eye);
        course.TryGetSampleAtProgress(jumps[0].takeoff - 8, out var target);
        guidance.SetDirection(1);
        if (renderer.sharedMaterial.GetFloat("_Direction") != 1) throw new InvalidOperationException("Forward flow not selected.");
        guidance.SetDirection(-1);
        foreach (float time in new[] {0f, .4f})
        {
            renderer.sharedMaterial.SetFloat("_PreviewTime", time);
            capture.Invoke(null, new object[] {eye.position + Vector3.up * 5, target.position + Vector3.up, Output + "/jump-gold-" + (time == 0 ? "a" : "b") + ".png"});
        }
        var first = new Texture2D(2,2); var second = new Texture2D(2,2);
        try
        {
            first.LoadImage(File.ReadAllBytes(Output + "/jump-gold-a.png")); second.LoadImage(File.ReadAllBytes(Output + "/jump-gold-b.png"));
            var a = first.GetPixels32(); var b = second.GetPixels32(); int changed = 0;
            for (int i=0;i<a.Length/2;i++) if (Math.Abs(a[i].r-b[i].r)+Math.Abs(a[i].g-b[i].g)+Math.Abs(a[i].b-b[i].b)>30) changed++;
            if (changed < a.Length/40) throw new InvalidOperationException("Gold arrows did not visibly move between animation phases.");
            Debug.Log("JUMP_FLOW_CHANGED_PIXELS: " + changed);
        }
        finally { UnityEngine.Object.DestroyImmediate(first); UnityEngine.Object.DestroyImmediate(second); }
        renderer.sharedMaterial.SetFloat("_PreviewTime", -1);
        Debug.Log("JUMP_GOLD_FLOW_PASS: production car run-up, no gap coverage/colliders, reverse-race flow, two animation phases captured.");
    }
    public static void RunPortal()
    {
        CircuitVisualRefinementValidation.RunTube();
        CourseBarrierValidation.Run();
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        var course = UnityEngine.Object.FindFirstObjectByType<RaceCourse>(); course.RebuildCache(); course.RebuildRoad(); Physics.SyncTransforms();
        Directory.CreateDirectory(Output);
        var tube = new SerializedObject(course).FindProperty("road.tubeInterior").objectReferenceValue as MeshCollider;
        typeof(CourseBarrierValidation).GetMethod("CaptureViews", BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[] {course,tube,Output});
        typeof(CourseBarrierValidation).GetMethod("Capture", BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,
            new object[] {new Vector3(282,8,573),new Vector3(282,7,535),Output+"/tube-west-portal.png"});
        var report=VerifyPortalFloor(course);
        File.WriteAllText(Output+"/portal-floor.json",JsonUtility.ToJson(report,true));
        Debug.Log("PORTAL_FOLLOWUP_PASS: "+JsonUtility.ToJson(report));
    }
    [Serializable] private sealed class PortalReport
    {
        public int connectedEdges, seamLanes, supportedPavingSamples, curveSegments;
        public float maximumFloorSeamGap, maximumSampledStep, maximumFloorNormalAngle, maximumCurveTurn;
    }
    private static PortalReport VerifyPortalFloor(RaceCourse course)
    {
        var report = new PortalReport();
        var inner=course.transform.Find(RaceCourseRoad.RootName+"/Tube Road Shoulders");
        var innerVertices=inner.GetComponent<MeshFilter>().sharedMesh.vertices; int columns=RaceCourseRoad.ShoulderSegments+1;
        foreach (var wall in course.GetComponentsInChildren<RaceBarrierPath>())
        {
            if (!wall.HasPortalBlend) continue;
            var nodes=new List<Vector3>(); wall.CopyPreviewPathWorld(nodes);
            var visual=wall.GetComponentInChildren<MeshFilter>(); var v=visual.sharedMesh.vertices;
            foreach (bool start in new[] {true,false})
            {
                var profile=typeof(RaceBarrierPath).GetField(start?"portalStart":"portalEnd",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(wall) as TubePortalProfile;
                if (profile==null) continue;
                int row=start?0:nodes.Count-1;
                Vector3 edge=nodes[row], foot=visual.transform.TransformPoint(v[row*33]);
                float best=float.PositiveInfinity; int closest=-1;
                for(int i=0;i<innerVertices.Length;i+=columns)
                {
                    float gap=Vector3.Distance(edge,inner.TransformPoint(innerVertices[i]));
                    if(gap<best){best=gap;closest=i;}
                }
                float seam=Mathf.Max(best,Vector3.Distance(foot,inner.TransformPoint(innerVertices[closest+columns-1])));
                report.maximumFloorSeamGap=Mathf.Max(report.maximumFloorSeamGap,seam);
                if(seam>.002f) throw new InvalidOperationException("Portal floor vertices do not meet: "+wall.name+" "+start+" gap="+seam);
                Vector3 normal=profile.outwardTangents[0].normalized;
                for(int lane=1;lane<10;lane++)
                {
                    Vector3 p=Vector3.Lerp(edge,foot,lane/10f);
                    bool a=Physics.Raycast(p+normal*.025f+Vector3.up*.4f,Vector3.down,out var outside,.8f,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore);
                    bool b=Physics.Raycast(p-normal*.025f+Vector3.up*.4f,Vector3.down,out var inside,.8f,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore);
                    if(!a||!b)throw new InvalidOperationException("Unsupported portal seam: "+wall.name+" "+start+" lane="+lane+" point="+p);
                    float normalAngle=Vector3.Angle(outside.normal,inside.normal);
                    report.maximumFloorNormalAngle=Mathf.Max(report.maximumFloorNormalAngle,normalAngle);
                    if(normalAngle>3f)throw new InvalidOperationException("Abrupt floor grade at portal: "+normalAngle);
                    float step=Mathf.Abs(outside.point.y-inside.point.y);
                    report.maximumSampledStep=Mathf.Max(report.maximumSampledStep,step); report.seamLanes++;
                    if(step>.015f)throw new InvalidOperationException("Step across portal: "+step+" "+outside.collider.name+"/"+inside.collider.name);
                }
                float distance=0;
                for(int j=1;j<nodes.Count-1;j++)
                {
                    int a=start?j-1:nodes.Count-j,b=start?j:nodes.Count-1-j,c=start?j+1:nodes.Count-2-j;
                    distance+=Vector3.Distance(nodes[a],nodes[b]); if(distance>31.5f)break;
                    for(int k=0;k<33;k++)
                    {
                        Vector3 before=visual.transform.TransformPoint(v[a*33+k]), here=visual.transform.TransformPoint(v[b*33+k]), after=visual.transform.TransformPoint(v[c*33+k]);
                        float turn=Vector3.Angle(here-before,after-here);
                        report.maximumCurveTurn=Mathf.Max(report.maximumCurveTurn,turn); report.curveSegments++;
                        if(Vector3.Dot(here-before,normal)<-.001f || turn>7f)throw new InvalidOperationException("Portal curve folds or kinks: "+wall.name+" section="+k+" turn="+turn);
                    }
                }
                report.connectedEdges++;
            }
        }
        foreach(var floor in course.GetComponentsInChildren<MeshCollider>())
        {
            bool portalPaving=floor.name.StartsWith("Portal Road Shoulder ");
            if(!portalPaving && floor.name!="Tube Road Shoulders")continue;
            var v=floor.sharedMesh.vertices; var triangles=floor.sharedMesh.triangles;
            for(int i=0;i<triangles.Length;i+=3)
                if(Vector3.Cross(v[triangles[i+1]]-v[triangles[i]],v[triangles[i+2]]-v[triangles[i]]).y < -.000001f)
                    throw new InvalidOperationException("Paving collision faces downward: "+floor.name+" triangle="+i/3);
            if(!portalPaving)continue;
            for(int row=1;row<v.Length/columns;row++)
            {
                var a=floor.transform.TransformPoint(v[(row-1)*columns]); var b=floor.transform.TransformPoint(v[row*columns]);
                var c=floor.transform.TransformPoint(v[(row-1)*columns+columns-1]); var d=floor.transform.TransformPoint(v[row*columns+columns-1]);
                if(Vector3.Distance(a,c)<.3f||Vector3.Distance(b,d)<.3f)continue;
                foreach(float width in new[]{.15f,.35f,.5f,.65f,.85f})
                {
                    Vector3 p=Vector3.Lerp((a+b)*.5f,(c+d)*.5f,width);
                    if(!floor.Raycast(new Ray(p+Vector3.up,Vector3.down),out _,2))throw new InvalidOperationException("Missing paving away from road centre: "+p);
                    report.supportedPavingSamples++;
                }
            }
        }
        if(report.connectedEdges!=4||report.seamLanes!=36||report.supportedPavingSamples<300)throw new InvalidOperationException("Incomplete portal coverage: "+JsonUtility.ToJson(report));
        return report;
    }
}
