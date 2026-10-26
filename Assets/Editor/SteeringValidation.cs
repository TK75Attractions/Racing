using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

// Unity -batchmode -executeMethod SteeringValidation.Run -quit でも実行できます。
public static class SteeringValidation
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    private sealed class StubInput : IDriveInputSource
    {
        public int PlayerIndex => 0;
        public string DeviceId => "VALIDATION";
        public bool IsConnected { get; set; } = true;
        public DriveInputState CurrentState { get; set; }
        public void UpdateInput(float deltaTime) { }
        public void Dispose() { }
    }

    private sealed class CalibratedInput : IDriveInputSource, IDriveSteeringCalibration
    {
        public int PlayerIndex => 0;
        public string DeviceId => "CALIBRATION";
        public bool IsConnected => true;
        public float FullSteeringInput { get; set; } = 90f / 6.5f;
        public DriveInputState CurrentState { get; set; }
        public void UpdateInput(float deltaTime) { }
        public void Dispose() { }
    }

    [MenuItem("Racing/Validate Steering")]
    public static void Run()
    {
        GameObject car = new GameObject("Steering validation");
        try
        {
            DebugMover mover = car.AddComponent<DebugMover>();
            Rigidbody body = car.GetComponent<Rigidbody>();
            Set(mover, "rb", body);

            StubInput input = new StubInput();
            mover.SetInputSource(input);
            Set(mover, "steeringDeadzone", 0f);
            Set(mover, "steeringResponseSeconds", 0f);

            // 初期設定：10 m/s から効き始め、30 m/s で 35% になる直線的な変化。
            Near(Steer(mover, body, input, 10f, 0f), 10f, "Standing still must use the full steering angle.");
            Near(mover.SteeringSpeedMultiplier, 1f, "Standing still must not reduce steering.");
            Near(Steer(mover, body, input, 10f, 10f), 10f, "Fade must not start below the fade start speed.");
            Near(Steer(mover, body, input, 10f, 20f), 6.75f, "Half of the fade range must apply half of the reduction.");
            Near(Steer(mover, body, input, 10f, 30f), 3.5f, "Full speed must apply the high speed multiplier.");
            Near(Steer(mover, body, input, 10f, 60f), .875f, "Beyond full speed must keep reducing steering with squared speed.");
            Require(Steer(mover, body, input, -10f, 30f) < 0f, "Fading must keep the steering direction.");

            float previous = float.MaxValue;
            for (float speed = 0f; speed <= 60f; speed += 2.5f)
            {
                float angle = Steer(mover, body, input, 10f, speed);
                Require(angle <= previous + 0.0001f, "Steering must never grow back as speed rises.");
                previous = angle;
            }

            Near(Steer(mover, body, input, 100f, 0f), 30f, "Max steering angle must still clamp the result.");
            Near(Get(mover, "rawSteeringInput"), 100f, "Raw handle input must stay unscaled for drift checks.");

            // 鋭さ：1 より大きいと低速側の効きを保ち、1 より小さいと早い段階から曲がりにくくする。
            Set(mover, "steeringFadeSharpness", 2f);
            Near(Steer(mover, body, input, 5f, 20f), 4.1875f, "A sharper curve must keep more steering at mid speed below the safety cap.");
            Near(Steer(mover, body, input, 5f, 30f), 1.75f, "A sharper curve must still reach the high speed multiplier.");
            Set(mover, "steeringFadeSharpness", 0.5f);
            Near(Steer(mover, body, input, 5f, 20f), Mathf.Lerp(1f, 0.35f, Mathf.Sqrt(0.5f)) * 5f,
                "A softer curve must reduce steering earlier.");
            Set(mover, "steeringFadeSharpness", 1f);

            // しきい値の逆転や同値を設定しても、開始速度を超えた時点で高速側の倍率になる。
            Set(mover, "steeringFadeFullSpeed", 5f);
            Near(Steer(mover, body, input, 10f, 10f), 10f, "An inverted range must not fade below the start speed.");
            Near(Steer(mover, body, input, 10f, 11f), 3.5f * Mathf.Pow(10.01f / 11f, 2f), "An inverted range must fade immediately and keep reducing above it.");
            Set(mover, "steeringFadeFullSpeed", 30f);

            Set(mover, "enableSpeedSensitiveSteering", false);
            Near(Steer(mover, body, input, 10f, 60f), 10f, "Disabling must keep the low speed steering at any speed.");
            Near(mover.SteeringSpeedMultiplier, 1f, "Disabling must report a neutral multiplier.");
            Set(mover, "enableSpeedSensitiveSteering", true);

            Steer(mover, body, input, 10f, 60f);
            mover.SuppressInputAfterRespawn();
            Near(Get(mover, "appliedSteeringAngle"), 0f, "Respawn must clear the steering angle.");
            Near(mover.SteeringSpeedMultiplier, 1f, "Respawn must reset the steering multiplier.");

            ValidateComfort(mover, body, input);
            ValidatePhysicalCalibration(mover, body, input);

            Debug.Log("Steering validation passed.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(car);
        }
    }

    private static void ValidatePhysicalCalibration(DebugMover mover, Rigidbody body, StubInput keyboard)
    {
        var serial = new CalibratedInput { CurrentState = new DriveInputState { steering = 90f / 6.5f } };
        var testMode = new P1SteeringTestInputSource(serial, keyboard) { Serial = serial };
        var lease = new DashboardInputLease();
        double now = 0;
        var dashboard = new DashboardDriveInputSource(testMode, lease, () => now);
        var live = new LiveDriveInputSource(0); live.SwitchSource(dashboard, false);
        var gate = new PlayerDriveInputGate(live); mover.SetInputSource(gate);
        Set(mover, "inputSuppressedUntil", -1f);
        Set(mover, "steeringDeadzone", 0f); Set(mover, "steeringResponseSeconds", 0f);
        Set(mover, "enableSpeedSensitiveSteering", false);
        body.linearVelocity = Vector3.zero;
        typeof(DebugMover).GetMethod("ReadInput", PrivateInstance).Invoke(mover, null);
        Near(mover.RawSteeringInput, 30f, "Physical 90-degree full lock must survive every live input adapter.");
        Near(mover.AppliedSteeringAngle, 30f, "Physical full lock must reach the selected maximum at rest.");
        foreach (var mode in new[] { P1SteeringTestMode.Normal, P1SteeringTestMode.KeyboardPedal, P1SteeringTestMode.ConstantPedal })
        {
            testMode.Mode = mode;
            Near(gate.FullSteeringInput, serial.FullSteeringInput, "Wheel-only modes must keep the serial calibration.");
        }
        serial.CurrentState = new DriveInputState { steering = -45f / 6.5f };
        typeof(DebugMover).GetMethod("ReadInput", PrivateInstance).Invoke(mover, null);
        Near(mover.RawSteeringInput, -15f, "Physical half lock must be symmetric before the speed safety cap.");
        Near(gate.CurrentState.steering, serial.CurrentState.steering, "Menus must retain original transport units.");
        var config = new Esp32SerialConfiguration();
        Near(config.SteeringLockDegrees, 90f, "Default physical full lock must be 90 degrees.");
        Near(config.FullSteeringInput * config.SteeringDivisor, 90f, "Transport division must not change physical calibration.");
        Near(DriveSteeringCalibration.CanonicalInput(float.NaN, 30f), 0f, "Invalid steering must remain neutral.");
        Near(DriveSteeringCalibration.CanonicalInput(10f, float.NaN), 10f, "Invalid calibration must fall back to canonical units.");
        testMode.Mode = P1SteeringTestMode.Normal;
        live.SwitchSource(keyboard, false);
        Near(gate.FullSteeringInput, 30f, "Switching to keyboard must restore canonical input units.");
        keyboard.CurrentState = new DriveInputState { steering = 10f };
        typeof(DebugMover).GetMethod("ReadInput", PrivateInstance).Invoke(mover, null);
        Near(mover.RawSteeringInput, 10f, "Keyboard and automatic drivers must retain their existing steering scale.");
    }

    private static void ValidateComfort(DebugMover mover, Rigidbody body, StubInput input)
    {
        float previousAngle = float.MaxValue, previousYaw = float.MaxValue;
        foreach (float speed in new[] { 10f, 20f, 30f, 40f, 50f, 60f, 90f, 120f })
        {
            float right = Steer(mover, body, input, 3600f, speed);
            float left = Steer(mover, body, input, -3600f, speed);
            Near(right, -left, "Limits must be symmetric for both directions.");
            Require(right <= previousAngle, "Full lock must keep decreasing through boost speeds.");
            Require(right <= mover.SteeringAngleLimit + .0001f, "No request may exceed the speed limit.");
            float acceleration = speed * speed * Mathf.Tan(right * Mathf.Deg2Rad) / 2.6f;
            Require(acceleration <= 22.001f, "Requested lateral acceleration must stay inside the safety envelope.");
            if (speed >= 30f)
            {
                float small = Steer(mover, body, input, 3f, speed);
                float yaw = speed * Mathf.Tan(small * Mathf.Deg2Rad) / 2.6f;
                Require(yaw <= previousYaw, "Small corrections must get gentler as boost speed rises.");
                previousYaw = yaw;
            }
            previousAngle = right;
            Debug.Log($"STEERING_ENVELOPE speed={speed:F0} limit={right:F3} gain={mover.SteeringSpeedMultiplier:F4} lateralRequest={acceleration:F2}");
        }
        Near(Steer(mover, body, input, 5f, -40f), Steer(mover, body, input, 5f, 40f), "Reverse travel must use the same speed envelope.");

        Set(mover, "steeringDeadzone", .08f);
        Set(mover, "steeringResponseSeconds", .045f);
        mover.SetInputSource(input);
        for (int i = 0; i < 100; i++)
            Near(Steer(mover, body, input, Mathf.Sin(i * 2.3f) * .07f, 40f), 0f, "IMU center noise must not steer the car.");

        mover.SetInputSource(input);
        float inputNoiseEnergy = 0f, outputNoiseEnergy = 0f;
        float gain = .35f * Mathf.Pow(30f / 40f, 2f);
        for (int i = 0; i < 250; i++)
        {
            float noise = .3f * Mathf.Sin(i * 2.1f);
            float angle = Steer(mover, body, input, 3f + noise, 40f);
            if (i < 50) continue;
            inputNoiseEnergy += noise * noise;
            float residual = angle / gain - (3f - .08f);
            outputNoiseEnergy += residual * residual;
        }
        float noiseRatio = Mathf.Sqrt(outputNoiseEnergy / inputNoiseEnergy);
        Require(noiseRatio < .4f, "Moving-handle IMU noise must lose at least 60% of its amplitude.");

        mover.SetInputSource(input);
        float elapsed = 0f, response90 = -1f;
        float target = (5f - .08f) * .35f;
        for (int i = 0; i < 50; i++)
        {
            float angle = Steer(mover, body, input, 5f, 30f);
            elapsed += Time.fixedDeltaTime;
            if (response90 < 0f && angle >= target * .9f) response90 = elapsed;
            Require(angle <= target + .0001f, "Filtering must never overshoot.");
        }
        Require(response90 > 0f && response90 <= .13f, "90% response must remain below 130ms.");
        float crossing = -1f;
        for (int i = 0; i < 50; i++)
        {
            float angle = Steer(mover, body, input, -5f, 30f);
            if (crossing < 0f && angle < 0f) crossing = (i + 1) * Time.fixedDeltaTime;
        }
        Require(crossing > 0f && crossing <= .06f, "Countersteer must cross neutral within 60ms.");

        // Excess IMU rotation may not add filter wind-up or delay the opposite direction.
        mover.SetInputSource(input);
        for (int i = 0; i < 100; i++) Steer(mover, body, input, 3600f, 60f);
        float bounded = Steer(mover, body, input, -5f, 60f);
        mover.SetInputSource(input);
        for (int i = 0; i < 100; i++) Steer(mover, body, input, 60f, 60f);
        Near(Steer(mover, body, input, -5f, 60f), bounded, "Excess handle travel must not delay countersteer.");
        Steer(mover, body, input, 30f, 10f);
        Require(Mathf.Abs(Steer(mover, body, input, 30f, 90f)) <= mover.SteeringAngleLimit + .0001f,
            "Abrupt acceleration must clamp even the previous filtered state.");
        mover.SuppressInputAfterRespawn();
        Near(mover.AppliedSteeringAngle, 0f, "Respawn clears filter history immediately.");
        Near(Steer(mover, body, input, 0f, 30f), 0f, "No stale steering may return after reset.");
        Steer(mover, body, input, 5f, 30f);
        input.IsConnected = false;
        Near(Steer(mover, body, input, 5f, 30f), 0f, "A disconnected IMU must stop steering immediately.");
        input.IsConnected = true;
        Near(Steer(mover, body, input, 0f, 30f), 0f, "Reconnecting at center must not restore stale input.");
        Debug.Log($"STEERING_COMFORT_PASS response90={response90:F3}s countersteer={crossing:F3}s centerNoise=0 movingNoiseRatio={noiseRatio:F3}");
    }

    private static float Steer(DebugMover mover, Rigidbody body, StubInput input, float handle, float speed)
    {
        input.CurrentState = new DriveInputState { pedal = 1f, steering = handle };
        body.linearVelocity = Vector3.forward * speed;
        Invoke(mover, "ReadInput");
        return Get(mover, "appliedSteeringAngle");
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
