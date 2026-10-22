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
            SerializedObject legacySettings = new SerializedObject(course);
            legacySettings.FindProperty("cornerRoundingDistance").floatValue = 0f;
            legacySettings.FindProperty("slopeBlendDistance").floatValue = 0f;
            legacySettings.ApplyModifiedPropertiesWithoutUndo();
            ValidateLegacy(course);
            ValidateSlopesAndWidth(course);
            ValidateCrossing(course);
            ValidateTransformAndUndo(course);
            ValidateEmpty(course);
            ValidateEditorOperations(course);
            ValidateOffCourseState(course);
            ValidateBarrierDrivingArea(course);
            ValidateWallStuckRecovery(course);
            ValidateGoalOnlyLaps();
            Debug.Log("RACE_COURSE_VALIDATION_PASS: legacy, XZ band/airborne containment, slopes, 3D progress, crossings, samples, transforms, undo, empty path, editor insertion/strokes, LapManager detection/respawn.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    public static void ValidateGoalOnlyLaps()
    {
        SkillGameplayValidation.ValidateStartingLine();
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

    private static void ValidateBarrierDrivingArea(RaceCourse course)
    {
        Vector3 origin = new Vector3(10000f, 20f, 10000f);
        SetPoints(course, new[] { origin, origin + Vector3.forward * 100f }, false);
        GameObject root = new GameObject("Wall driving validation");
        try
        {
            LapManager manager = root.AddComponent<LapManager>();
            SerializedObject settings = new SerializedObject(manager);
            settings.FindProperty("raceCourse").objectReferenceValue = course;
            settings.FindProperty("respawnWhenOffCourse").boolValue = true;
            settings.ApplyModifiedPropertiesWithoutUndo();
            GameObject car = new GameObject("Wall driving car");
            car.transform.SetParent(root.transform);
            Rigidbody body = car.AddComponent<Rigidbody>();
            body.isKinematic = true;
            var data = new LapManager.CarTimeData { rb = body, carName = car.name };
            MethodInfo update = typeof(LapManager).GetMethod("UpdateCourseState", BindingFlags.Instance | BindingFlags.NonPublic);
            foreach (int side in new[] { -1, 1 })
            {
                GameObject wall = new GameObject("Driving wall");
                wall.transform.SetParent(root.transform);
                RaceBarrierPath path = wall.AddComponent<RaceBarrierPath>();
                path.SetWorldPath(new[] { origin + Vector3.right * (side * 5f),
                    origin + Vector3.right * (side * 5f) + Vector3.forward * 100f }, side, false, null, null, true);
                MeshFilter visual = wall.GetComponentInChildren<MeshFilter>();
                Vector3[] vertices = visual.sharedMesh.vertices;
                // Test every section, including the point furthest out and the inward return.
                float maximumX = 0f;
                for (int section = 0; section <= 32; section++)
                {
                    Vector3 position = visual.transform.TransformPoint(vertices[section]) + Vector3.forward * 50f;
                    maximumX = Mathf.Max(maximumX, Mathf.Abs(position.x - origin.x));
                    Require(course.IsPointInsideDrivingArea(position), $"Wall section {side}/{section} is drivable.");
                    body.position = position;
                    data.isOffCourse = true;
                    data.offCourseTimer = 1f;
                    update.Invoke(manager, new object[] { data, 3f });
                    Require(!data.isOffCourse && data.offCourseTimer == 0f, "Wall driving clears the off-course timer.");
                    Near(body.position, position, "Wall driving must not respawn after the timeout");
                }
                Vector3 onWall = origin + new Vector3(side * 12f, 5f, 50f);
                Require(!course.IsPointInsideCourse(onWall) && course.IsPointInsideDrivingArea(onWall),
                    "The wall extends the driving area without changing the road band.");
                Vector3 beyond = origin + new Vector3(side * (maximumX + .2f), 5f, 50f);
                Require(!course.IsPointInsideDrivingArea(beyond), "The driving area ends at the wall's outer extent.");
                settings.Update();
                settings.FindProperty("respawnWhenOffCourse").boolValue = false;
                settings.ApplyModifiedPropertiesWithoutUndo();
                body.position = beyond;
                update.Invoke(manager, new object[] { data, 3f });
                Require(data.isOffCourse && data.offCourseTimer >= 3f, "Leaving the wall still starts off-course detection.");
                settings.Update();
                settings.FindProperty("respawnWhenOffCourse").boolValue = true;
                settings.ApplyModifiedPropertiesWithoutUndo();
                Require(!course.IsPointInsideDrivingArea(origin + new Vector3(side * 12f, 5f, -1f)),
                    "An open wall must not extend past its end.");
                path.Rebuild();
                Require(course.IsPointInsideDrivingArea(onWall), "Rebuilding preserves the wall driving area.");
                path.enabled = false;
                Require(!course.IsPointInsideDrivingArea(onWall), "Disabled barriers do not extend the driving area.");
                path.enabled = true;
                Require(course.IsPointInsideDrivingArea(onWall), "Re-enabling restores the wall driving area.");
                path.SetWorldPath(new[] { origin + Vector3.right * (side * 5f),
                    origin + Vector3.right * (side * 5f) + Vector3.forward * 100f }, side, false, null, null, false);
                Require(!course.IsPointInsideDrivingArea(onWall), "Visual-only barriers are not drivable.");
                UnityEngine.Object.DestroyImmediate(wall);
            }
            Debug.Log("WALL_DRIVING_CONTAINMENT_PASS: both sides, all 66 arc sections, timer/respawn, outer bounds, open ends, rebuild, disable/re-enable, visual-only walls.");
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }

    private sealed class RecoveryInput : IDriveInputSource
    {
        public int PlayerIndex => 0;
        public string DeviceId => "WALL_RECOVERY_VALIDATION";
        public bool IsConnected { get; set; } = true;
        public DriveInputState CurrentState { get; set; }
        public void UpdateInput(float dt) { }
        public void Dispose() { }
    }

    private static void ValidateWallStuckRecovery(RaceCourse course)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        Vector3 origin = new Vector3(10000f, 20f, 10000f);
        SetPoints(course, new[] { origin, origin + Vector3.forward * 100f }, false);
        GameObject root = new GameObject("Wall recovery validation");
        GameObject managerRoot = new GameObject("Inactive recovery game manager");
        managerRoot.SetActive(false);
        Gmanager previous = Gmanager.Control;
        try
        {
            Gmanager manager = managerRoot.AddComponent<Gmanager>();
            manager.state = Gmanager.State.Game;
            Gmanager.Control = manager;
            LapManager laps = root.AddComponent<LapManager>();
            typeof(LapManager).GetField("raceCourse", flags).SetValue(laps, course);
            GameObject wall = new GameObject("Recovery wall"); wall.transform.SetParent(root.transform);
            RaceBarrierPath path = wall.AddComponent<RaceBarrierPath>();
            path.SetWorldPath(new[] { origin + Vector3.right * 5f, origin + new Vector3(5f, 0f, 100f) }, 1, false, null, null, true);
            Collider wallCollider = wall.GetComponentInChildren<MeshCollider>();
            GameObject car = new GameObject("Wall recovery car", typeof(DebugMover), typeof(BoxCollider), typeof(CarResetter));
            car.transform.SetParent(root.transform);
            Rigidbody body = car.GetComponent<Rigidbody>(); body.useGravity = false;
            DebugMover mover = car.GetComponent<DebugMover>();
            typeof(DebugMover).GetMethod("Awake", flags).Invoke(mover, null);
            CarResetter resetter = car.GetComponent<CarResetter>();
            typeof(CarResetter).GetMethod("Awake", flags).Invoke(resetter, null);
            var input = new RecoveryInput { CurrentState = new DriveInputState { pedal = 1f } };
            resetter.SetInputSource(input);
            body.position = origin + new Vector3(0f, 1f, 30f);
            laps.RegisterCar(body); laps.GetCarData(body).lapCount = 1;
            resetter.SetCourse(course, laps);
            MethodInfo tick = typeof(CarResetter).GetMethod("FixedUpdate", flags);
            MethodInfo contact = typeof(CarResetter).GetMethod("RememberWallContact", flags, null,
                new[] { typeof(Collider), typeof(Vector3) }, null);
            Vector3 pinned = origin + new Vector3(12f, 5f, 30f);
            int frames = Mathf.CeilToInt(4.2f / Time.fixedDeltaTime);
            void Prepare()
            {
                typeof(CarResetter).GetMethod("OnDisable", flags).Invoke(resetter, null);
                typeof(DebugMover).GetField("inputSuppressedUntil", flags).SetValue(mover, float.NegativeInfinity);
                body.position = pinned; body.rotation = Quaternion.identity;
                body.WakeUp();
                input.CurrentState = new DriveInputState { pedal = 1f }; input.IsConnected = true;
            }
            void Step(bool touching = true)
            {
                if (touching) contact.Invoke(resetter, new object[] { wallCollider, Vector3.left });
                tick.Invoke(resetter, null);
            }
            Prepare();
            for (int i = 0; i < frames * 2; i++)
            { body.position = pinned + Vector3.forward * (i * Time.fixedDeltaTime * 2f); Step(); }
            Require(resetter.AutomaticRecoveryCount == 0, "Smooth wall driving must not trigger stuck recovery.");
            Prepare(); input.CurrentState = DriveInputState.Neutral;
            for (int i = 0; i < frames; i++) Step();
            Require(resetter.AutomaticRecoveryCount == 0, "Parking against a wall without input must not recover.");
            Prepare(); input.IsConnected = false;
            for (int i = 0; i < frames; i++) Step();
            Require(resetter.AutomaticRecoveryCount == 0, "Disconnected input must not cause wall recovery.");
            Prepare();
            contact.Invoke(resetter, new object[] { wallCollider, Vector3.left });
            typeof(CarResetter).GetField("lastWallContactTime", flags).SetValue(resetter, Time.fixedTime - .2f);
            for (int i = 0; i < frames; i++) Step(false);
            Require(resetter.AutomaticRecoveryCount == 0, "An old wall contact must not recover an airborne car.");
            GameObject support = new GameObject("Non-wall contact", typeof(BoxCollider));
            support.transform.SetParent(root.transform);
            Prepare();
            for (int i = 0; i < frames; i++)
            { contact.Invoke(resetter, new object[] { support.GetComponent<Collider>(), Vector3.up }); Step(false); }
            Require(resetter.AutomaticRecoveryCount == 0, "A flat floor must not count as a wall contact.");
            support.AddComponent<Rigidbody>().useGravity = false;
            Prepare();
            for (int i = 0; i < frames; i++)
            { contact.Invoke(resetter, new object[] { support.GetComponent<Collider>(), Vector3.left }); Step(false); }
            Require(resetter.AutomaticRecoveryCount == 0, "Contact with another dynamic car must not count as a wall.");
            Prepare();
            for (int i = 0; i < frames && resetter.AutomaticRecoveryCount == 0; i++)
            {
                body.position = pinned + Vector3.forward * (i % 2 == 0 ? .15f : -.15f);
                body.linearVelocity = Vector3.forward * 2f;
                Step();
            }
            Require(resetter.AutomaticRecoveryCount == 1 && resetter.RecoveryReason == "stuck" &&
                course.IsPointInsideCourse(body.position) && body.position.y < origin.y + 2f,
                "Wall jitter with no grounded tires must recover to the safe road.");
            Require(laps.GetCarData(body).lapCount == 1 && body.linearVelocity == Vector3.zero,
                "Recovery must preserve the lap and stop the trapped car's velocity.");
            Prepare();
            contact.Invoke(resetter, new object[] { wallCollider, Vector3.left });
            typeof(CarResetter).GetField("lastWallContactTime", flags).SetValue(resetter, Time.fixedTime - .2f);
            body.Sleep();
            Require(body.IsSleeping(), "The stationary wall case must exercise a sleeping rigidbody.");
            for (int i = 0; i < frames && resetter.AutomaticRecoveryCount == 1; i++) Step(false);
            Require(resetter.AutomaticRecoveryCount == 2, "Sleeping wall contacts must still trigger recovery without collision-stay messages.");
            Prepare(); input.CurrentState = new DriveInputState { pedal = -1f };
            GameObject blocker = new GameObject("Occupied recovery lanes", typeof(BoxCollider), typeof(Rigidbody));
            blocker.transform.SetParent(root.transform);
            blocker.GetComponent<Rigidbody>().isKinematic = true;
            blocker.GetComponent<BoxCollider>().size = new Vector3(20f, 4f, 10f);
            blocker.transform.position = origin + new Vector3(0f, 1f, 30f); Physics.SyncTransforms();
            for (int i = 0; i < frames; i++) Step();
            Require(resetter.AutomaticRecoveryCount == 2 && resetter.IsRecoveryPending,
                "Reverse input must request recovery but wait while the road is occupied.");
            UnityEngine.Object.DestroyImmediate(blocker); Physics.SyncTransforms(); Step();
            Require(resetter.AutomaticRecoveryCount == 3 && !resetter.IsRecoveryPending,
                "Pending wall recovery must complete as soon as a lane clears.");
            Debug.Log("WALL_STUCK_RECOVERY_PASS: smooth driving, neutral/disconnected input, stale contacts, ungrounded jitter, sleeping contacts, safe road/lap preservation, reverse input, occupied-road retry.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
            UnityEngine.Object.DestroyImmediate(managerRoot);
            Gmanager.Control = previous;
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
