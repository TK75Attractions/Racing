using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Flush ramp edges and a gold, flowing takeoff surface. No collision or paint over the air gap.</summary>
public sealed class RaceJumpGuidance : MonoBehaviour
{
    private Material amber, ink, gold;
    private RaceCourse course;
    private float direction;
    private readonly List<GameObject> forwardArrows=new List<GameObject>(),reverseArrows=new List<GameObject>();
    public void SetDirection(float sign)
    {
        direction = sign < 0 ? -1 : 1;
        if (gold != null) gold.SetFloat("_Direction", direction);
        foreach(var part in forwardArrows)part.SetActive(direction>0);
        foreach(var part in reverseArrows)part.SetActive(direction<0);
    }
    private void Update()
    {
        var race = Gmanager.Control;
        if (race != null && race.course == course && race.CourseProgressSign != direction)
            SetDirection(race.CourseProgressSign);
    }
    public static void Build(Transform owner, Transform root, List<Vector3> left, List<Vector3> right,
        List<float> distances, List<bool> road, bool closed)
    {
        int segmentCount = Mathf.Min(road.Count, left.Count - 1);
        bool any = false; foreach (bool solid in road) if (!solid) { any = true; break; }
        if (!any) return;
        var host = new GameObject("Jump Guidance") { hideFlags = HideFlags.DontSave };
        host.transform.SetParent(root, false);
        var guidance = host.AddComponent<RaceJumpGuidance>();
        guidance.amber = Material("Jump amber", new Color(1f, .66f, .055f));
        guidance.ink = Material("Jump outline contrast", new Color(.025f, .035f, .05f));
        guidance.course = owner.GetComponent<RaceCourse>();
        guidance.gold = new Material(Shader.Find("Racing/JumpGoldFlow")) { name = "Gold takeoff flow", hideFlags = HideFlags.DontSave };
        float sign = 1; CheckpointSensor start = null;
        foreach (var checkpoint in Object.FindObjectsByType<CheckpointSensor>(FindObjectsSortMode.None))
            if (start == null || checkpoint.CheckpointIndex < start.CheckpointIndex) start = checkpoint;
        if (start != null && guidance.course != null && guidance.course.TryGetNearestCenterLineDirection(start.transform.position, out var forward))
            sign = Vector3.Dot(start.transform.forward, forward) < 0 ? -1 : 1;
        guidance.SetDirection(sign);
        var backing = new List<Vector3>(); var bright = new List<Vector3>();
        var gilded = new List<Vector3>(); var flowUV = new List<Vector2>(); var approachUV = new List<Vector2>();
        for (int i = 0; i < segmentCount; i++)
        {
            if (!road[i]) continue;
            bool near = false;
            for (int j = i - 30; j <= i + 30; j++)
            {
                int index = closed ? (j % segmentCount + segmentCount) % segmentCount : Mathf.Clamp(j, 0, segmentCount - 1);
                float delta = Mathf.Abs(distances[index] - distances[i]);
                if (closed) delta = Mathf.Min(delta, distances[distances.Count - 1] - delta);
                if (!road[index] && delta <= 24f) { near = true; break; }
            }
            if (!near) continue;
            // Paint each solid approach only. The shader selects the active race direction,
            // including reverse races, so landing arrows never invite a turn into the gap.
            foreach (int approach in new[] { -1, 1 })
            {
                float runUp = 0; int cursor = i; bool found = false;
                for (int step = 0; step < segmentCount && runUp <= 18f; step++)
                {
                    if (!road[cursor]) { found = true; break; }
                    runUp += distances[cursor + 1] - distances[cursor];
                    cursor += approach;
                    if (!closed && (cursor < 0 || cursor >= segmentCount)) break;
                    cursor = (cursor + segmentCount) % segmentCount;
                }
                if (!found) continue;
                Vector3[] corners = { left[i], right[i], right[i + 1], left[i + 1] };
                Vector2[] coords = { new Vector2(-1, distances[i]), new Vector2(1, distances[i]),
                    new Vector2(1, distances[i + 1]), new Vector2(-1, distances[i + 1]) };
                int[] order = Vector3.Cross(corners[1]-corners[0], corners[2]-corners[0]).y < 0 ? new[] {0,3,2,1} : new[] {0,1,2,3};
                foreach (int k in order) { gilded.Add(corners[k] + Vector3.up * .045f); flowUV.Add(coords[k]); approachUV.Add(new Vector2(approach, runUp)); }
            }
            foreach (int side in new[] { -1, 1 })
            {
                Vector3 a = side < 0 ? left[i] : right[i], b = side < 0 ? left[i + 1] : right[i + 1];
                Vector3 inwardA = (right[i] - left[i]).normalized * -side;
                Vector3 inwardB = (right[i + 1] - left[i + 1]).normalized * -side;
                Quad(backing, a, b, b + inwardB * 1.55f, a + inwardA * 1.55f, .025f);
                Quad(bright, a + inwardA * .25f, b + inwardB * .25f, b + inwardB * 1.15f, a + inwardA * 1.15f, .03f);
            }
            bool beforeGap = i + 1 < segmentCount ? !road[i + 1] : closed && !road[0];
            bool afterGap = i > 0 ? !road[i - 1] : closed && !road[segmentCount - 1];
            if (beforeGap || afterGap)
            {
                int edge = beforeGap ? i + 1 : i;
                Vector3 a = left[edge], b = right[edge];
                Vector3 towardRoad = ((left[beforeGap ? i : i + 1] + right[beforeGap ? i : i + 1]) * .5f - (a + b) * .5f).normalized;
                var ring=new GameObject("Rotating takeoff arrows");ring.transform.SetParent(host.transform,false);
                ring.transform.SetPositionAndRotation((a+b)*.5f+towardRoad*6f+Vector3.up*1.5f,Quaternion.LookRotation(-towardRoad,Vector3.up));
                ring.AddComponent<RotatingBoostArrows>().Configure(new Color(1f,.72f,.12f),1.05f);
                (beforeGap?guidance.forwardArrows:guidance.reverseArrows).Add(ring);
                Quad(backing, a, b, b + towardRoad * 1.35f, a + towardRoad * 1.35f, .025f);
                Quad(bright, a + towardRoad * .2f, b + towardRoad * .2f, b + towardRoad * .95f, a + towardRoad * .95f, .03f);
            }
        }
        guidance.SetDirection(sign);
        guidance.Mesh(owner, host.transform, "Dark outline", backing, guidance.ink);
        guidance.Mesh(owner, host.transform, "Amber ramp edges", bright, guidance.amber);
        guidance.Mesh(owner, host.transform, "Gold animated takeoff", gilded, guidance.gold, flowUV, approachUV);
    }
    private static void Quad(List<Vector3> output, Vector3 a, Vector3 b, Vector3 c, Vector3 d, float offset)
    {
        if (Vector3.Cross(b - a, c - a).y < 0) { Vector3 swap = b; b = d; d = swap; }
        foreach (Vector3 v in new[] { a, b, c, d }) output.Add(v + Vector3.up * offset);
    }
    private void Mesh(Transform owner, Transform root, string name, List<Vector3> vertices, Material material, List<Vector2> uv = null, List<Vector2> uv2 = null)
    {
        var local = new Vector3[vertices.Count]; var triangles = new int[vertices.Count / 4 * 6];
        for (int i = 0; i < local.Length; i++) local[i] = owner.InverseTransformPoint(vertices[i]);
        for (int i = 0; i < vertices.Count / 4; i++)
        { int v = i * 4, t = i * 6; triangles[t] = v; triangles[t + 1] = v + 1; triangles[t + 2] = v + 2; triangles[t + 3] = v; triangles[t + 4] = v + 2; triangles[t + 5] = v + 3; }
        var mesh = new Mesh { name = name, hideFlags = HideFlags.DontSave, vertices = local, triangles = triangles };
        if (uv != null) mesh.SetUVs(0, uv); if (uv2 != null) mesh.SetUVs(1, uv2);
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
        if (Application.isPlaying) { Destroy(amber); Destroy(ink); Destroy(gold); }
        else { DestroyImmediate(amber); DestroyImmediate(ink); DestroyImmediate(gold); }
    }
}
