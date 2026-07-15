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

    private readonly List<Vector3> cachedCenterPath = new List<Vector3>();
    private readonly List<float> cachedWidthPath = new List<float>();
    private readonly List<Vector3> cachedInnerPath = new List<Vector3>();
    private readonly List<Vector3> cachedOuterPath = new List<Vector3>();
    private readonly List<Vector2> cachedCoursePolygon = new List<Vector2>();
    private bool cacheDirty = true;
    private Vector3 cachedPosition;
    private Quaternion cachedRotation;
    private Vector3 cachedScale;

    private void Awake()
    {
        RebuildCache();
    }

    private void OnValidate()
    {
        cacheDirty = true;
    }

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

        EnsureCache();
        if (cachedCenterPath.Count < 2)
        {
            return;
        }

        Gizmos.color = pathColor;
        DrawPolyline(cachedInnerPath);
        DrawPolyline(cachedOuterPath);

        if (drawCenterLine)
        {
            DrawPolyline(cachedCenterPath);
        }
    }

    public bool IsPointInsideCourse(Vector2 p)
    {
        if (waypoints == null || waypoints.Length < 2)
        {
            return false;
        }

        EnsureCache();
        if (cachedCenterPath.Count < 2 || cachedWidthPath.Count != cachedCenterPath.Count)
        {
            return false;
        }

        for (int i = 1; i < cachedCenterPath.Count; i++)
        {
            Vector2 a = ToXZ(cachedCenterPath[i - 1]);
            Vector2 b = ToXZ(cachedCenterPath[i]);
            Vector2 nearestPoint = ClosestPointOnSegment2D(p, a, b);
            float distanceSqr = (nearestPoint - p).sqrMagnitude;

            float segmentLengthSqr = (b - a).sqrMagnitude;
            if (segmentLengthSqr <= Mathf.Epsilon)
            {
                continue;
            }

            float t = Vector2.Dot(nearestPoint - a, b - a) / segmentLengthSqr;
            float width = Mathf.Lerp(cachedWidthPath[i - 1], cachedWidthPath[i], Mathf.Clamp01(t));
            float halfWidth = Mathf.Max(0f, width) * 0.5f;

            if (distanceSqr <= (halfWidth * halfWidth) + Mathf.Epsilon)
            {
                return true;
            }
        }

        return false;
    }

    public Vector2 GetNearestPointOnCenterLine(Vector2 p)
    {
        if (waypoints == null || waypoints.Length < 2)
        {
            return p;
        }

        EnsureCache();
        if (cachedCenterPath.Count < 2)
        {
            return p;
        }

        Vector2 nearestPoint = ToXZ(cachedCenterPath[0]);
        float nearestDistanceSqr = float.PositiveInfinity;

        for (int i = 1; i < cachedCenterPath.Count; i++)
        {
            Vector2 a = ToXZ(cachedCenterPath[i - 1]);
            Vector2 b = ToXZ(cachedCenterPath[i]);
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

    public Vector3 GetNearestPointOnCenterLineWorld(Vector3 worldPosition)
    {
        Vector2 nearest = GetNearestPointOnCenterLine(ToXZ(worldPosition));
        return new Vector3(nearest.x, worldPosition.y, nearest.y);
    }

    /// <summary>
    /// 指定位置に最も近いセンターライン区間の、レース進行方向を取得します。
    /// waypoint の配列順をレース進行方向として扱います。
    /// </summary>
    public bool TryGetNearestCenterLineDirection(Vector3 worldPosition, out Vector3 direction)
    {
        direction = Vector3.zero;

        if (waypoints == null || waypoints.Length < 2)
        {
            return false;
        }

        EnsureCache();
        if (cachedCenterPath.Count < 2)
        {
            return false;
        }

        Vector2 point = ToXZ(worldPosition);
        float nearestDistanceSqr = float.PositiveInfinity;

        for (int index = 1; index < cachedCenterPath.Count; index++)
        {
            Vector2 start = ToXZ(cachedCenterPath[index - 1]);
            Vector2 end = ToXZ(cachedCenterPath[index]);
            Vector2 segment = end - start;
            if (segment.sqrMagnitude <= Mathf.Epsilon)
            {
                continue;
            }

            Vector2 nearestPoint = ClosestPointOnSegment2D(point, start, end);
            float distanceSqr = (nearestPoint - point).sqrMagnitude;
            if (distanceSqr >= nearestDistanceSqr)
            {
                continue;
            }

            nearestDistanceSqr = distanceSqr;
            direction = new Vector3(segment.x, 0f, segment.y).normalized;
        }

        return direction.sqrMagnitude > Mathf.Epsilon;
    }

    public void RebuildCache()
    {
        cachedCenterPath.Clear();
        cachedWidthPath.Clear();
        cachedInnerPath.Clear();
        cachedOuterPath.Clear();
        cachedCoursePolygon.Clear();

        if (waypoints == null || waypoints.Length < 2)
        {
            cacheDirty = false;
            return;
        }

        BuildCenterPath(cachedCenterPath, cachedWidthPath);
        if (cachedCenterPath.Count >= 2)
        {
            BuildOffsetPaths(cachedCenterPath, cachedWidthPath, cachedInnerPath, cachedOuterPath);
        }

        if (cachedInnerPath.Count >= 2 && cachedOuterPath.Count >= 2)
        {
            BuildCoursePolygon(cachedInnerPath, cachedOuterPath, cachedCoursePolygon);
        }

        cacheDirty = false;
        cachedPosition = transform.position;
        cachedRotation = transform.rotation;
        cachedScale = transform.lossyScale;
    }

    private void EnsureCache()
    {
        if (cacheDirty ||
            cachedCenterPath.Count == 0 ||
            cachedPosition != transform.position ||
            cachedRotation != transform.rotation ||
            cachedScale != transform.lossyScale)
        {
            RebuildCache();
        }
    }

    private void BuildCenterPath(out List<Vector3> centerPath, out List<float> widthPath)
    {
        centerPath = new List<Vector3>();
        widthPath = new List<float>();
        BuildCenterPath(centerPath, widthPath);
    }

    private void BuildCenterPath(List<Vector3> centerPath, List<float> widthPath)
    {
        centerPath.Clear();
        widthPath.Clear();

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
        BuildCoursePolygon(innerPath, outerPath, polygon);
        return polygon;
    }

    private static void BuildCoursePolygon(List<Vector3> innerPath, List<Vector3> outerPath, List<Vector2> polygon)
    {
        polygon.Clear();

        for (int i = 0; i < outerPath.Count; i++)
        {
            polygon.Add(ToXZ(outerPath[i]));
        }

        for (int i = innerPath.Count - 1; i >= 0; i--)
        {
            polygon.Add(ToXZ(innerPath[i]));
        }
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
        BuildOffsetPaths(centerPath, widthPath, innerPath, outerPath);
    }

    private void BuildOffsetPaths(
        List<Vector3> centerPath,
        List<float> widthPath,
        List<Vector3> innerPath,
        List<Vector3> outerPath)
    {
        innerPath.Clear();
        outerPath.Clear();

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
