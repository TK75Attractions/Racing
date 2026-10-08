using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

public static class RaceCourseValidation
{
    [MenuItem("Racing/Validate Race Course")]
    public static void Run()
    {
        GameObject root = new GameObject("Race course validation");
        try
        {
            RaceCourse course = root.AddComponent<RaceCourse>();
            ValidateLegacy(course);
            ValidateSlopesAndWidth(course);
            ValidateCrossing(course);
            ValidateTransformAndUndo(course);
            ValidateEmpty(course);
            ValidateEditorOperations(course);
            ValidateOffCourseState(course);
            Debug.Log("RACE_COURSE_VALIDATION_PASS: legacy, XZ band/airborne containment, slopes, 3D progress, crossings, samples, transforms, undo, empty path, editor insertion/strokes, LapManager detection/respawn.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    private static void ValidateLegacy(RaceCourse course)
    {
        // 高さフィールドを持たない既存の serialized データを読み込む。
        JsonUtility.FromJsonOverwrite("{\"waypoints\":[{\"position\":{\"x\":0,\"y\":0},\"width\":10},{\"position\":{\"x\":100,\"y\":0},\"width\":10},{\"position\":{\"x\":100,\"y\":100},\"width\":10},{\"position\":{\"x\":0,\"y\":100},\"width\":10}]}", course);
        course.RebuildCache();
        Near(course.TotalLength, 400f, "Legacy length");
        Require(course.ClosedLoop, "Legacy course must remain closed.");
        Require(course.IsPointInsideCourse(new Vector2(50f, 0f)), "Legacy XZ API");
        Require(!course.IsPointInsideCourse(new Vector2(50f, 50f)), "Loop interior is not road.");
        course.TryGetPointAtProgress(100f, out Vector3 quarter);
        course.TryGetPointAtProgress(500f, out Vector3 wrapped);
        course.TryGetPointAtProgress(-300f, out Vector3 negative);
        Near(quarter, new Vector3(100f, 0f, 0f), "Quarter lap");
        Near(quarter, wrapped, "Wrapped distance");
        Near(quarter, negative, "Negative distance");
        var left = new List<Vector3>();
        var right = new List<Vector3>();
        course.CopyCourseBandWorld(left, right);
        Near(left[0], left[left.Count - 1], "Left seam");
        Near(right[0], right[right.Count - 1], "Right seam");
    }

    private static void ValidateSlopesAndWidth(RaceCourse course)
    {
        SetPoints(course, new[] { Vector3.zero, new Vector3(100f, 20f, 0f) }, false, new[] { 10f, 30f });
        Near(course.TotalLength, Mathf.Sqrt(10400f), "Slope length includes height");
        Vector3 midpoint = new Vector3(50f, 10f, 0f);
        Require(course.IsPointInsideCourse(midpoint + Vector3.up), "Slope car above road");
        Require(course.IsPointInsideCourse(midpoint + Vector3.up * 100f), "Airborne car stays inside");
        Require(course.IsPointInsideCourse(midpoint - Vector3.up * 100f), "Containment ignores height below road");
        Require(course.IsPointInsideCourse(midpoint + Vector3.forward * 9.9f), "Interpolated width inside");
        Require(!course.IsPointInsideCourse(midpoint + Vector3.forward * 10.1f), "Interpolated width outside");
        Require(!course.IsPointInsideCourse(midpoint + Vector3.forward * 10.1f + Vector3.up * 100f),
            "Airborne car outside width stays outside");
        Near(course.GetProgressDistance(midpoint), course.TotalLength * 0.5f, "3D slope progress");
        Near(course.GetNearestPointOnCenterLineWorld(midpoint + Vector3.forward * 30f), midpoint, "Nearest center has course height");
        Require(course.TryGetSampleAtProgress(course.TotalLength * 0.5f, out RaceCourse.CourseSample sample), "Decoration sample");
        Near(sample.position, midpoint, "Sample position");
        Near(sample.width, 20f, "Sample width");
        Require(sample.forward.y > 0.1f, "Sample slope direction");
        Near(Vector3.Dot(sample.forward, sample.up), 0f, "Sample normal");
        Near(Vector3.Distance(sample.leftEdge, sample.rightEdge), 20f, "Sample edges");
        Require(course.IsPointInsideCourse(sample.leftEdge), "Visible left edge is inside");
        Require(course.IsPointInsideCourse(sample.rightEdge), "Visible right edge is inside");
        Require(course.IsPointInsideCourse(sample.leftEdge + Vector3.up * 100f), "Airborne left edge is inside");
        Require(course.IsPointInsideCourse(sample.rightEdge + Vector3.up * 100f), "Airborne right edge is inside");
        course.TryGetPointAtProgress(-100f, out Vector3 before);
        course.TryGetPointAtProgress(10000f, out Vector3 after);
        Near(before, Vector3.zero, "Open start clamps");
        Near(after, new Vector3(100f, 20f, 0f), "Open end clamps");
        Require(!course.IsPointInsideCourse(new Vector3(-1f, 0f, 0f)), "Open end has no invisible extension");
    }

    private static void ValidateCrossing(RaceCourse course)
    {
        SetPoints(course, new[] {
            new Vector3(-20f, 0f, 0f), new Vector3(20f, 0f, 0f),
            new Vector3(20f, 20f, -20f), new Vector3(0f, 20f, -20f), new Vector3(0f, 20f, 20f)
        }, false);
        Require(course.IsPointInsideCourse(new Vector3(0f, 1f, 0f)), "Lower crossing road");
        Require(course.IsPointInsideCourse(new Vector3(0f, 21f, 0f)), "Upper crossing road");
        Require(course.IsPointInsideCourse(new Vector3(0f, 10f, 0f)), "Crossing containment ignores height");
        Near(course.GetProgressDistance(Vector3.zero), 20f, "Lower crossing progress");
        Require(course.GetProgressDistance(new Vector3(0f, 20f, 0f)) > 90f, "Upper crossing progress must use upper segment.");
        Near(course.GetNearestPointOnCenterLineWorld(new Vector3(0f, 20f, 0f)), new Vector3(0f, 20f, 0f), "Upper nearest height");
        Require(course.TryGetNearestCenterLineDirection(new Vector3(0f, 20f, 0f), out Vector3 direction), "Crossing direction");
        Near(direction, Vector3.forward, "Upper direction");
    }

    private static void ValidateTransformAndUndo(RaceCourse course)
    {
        SetPoints(course, new[] { Vector3.zero, new Vector3(100f, 20f, 0f) }, false);
        course.transform.SetPositionAndRotation(new Vector3(300f, 50f, -100f), Quaternion.Euler(0f, 70f, 0f));
        course.transform.localScale = new Vector3(2f, 3f, 4f);
        Vector3 midpoint = course.transform.TransformPoint(new Vector3(50f, 10f, 0f));
        Near(course.GetNearestPointOnCenterLineWorld(midpoint), midpoint, "Transform invalidates cache");
        Require(course.IsPointInsideCourse(midpoint), "Transformed band containment");
        Require(course.TryGetSampleAtProgress(course.TotalLength * 0.5f, out RaceCourse.CourseSample sample), "Transformed sample");
        Near(sample.width, 10f, "Width stays in world units");
        Near(Vector3.Distance(sample.leftEdge, sample.rightEdge), 10f, "Transformed world width");
        float length = course.TotalLength;
        Undo.IncrementCurrentGroup();
        int group = Undo.GetCurrentGroup();
        SerializedObject serialized = new SerializedObject(course);
        serialized.FindProperty("waypoints").GetArrayElementAtIndex(1).FindPropertyRelative("height").floatValue = 40f;
        serialized.ApplyModifiedProperties();
        Undo.FlushUndoRecordObjects();
        course.RebuildCache();
        Require(course.TotalLength > length, "Height edit updates length");
        Undo.CollapseUndoOperations(group);
        Undo.PerformUndo();
        course.RebuildCache();
        Near(course.TotalLength, length, "Height undo restores geometry");
        Undo.ClearUndo(course);
        course.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        course.transform.localScale = Vector3.one;
    }

    private static void ValidateEmpty(RaceCourse course)
    {
        SetPoints(course, Array.Empty<Vector3>(), false);
        Require(!course.HasValidPath && !course.IsPointInsideCourse(Vector3.zero), "Empty path must be invalid.");
        Require(!course.TryGetSampleAtProgress(0f, out _), "Empty sample");
        SetPoints(course, new[] { Vector3.zero, Vector3.zero, Vector3.zero }, false);
        Require(!course.HasValidPath, "Duplicate-only path must be invalid.");
        SetPoints(course, new[] { Vector3.zero, new Vector3(100f, 0f, 0f), new Vector3(100f, 0f, 0f) }, false);
        Require(course.TryGetPointAtProgress(course.TotalLength, out Vector3 endpoint), "Trailing duplicate must still resolve endpoint.");
        Near(endpoint, new Vector3(100f, 0f, 0f), "Trailing duplicate endpoint");
    }

    private static void ValidateEditorOperations(RaceCourse course)
    {
        SetPoints(course, Array.Empty<Vector3>(), false);
        Editor editor = Editor.CreateEditor(course, typeof(RaceCourseEditor));
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        MethodInfo insert = typeof(RaceCourseEditor).GetMethod("InsertPoint", flags);
        MethodInfo remove = typeof(RaceCourseEditor).GetMethod("RemoveSelected", flags);
        try
        {
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            insert.Invoke(editor, new object[] { 0, Vector3.zero });
            insert.Invoke(editor, new object[] { 1, new Vector3(100f, 20f, 30f) });
            Undo.FlushUndoRecordObjects();
            Undo.CollapseUndoOperations(group);
            Require(course.HasValidPath, "Scene drawing must create a valid XYZ path.");
            Undo.PerformUndo();
            course.RebuildCache();
            Require(!course.HasValidPath, "A whole drawing stroke must undo together.");
            Undo.PerformRedo();
            course.RebuildCache();
            Require(course.HasValidPath, "Drawing stroke redo");
            insert.Invoke(editor, new object[] { 1, new Vector3(40f, 5f, 10f) });
            var center = new List<Vector3>();
            course.CopyCenterPathWorld(center);
            Require(center.Exists(p => Vector3.Distance(p, new Vector3(40f, 5f, 10f)) < 0.01f), "Inserted point keeps XYZ.");
            remove.Invoke(editor, null);
            Near(course.TotalLength, new Vector3(100f, 20f, 30f).magnitude, "Deleting selected point restores straight path.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(editor);
            Undo.ClearUndo(course);
        }
    }

    private static void ValidateOffCourseState(RaceCourse course)
    {
        SetPoints(course, new[] { Vector3.zero, new Vector3(100f, 20f, 0f) }, false);
        GameObject managerRoot = new GameObject("Course validation lap manager");
        GameObject carRoot = new GameObject("Course validation car");
        try
        {
            LapManager manager = managerRoot.AddComponent<LapManager>();
            SerializedObject serialized = new SerializedObject(manager);
            serialized.FindProperty("raceCourse").objectReferenceValue = course;
            serialized.FindProperty("respawnWhenOffCourse").boolValue = false;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            Rigidbody rb = carRoot.AddComponent<Rigidbody>();
            var data = new LapManager.CarTimeData { rb = rb, carName = carRoot.name };
            MethodInfo update = typeof(LapManager).GetMethod("UpdateCourseState", BindingFlags.Instance | BindingFlags.NonPublic);
            rb.position = new Vector3(50f, 11f, 0f);
            update.Invoke(manager, new object[] { data, 0.1f });
            Require(data.hasValidRacePosition && !data.isOffCourse, "LapManager accepts slope height.");
            rb.position = new Vector3(50f, 100f, 0f);
            update.Invoke(manager, new object[] { data, 3f });
            Require(data.hasValidRacePosition && !data.isOffCourse && data.offCourseTimer == 0f,
                "Airborne driving does not start off-course timer.");
            serialized.Update();
            serialized.FindProperty("respawnWhenOffCourse").boolValue = true;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            update.Invoke(manager, new object[] { data, 3f });
            Require(!data.isOffCourse && data.offCourseTimer == 0f,
                "Airborne driving does not respawn with automatic respawn enabled.");
            Near(rb.position, new Vector3(50f, 100f, 0f), "Airborne driving preserves position");
            // 復帰先を路面近くに戻し、横方向の逸脱とリスポーンを検証する。
            rb.position = new Vector3(50f, 11f, 0f);
            update.Invoke(manager, new object[] { data, 0.1f });
            serialized.Update();
            serialized.FindProperty("respawnWhenOffCourse").boolValue = false;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            rb.position = new Vector3(50f, 100f, 20f);
            update.Invoke(manager, new object[] { data, 3f });
            Require(data.isOffCourse && data.offCourseTimer >= 3f, "Detection remains active with automatic respawn disabled.");
            Near(rb.position, new Vector3(50f, 100f, 20f), "Disabled respawn preserves position");
            rb.position = new Vector3(50f, 100f, 0f);
            update.Invoke(manager, new object[] { data, 0.1f });
            Require(!data.isOffCourse && data.offCourseTimer == 0f, "Airborne return clears off-course state.");
            rb.position = new Vector3(50f, 11f, 0f);
            update.Invoke(manager, new object[] { data, 0.1f });
            rb.position = new Vector3(50f, 100f, 20f);
            update.Invoke(manager, new object[] { data, 3f });
            serialized.Update();
            serialized.FindProperty("respawnWhenOffCourse").boolValue = true;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            update.Invoke(manager, new object[] { data, 0.1f });
            Require(!data.isOffCourse && data.offCourseTimer == 0f && rb.position.y < 20f,
                "Enabled respawn returns to last valid road height.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(carRoot);
            UnityEngine.Object.DestroyImmediate(managerRoot);
        }
    }

    private static void SetPoints(RaceCourse course, Vector3[] positions, bool loop, float[] widths = null)
    {
        SerializedObject serialized = new SerializedObject(course);
        serialized.FindProperty("closedLoop").boolValue = loop;
        SerializedProperty points = serialized.FindProperty("waypoints");
        points.arraySize = positions.Length;
        for (int i = 0; i < positions.Length; i++)
        {
            SerializedProperty point = points.GetArrayElementAtIndex(i);
            point.FindPropertyRelative("position").vector2Value = new Vector2(positions[i].x, positions[i].z);
            point.FindPropertyRelative("height").floatValue = positions[i].y;
            point.FindPropertyRelative("width").floatValue = widths == null ? 10f : widths[i];
            point.FindPropertyRelative("curve").floatValue = 0f;
        }
        serialized.ApplyModifiedPropertiesWithoutUndo();
        course.RebuildCache();
    }

    private static void Near(float a, float b, string message) => Require(Mathf.Abs(a - b) < 0.01f, $"{message}: {a} != {b}");
    private static void Near(Vector3 a, Vector3 b, string message) => Require(Vector3.Distance(a, b) < 0.01f, $"{message}: {a} != {b}");
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
