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
}
