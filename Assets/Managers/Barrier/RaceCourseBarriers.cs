using System.Collections.Generic;
using UnityEngine;

/// <summary>Uses the road's exact edge samples and the existing blue barrier cross-section.</summary>
public static class RaceCourseBarriers
{
    public const string RootName = "Course Edge Barriers";
    private struct Row
    {
        public Vector3 center, left, right;
        public static Row Lerp(Row a, Row b, float t) => new Row
        {
            center = Vector3.Lerp(a.center, b.center, t),
            left = Vector3.Lerp(a.left, b.left, t), right = Vector3.Lerp(a.right, b.right, t)
        };
    }

    /// <summary>Only the authored tube collider is queried; scenery and generated barriers cannot hide road sections.</summary>
    public static bool IsInsideTube(Vector3 center, Vector3 side, MeshCollider tube)
    {
        if (tube == null || !tube.enabled || !tube.gameObject.activeInHierarchy) return false;
        Vector3 origin = center + Vector3.up * .25f;
        return tube.Raycast(new Ray(origin, -side), out _, 25f) &&
            tube.Raycast(new Ray(origin, side), out _, 25f) &&
            tube.Raycast(new Ray(origin, Vector3.up), out _, 40f);
    }
    private static bool IsInsideTube(Row row, MeshCollider tube) =>
        IsInsideTube(row.center, (row.right - row.left).normalized, tube);

    public static void Build(Transform roadRoot, List<Vector3> center, List<Vector3> left,
        List<Vector3> right, bool closedLoop, RaceCourseRoad.Settings settings)
    {
        if (!settings.generateBarriers || center.Count < 2) return;
        var runs = new List<List<Row>>();
        List<Row> current = null;
        Row previous = new Row { center = center[0], left = left[0], right = right[0] };
        bool previousInside = IsInsideTube(previous, settings.tubeInterior);
        for (int i = 1; i < center.Count; i++)
        {
            Row next = new Row { center = center[i], left = left[i], right = right[i] };
            bool nextInside = IsInsideTube(next, settings.tubeInterior);
            if (!previousInside || !nextInside)
            {
                Row start = previous, end = next;
                if (previousInside != nextInside)
                {
                    // End each wall at the actual portal instead of leaving a whole sampling interval unprotected.
                    float low = 0, high = 1;
                    for (int step = 0; step < 16; step++)
                    {
                        float t = (low + high) * .5f;
                        if (IsInsideTube(Row.Lerp(previous, next, t), settings.tubeInterior) == previousInside) low = t;
                        else high = t;
                    }
                    Row portal = Row.Lerp(previous, next, previousInside ? high : low);
                    if (previousInside) start = portal; else end = portal;
                }
                if (current == null) { current = new List<Row>(); runs.Add(current); }
                Append(current, start); Append(current, end);
            }
            if (nextInside) current = null;
            previous = next; previousInside = nextInside;
        }
        // A closed course's array seam is not an opening in the wall.
        if (closedLoop && runs.Count > 1 && Coincides(runs[runs.Count - 1], runs[0]))
        {
            var last = runs[runs.Count - 1];
            for (int i = 1; i < runs[0].Count; i++) Append(last, runs[0][i]);
            runs.RemoveAt(0);
        }
        Transform root = new GameObject(RootName) { hideFlags = HideFlags.DontSave, layer = roadRoot.gameObject.layer }.transform;
        root.SetParent(roadRoot, false);
        for (int i = 0; i < runs.Count; i++)
        {
            List<Row> rows = runs[i];
            if (rows.Count < 2) continue;
            bool loop = closedLoop && Vector3.Distance(rows[0].center, rows[rows.Count - 1].center) < .001f;
            int count = rows.Count - (loop ? 1 : 0);
            var leftPoints = new List<Vector3>(count); var rightPoints = new List<Vector3>(count);
            for (int j = 0; j < count; j++) { leftPoints.Add(rows[j].left); rightPoints.Add(rows[j].right); }
            Create(root, "Left " + (i + 1), leftPoints, -1, loop, settings);
            Create(root, "Right " + (i + 1), rightPoints, 1, loop, settings);
        }
    }
    private static void Append(List<Row> rows, Row row)
    {
        if (rows.Count == 0 || Vector3.Distance(rows[rows.Count - 1].center, row.center) > .0001f) rows.Add(row);
    }
    private static bool Coincides(List<Row> end, List<Row> start) => end.Count > 0 && start.Count > 0 &&
        Vector3.Distance(end[end.Count - 1].center, start[0].center) < .001f;
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
        float distance = 0;
        for (int i = 0; i < edge.Count; i++)
        {
            if (i > 0) distance += Vector3.Distance(edge[i - 1], edge[i]);
            Vector3 foot = wall.transform.TransformPoint(surface[i * stride]);
            Vector3 outward = Vector3.Cross(Vector3.up, edge[Mathf.Min(edge.Count - 1, i + 1)] - edge[Mathf.Max(0, i - 1)]).normalized * outside;
            // A narrowing wall already sits over the existing road. Only pave the
            // widening side; an inward ribbon would overlap/fold across the asphalt.
            if (Vector3.Dot(foot - edge[i], outward) <= .01f) foot = edge[i];
            Vector3 span = foot - edge[i];
            vertices.Add(root.InverseTransformPoint(edge[i]));
            vertices.Add(root.InverseTransformPoint(foot + (span.sqrMagnitude > .0001f ? span.normalized * .04f : Vector3.zero)));
            uv.Add(new Vector2(0, distance)); uv.Add(new Vector2(span.magnitude, distance));
            if (i == 0 || (span.sqrMagnitude < .0001f && (vertices[(i - 1) * 2] - vertices[(i - 1) * 2 + 1]).sqrMagnitude < .0001f)) continue;
            int a = (i - 1) * 2, b = i * 2;
            bool flip = Vector3.Cross(vertices[b] - vertices[a], vertices[a + 1] - vertices[a]).y < 0;
            triangles.AddRange(flip ? new[] { a, a + 1, b, a + 1, b + 1, b } : new[] { a, b, a + 1, a + 1, b, b + 1 });
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
