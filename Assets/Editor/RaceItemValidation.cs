using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

// Unity -batchmode -executeMethod RaceItemValidation.Run -quit でも実行できます。
public static class RaceItemValidation
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    [MenuItem("Racing/Validate Race Items")]
    public static void Run()
    {
        ValidateItemTable();
        ValidateCourseProgressPoint();
        ValidateChargeCrystalCharge();
        ValidateStatusEffects();
        ValidateRocketAutopilot();
        Debug.Log("Race item validation passed.");
    }

    private static void ValidateItemTable()
    {
        RaceItemTable table = new RaceItemTable();
        bool[] trailerSeen = new bool[Enum.GetValues(typeof(RaceItemType)).Length];
        for (int step = 0; step < 1000; step++)
        {
            float random = step / 1000f;
            RaceItemType leader = table.Roll(1, true, random);
            Require(leader == RaceItemType.Shield || leader == RaceItemType.Oil,
                $"The leader must only get defensive items, but got {leader}.");
            RaceItemType alone = table.Roll(2, false, random);
            Require(alone != RaceItemType.Confuse && alone != RaceItemType.None,
                "Confuse must not be rolled when there is no opponent to affect.");
            trailerSeen[(int)table.Roll(2, true, random)] = true;
        }

        Require(trailerSeen[(int)RaceItemType.Shield] && trailerSeen[(int)RaceItemType.Rocket] &&
            trailerSeen[(int)RaceItemType.Oil] && trailerSeen[(int)RaceItemType.Confuse],
            "The trailing car must be able to roll every item.");
        Require(!trailerSeen[(int)RaceItemType.None], "A populated table must never roll None.");
        Require(table.Roll(2, true, 0.9999f) != RaceItemType.None, "The upper random bound must still pick an item.");
    }

    private static void ValidateCourseProgressPoint()
    {
        GameObject courseObject = new GameObject("Item course validation");
        try
        {
            RaceCourse course = courseObject.AddComponent<RaceCourse>();
            SerializedObject serialized = new SerializedObject(course);
            SerializedProperty waypoints = serialized.FindProperty("waypoints");
            Vector2[] corners = { new Vector2(0f, 0f), new Vector2(100f, 0f), new Vector2(100f, 100f), new Vector2(0f, 100f) };
            waypoints.arraySize = corners.Length;
            for (int i = 0; i < corners.Length; i++)
            {
                SerializedProperty waypoint = waypoints.GetArrayElementAtIndex(i);
                waypoint.FindPropertyRelative("position").vector2Value = corners[i];
                waypoint.FindPropertyRelative("curve").floatValue = 0f;
                waypoint.FindPropertyRelative("width").floatValue = 10f;
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
            course.RebuildCache();

            float total = course.TotalLength;
            Require(total > 1f, "The validation course must have a length.");
            Require(course.TryGetPointAtProgress(0f, out Vector3 start), "Progress zero must resolve to a point.");
            Require(course.TryGetPointAtProgress(total * 0.25f, out Vector3 quarter), "A quarter lap must resolve.");
            Require(course.TryGetPointAtProgress(total * 1.25f, out Vector3 wrapped), "Distances past a lap must wrap.");
            Require(course.TryGetPointAtProgress(-total * 0.75f, out Vector3 negative), "Negative distances must wrap.");
            NearVector(wrapped, quarter, "A lap and a quarter must land on the quarter point.");
            NearVector(negative, quarter, "Minus three quarters must land on the quarter point.");
            Near(course.GetProgressDistance(quarter), total * 0.25f, 0.5f,
                "The point must project back to the progress it was sampled from.");
            Require(Vector3.Distance(start, quarter) > 1f, "Different progress values must give different points.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(courseObject);
        }
    }

    private static void ValidateChargeCrystalCharge()
    {
        GameObject car = new GameObject("Charge crystal validation");
        try
        {
            DebugMover mover = car.AddComponent<DebugMover>();
            Require(mover.AddDriftCharge(0.5f), "A partial crystal must raise an empty charge.");
            Near(mover.NormalizedDriftCharge, 0.5f, 0.001f, "A half crystal must fill half the charge.");
            Require(mover.AddDriftCharge(1f), "A full crystal must top up a partial charge.");
            Require(mover.IsDriftChargeFull, "A full crystal must reach the last charge tier.");
            Require(!mover.AddDriftCharge(1f), "A full car must leave the crystal for the opponent.");
            mover.CancelDrift();
            Near(mover.NormalizedDriftCharge, 0f, 0.001f, "A spin must drop the stored charge.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(car);
        }
    }

    private static void ValidateStatusEffects()
    {
        GameObject car = CreateCar("Status effect validation", out DebugMover mover, out CarItemEffects effects);
        GameObject other = CreateCar("Status effect opponent", out _, out CarItemEffects opponent);
        try
        {
            int afflictions = 0;
            int uses = 0;
            effects.Afflicted += _ => afflictions++;
            effects.ItemUsed += _ => uses++;

            Require(effects.SteeringSign == 1f && effects.FrontGripMultiplier == 1f && effects.RearGripMultiplier == 1f,
                "A fresh car must drive normally.");

            opponent.UseItem(RaceItemType.Confuse, effects);
            Require(effects.IsConfused && effects.SteeringSign == -1f, "Confuse must invert the victim's steering.");
            Require(!opponent.IsConfused, "Confuse must not affect the user.");
            Require(afflictions == 1, "The victim HUD must be told about confuse.");

            Require(effects.ApplyOil(), "Stepping on oil must consume the slick.");
            Require(effects.IsOiled, "Oil must make the car slip.");
            Require(effects.RearGripMultiplier < effects.FrontGripMultiplier && effects.FrontGripMultiplier < 1f,
                "Oil must cut rear grip more than front grip.");

            mover.AddDriftCharge(1f);
            Require(effects.ApplySpin(Vector3.right), "An unshielded car must spin when hit by a shield.");
            Require(effects.IsSpinning && effects.BlocksDriverInput, "A spinning car must ignore the driver.");
            Require(effects.RearGripMultiplier < 0.5f, "A spinning car must lose grip.");
            Require(mover.NormalizedDriftCharge <= 0f, "A spin must cancel the stored drift charge.");
            Require(!effects.ApplySpin(Vector3.left), "A spinning car must not be spun again.");

            effects.ClearAll();
            Require(!effects.IsConfused && !effects.IsOiled && !effects.IsSpinning && !effects.BlocksDriverInput,
                "ClearAll must remove every status effect.");

            effects.UseItem(RaceItemType.Shield, opponent);
            Require(effects.IsShieldActive && uses == 1, "The shield must activate and notify the HUD.");
            Require(!effects.ApplyConfuse(), "A shield must block confuse.");
            Require(effects.ApplyOil() && !effects.IsOiled, "A shield must block oil while still consuming the slick.");
            Require(!effects.ApplySpin(Vector3.right), "A shielded car must not spin.");

            effects.ClearAll();
            effects.UseItem(RaceItemType.Oil, opponent);
            Require(uses == 2 && !effects.IsOiled, "Using oil must drop it behind instead of slipping the user.");
            effects.enabled = false;
            Require(!opponent.IsConfused && !effects.ApplyConfuse(), "A disabled (finished) car must ignore attacks.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(car);
            UnityEngine.Object.DestroyImmediate(other);
        }
    }

    private static void ValidateRocketAutopilot()
    {
        GameObject car = CreateCar("Rocket validation", out _, out CarItemEffects effects);
        try
        {
            Rigidbody body = car.GetComponent<Rigidbody>();
            body.useGravity = false;
            effects.UseItem(RaceItemType.Rocket, null);
            Require(effects.IsRocketActive, "The rocket must start immediately.");
            Require(effects.ApplyOil() && !effects.IsOiled, "Oil must not slow a rocketing car.");
            Require(!effects.ApplySpin(Vector3.right), "A rocketing car must not spin.");

            effects.ApplyRocketAutopilot(0.02f);
            float startSpeed = (float)typeof(CarItemEffects).GetField("rocketStartSpeed", PrivateInstance).GetValue(effects);
            Require(Vector3.ProjectOnPlane(body.linearVelocity, Vector3.up).magnitude >= startSpeed,
                "The rocket must launch the car to at least its start speed.");
            Require(Vector3.Dot(body.linearVelocity.normalized, car.transform.forward) > 0.99f,
                "Without a course the rocket must keep heading forward.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(car);
        }
    }

    private static GameObject CreateCar(string name, out DebugMover mover, out CarItemEffects effects)
    {
        GameObject car = new GameObject(name);
        car.AddComponent<Rigidbody>();
        mover = car.AddComponent<DebugMover>();
        effects = car.AddComponent<CarItemEffects>();
        // Edit モードでは Awake が呼ばれないため、実行時と同じ初期化を行います。
        typeof(DebugMover).GetMethod("Awake", PrivateInstance).Invoke(mover, null);
        typeof(CarItemEffects).GetMethod("Awake", PrivateInstance).Invoke(effects, null);
        return car;
    }

    private static void NearVector(Vector3 actual, Vector3 expected, string message) =>
        Require(Vector3.Distance(actual, expected) <= 0.05f, $"{message} Expected {expected}, got {actual}.");

    private static void Near(float actual, float expected, float tolerance, string message) =>
        Require(Mathf.Abs(actual - expected) <= tolerance, $"{message} Expected {expected}, got {actual}.");

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
