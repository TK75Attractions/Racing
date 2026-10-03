using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// RaceCourse の中心線からコースの直線区間を求め、アイテムボックスの列とチャージクリスタルを自動配置します。
/// 走行方向はスタートのチェックポイントの向きから LapManager と同じ規則で決めます。
/// </summary>
public static class RaceItemLayoutBuilder
{
    private const string RootName = "RaceItems";
    private const string ScenePath = "Assets/Scenes/SampleScene.unity";

    private const float SampleStep = 5f;
    private const float CornerAngle = 15f;
    // アイテムボックス列：この長さ以上の直線の途中に置き、列どうしはこの距離以上離します。
    private const float MinRowStraight = 180f;
    private const float RowFraction = 0.45f;
    private const float MinRowSpacing = 250f;
    private const float RowBoxSpacing = 6f;
    // チャージクリスタル：直線の終わり（次のコーナーの手前）で、コーナーの内側へずらして置きます。
    private const float MinCrystalStraight = 120f;
    private const float CrystalBeforeCorner = 35f;
    private const float CrystalInsideOffset = 7f;
    // スタート直後と直前、加速度盤の近くには置きません。
    private const float StartClearAfter = 80f;
    private const float StartClearBefore = 30f;
    private const float PadClearance = 15f;

    public struct Placement
    {
        public bool isRow;
        public Vector3 position;
        public Vector3 forward;
        public float distanceFromStart;
    }

    [MenuItem("Racing/Items/Auto Layout Items On Course")]
    public static void BuildInActiveScene()
    {
        List<Placement> placements = Build(true);
        if (placements == null) return;
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log($"Placed {CountRows(placements)} item box rows and {placements.Count - CountRows(placements)} charge crystals.");
    }

    /// <summary>
    /// Unity -batchmode -executeMethod RaceItemLayoutBuilder.RunBatch -quit [-itemCaptureDir パス]
    /// SampleScene に配置して保存し、指定があれば確認用の画像を書き出します。
    /// </summary>
    public static void RunBatch()
    {
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        List<Placement> placements = Build(false);
        if (placements == null) throw new InvalidOperationException("Item layout failed.");
        if (!EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene()))
            throw new InvalidOperationException("SampleScene could not be saved.");
        foreach (Placement placement in placements)
        {
            Debug.Log($"RACE_ITEM_LAYOUT {(placement.isRow ? "ROW" : "CRYSTAL")} " +
                $"start+{placement.distanceFromStart:0}m pos={placement.position:F1} forward={placement.forward:F2}");
        }

        string captureDirectory = GetArgument("-itemCaptureDir");
        if (!string.IsNullOrEmpty(captureDirectory)) RaceItemLayoutCapture.Capture(placements, captureDirectory);
        Debug.Log("RACE_ITEM_LAYOUT_DONE");
    }

    public static List<Placement> Build(bool recordUndo)
    {
        RaceCourse course = UnityEngine.Object.FindFirstObjectByType<RaceCourse>();
        CheckpointSensor start = FindStartCheckpoint();
        if (course == null || start == null)
        {
            Debug.LogError("RaceCourse とスタートのチェックポイント（index 0）がシーンに必要です。");
            return null;
        }

        course.RebuildCache();
        Physics.SyncTransforms();
        List<Placement> placements = Plan(course, start.transform);

        GameObject existing = GameObject.Find(RootName);
        if (existing != null)
        {
            if (recordUndo) Undo.DestroyObjectImmediate(existing);
            else UnityEngine.Object.DestroyImmediate(existing);
        }

        GameObject root = new GameObject(RootName);
        if (recordUndo) Undo.RegisterCreatedObjectUndo(root, "Auto layout race items");
        int rowNumber = 0;
        int crystalNumber = 0;
        foreach (Placement placement in placements)
        {
            Quaternion rotation = Quaternion.LookRotation(placement.forward, Vector3.up);
            if (placement.isRow)
            {
                GameObject row = new GameObject($"Item Box Row {++rowNumber}");
                row.transform.SetParent(root.transform, false);
                row.transform.SetPositionAndRotation(placement.position, rotation);
                for (int i = -1; i <= 1; i++)
                {
                    Vector3 offset = rotation * Vector3.right * i * RowBoxSpacing;
                    GameObject box = new GameObject($"Item Box {i + 2}");
                    box.transform.SetParent(row.transform, false);
                    box.transform.SetPositionAndRotation(SnapToGround(placement.position + offset), rotation);
                    box.AddComponent<ItemBox>();
                }
            }
            else
            {
                GameObject crystal = new GameObject($"Charge Crystal {++crystalNumber}");
                crystal.transform.SetParent(root.transform, false);
                crystal.transform.SetPositionAndRotation(placement.position, rotation);
                crystal.AddComponent<ChargeCrystal>();
            }
        }

        return placements;
    }

    private static List<Placement> Plan(RaceCourse course, Transform start)
    {
        float total = course.TotalLength;
        float startProgress = course.GetProgressDistance(start.position);
        float sign = 1f;
        if (course.TryGetNearestCenterLineDirection(start.position, out Vector3 courseDirection))
        {
            Vector3 startForward = Vector3.ProjectOnPlane(start.forward, Vector3.up).normalized;
            if (Vector3.Dot(startForward, courseDirection) < 0f) sign = -1f;
        }

        int count = Mathf.FloorToInt(total / SampleStep);
        Vector3[] points = new Vector3[count + 1];
        for (int i = 0; i <= count; i++)
            course.TryGetPointAtProgress(startProgress + sign * i * SampleStep, out points[i]);
        Vector3[] headings = new Vector3[count];
        for (int i = 0; i < count; i++)
            headings[i] = Vector3.ProjectOnPlane(points[i + 1] - points[i], Vector3.up).normalized;

        // スタートは直線の途中にあるため、最初のコーナーから区切り始めて、スタートをまたぐ直線を1本として扱います。
        int firstCorner = 0;
        for (int i = 1; i < count; i++)
        {
            if (Vector3.Angle(headings[i], headings[0]) > CornerAngle)
            {
                firstCorner = i;
                break;
            }
        }

        var straights = new List<(int start, int length, Vector3 heading)>();
        int segmentStart = firstCorner;
        Vector3 segmentHeading = headings[firstCorner];
        for (int step = 1; step <= count; step++)
        {
            int index = (firstCorner + step) % count;
            if (step < count && Vector3.Angle(headings[index], segmentHeading) <= CornerAngle) continue;
            straights.Add((segmentStart, step - (segmentStart - firstCorner + count) % count, segmentHeading));
            segmentStart = index;
            segmentHeading = headings[index];
        }

        AccelerationPad[] pads = UnityEngine.Object.FindObjectsByType<AccelerationPad>(FindObjectsSortMode.None);
        var placements = new List<Placement>();
        var rowDistances = new List<float>();
        for (int s = 0; s < straights.Count; s++)
        {
            var straight = straights[s];
            float length = straight.length * SampleStep;
            float startDistance = straight.start * SampleStep;
            Vector3 nextHeading = straights[(s + 1) % straights.Count].heading;
            float turn = Vector3.Cross(straight.heading, nextHeading).y;
            Vector3 right = Vector3.Cross(Vector3.up, straight.heading).normalized;
            Vector3 inside = right * (turn >= 0f ? 1f : -1f);

            if (length >= MinRowStraight)
            {
                float distance = Mathf.Repeat(startDistance + length * RowFraction, total);
                if (IsClearOfStart(distance, total) && IsSpaced(distance, rowDistances, total))
                {
                    Vector3 center = PointAt(course, startProgress, sign, distance);
                    if (IsClearOfPads(center, pads))
                    {
                        rowDistances.Add(distance);
                        placements.Add(new Placement
                        {
                            isRow = true, position = center, forward = straight.heading, distanceFromStart = distance
                        });
                    }
                }
            }

            if (length >= MinCrystalStraight)
            {
                float distance = Mathf.Repeat(startDistance + length - CrystalBeforeCorner, total);
                Vector3 position = PointAt(course, startProgress, sign, distance) + inside * CrystalInsideOffset;
                if (IsClearOfStart(distance, total) && IsClearOfPads(position, pads))
                {
                    placements.Add(new Placement
                    {
                        isRow = false, position = SnapToGround(position), forward = straight.heading,
                        distanceFromStart = distance
                    });
                }
            }
        }

        placements.Sort((a, b) => a.distanceFromStart.CompareTo(b.distanceFromStart));
        return placements;
    }

    private static Vector3 PointAt(RaceCourse course, float startProgress, float sign, float distance)
    {
        course.TryGetPointAtProgress(startProgress + sign * distance, out Vector3 point);
        return point;
    }

    /// <summary>中心線の少し上から下へ調べ、屋根や木を避けて路面の高さへ合わせます。</summary>
    private static Vector3 SnapToGround(Vector3 point)
    {
        Vector3 origin = point + Vector3.up * 6f;
        RaycastHit[] hits = Physics.RaycastAll(origin, Vector3.down, 40f, Physics.DefaultRaycastLayers,
            QueryTriggerInteraction.Ignore);
        float nearest = float.PositiveInfinity;
        Vector3 result = point;
        foreach (RaycastHit hit in hits)
        {
            if (hit.distance >= nearest || hit.collider.GetComponentInParent<RaceItemPickup>() != null) continue;
            nearest = hit.distance;
            result = hit.point;
        }

        if (float.IsPositiveInfinity(nearest)) Debug.LogWarning($"No ground below item position {point}.");
        return result;
    }

    private static bool IsClearOfStart(float distance, float total) =>
        distance >= StartClearAfter && distance <= total - StartClearBefore;

    private static bool IsSpaced(float distance, List<float> others, float total)
    {
        foreach (float other in others)
        {
            float gap = Mathf.Abs(distance - other);
            if (Mathf.Min(gap, total - gap) < MinRowSpacing) return false;
        }
        return true;
    }

    private static bool IsClearOfPads(Vector3 position, AccelerationPad[] pads)
    {
        foreach (AccelerationPad pad in pads)
        {
            Vector3 delta = Vector3.ProjectOnPlane(pad.transform.position - position, Vector3.up);
            if (delta.magnitude < PadClearance) return false;
        }
        return true;
    }

    private static CheckpointSensor FindStartCheckpoint()
    {
        CheckpointSensor selected = null;
        foreach (CheckpointSensor checkpoint in UnityEngine.Object.FindObjectsByType<CheckpointSensor>(FindObjectsSortMode.None))
        {
            if (checkpoint.CheckpointIndex != 0) continue;
            if (selected == null || checkpoint.transform.GetSiblingIndex() < selected.transform.GetSiblingIndex())
                selected = checkpoint;
        }
        return selected;
    }

    private static int CountRows(List<Placement> placements)
    {
        int rows = 0;
        foreach (Placement placement in placements) if (placement.isRow) rows++;
        return rows;
    }

    private static string GetArgument(string name)
    {
        string[] args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
            if (args[i] == name) return args[i + 1];
        return null;
    }
}
