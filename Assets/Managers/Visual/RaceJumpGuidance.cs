using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>High-contrast, flush ramp outlines. Never adds collision or covers the air gap.</summary>
public sealed class RaceJumpGuidance : MonoBehaviour
{
    private Material amber, ink;
    public static void Build(Transform owner, Transform root, List<Vector3> left, List<Vector3> right,
        List<float> distances, List<bool> road, bool closed)
    {
        bool any = false; foreach (bool solid in road) if (!solid) { any = true; break; }
        if (!any) return;
        var host = new GameObject("Jump Guidance") { hideFlags = HideFlags.DontSave };
        host.transform.SetParent(root, false);
        var guidance = host.AddComponent<RaceJumpGuidance>();
        guidance.amber = Material("Jump amber", new Color(1f, .66f, .055f));
        guidance.ink = Material("Jump outline contrast", new Color(.025f, .035f, .05f));
        var backing = new List<Vector3>(); var bright = new List<Vector3>();
        for (int i = 0; i < road.Count; i++)
        {
            if (!road[i]) continue;
            bool near = false;
            for (int j = i - 30; j <= i + 30; j++)
            {
                int index = closed ? (j % road.Count + road.Count) % road.Count : Mathf.Clamp(j, 0, road.Count - 1);
                float delta = Mathf.Abs(distances[index] - distances[i]);
                if (closed) delta = Mathf.Min(delta, distances[distances.Count - 1] - delta);
                if (!road[index] && delta <= 24f) { near = true; break; }
            }
            if (!near) continue;
            foreach (int side in new[] { -1, 1 })
            {
                Vector3 a = side < 0 ? left[i] : right[i], b = side < 0 ? left[i + 1] : right[i + 1];
                Vector3 inwardA = (right[i] - left[i]).normalized * -side;
                Vector3 inwardB = (right[i + 1] - left[i + 1]).normalized * -side;
                Quad(backing, a, b, b + inwardB * 1.55f, a + inwardA * 1.55f, .025f);
                Quad(bright, a + inwardA * .25f, b + inwardB * .25f, b + inwardB * 1.15f, a + inwardA * 1.15f, .03f);
            }
            bool beforeGap = i + 1 < road.Count ? !road[i + 1] : closed && !road[0];
            bool afterGap = i > 0 ? !road[i - 1] : closed && !road[road.Count - 1];
            if (beforeGap || afterGap)
            {
                int edge = beforeGap ? i + 1 : i;
                Vector3 a = left[edge], b = right[edge];
                Vector3 towardRoad = ((left[beforeGap ? i : i + 1] + right[beforeGap ? i : i + 1]) * .5f - (a + b) * .5f).normalized;
                Quad(backing, a, b, b + towardRoad * 1.35f, a + towardRoad * 1.35f, .025f);
                Quad(bright, a + towardRoad * .2f, b + towardRoad * .2f, b + towardRoad * .95f, a + towardRoad * .95f, .03f);
            }
        }
        guidance.Mesh(owner, host.transform, "Dark outline", backing, guidance.ink);
        guidance.Mesh(owner, host.transform, "Amber ramp edges", bright, guidance.amber);
    }
    private static void Quad(List<Vector3> output, Vector3 a, Vector3 b, Vector3 c, Vector3 d, float offset)
    {
        if (Vector3.Cross(b - a, c - a).y < 0) { Vector3 swap = b; b = d; d = swap; }
        foreach (Vector3 v in new[] { a, b, c, d }) output.Add(v + Vector3.up * offset);
    }
    private void Mesh(Transform owner, Transform root, string name, List<Vector3> vertices, Material material)
    {
        var local = new Vector3[vertices.Count]; var triangles = new int[vertices.Count / 4 * 6];
        for (int i = 0; i < local.Length; i++) local[i] = owner.InverseTransformPoint(vertices[i]);
        for (int i = 0; i < vertices.Count / 4; i++)
        { int v = i * 4, t = i * 6; triangles[t] = v; triangles[t + 1] = v + 1; triangles[t + 2] = v + 2; triangles[t + 3] = v; triangles[t + 4] = v + 2; triangles[t + 5] = v + 3; }
        var mesh = new Mesh { name = name, hideFlags = HideFlags.DontSave, vertices = local, triangles = triangles };
        mesh.RecalculateNormals(); mesh.RecalculateBounds();
        var part = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer)) { hideFlags = HideFlags.DontSave };
        part.transform.SetParent(root, false); part.GetComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = part.GetComponent<MeshRenderer>(); renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
    }
    private static Material Material(string name, Color color)
    {
        var material = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = name, hideFlags = HideFlags.DontSave };
        material.SetColor("_BaseColor", color); return material;
    }
    private void OnDestroy()
    {
        if (Application.isPlaying) { Destroy(amber); Destroy(ink); }
        else { DestroyImmediate(amber); DestroyImmediate(ink); }
    }
}
