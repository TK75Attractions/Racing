using UnityEngine;
using System.Collections.Generic;

public class RaceCourse : MonoBehaviour
{

    [System.Serializable]
    private class Waypoint
    {
        public Vector2 position;
        public float curve = 0;
        public float width = 10.0f;
    }

    [SerializeField] private Waypoint[] waypoints;

    [Header("Gizmo")]
    [SerializeField] private Color waypointColor = Color.cyan;
    [SerializeField] private Color pathColor = Color.yellow;
    [SerializeField] private float waypointRadius = 1.0f;
    [SerializeField] private int curveSegments = 20;
    [SerializeField] private bool drawCenterLine = false;

    private void OnDrawGizmos()
    {
        if (waypoints == null || waypoints.Length == 0)
        {
            return;
        }

        for (int i = 0; i < waypoints.Length; i++)
        {
            Vector3 current = transform.TransformPoint(ToLocalPoint(waypoints[i].position));

            Gizmos.color = waypointColor;
            Gizmos.DrawSphere(current, waypointRadius);

            if (waypoints.Length < 2)
            {
                continue;
            }
        }

        if (waypoints.Length < 2)
        {
            return;
        }

        BuildCenterPath(out List<Vector3> centerPath, out List<float> widthPath);
        if (centerPath.Count < 2)
        {
            return;
        }

        BuildOffsetPaths(centerPath, widthPath, out List<Vector3> innerPath, out List<Vector3> outerPath);

        Gizmos.color = pathColor;
        DrawPolyline(innerPath);
        DrawPolyline(outerPath);

        if (drawCenterLine)
        {
            DrawPolyline(centerPath);
        }
    }

    public bool IsPointInsideCourse(Vector2 p)
    {
        if (waypoints == null || waypoints.Length < 2)
        {
            return false;
        }

        BuildCenterPath(out List<Vector3> centerPath, out List<float> widthPath);
        if (centerPath.Count < 3)
        {
            return false;
        }

        BuildOffsetPaths(centerPath, widthPath, out List<Vector3> innerPath, out List<Vector3> outerPath);
        if (innerPath.Count < 2 || outerPath.Count < 2)
        {
            return false;
        }

        List<Vector2> polygon = BuildCoursePolygon(innerPath, outerPath);
        return IsPointInPolygon(p, polygon);
    }

    public Vector2 GetNearestPointOnCenterLine(Vector2 p)
    {
        if (waypoints == null || waypoints.Length < 2)
        {
            return p;
        }

        BuildCenterPath(out List<Vector3> centerPath, out _);
        if (centerPath.Count < 2)
        {
            return p;
        }

        Vector2 nearestPoint = ToXZ(centerPath[0]);
        float nearestDistanceSqr = float.PositiveInfinity;

        for (int i = 1; i < centerPath.Count; i++)
        {
            Vector2 a = ToXZ(centerPath[i - 1]);
            Vector2 b = ToXZ(centerPath[i]);
            Vector2 candidate = ClosestPointOnSegment2D(p, a, b);
            float distanceSqr = (candidate - p).sqrMagnitude;

            if (distanceSqr < nearestDistanceSqr)
            {
                nearestDistanceSqr = distanceSqr;
                nearestPoint = candidate;
            }
        }

        return nearestPoint;
    }

    private void BuildCenterPath(out List<Vector3> centerPath, out List<float> widthPath)
    {
        centerPath = new List<Vector3>();
        widthPath = new List<float>();

        int segmentCount = Mathf.Max(1, curveSegments);

        for (int waypointIndex = 0; waypointIndex < waypoints.Length; waypointIndex++)
        {
            int nextIndex = (waypointIndex + 1) % waypoints.Length;

            Vector3 start = transform.TransformPoint(ToLocalPoint(waypoints[waypointIndex].position));
            Vector3 end = transform.TransformPoint(ToLocalPoint(waypoints[nextIndex].position));

            float startWidth = Mathf.Max(0f, waypoints[waypointIndex].width);
            float endWidth = Mathf.Max(0f, waypoints[nextIndex].width);
            float curve = waypoints[waypointIndex].curve;

            int sampleStart = waypointIndex == 0 ? 0 : 1;
            for (int sample = sampleStart; sample <= segmentCount; sample++)
            {
                float t = sample / (float)segmentCount;
                centerPath.Add(EvaluateEllipticSegmentPoint(start, end, curve, t));

                float easedT = Mathf.SmoothStep(0f, 1f, t);
                widthPath.Add(Mathf.Lerp(startWidth, endWidth, easedT));
            }
        }
    }

    private static List<Vector2> BuildCoursePolygon(List<Vector3> innerPath, List<Vector3> outerPath)
    {
        List<Vector2> polygon = new List<Vector2>(innerPath.Count + outerPath.Count);

        for (int i = 0; i < outerPath.Count; i++)
        {
            polygon.Add(ToXZ(outerPath[i]));
        }

        for (int i = innerPath.Count - 1; i >= 0; i--)
        {
            polygon.Add(ToXZ(innerPath[i]));
        }

        return polygon;
    }

    private static bool IsPointInPolygon(Vector2 p, List<Vector2> polygon)
    {
        if (polygon == null || polygon.Count < 3)
        {
            return false;
        }

        bool inside = false;
        int j = polygon.Count - 1;

        for (int i = 0; i < polygon.Count; i++)
        {
            Vector2 a = polygon[i];
            Vector2 b = polygon[j];

            bool crosses = (a.y > p.y) != (b.y > p.y);
            if (crosses)
            {
                float t = (p.y - a.y) / (b.y - a.y);
                float xAtY = a.x + ((b.x - a.x) * t);
                if (p.x < xAtY)
                {
                    inside = !inside;
                }
            }

            j = i;
        }

        return inside;
    }

    private static Vector2 ClosestPointOnSegment2D(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float abLengthSqr = ab.sqrMagnitude;
        if (abLengthSqr <= Mathf.Epsilon)
        {
            return a;
        }

        float t = Vector2.Dot(p - a, ab) / abLengthSqr;
        t = Mathf.Clamp01(t);
        return a + (ab * t);
    }

    private static Vector3 ToLocalPoint(Vector2 p)
    {
        return new Vector3(p.x, 0f, p.y);
    }

    private static Vector2 ToXZ(Vector3 p)
    {
        return new Vector2(p.x, p.z);
    }

    private void BuildOffsetPaths(
        List<Vector3> centerPath,
        List<float> widthPath,
        out List<Vector3> innerPath,
        out List<Vector3> outerPath)
    {
        innerPath = new List<Vector3>(centerPath.Count);
        outerPath = new List<Vector3>(centerPath.Count);

        Vector3 fallbackLateral = Vector3.right;

        for (int i = 0; i < centerPath.Count; i++)
        {
            Vector3 tangent = EvaluatePathTangent(centerPath, i);
            if (tangent.sqrMagnitude <= Mathf.Epsilon)
            {
                tangent = Vector3.forward;
            }

            Vector3 lateral = Vector3.Cross(Vector3.up, tangent.normalized);
            if (lateral.sqrMagnitude <= Mathf.Epsilon)
            {
                lateral = fallbackLateral;
            }
            else
            {
                lateral.Normalize();
                fallbackLateral = lateral;
            }

            float halfWidth = widthPath[i] * 0.5f;
            innerPath.Add(centerPath[i] - lateral * halfWidth);
            outerPath.Add(centerPath[i] + lateral * halfWidth);
        }
    }

    private static Vector3 EvaluatePathTangent(List<Vector3> path, int index)
    {
        if (path.Count < 2)
        {
            return Vector3.zero;
        }

        if (index == 0)
        {
            return path[1] - path[0];
        }

        if (index == path.Count - 1)
        {
            return path[path.Count - 1] - path[path.Count - 2];
        }

        return path[index + 1] - path[index - 1];
    }

    private static void DrawPolyline(List<Vector3> points)
    {
        for (int i = 1; i < points.Count; i++)
        {
            Gizmos.DrawLine(points[i - 1], points[i]);
        }
    }

    private static Vector3 EvaluateEllipticSegmentPoint(
        Vector3 start,
        Vector3 end,
        float curve,
        float t,
        Vector3 worldUp = default)
    {
        Vector3 chord = end - start;
        float chordLength = chord.magnitude;
        if (chordLength <= Mathf.Epsilon)
        {
            return start;
        }

        Vector3 direction = chord / chordLength;
        Vector3 upAxis = worldUp == default ? Vector3.up : worldUp;
        Vector3 normal = Vector3.Cross(upAxis, direction);
        if (normal.sqrMagnitude <= Mathf.Epsilon)
        {
            normal = Vector3.right;
        }
        else
        {
            normal.Normalize();
        }

        Vector3 midpoint = (start + end) * 0.5f;
        float semiMajorAxis = chordLength * 0.5f;
        float semiMinorAxis = Mathf.Abs(curve);
        float normalSign = Mathf.Sign(curve);

        float angle = (1f - t) * Mathf.PI;

        float x = Mathf.Cos(angle) * semiMajorAxis;
        float y = Mathf.Sin(angle) * semiMinorAxis * normalSign;
        return midpoint + direction * x + normal * y;
    }
}
