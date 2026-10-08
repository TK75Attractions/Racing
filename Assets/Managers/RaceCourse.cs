using UnityEngine;
using System.Collections.Generic;

public class RaceCourse : MonoBehaviour
{
    [System.Serializable]
    private class Waypoint
    {
        // 旧シーンの position (X,Z) を保持し、高さだけを追加する。
        public Vector2 position;
        public float height;
        public float curve;
        public float width = 10f;
        public Vector3 LocalPosition => new Vector3(position.x, height, position.y);
    }

    /// <summary>装飾配置用のワールド座標、方向、幅、左右の縁。</summary>
    public struct CourseSample
    {
        public Vector3 position;
        public Vector3 forward;
        public Vector3 right;
        public Vector3 up;
        public float width;
        public Vector3 leftEdge;
        public Vector3 rightEdge;
    }

    [SerializeField] private Waypoint[] waypoints;
    [SerializeField] private bool closedLoop = true;
    [Tooltip("コース面から上下に許容するワールド距離。車体の高さやジャンプを考慮して設定します。")]
    [SerializeField, Min(0f)] private float verticalTolerance = 8f;

    [Header("Gizmo")]
    [SerializeField] private Color waypointColor = Color.cyan;
    [SerializeField] private Color pathColor = Color.yellow;
    [SerializeField, Min(0f)] private float waypointRadius = 1f;
    [SerializeField, Range(1, 100)] private int curveSegments = 20;
    [SerializeField] private bool drawCenterLine = true;

    private readonly List<Vector3> cachedCenterPath = new List<Vector3>();
    private readonly List<float> cachedWidthPath = new List<float>();
    private readonly List<Vector3> cachedInnerPath = new List<Vector3>();
    private readonly List<Vector3> cachedOuterPath = new List<Vector3>();
    private readonly List<float> cachedCumulativeDistances = new List<float>();
    private bool cacheDirty = true;
    private Vector3 cachedPosition;
    private Quaternion cachedRotation;
    private Vector3 cachedScale;

    public bool ClosedLoop => closedLoop;
    public bool HasValidPath => TotalLength > Mathf.Epsilon;

    /// <summary>高さを含む中心線の全長（ワールド単位）。</summary>
    public float TotalLength
    {
        get
        {
            EnsureCache();
            return cachedCumulativeDistances.Count == 0 ? 0f :
                cachedCumulativeDistances[cachedCumulativeDistances.Count - 1];
        }
    }

    private void Awake() => RebuildCache();
    private void OnValidate() => cacheDirty = true;

    private void OnDrawGizmos()
    {
        if (waypoints == null) return;
        Gizmos.color = waypointColor;
        foreach (Waypoint point in waypoints)
            Gizmos.DrawSphere(transform.TransformPoint(point.LocalPosition), waypointRadius);
        EnsureCache();
        Gizmos.color = pathColor;
        DrawPolyline(cachedInnerPath);
        DrawPolyline(cachedOuterPath);
        for (int i = 0; i < cachedInnerPath.Count; i++)
            Gizmos.DrawLine(cachedInnerPath[i], cachedOuterPath[i]);
        if (drawCenterLine) DrawPolyline(cachedCenterPath);
    }

    /// <summary>高さを無視する旧API。3Dの逸脱判定には Vector3 の overload を使います。</summary>
    public bool IsPointInsideCourse(Vector2 point) => IsInsideBand(point, null);

    /// <summary>Sceneに表示した帯の範囲と、その地点の路面からの高さで判定します。</summary>
    public bool IsPointInsideCourse(Vector3 worldPosition) =>
        IsInsideBand(ToXZ(worldPosition), worldPosition.y);

    private bool IsInsideBand(Vector2 point, float? worldHeight)
    {
        EnsureCache();
        for (int i = 1; i < cachedInnerPath.Count; i++)
        {
            // 描画に使う左右の縁と同じ三角形で判定。立体交差では全区間の高さを調べる。
            if (IsInsideTriangle(point, worldHeight,
                    cachedInnerPath[i - 1], cachedOuterPath[i - 1], cachedInnerPath[i]) ||
                IsInsideTriangle(point, worldHeight,
                    cachedOuterPath[i - 1], cachedOuterPath[i], cachedInnerPath[i])) return true;
        }
        return false;
    }

    private bool IsInsideTriangle(Vector2 point, float? height, Vector3 a, Vector3 b, Vector3 c)
    {
        Vector2 ab = ToXZ(b - a);
        Vector2 ac = ToXZ(c - a);
        Vector2 ap = point - ToXZ(a);
        float determinant = ab.x * ac.y - ab.y * ac.x;
        if (Mathf.Abs(determinant) < 0.000001f) return false;
        float u = (ap.x * ac.y - ap.y * ac.x) / determinant;
        float v = (ab.x * ap.y - ab.y * ap.x) / determinant;
        const float tolerance = 0.00001f;
        if (u < -tolerance || v < -tolerance || u + v > 1f + tolerance) return false;
        float surfaceHeight = a.y + u * (b.y - a.y) + v * (c.y - a.y);
        return !height.HasValue || Mathf.Abs(height.Value - surfaceHeight) <= Mathf.Max(0f, verticalTolerance);
    }

    public Vector2 GetNearestPointOnCenterLine(Vector2 point)
    {
        EnsureCache();
        Vector2 nearest = point;
        float bestDistance = float.PositiveInfinity;
        for (int i = 1; i < cachedCenterPath.Count; i++)
        {
            Vector2 start = ToXZ(cachedCenterPath[i - 1]);
            Vector2 delta = ToXZ(cachedCenterPath[i]) - start;
            float t = delta.sqrMagnitude > Mathf.Epsilon ?
                Mathf.Clamp01(Vector2.Dot(point - start, delta) / delta.sqrMagnitude) : 0f;
            Vector2 candidate = start + delta * t;
            float distance = (point - candidate).sqrMagnitude;
            if (distance >= bestDistance) continue;
            bestDistance = distance;
            nearest = candidate;
        }
        return nearest;
    }

    /// <summary>高さを含めて最寄りの区間を選び、実際の中心線の高さを返します。</summary>
    public Vector3 GetNearestPointOnCenterLineWorld(Vector3 worldPosition)
    {
        return TryGetNearestSegment(worldPosition, out int index, out float t)
            ? Vector3.Lerp(cachedCenterPath[index - 1], cachedCenterPath[index], t) : worldPosition;
    }

    public float GetProgressDistance(Vector3 worldPosition)
    {
        if (!TryGetNearestSegment(worldPosition, out int index, out float t)) return 0f;
        float progress = Mathf.Lerp(cachedCumulativeDistances[index - 1], cachedCumulativeDistances[index], t);
        return closedLoop && TotalLength > Mathf.Epsilon ? Mathf.Repeat(progress, TotalLength) : progress;
    }

    public bool TryGetPointAtProgress(float progressDistance, out Vector3 point)
    {
        bool success = TryGetSampleAtProgress(progressDistance, out CourseSample sample);
        point = sample.position;
        return success;
    }

    /// <summary>閉路では距離を周回、開路では端点へ制限。装飾を等間隔に配置するためのAPI。</summary>
    public bool TryGetSampleAtProgress(float progressDistance, out CourseSample sample)
    {
        sample = default;
        EnsureCache();
        int count = cachedCenterPath.Count;
        if (count < 2 || TotalLength <= Mathf.Epsilon) return false;
        float distance = closedLoop ? Mathf.Repeat(progressDistance, TotalLength) :
            Mathf.Clamp(progressDistance, 0f, TotalLength);
        int low = 1;
        int high = count - 1;
        while (low < high)
        {
            int middle = (low + high) / 2;
            if (cachedCumulativeDistances[middle] <= distance) low = middle + 1;
            else high = middle;
        }
        // 末尾に重複点があっても、最後の有効な区間から端点を取得する。
        while (low > 1 && cachedCumulativeDistances[low] - cachedCumulativeDistances[low - 1] <= Mathf.Epsilon) low--;
        float length = cachedCumulativeDistances[low] - cachedCumulativeDistances[low - 1];
        if (length <= Mathf.Epsilon) return false;
        float t = Mathf.Clamp01((distance - cachedCumulativeDistances[low - 1]) / length);
        sample.position = Vector3.Lerp(cachedCenterPath[low - 1], cachedCenterPath[low], t);
        sample.forward = (cachedCenterPath[low] - cachedCenterPath[low - 1]).normalized;
        sample.leftEdge = Vector3.Lerp(cachedInnerPath[low - 1], cachedInnerPath[low], t);
        sample.rightEdge = Vector3.Lerp(cachedOuterPath[low - 1], cachedOuterPath[low], t);
        sample.right = (sample.rightEdge - sample.leftEdge).normalized;
        sample.up = Vector3.Cross(sample.forward, sample.right).normalized;
        sample.width = Mathf.Lerp(cachedWidthPath[low - 1], cachedWidthPath[low], t);
        return true;
    }

    public bool TryGetNearestCenterLineDirection(Vector3 worldPosition, out Vector3 direction)
    {
        direction = Vector3.zero;
        if (!TryGetNearestSegment(worldPosition, out int index, out _)) return false;
        direction = (cachedCenterPath[index] - cachedCenterPath[index - 1]).normalized;
        return true;
    }

    private bool TryGetNearestSegment(Vector3 point, out int index, out float t)
    {
        EnsureCache();
        index = -1;
        t = 0f;
        float bestDistance = float.PositiveInfinity;
        for (int i = 1; i < cachedCenterPath.Count; i++)
        {
            Vector3 start = cachedCenterPath[i - 1];
            Vector3 delta = cachedCenterPath[i] - start;
            if (delta.sqrMagnitude <= Mathf.Epsilon) continue;
            float candidateT = Mathf.Clamp01(Vector3.Dot(point - start, delta) / delta.sqrMagnitude);
            float distance = (point - (start + delta * candidateT)).sqrMagnitude;
            if (distance >= bestDistance) continue;
            bestDistance = distance;
            index = i;
            t = candidateT;
        }
        return index >= 1;
    }

    public void CopyCenterPathWorld(List<Vector3> destination)
    {
        if (destination == null) return;
        EnsureCache();
        destination.Clear();
        destination.AddRange(cachedCenterPath);
    }

    public void CopyCourseBandWorld(List<Vector3> innerDestination, List<Vector3> outerDestination)
    {
        if (innerDestination == null || outerDestination == null) return;
        EnsureCache();
        innerDestination.Clear();
        innerDestination.AddRange(cachedInnerPath);
        outerDestination.Clear();
        outerDestination.AddRange(cachedOuterPath);
    }

    public void RebuildCache()
    {
        cachedCenterPath.Clear();
        cachedWidthPath.Clear();
        cachedInnerPath.Clear();
        cachedOuterPath.Clear();
        cachedCumulativeDistances.Clear();
        if (waypoints != null && waypoints.Length >= 2)
        {
            BuildCenterPath();
            float distance = 0f;
            cachedCumulativeDistances.Add(0f);
            for (int i = 1; i < cachedCenterPath.Count; i++)
            {
                distance += Vector3.Distance(cachedCenterPath[i - 1], cachedCenterPath[i]);
                cachedCumulativeDistances.Add(distance);
            }
            BuildOffsetPaths();
        }
        cacheDirty = false;
        cachedPosition = transform.position;
        cachedRotation = transform.rotation;
        cachedScale = transform.lossyScale;
    }

    private void EnsureCache()
    {
        if (cacheDirty || cachedPosition != transform.position || cachedRotation != transform.rotation ||
            cachedScale != transform.lossyScale) RebuildCache();
    }

    private void BuildCenterPath()
    {
        int segments = Mathf.Clamp(curveSegments, 1, 100);
        int count = closedLoop ? waypoints.Length : waypoints.Length - 1;
        for (int i = 0; i < count; i++)
        {
            Waypoint start = waypoints[i];
            Waypoint end = waypoints[(i + 1) % waypoints.Length];
            for (int step = i == 0 ? 0 : 1; step <= segments; step++)
            {
                float t = step / (float)segments;
                // ローカル空間で曲率を計算するため、オブジェクトの回転にも追従する。
                Vector3 local = EvaluateEllipticSegmentPoint(start.LocalPosition, end.LocalPosition, start.curve, t);
                cachedCenterPath.Add(transform.TransformPoint(local));
                cachedWidthPath.Add(Mathf.Max(0f, Mathf.Lerp(start.width, end.width, Mathf.SmoothStep(0f, 1f, t))));
            }
        }
    }

    private void BuildOffsetPaths()
    {
        Vector3 fallback = Vector3.right;
        int last = cachedCenterPath.Count - 1;
        for (int i = 0; i <= last; i++)
        {
            Vector3 tangent;
            if (closedLoop && last > 1 && (i == 0 || i == last))
                tangent = cachedCenterPath[1] - cachedCenterPath[last - 1];
            else if (i == 0) tangent = cachedCenterPath[1] - cachedCenterPath[0];
            else if (i == last) tangent = cachedCenterPath[last] - cachedCenterPath[last - 1];
            else tangent = cachedCenterPath[i + 1] - cachedCenterPath[i - 1];
            Vector3 lateral = Vector3.Cross(Vector3.up, tangent).normalized;
            if (lateral.sqrMagnitude <= Mathf.Epsilon) lateral = fallback;
            else fallback = lateral;
            float halfWidth = cachedWidthPath[i] * 0.5f;
            cachedInnerPath.Add(cachedCenterPath[i] - lateral * halfWidth);
            cachedOuterPath.Add(cachedCenterPath[i] + lateral * halfWidth);
        }
    }

    private static Vector2 ToXZ(Vector3 point) => new Vector2(point.x, point.z);

    private static void DrawPolyline(List<Vector3> points)
    {
        for (int i = 1; i < points.Count; i++) Gizmos.DrawLine(points[i - 1], points[i]);
    }

    private static Vector3 EvaluateEllipticSegmentPoint(Vector3 start, Vector3 end, float curve, float t)
    {
        // 端点を正確に一致させ、閉路や重複点で浮動小数の隙間を作らない。
        if (t <= 0f) return start;
        if (t >= 1f) return end;
        Vector3 chord = end - start;
        float length = chord.magnitude;
        if (length <= Mathf.Epsilon) return start;
        Vector3 normal = Vector3.Cross(Vector3.up, chord / length).normalized;
        if (normal.sqrMagnitude <= Mathf.Epsilon) normal = Vector3.right;
        float angle = (1f - t) * Mathf.PI;
        return (start + end) * 0.5f + chord * (Mathf.Cos(angle) * 0.5f) + normal * (Mathf.Sin(angle) * curve);
    }
}
