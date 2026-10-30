using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;

/// <summary>Regression checks for road recovery, serial reset shortcuts and single-step UI.</summary>
public static class RacingPolishValidation
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    [MenuItem("Racing/Validate Racing Polish")]
    public static void Run()
    {
        ValidateRecovery();
        ValidateTutorial();
        ValidateDeviceMenu();
        Material road = Resources.Load<Material>("RaceCourse/M_RoadAsphalt");
        Require(road != null && road.GetTexture("_BaseMap") != null && road.GetTexture("_BumpMap") != null,
            "Asphalt must have real albedo and normal maps.");
        Require(road.GetTextureScale("_BaseMap") == new Vector2(.5f, .5f), "Asphalt repeats every two meters.");
        PhysicsMaterial contact = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>("Assets/TestAddForce/New Physics Material.physicMaterial");
        Require(contact.frictionCombine == PhysicsMaterialCombine.Minimum && contact.dynamicFriction == 0f,
            "Road adhesion must not add collision friction on top of the explicit tire-force model.");
        Debug.Log("RACING_POLISH_VALIDATION_PASS: nearest road, jump-gap exclusion, slope clearance, lap state, current tutorial instruction and display-aware dropdowns.");
    }

    public static void RunBatch()
    {
        Run();
        RaceCourseRoadValidation.Run();
    }

    private static void ValidateRecovery()
    {
        GameObject root = new GameObject("Recovery QA");
        root.transform.position = new Vector3(2000f, 2000f, 2000f);
        GameObject car = null;
        try
        {
            RaceCourse course = root.AddComponent<RaceCourse>();
            SetPath(course, new[] { Vector3.zero, new Vector3(0, 0, 30), new Vector3(0, 0, 50), new Vector3(0, 0, 80) }, 1);
            Vector3 query = root.transform.position + new Vector3(8f, 2f, 42f);
            Require(course.TryGetNearestRoadSample(query, out RaceCourse.CourseSample sample), "Nearest road exists.");
            Require(sample.position.z >= root.transform.position.z + 53f, "Gap recovery chooses the nearest solid landing road, with car-length clearance.");
            Require(course.HasRoadAtProgress(course.GetProgressDistance(sample.position)), "Respawn never chooses a jump gap.");

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefab/car/car2.prefab");
            car = UnityEngine.Object.Instantiate(prefab, query, Quaternion.Euler(0f, 180f, 90f));
            Rigidbody body = car.GetComponent<Rigidbody>();
            CarResetter resetter = car.GetComponent<CarResetter>();
            Call(resetter, "Awake"); Call(resetter, "Start");
            LapManager laps = root.AddComponent<LapManager>();
            laps.RegisterCar(body);
            LapManager.CarTimeData data = laps.GetCarData(body);
            data.nextCheckpointIndex = 2; data.lastCheckpointIndex = 1; data.lapCount = 1;
            data.offCourseTimer = 1.9f; data.isOffCourse = true;
            resetter.SetCourse(course, laps);
            body.linearVelocity = Vector3.one * 10f; body.angularVelocity = Vector3.one;
            resetter.ResetCar(); Physics.SyncTransforms();
            Require(Vector3.Distance(body.position, sample.position) < 1.5f && body.position.y > sample.position.y, "Recovery uses the nearby road, just above its surface.");
            Require(Vector3.Dot(body.rotation * Vector3.up, sample.up) > .999f, "Recovery makes the car upright.");
            Require(body.linearVelocity == Vector3.zero && body.angularVelocity == Vector3.zero, "Recovery clears motion.");
            Require(data.nextCheckpointIndex == 2 && data.lastCheckpointIndex == 1 && data.lapCount == 1 && !data.isOffCourse && data.offCourseTimer == 0f,
                "Recovery preserves completed checkpoints and clears the off-course timer.");

            SetPath(course, new[] { Vector3.zero, new Vector3(0, 20, 40) }, -1);
            car.transform.SetPositionAndRotation(root.transform.position + new Vector3(6f, 8f, 20f), Quaternion.identity);
            Require(course.TryGetNearestRoadSample(car.transform.position, out sample), "Slope recovery sample.");
            resetter.ResetCar(); Physics.SyncTransforms();
            Require(Vector3.Dot(body.rotation * Vector3.up, sample.up) > .999f, "Car follows slope normal after recovery.");
            foreach (SphereCollider wheel in car.GetComponentsInChildren<SphereCollider>())
            {
                Vector3 scale = wheel.transform.lossyScale;
                float radius = wheel.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
                Require(Vector3.Dot(wheel.transform.TransformPoint(wheel.center) - sample.position, sample.up) >= radius + .035f,
                    "No tire embeds in the sloping road.");
            }
        }
        finally
        {
            if (car != null) UnityEngine.Object.DestroyImmediate(car);
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    public static IEnumerator ValidatePlayModeControls()
    {
        yield return ValidateResetKeys();
        yield return NoviceInputValidation.ValidateKeyboard();
        ValidateDeviceMenu();
        Debug.Log("CONTROLS_PLAYMODE_PASS: reset keys and dropdown popups/blockers on the owning display.");
    }

    private static IEnumerator ValidateResetKeys()
    {
        InputSettings.BackgroundBehavior background = InputSystem.settings.backgroundBehavior;
        InputSettings.EditorInputBehaviorInPlayMode editorInput = InputSystem.settings.editorInputBehaviorInPlayMode;
        InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        Keyboard keyboard = InputSystem.AddDevice<Keyboard>();
        var shared = new TwoPlayerSerialInputSource(new Esp32SerialConfiguration(), null);
        try
        {
            var p1 = new SharedPlayerDriveInputSource(shared, 0);
            var p2 = new SharedPlayerDriveInputSource(shared, 1);
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            yield return null;
            keyboard.MakeCurrent();
            _ = keyboard.spaceKey.wasPressedThisFrame; _ = keyboard.rightCtrlKey.wasPressedThisFrame; _ = keyboard.slashKey.wasPressedThisFrame;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Space));
            yield return null;
            Require(p1.CurrentState.resetPressed && !p2.CurrentState.resetPressed, "Space resets only P1, including serial mode.");
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Slash));
            yield return null;
            Require(!p1.CurrentState.resetPressed && p2.CurrentState.resetPressed, "Slash resets only P2, including serial mode.");
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.RightCtrl));
            yield return null;
            Require(!p1.CurrentState.resetPressed && p2.CurrentState.resetPressed, "Right Ctrl remains a P2 alias.");
            var gate = new PlayerDriveInputGate(p2); gate.SetBlocked(true);
            Require(!gate.CurrentState.resetPressed, "Pause/countdown still blocks reset input.");
            Debug.Log("RESET_KEYS_PLAYMODE_PASS: independent Space/Slash resets through serial inputs, blocked while paused.");
        }
        finally
        {
            shared.Dispose(); InputSystem.RemoveDevice(keyboard);
            InputSystem.settings.backgroundBehavior = background;
            InputSystem.settings.editorInputBehaviorInPlayMode = editorInput;
        }
    }

    private static void ValidateTutorial()
    {
        GameObject canvas = new GameObject("Tutorial UI QA", typeof(RectTransform), typeof(Canvas));
        DrivingTutorialUI ui = new DrivingTutorialUI(canvas.transform, 1);
        try
        {
            var lesson = new DrivingTutorialProgress();
            TMP_Text message = canvas.transform.Find("DrivingTutorial/CurrentInstruction/Message").GetComponent<TMP_Text>();
            Require(canvas.transform.Find("DrivingTutorial/Lessons") == null, "The legacy multi-lesson strip must stay retired; one compact current-action card is shown.");
            ui.Update(lesson, DriveInputState.Neutral, 0f, true);
            Require(message.text.Contains("まずペダル"), "Only pedal release is shown before starting.");
            lesson.Tick(.01f, true, 0f, 0f, 0f, 0f); ui.Update(lesson, DriveInputState.Neutral, 0f, true);
            Require(message.text == "ペダルを踏んで\n加速しよう", "Show acceleration first.");
            lesson.Tick(.7f, true, 1f, 0f, 4f, 0f); ui.Update(lesson, DriveInputState.Neutral, 4f, true);
            Require(message.text.Contains("右カーブ"), "Show the upcoming right curve only after actual acceleration.");
            lesson.Tick(.7f, true, 1f, 10f, 4f, 30f); ui.Update(lesson, DriveInputState.Neutral, 4f, true);
            Require(message.text.Contains("アイテム"), "Show item collection only after actual steering.");
            ui.Update(lesson, DriveInputState.Neutral, 4f, false);
            Require(message.text.Contains("接続"), "Disconnected controller replaces the action prompt.");
            Require(lesson.CollectItem(true), "A real pickup completes the third lesson.");
            ui.Update(lesson, DriveInputState.Neutral, 0f, true);
            Require(message.text.Contains("準備待ち"), "Complete player sees the wait prompt.");
        }
        finally { UnityEngine.Object.DestroyImmediate(canvas); }
    }

    private static void ValidateDeviceMenu()
    {
        GameObject outputObject = new GameObject("Output QA"); outputObject.SetActive(false);
        RaceAudioOutput output = outputObject.AddComponent<RaceAudioOutput>();
        FieldInfo instance = typeof(RaceAudioOutput).GetField("<Instance>k__BackingField", BindingFlags.Static | BindingFlags.NonPublic);
        RaceAudioOutput previous = RaceAudioOutput.Instance;
        GameObject canvasObject = new GameObject("Output menu QA", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
        Canvas canvas = canvasObject.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.targetDisplay = 1;
        try
        {
            instance.SetValue(null, output);
            Set(output, "available", true);
            Set(output, "<DeviceNames>k__BackingField", new[] { "USB speaker", "HDMI monitor", "Headphones" });
            int[] selected = (int[])Get(output, "selected"); selected[0] = 1;
            ((string[])Get(output, "status"))[0] = "HDMI monitor";
            InterruptionMenuUI menu = InterruptionMenuUI.Create(canvas.transform, null, null, null);
            menu.Show(false); Call(menu, "ShowOptions", true);
            TMP_Dropdown[] dropdowns = (TMP_Dropdown[])Get(menu, "deviceDropdowns");
            Require(dropdowns[0].options.Count == 4 && dropdowns[0].value == 2, "P1 list includes the selected output and output-off.");
            Require(dropdowns[1].options.Count == 3 && dropdowns[1].options.TrueForAll(option => option.text != "HDMI monitor"), "P2 cannot select P1's output.");
            Require(!output.SelectDevice(1, 1), "Duplicate assignment is rejected before any native audio call.");
            if (Application.isPlaying)
            {
                // This fixture opens a newly created control in the same frame, before Unity calls Start.
                typeof(TMP_Dropdown).GetMethod("Start", Private).Invoke(dropdowns[1], null);
                dropdowns[1].Show();
                Transform list = dropdowns[1].transform.Find("Dropdown List");
                Require(list != null && list.GetComponent<Canvas>().targetDisplay == 1, "Dropdown popup stays on P2's display.");
                Transform blocker = canvas.transform.Find("Blocker");
                Require(blocker != null && blocker.GetComponent<Canvas>().targetDisplay == 1, "Dropdown blocker stays on P2's display.");
            }
        }
        finally
        {
            Set(output, "available", false); instance.SetValue(null, previous);
            UnityEngine.Object.DestroyImmediate(canvasObject); UnityEngine.Object.DestroyImmediate(outputObject);
        }
    }

    private static void SetPath(RaceCourse course, Vector3[] positions, int jumpIndex)
    {
        var serialized = new SerializedObject(course);
        serialized.FindProperty("closedLoop").boolValue = false;
        serialized.FindProperty("cornerRoundingDistance").floatValue = 0f;
        serialized.FindProperty("slopeBlendDistance").floatValue = 0f;
        SerializedProperty points = serialized.FindProperty("waypoints"); points.arraySize = positions.Length;
        for (int i = 0; i < positions.Length; i++)
        {
            SerializedProperty point = points.GetArrayElementAtIndex(i);
            point.FindPropertyRelative("position").vector2Value = new Vector2(positions[i].x, positions[i].z);
            point.FindPropertyRelative("height").floatValue = positions[i].y;
            point.FindPropertyRelative("width").floatValue = 12f;
            point.FindPropertyRelative("curve").floatValue = 0f;
            point.FindPropertyRelative("jumpToNext").boolValue = i == jumpIndex;
        }
        serialized.ApplyModifiedPropertiesWithoutUndo(); course.RebuildCache(); course.RebuildRoad();
    }

    private static object Get(object target, string field) => target.GetType().GetField(field, Private).GetValue(target);
    private static void Set(object target, string field, object value) => target.GetType().GetField(field, Private).SetValue(target, value);
    private static void Call(object target, string method, params object[] args) => target.GetType().GetMethod(method, Private).Invoke(target, args);
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
