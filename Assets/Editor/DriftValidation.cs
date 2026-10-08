using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

// Unity -batchmode -executeMethod DriftValidation.Run -quit でも実行できます。
public static class DriftValidation
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    [MenuItem("Racing/Validate Drift")]
    public static void Run()
    {
        GameObject car = new GameObject("Drift validation");
        GameObject road = GameObject.CreatePrimitive(PrimitiveType.Cube);
        road.transform.position = new Vector3(0f, -.5f, 0f);
        road.transform.localScale = new Vector3(100f, 1f, 100f);
        try
        {
            DebugMover mover = car.AddComponent<DebugMover>();
            Rigidbody body = car.GetComponent<Rigidbody>();
            Set(mover, "rb", body);
            body.useGravity = false;
            for (int index = 0; index < 2; index++)
            {
                GameObject wheel = new GameObject("Validation wheel");
                wheel.transform.SetParent(car.transform, false);
                wheel.transform.localPosition = new Vector3(0f, .3f, index == 0 ? 1f : -1f);
                wheel.AddComponent<SphereCollider>().radius = .3f;
                TireForce tire = wheel.AddComponent<TireForce>();
                typeof(TireForce).GetField("isFrontTire", PrivateInstance).SetValue(tire, index == 0);
            }
            Invoke(mover, "RefreshTires");
            Physics.SyncTransforms();
            body.linearVelocity = Vector3.zero;
            Step(mover, 30f, 1f);
            Require(!mover.IsDrifting, "Stationary steering must not start a drift.");
            body.linearVelocity = Vector3.back * 15f;
            Step(mover, -30f, 1f);
            Require(!mover.IsDrifting, "Reversing must not start a drift.");
            body.linearVelocity = Vector3.forward * 15f;
            car.transform.position = Vector3.up * 2f;
            Physics.SyncTransforms();
            Step(mover, -30f, 1f);
            Require(!mover.IsDrifting, "Airborne steering must not start a drift.");
            car.transform.position = Vector3.zero;
            Physics.SyncTransforms();
            Step(mover, -30f, .1f);
            Step(mover, 0f, .02f);
            Require(!mover.IsDrifting, "A short steering spike must not start a drift.");

            Step(mover, 7.9f, 1f);
            Require(!mover.IsDrifting && mover.DriftCharge == 0f, "Small steering must not start a drift.");
            Step(mover, 8f, 1f);
            Require(mover.IsDrifting && mover.DriftCharge > 0f, "Threshold must start charging.");
            float smallCharge = mover.DriftCharge;
            Step(mover, 0f, 1f);
            Require(mover.IsDrifting, "Neutral must retain the drift direction.");
            Near(mover.DriftCharge, smallCharge, "Neutral must not add charge.");
            Near(Step(mover, -.1f, .1f), 0f, "Small opposite noise must not release the drift.");
            Require(mover.IsDrifting, "Noise near center must retain the drift.");
            Near(Step(mover, -2f, .02f), 0f, "Brief countersteering must not release the drift.");
            Step(mover, 0f, .02f);
            Near(Step(mover, -2f, 1f), smallCharge * 3f, "Countersteering must release proportional boost.");
            Require(!mover.IsDrifting && mover.DriftCharge == 0f, "Release must clear the drift and charge.");
            Near(Step(mover, -2f, 1f), 0f, "Charge must not release twice.");

            Step(mover, -30f, 1f);
            Require(mover.DriftCharge > smallCharge, "Larger steering must charge faster in either direction.");
            Step(mover, -300f, 100f);
            Near(mover.DriftCharge, 3f, "Charge must stop at its cap.");
            Near(mover.NormalizedDriftCharge, 1f, "Full charge monitor must be normalized.");
            Near(Step(mover, 30f, 1f), 9f, "Left-to-right release must use the full charge.");
            Require(!mover.IsDrifting, "Release must not immediately restart the opposite drift.");

            Step(mover, 30f, 0.5f);
            float halfSecondCharge = mover.DriftCharge;
            mover.SetInputSource(null);
            Step(mover, 30f, 0.25f);
            Step(mover, 30f, 0.25f);
            Near(mover.DriftCharge, halfSecondCharge, "Charging must be independent of timestep subdivision.");

            Step(mover, 30f, 1f);
            body.linearVelocity = Vector3.forward * 10f;
            Invoke(mover, "ApplyVelocityResistance");
            Near(Get(mover, "resistanceForce"), 7.5f, "Drift must increase resistance.");
            mover.SuppressInputAfterRespawn();
            Require(!mover.IsDrifting && mover.DriftCharge == 0f, "Respawn must discard charge.");
            Invoke(mover, "ApplyVelocityResistance");
            Near(Get(mover, "resistanceForce"), 6f, "Normal resistance must be restored.");

            Step(mover, 30f, 1f);
            Invoke(mover, "FixedUpdate");
            Require(!mover.IsDrifting && mover.DriftCharge == 0f, "Unavailable driving input must discard charge.");
            Step(mover, 30f, 1f);
            Invoke(mover, "OnDisable");
            Require(!mover.IsDrifting && mover.DriftCharge == 0f, "Disabling must discard charge.");
            ValidateSpinProtection(mover, body);
            ValidateTimedBoost(mover);
            Debug.Log("Drift validation passed.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(car);
            UnityEngine.Object.DestroyImmediate(road);
        }
    }

    private static void ValidateSpinProtection(DebugMover mover, Rigidbody body)
    {
        Set(mover, "inputSuppressedUntil", -1f);
        Set(mover, "appliedSteeringAngle", 30f);
        body.linearVelocity = Vector3.forward * 40f;
        var steering = typeof(DebugMover).GetMethod("GetProtectedSteeringAngle", PrivateInstance);
        float Evaluate(Vector3 velocity) => (float)steering.Invoke(mover, new object[] { Vector3.forward, velocity });
        Near(Evaluate(body.linearVelocity), 30f, "Normal steering must be unchanged outside drift protection.");
        Set(mover, "isDrifting", true);
        float right = Evaluate(body.linearVelocity);
        Require(right > 0f && right < 5f, "A fast drift must retain turning direction while limiting full lock.");
        Set(mover, "appliedSteeringAngle", -30f);
        Near(Evaluate(body.linearVelocity), -right, "Drift steering protection must be symmetric.");
        Near(Evaluate(Vector3.back * 40f), -30f, "Protection must not affect reverse steering.");
        body.transform.position = Vector3.up * 2f;
        Physics.SyncTransforms();
        Near(Evaluate(body.linearVelocity), -30f, "Protection must not steer an airborne car.");
        body.transform.position = Vector3.zero;
        Physics.SyncTransforms();
        Set(mover, "enableDriftSpinProtection", false);
        Near(Evaluate(body.linearVelocity), -30f, "Disabling the protection must restore the original steering.");
        Set(mover, "enableDriftSpinProtection", true);
        mover.CancelDrift();
        body.linearVelocity = Vector3.forward * 10f;
    }

    private static void ValidateTimedBoost(DebugMover mover)
    {
        Set(mover, "driftBoostDuration", 0.55f);
        Set(mover, "driftBoostAccelerationPerCharge", 4f);
        Step(mover, 30f, 1f);
        float expectedCharge = mover.DriftCharge;
        float acceleration = Step(mover, -2f, .1f);
        Near(acceleration, expectedCharge * 4f, "Inspector acceleration must scale the released charge.");
        acceleration = 4f;
        StartBoost(mover, acceleration);
        Near(ConsumeBoost(mover, 0.2f), 0.8f, "Boost must accelerate over elapsed time.");
        Near(Get(mover, "driftBoostTimeRemaining"), 0.35f, "Boost must retain remaining duration.");
        Near(ConsumeBoost(mover, 0.2f), 0.8f, "Boost must continue across physics frames.");
        Near(ConsumeBoost(mover, 0.2f), 0.6f, "Final frame must only apply the remaining duration.");
        Near(ConsumeBoost(mover, 1f), 0f, "Expired boost must stop accelerating.");
        StartBoost(mover, acceleration);
        Near(ConsumeBoost(mover, 1f), 2.2f, "Total boost must be independent of timestep size.");

        Set(mover, "driftBoostDuration", 0f);
        StartBoost(mover, acceleration);
        Near(ConsumeBoost(mover, 1f), 0f, "Zero duration must disable boost.");
        Set(mover, "driftBoostDuration", 2f);
        StartBoost(mover, 0f);
        Near(ConsumeBoost(mover, 1f), 0f, "Zero acceleration must not change speed.");
        StartBoost(mover, acceleration);
        ConsumeBoost(mover, 0.5f);
        StartBoost(mover, 2f);
        Near(Get(mover, "driftBoostTimeRemaining"), 2f, "New release must restart the configured duration.");
        Near(ConsumeBoost(mover, 0.5f), 1f, "New release must replace, not stack, acceleration.");

        mover.SuppressInputAfterRespawn();
        Near(ConsumeBoost(mover, 1f), 0f, "Respawn must cancel ongoing boost.");
        StartBoost(mover, acceleration);
        mover.SetInputSource(null);
        Near(ConsumeBoost(mover, 1f), 0f, "Input changes must cancel ongoing boost.");
        StartBoost(mover, acceleration);
        Invoke(mover, "FixedUpdate");
        Near(ConsumeBoost(mover, 1f), 0f, "Unavailable driving must cancel ongoing boost.");
        StartBoost(mover, acceleration);
        Invoke(mover, "OnDisable");
        Near(ConsumeBoost(mover, 1f), 0f, "Disabling must cancel ongoing boost.");
    }

    private static void StartBoost(DebugMover mover, float acceleration) =>
        typeof(DebugMover).GetMethod("StartDriftBoost", PrivateInstance)
            .Invoke(mover, new object[] { acceleration });

    private static float ConsumeBoost(DebugMover mover, float deltaTime) =>
        (float)typeof(DebugMover).GetMethod("ConsumeDriftBoost", PrivateInstance)
            .Invoke(mover, new object[] { deltaTime });

    private static float Step(DebugMover mover, float handle, float deltaTime)
    {
        Set(mover, "rawSteeringInput", handle);
        return (float)typeof(DebugMover).GetMethod("UpdateDrift", PrivateInstance)
            .Invoke(mover, new object[] { deltaTime });
    }

    private static void Set(DebugMover mover, string name, object value) =>
        typeof(DebugMover).GetField(name, PrivateInstance).SetValue(mover, value);

    private static float Get(DebugMover mover, string name) =>
        (float)typeof(DebugMover).GetField(name, PrivateInstance).GetValue(mover);

    private static void Invoke(DebugMover mover, string name) =>
        typeof(DebugMover).GetMethod(name, PrivateInstance).Invoke(mover, null);

    private static void Near(float actual, float expected, string message) =>
        Require(Mathf.Abs(actual - expected) < 0.0001f, message);

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
