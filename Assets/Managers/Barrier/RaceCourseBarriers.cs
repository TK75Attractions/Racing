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
        wall.AddComponent<RaceBarrierPath>().SetWorldPath(points, outside, loop,
            settings.barrierMaterial, settings.colliderMaterial, settings.generateCollider);
    }
}
