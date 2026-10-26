using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

/// <summary>Focused production-component regressions for the October circuit refinements.</summary>
public static class CircuitDrivingRefinementValidation
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    [MenuItem("Racing/Validate Circuit Rocket Acceleration")]
    public static void RunRocket()
    {
        RaceItemValidation.Run();
        GameObject root = new GameObject("Rocket landing QA");
        GameObject inactive = new GameObject("Inactive rocket manager"); inactive.SetActive(false);
        Gmanager previous = Gmanager.Control;
        try
        {
            root.transform.position = new Vector3(10000f, 100f, 10000f);
            RaceCourse course = root.AddComponent<RaceCourse>();
            SetPath(course, new[] { Vector3.zero, Vector3.forward * 100, Vector3.forward * 112, Vector3.forward * 400 }, 1);
            Gmanager manager = inactive.AddComponent<Gmanager>(); manager.course = course; Gmanager.Control = manager;
            GameObject car = new GameObject("Rocket contact QA", typeof(Rigidbody), typeof(CarItemEffects));
            car.transform.SetParent(root.transform, false);
            var wheel = new GameObject("Contact probe", typeof(GroundCheck)); wheel.transform.SetParent(car.transform, false);
            Call(wheel.GetComponent<GroundCheck>(), "Awake");
            CarItemEffects effects = car.GetComponent<CarItemEffects>(); Call(effects, "Awake");
            Rigidbody body = car.GetComponent<Rigidbody>(); body.useGravity = false;
            // Contact uses a real raycast, including during autopilot when no TireForce tick runs.
            var floor = new GameObject("Landing", typeof(BoxCollider)); floor.transform.SetParent(root.transform, false);
            floor.transform.localPosition = new Vector3(0, -.5f, 256); floor.GetComponent<BoxCollider>().size = new Vector3(20, 1, 288);
            body.position = root.transform.position + new Vector3(0, .5f, 118); Physics.SyncTransforms();
            effects.UseItem(RaceItemType.Rocket, null);
            for (int i = 0; i < 100; i++) effects.ApplyRocketAutopilot(.02f);
            Require(Mathf.Abs(body.linearVelocity.magnitude * 3.6f - 240f) < .1f,
                "A grounded pickup 6m after the gap must reach 240 km/h, rather than remain at 79.2.");
            body.position = root.transform.position + new Vector3(0, 5, 118); Physics.SyncTransforms();
            effects.ApplyRocketAutopilot(.02f);
            Require(body.linearVelocity.magnitude <= 22.01f, "Airborne landing overshoot remains protected.");
            Set(effects, "rocketTimeRemaining", 0f); body.linearVelocity = new Vector3(0, -5, 65);
            effects.LimitRocketJumpMomentum();
            Require(Mathf.Abs(body.linearVelocity.z - 22) < .01f && body.linearVelocity.y == -5,
                "Expiry in flight retains the horizontal limit without slowing the fall.");
            effects.UseItem(RaceItemType.Rocket, null);
            body.position = root.transform.position + new Vector3(0, .5f, 85); Physics.SyncTransforms();
            effects.ApplyRocketAutopilot(.02f);
            Require(Vector3.ProjectOnPlane(body.linearVelocity, Vector3.up).magnitude <= 22.01f, "Takeoff braking remains active.");
            Debug.Log("CIRCUIT_ROCKET_PASS: landed pickup=240km/h; approach/flight=79.2km/h; expiry preserves fall; fresh contacts release cap.");
        }
        finally { Gmanager.Control = previous; UnityEngine.Object.DestroyImmediate(root); UnityEngine.Object.DestroyImmediate(inactive); }
    }

    private static void SetPath(RaceCourse course, Vector3[] positions, int jump)
    {
        var so = new SerializedObject(course);
        so.FindProperty("closedLoop").boolValue = false;
        so.FindProperty("generateRoad").boolValue = false;
        so.FindProperty("cornerRoundingDistance").floatValue = 0;
        so.FindProperty("slopeBlendDistance").floatValue = 0;
        var points = so.FindProperty("waypoints"); points.arraySize = positions.Length;
        for (int i = 0; i < positions.Length; i++)
        {
            var point = points.GetArrayElementAtIndex(i);
            point.FindPropertyRelative("position").vector2Value = new Vector2(positions[i].x, positions[i].z);
            point.FindPropertyRelative("height").floatValue = positions[i].y;
            point.FindPropertyRelative("width").floatValue = 20;
            point.FindPropertyRelative("jumpToNext").boolValue = i == jump;
        }
        so.ApplyModifiedPropertiesWithoutUndo(); course.RebuildCache();
    }
    private static void Set(object target, string name, object value) => target.GetType().GetField(name, Private).SetValue(target, value);
    private static void Call(object target, string name) => target.GetType().GetMethod(name, Private).Invoke(target, null);
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
