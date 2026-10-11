using UnityEngine;
using System.Collections.Generic;

[ExecuteAlways]
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
        public bool jumpToNext;
        public bool preserveAlignment;
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

    [Header("走行線形")]
    [SerializeField, Min(0f), Tooltip("角を前後の距離で丸めます (World)。0で従来の中心線")]
    private float cornerRoundingDistance = 20f;
    [SerializeField, Min(0f), Tooltip("坂の入口・頂上の勾配を滑らかにつなぐ距離 (World)。0で従来の高さ")]
    private float slopeBlendDistance = 20f;
    [SerializeField, Min(0.25f), Tooltip("道路メッシュの最大サンプル間隔 (World)")]
    private float maximumSampleSpacing = 1.5f;

    [Header("道路生成")]
    [SerializeField] private bool generateRoad = true;
    [SerializeField] private RaceCourseRoad.Settings road = new RaceCourseRoad.Settings();

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
    private readonly List<int> cachedSegmentIndices = new List<int>();
    private readonly List<bool> cachedRoadSegments = new List<bool>();
    private bool cacheDirty = true;
    private bool roadDirty = true;
    private int cachedRoadLayer = -1;
    private Vector3 cachedPosition;
    private Quaternion cachedRotation;
    private Vector3 cachedScale;

    public bool ClosedLoop => closedLoop;
    public bool GeneratesRoad => generateRoad;
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

    private void OnEnable() => RebuildRoad();
    private void OnDisable()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.delayCall -= RefreshRoadInEditor;
#endif
        RaceCourseRoad.Clear(transform);
    }

    private void OnValidate()
    {
        cacheDirty = true;
        roadDirty = true;
        // OnValidate may run during deserialization. Create meshes on the editor main thread.
#if UNITY_EDITOR
        UnityEditor.EditorApplication.delayCall -= RefreshRoadInEditor;
        UnityEditor.EditorApplication.delayCall += RefreshRoadInEditor;
#endif
    }

#if UNITY_EDITOR
    private void RefreshRoadInEditor()
    {
        if (this != null && isActiveAndEnabled && roadDirty) RebuildRoad();
    }
#endif

    private void Update()
    {
        EnsureCache();
        if (roadDirty || cachedRoadLayer != gameObject.layer) RebuildRoad();
    }

    /// <summary>コース境界と同じ形状の路面・模様・非凸MeshColliderを再生成します。</summary>
    [ContextMenu("道路を再生成")]
    public void RebuildRoad()
    {
        EnsureCache();
        RaceCourseRoad.Clear(transform);
        if (generateRoad && isActiveAndEnabled && HasValidPath)
        {
            if (road == null) road = new RaceCourseRoad.Settings();
            RaceCourseRoad.Build(transform, cachedCenterPath, cachedInnerPath, cachedOuterPath,
                cachedCumulativeDistances, closedLoop, road, cachedRoadSegments);
        }
        roadDirty = false;
        cachedRoadLayer = gameObject.layer;
    }

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

    /// <summary>上から見たコース帯の範囲で判定します。</summary>
    public bool IsPointInsideCourse(Vector2 point) => IsInsideBand(point);

    /// <summary>高さを無視し、浮いていても上から見たコース帯の内側なら範囲内とします。</summary>
    public bool IsPointInsideCourse(Vector3 worldPosition) =>
        IsInsideBand(ToXZ(worldPosition));

    private bool IsInsideBand(Vector2 point)
    {
        EnsureCache();
        for (int i = 1; i < cachedInnerPath.Count; i++)
        {
            // 描画に使う左右の縁と同じ三角形をXZへ投影して判定する。
            if (IsInsideTriangle(point,
                    cachedInnerPath[i - 1], cachedOuterPath[i - 1], cachedInnerPath[i]) ||
                IsInsideTriangle(point,
                    cachedOuterPath[i - 1], cachedOuterPath[i], cachedInnerPath[i])) return true;
        }
        return false;
    }

    private static bool IsInsideTriangle(Vector2 point, Vector3 a, Vector3 b, Vector3 c)
    {
        Vector2 ab = ToXZ(b - a);
        Vector2 ac = ToXZ(c - a);
        Vector2 ap = point - ToXZ(a);
        float determinant = ab.x * ac.y - ab.y * ac.x;
        if (Mathf.Abs(determinant) < 0.000001f) return false;
        float u = (ap.x * ac.y - ap.y * ac.x) / determinant;
        float v = (ab.x * ap.y - ab.y * ap.x) / determinant;
        const float tolerance = 0.00001f;
        return u >= -tolerance && v >= -tolerance && u + v <= 1f + tolerance;
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

    /// <summary>ジャンプ区間は進捗用の中心線だけを持ち、路面を生成しません。</summary>
    public bool HasRoadAtProgress(float progressDistance)
    {
        EnsureCache();
        if (cachedCenterPath.Count < 2 || TotalLength <= Mathf.Epsilon) return false;
        float distance = closedLoop ? Mathf.Repeat(progressDistance, TotalLength) : Mathf.Clamp(progressDistance, 0f, TotalLength);
        int low = 0, high = cachedCenterPath.Count - 2;
        while (low < high)
        {
            int middle = (low + high) / 2;
            if (cachedCumulativeDistances[middle + 1] <= distance) low = middle + 1;
            else high = middle;
        }
        return cachedRoadSegments[low];
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

    public int GetWaypointInsertionIndexWorld(Vector3 worldPosition)
    {
        return TryGetNearestSegment(worldPosition, out int index, out _)
            ? Mathf.Min(waypoints.Length, cachedSegmentIndices[index - 1] + 1) : 0;
    }

    public void RebuildCache()
    {
        cachedCenterPath.Clear();
        cachedWidthPath.Clear();
        cachedInnerPath.Clear();
        cachedOuterPath.Clear();
        cachedCumulativeDistances.Clear();
        cachedSegmentIndices.Clear();
        cachedRoadSegments.Clear();
        if (waypoints != null && waypoints.Length >= 2)
        {
            BuildCenterPath();
            bool[] jumps = new bool[waypoints.Length];
            bool[] alignments = new bool[waypoints.Length];
            for (int i = 0; i < waypoints.Length; i++) jumps[i] = waypoints[i].jumpToNext;
            for (int i = 0; i < waypoints.Length; i++) alignments[i] = waypoints[i].preserveAlignment;
            if (cornerRoundingDistance > 0f || slopeBlendDistance > 0f)
                RaceCourseLineSmoothing.Apply(cachedCenterPath, cachedWidthPath, cachedSegmentIndices,
                    closedLoop, cornerRoundingDistance, slopeBlendDistance, maximumSampleSpacing, jumps, alignments);
            for (int i = 0; i < cachedCenterPath.Count; i++)
                cachedRoadSegments.Add(!jumps[cachedSegmentIndices[i]]);
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
        roadDirty = true;
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
                cachedSegmentIndices.Add(i);
            }
        }
        // Each entry owns the outgoing interval, including the interval after a waypoint.
        for (int i = 0; i < cachedSegmentIndices.Count - 1; i++)
            cachedSegmentIndices[i] = cachedSegmentIndices[i + 1];
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
