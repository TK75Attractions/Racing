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
        public bool IsConnected => true;
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

            // 初期設定：10 m/s から効き始め、30 m/s で 35% になる直線的な変化。
            Near(Steer(mover, body, input, 10f, 0f), 10f, "Standing still must use the full steering angle.");
            Near(mover.SteeringSpeedMultiplier, 1f, "Standing still must not reduce steering.");
            Near(Steer(mover, body, input, 10f, 10f), 10f, "Fade must not start below the fade start speed.");
            Near(Steer(mover, body, input, 10f, 20f), 6.75f, "Half of the fade range must apply half of the reduction.");
            Near(Steer(mover, body, input, 10f, 30f), 3.5f, "Full speed must apply the high speed multiplier.");
            Near(Steer(mover, body, input, 10f, 60f), 3.5f, "Beyond full speed must hold the high speed multiplier.");
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
            Near(Steer(mover, body, input, 10f, 20f), 8.375f, "A sharper curve must keep more steering at mid speed.");
            Near(Steer(mover, body, input, 10f, 30f), 3.5f, "A sharper curve must still reach the high speed multiplier.");
            Set(mover, "steeringFadeSharpness", 0.5f);
            Near(Steer(mover, body, input, 10f, 20f), Mathf.Lerp(1f, 0.35f, Mathf.Sqrt(0.5f)) * 10f,
                "A softer curve must reduce steering earlier.");
            Set(mover, "steeringFadeSharpness", 1f);

            // しきい値の逆転や同値を設定しても、開始速度を超えた時点で高速側の倍率になる。
            Set(mover, "steeringFadeFullSpeed", 5f);
            Near(Steer(mover, body, input, 10f, 10f), 10f, "An inverted range must not fade below the start speed.");
            Near(Steer(mover, body, input, 10f, 11f), 3.5f, "An inverted range must fade immediately above it.");
            Set(mover, "steeringFadeFullSpeed", 30f);

            Set(mover, "enableSpeedSensitiveSteering", false);
            Near(Steer(mover, body, input, 10f, 60f), 10f, "Disabling must keep the low speed steering at any speed.");
            Near(mover.SteeringSpeedMultiplier, 1f, "Disabling must report a neutral multiplier.");
            Set(mover, "enableSpeedSensitiveSteering", true);

            Steer(mover, body, input, 10f, 60f);
            mover.SuppressInputAfterRespawn();
            Near(Get(mover, "appliedSteeringAngle"), 0f, "Respawn must clear the steering angle.");
            Near(mover.SteeringSpeedMultiplier, 1f, "Respawn must reset the steering multiplier.");

            Debug.Log("Steering validation passed.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(car);
        }
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
