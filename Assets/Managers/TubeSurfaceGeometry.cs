using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Reconstructs local UV-grid quads and C1 ring/profile curves from the authored tube.</summary>
public static class TubeSurfaceGeometry
{
    public const int ProfileSegments = 96;
    public const string SmoothMeshName = "TubeInteriorSmooth";
    public static Mesh BuildSmooth(Mesh source, Transform frame, out Mesh collision)
    {
        List<Vector3[]> rings = ReadRings(source);
        if (rings.Count < 2 || rings[0].Length < 4) throw new InvalidOperationException("Tube UV ring grid is missing.");
        var centers = new Vector3[rings.Count];
        for (int i = 0; i < centers.Length; i++) foreach (var v in rings[i]) centers[i] += v / rings[i].Length;
        var vertices = new List<Vector3>(); var uv = new List<Vector2>();
        for (int i = 0; i < rings.Count - 1; i++)
        {
            int steps = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(frame.TransformPoint(centers[i]), frame.TransformPoint(centers[i + 1])) / 1.25f));
            for (int j = 0; j < steps; j++) AddRing(i, j / (float)steps);
        }
        AddRing(rings.Count - 2, 1);
        void AddRing(int index, float t)
        {
            for (int k = 0; k <= ProfileSegments; k++)
            {
                float p = k / (float)ProfileSegments;
                Vector3 a = At(rings[Mathf.Max(0, index - 1)], p), b = At(rings[index], p);
                Vector3 c = At(rings[index + 1], p), d = At(rings[Mathf.Min(rings.Count - 1, index + 2)], p);
                vertices.Add(Catmull(a, b, c, d, t)); uv.Add(new Vector2((index + t) / (rings.Count - 1), p));
            }
        }
        int rows = vertices.Count / (ProfileSegments + 1), stride = ProfileSegments + 1;
        var triangles = new int[(rows - 1) * ProfileSegments * 6];
        for (int i = 0; i < rows - 1; i++) for (int k = 0; k < ProfileSegments; k++)
        {
            int a = i * stride + k, b = a + 1, c = a + stride, d = c + 1;
            // Choose inward-facing triangles regardless of the FBX profile orientation.
            Vector3 center = Vector3.zero;
            int first = i * stride;
            for (int n = 0; n < ProfileSegments; n++) center += vertices[first + n] / ProfileSegments;
            bool inward = Vector3.Dot(Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]), center - vertices[a]) > 0;
            int offset = (i * ProfileSegments + k) * 6;
            triangles[offset] = a; triangles[offset + 1] = inward ? b : c; triangles[offset + 2] = inward ? c : b;
            triangles[offset + 3] = b; triangles[offset + 4] = inward ? d : c; triangles[offset + 5] = inward ? c : d;
        }
        var visual = new Mesh { name = SmoothMeshName, indexFormat = IndexFormat.UInt32 };
        visual.SetVertices(vertices); visual.SetUVs(0, uv); visual.triangles = triangles; visual.RecalculateNormals();
        var normals = visual.normals;
        for (int i = 0; i < rows; i++)
        {
            int a = i * stride, b = a + ProfileSegments;
            normals[a] = normals[b] = (normals[a] + normals[b]).normalized;
        }
        visual.normals = normals; visual.RecalculateBounds(); visual.RecalculateTangents();
        var both = new int[triangles.Length * 2]; Array.Copy(triangles, both, triangles.Length);
        for (int i = 0; i < triangles.Length; i += 3)
        { both[triangles.Length + i] = triangles[i]; both[triangles.Length + i + 1] = triangles[i + 2]; both[triangles.Length + i + 2] = triangles[i + 1]; }
        collision = new Mesh { name = "Smooth tube double sided collision", indexFormat = IndexFormat.UInt32 };
        collision.SetVertices(vertices); collision.triangles = both; collision.RecalculateBounds();
        return visual;
    }
    public static List<Vector3[]> ReadRings(Mesh mesh)
    {
        Vector3[] vertices = mesh.vertices; Vector2[] uv = mesh.uv;
        var groups = new SortedDictionary<int, SortedDictionary<int, Vector3>>();
        for (int i = 0; i < vertices.Length; i++)
        {
            if (uv[i].y > .999f) continue;
            int along = Mathf.RoundToInt(uv[i].x * 1000000), around = Mathf.RoundToInt(uv[i].y * 1000000);
            if (!groups.TryGetValue(along, out var ring)) groups.Add(along, ring = new SortedDictionary<int, Vector3>());
            ring[around] = vertices[i];
        }
        var result = new List<Vector3[]>(); foreach (var ring in groups.Values) result.Add(new List<Vector3>(ring.Values).ToArray());
        return result;
    }
    private static Vector3 At(Vector3[] ring, float phase)
    {
        float p = Mathf.Repeat(phase, 1) * ring.Length; int i = Mathf.FloorToInt(p), count = ring.Length;
        return Catmull(ring[(i - 1 + count) % count], ring[i % count], ring[(i + 1) % count], ring[(i + 2) % count], p - i);
    }
    private static Vector3 Catmull(Vector3 a, Vector3 b, Vector3 c, Vector3 d, float t) =>
        .5f * (2 * b + (-a + c) * t + (2 * a - 5 * b + 4 * c - d) * t * t + (-a + 3 * b - 3 * c + d) * t * t * t);
}

/// <summary>One half of the actual portal, including its longitudinal tangent in world meters.</summary>
public sealed class TubePortalProfile
{
    public const float BlendLength = 32f;
    private static readonly Dictionary<Mesh, List<Vector3[]>> ringCache = new Dictionary<Mesh, List<Vector3[]>>();
    public static void InvalidateCache() => ringCache.Clear();
    public Vector3[] points, outwardTangents;
    public Vector3 Evaluate(float phase, float outsideDistance)
    {
        float p = Mathf.Clamp01(phase) * (points.Length - 1); int a = Mathf.FloorToInt(p), b = Mathf.Min(a + 1, points.Length - 1);
        return Vector3.Lerp(points[a], points[b], p - a) + Vector3.Lerp(outwardTangents[a], outwardTangents[b], p - a) * outsideDistance;
    }
    public static TubePortalProfile Create(MeshCollider tube, Vector3 roadEdge, Vector3 outward, int sections)
    {
        MeshFilter filter = tube != null ? tube.GetComponent<MeshFilter>() : null;
        // Preparation bakes a readable mesh asset. No runtime CPU reads of the non-readable FBX.
        if (filter == null || filter.sharedMesh == null || !filter.sharedMesh.isReadable ||
            filter.sharedMesh.name != TubeSurfaceGeometry.SmoothMeshName) return null;
        if (!ringCache.TryGetValue(filter.sharedMesh, out var rings))
            ringCache.Add(filter.sharedMesh, rings = TubeSurfaceGeometry.ReadRings(filter.sharedMesh));
        Vector3 Center(Vector3[] ring) { Vector3 p = Vector3.zero; foreach (var v in ring) p += filter.transform.TransformPoint(v) / ring.Length; return p; }
        int end = Vector3.Distance(Center(rings[0]), roadEdge) < Vector3.Distance(Center(rings[rings.Count - 1]), roadEdge) ? 0 : rings.Count - 1;
        if (Vector3.Distance(Center(rings[end]), roadEdge) > 25f) return null;
        int adjacent = end == 0 ? 1 : end - 1;
        var border = Array.ConvertAll(rings[end], filter.transform.TransformPoint);
        var inside = Array.ConvertAll(rings[adjacent], filter.transform.TransformPoint);
        float span = Vector3.Distance(Center(rings[end]), Center(rings[adjacent]));
        if (span < .001f) return null;
        Vector3 center = Center(rings[end]); int top = 0; float floor = -1, best = float.NegativeInfinity;
        for (int k = 0; k < border.Length; k++)
        {
            if (border[k].y > border[top].y) top = k;
            int n = (k + 1) % border.Length;
            if ((border[k].y <= roadEdge.y) == (border[n].y <= roadEdge.y)) continue;
            float t = (roadEdge.y - border[k].y) / (border[n].y - border[k].y);
            float side = Vector3.Dot(Vector3.Lerp(border[k], border[n], t) - center, outward);
            if (side > best) { best = side; floor = k + t; }
        }
        if (floor < 0 || best <= 0) return null;
        float forward = Mathf.Repeat(top - floor, border.Length), backward = forward - border.Length;
        Vector3 At(Vector3[] ring, float u)
        { u = Mathf.Repeat(u, ring.Length); int a = Mathf.FloorToInt(u); return Vector3.Lerp(ring[a], ring[(a + 1) % ring.Length], u - a); }
        float route = Vector3.Dot(At(border, floor + forward * .5f) - center, outward) >
            Vector3.Dot(At(border, floor + backward * .5f) - center, outward) ? forward : backward;
        var profile = new TubePortalProfile { points = new Vector3[sections + 1], outwardTangents = new Vector3[sections + 1] };
        for (int k = 0; k <= sections; k++)
        {
            float u = floor + route * k / sections;
            profile.points[k] = At(border, u);
            profile.outwardTangents[k] = (At(border, u) - At(inside, u)) / span;
        }
        return profile;
    }
}
