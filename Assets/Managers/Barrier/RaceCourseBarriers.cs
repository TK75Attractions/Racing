using System.Collections.Generic;
using UnityEngine;

/// <summary>Uses the road's exact edge samples and the existing blue barrier cross-section.</summary>
public static class RaceCourseBarriers
{
    public const string RootName = "Course Edge Barriers";
    /// <summary>Only the authored tube collider is queried; scenery and generated barriers cannot hide road sections.</summary>
    public static bool IsInsideTube(Vector3 center, Vector3 side, MeshCollider tube)
    {
        if (tube == null || !tube.enabled || !tube.gameObject.activeInHierarchy) return false;
        Vector3 origin = center + Vector3.up * .25f;
        return tube.Raycast(new Ray(origin, -side), out _, 25f) &&
            tube.Raycast(new Ray(origin, side), out _, 25f) &&
            tube.Raycast(new Ray(origin, Vector3.up), out _, 40f);
    }
    public static void Build(Transform roadRoot, List<Vector3> center, List<Vector3> left,
        List<Vector3> right, bool closedLoop, RaceCourseRoad.Settings settings)
    {
        if (!settings.generateBarriers || center.Count < 2) return;
        Transform root = new GameObject(RootName) { hideFlags = HideFlags.DontSave, layer = roadRoot.gameObject.layer }.transform;
        root.SetParent(roadRoot, false);
        // Each road edge crosses a portal plane at a different distance on a bend.
        // Clip both independently so every wall and its paving end exactly on that plane.
        foreach (int outside in new[] {-1, 1})
        {
            var edge = outside < 0 ? left : right;
            var runs = new List<List<Vector3>>(); List<Vector3> current = null;
            bool Inside(int i) => TubePortalProfile.IsInsideAtEdge(settings.tubeInterior, edge[i], center[i], (right[i] - left[i]).normalized);
            bool previousInside = Inside(0);
            for (int i = 1; i < center.Count; i++)
            {
                bool nextInside = Inside(i);
                if (!previousInside || !nextInside)
                {
                    Vector3 start = edge[i - 1], end = edge[i];
                    if (previousInside != nextInside)
                    {
                        float low = 0, high = 1;
                        for (int step = 0; step < 18; step++)
                        {
                            float t = (low + high) * .5f;
                            bool inside = TubePortalProfile.IsInsideAtEdge(settings.tubeInterior, Vector3.Lerp(edge[i-1], edge[i], t),
                                Vector3.Lerp(center[i-1], center[i], t), (Vector3.Lerp(right[i-1], right[i], t)-Vector3.Lerp(left[i-1], left[i], t)).normalized);
                            if (inside == previousInside) low = t; else high = t;
                        }
                        Vector3 portal = Vector3.Lerp(edge[i-1], edge[i], (low + high) * .5f);
                        if (previousInside) start = portal; else end = portal;
                    }
                    if (current == null) { current = new List<Vector3>(); runs.Add(current); }
                    if (current.Count == 0 || Vector3.Distance(current[current.Count-1], start) > .0001f) current.Add(start);
                    current.Add(end);
                }
                if (nextInside) current = null;
                previousInside = nextInside;
            }
            if (closedLoop && runs.Count > 1 && Vector3.Distance(runs[runs.Count-1][runs[runs.Count-1].Count-1], runs[0][0]) < .001f)
            {
                runs[runs.Count-1].AddRange(runs[0].GetRange(1, runs[0].Count-1)); runs.RemoveAt(0);
            }
            for (int i = 0; i < runs.Count; i++)
            {
                var points = runs[i]; if (points.Count < 2) continue;
                bool loop = closedLoop && Vector3.Distance(points[0], points[points.Count-1]) < .001f;
                if (loop) points.RemoveAt(points.Count-1);
                Create(root, (outside < 0 ? "Left " : "Right ") + (i+1), points, outside, loop, settings);
            }
        }
    }
    private static void Create(Transform root, string name, List<Vector3> points, int outside,
        bool loop, RaceCourseRoad.Settings settings)
    {
        var wall = new GameObject(name) { layer = root.gameObject.layer, hideFlags = HideFlags.DontSave };
        wall.transform.position = root.position;
        wall.transform.SetParent(root, true); // Keep the inherited wall profile in world meters, including on scaled courses.
        TubePortalProfile start = null, end = null;
        if (!loop && settings.tubeInterior != null)
        {
            start = TubePortalProfile.Create(settings.tubeInterior, points[0],
                Vector3.Cross(Vector3.up, points[1] - points[0]).normalized * outside, 32);
            end = TubePortalProfile.Create(settings.tubeInterior, points[points.Count - 1],
                Vector3.Cross(Vector3.up, points[points.Count - 1] - points[points.Count - 2]).normalized * outside, 32);
            if (start != null || end != null) points = DensifyPortals(points, start != null, end != null);
        }
        var path = wall.AddComponent<RaceBarrierPath>();
        path.SetWorldPath(points, outside, loop,
            settings.barrierMaterial, settings.colliderMaterial, settings.generateCollider, start, end);
        if (path.HasPortalBlend) BuildPortalShoulder(root, path, outside, settings);
    }
    private static void BuildPortalShoulder(Transform root, RaceBarrierPath path, int outside, RaceCourseRoad.Settings settings)
    {
        var edge = new List<Vector3>(); path.CopyPreviewPathWorld(edge);
        MeshFilter wall = path.GetComponentInChildren<MeshFilter>(); var surface = wall.sharedMesh.vertices;
        int stride = surface.Length / edge.Count;
        var vertices = new List<Vector3>(); var uv = new List<Vector2>(); var triangles = new List<int>();
        float distance = 0; int columns = RaceCourseRoad.ShoulderSegments + 1;
        bool previousWide = false;
        for (int i = 0; i < edge.Count; i++)
        {
            if (i > 0) distance += Vector3.Distance(edge[i - 1], edge[i]);
            Vector3 foot = wall.transform.TransformPoint(surface[i * stride]);
            Vector3 outward = Vector3.Cross(Vector3.up, edge[Mathf.Min(edge.Count - 1, i + 1)] - edge[Mathf.Max(0, i - 1)]).normalized * outside;
            if (Vector3.Dot(foot - edge[i], outward) <= .01f) foot = edge[i];
            float width = Vector3.Distance(edge[i], foot);
            // A grid across the paving prevents a single wide, twisted quad from
            // turning a smooth grade into two large collision planes at the seam.
            for (int k=0;k<columns;k++)
            {
                float t=k/(float)(columns-1);
                vertices.Add(root.InverseTransformPoint(Vector3.Lerp(edge[i],foot,t)));
                uv.Add(new Vector2(width*t,distance));
            }
            bool wide=width>.01f;
            if(i>0 && (wide || previousWide))
            {
                int a=(i-1)*columns,b=i*columns;
                bool flip=Vector3.Cross(vertices[b]-vertices[a],vertices[a+columns-1]-vertices[a]+vertices[b+columns-1]-vertices[b]).y<0;
                for(int k=0;k<columns-1;k++)
                {
                    int c=a+k,d=b+k;
                    triangles.AddRange(flip ? new[] {c,c+1,d,c+1,d+1,d} : new[] {c,d,c+1,c+1,d,d+1});
                }
            }
            previousWide=wide;
        }
        if (triangles.Count == 0) return;
        var mesh = new Mesh { name = "Portal shoulder", hideFlags = HideFlags.DontSave, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
        mesh.SetVertices(vertices); mesh.SetUVs(0, uv); mesh.SetTriangles(triangles, 0); mesh.RecalculateNormals(); mesh.RecalculateBounds(); mesh.RecalculateTangents();
        var part = new GameObject("Portal Road Shoulder " + path.name, typeof(MeshFilter), typeof(MeshRenderer)) { hideFlags = HideFlags.DontSave, layer = root.gameObject.layer };
        part.transform.SetParent(root, false); part.GetComponent<MeshFilter>().sharedMesh = mesh;
        part.GetComponent<MeshRenderer>().sharedMaterial = settings.asphaltMaterial != null ? settings.asphaltMaterial : Resources.Load<Material>("RaceCourse/M_RoadAsphalt");
        if (settings.generateCollider) { var collider = part.AddComponent<MeshCollider>(); collider.sharedMesh = mesh; collider.sharedMaterial = settings.colliderMaterial; }
    }
    private static List<Vector3> DensifyPortals(List<Vector3> points, bool start, bool end)
    {
        float total = 0; for (int i = 1; i < points.Count; i++) total += Vector3.Distance(points[i - 1], points[i]);
        var result = new List<Vector3> { points[0] }; float distance = 0;
        for (int i = 1; i < points.Count; i++)
        {
            float segment = Vector3.Distance(points[i - 1], points[i]);
            bool portal = start && distance < TubePortalProfile.BlendLength || end && total - distance - segment < TubePortalProfile.BlendLength;
            int steps = portal ? Mathf.Max(1, Mathf.CeilToInt(segment / .4f)) : 1;
            for (int step = 1; step <= steps; step++) result.Add(Vector3.Lerp(points[i - 1], points[i], step / (float)steps));
            distance += segment;
        }
        return result;
    }
}
