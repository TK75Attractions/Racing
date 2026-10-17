using System;
using UnityEditor;
using UnityEngine;

public static class HudIndicatorValidation
{
    [MenuItem("Racing/UI/Validate Steering and Acceleration Indicators")]
    public static void Run()
    {
        GameObject root = new GameObject("Acceleration indicator validation", typeof(RectTransform));
        try
        {
            var gauge = root.AddComponent<RacingAccelerationGauge>();
            gauge.Configure(0, null);
            gauge.SampleSpeed(30f, .02f);
            Require(gauge.SignedAmount == 0f, "Binding to a moving car must not invent acceleration.");
            for (int i = 0; i < 50; i++) gauge.SampleSpeed(30f, .02f);
            Require(gauge.SignedAmount == 0f, "Constant speed must remain neutral regardless of pedal input.");

            gauge.ResetMeasurement();
            gauge.SampleSpeed(0f, .02f);
            for (int i = 1; i <= 50; i++) gauge.SampleSpeed(i * .2f, .02f);
            Require(gauge.AccelerationMetersPerSecondSquared > 9.8f && gauge.SignedAmount > 0f,
                "A 10 m/s² speed ramp must light the clockwise acceleration arc.");
            for (int i = 1; i <= 50; i++) gauge.SampleSpeed(10f - i * .2f, .02f);
            Require(gauge.AccelerationMetersPerSecondSquared < -9.8f && gauge.SignedAmount < 0f,
                "Decreasing speed must light the counterclockwise deceleration arc.");
            for (int i = 0; i < 50; i++) gauge.SampleSpeed(0f, .02f);
            Require(gauge.SignedAmount == 0f, "Stopping must clear the deceleration arc.");
            gauge.SampleSpeed(40f, .02f, true);
            Require(gauge.SignedAmount == 0f, "Respawn suppression must reset the speed baseline.");
            gauge.SampleSpeed(0f, .02f, true);
            gauge.SampleSpeed(0f, .02f);
            Require(gauge.SignedAmount == 0f, "Respawn must not leave a false braking indication.");

            float at50Hz = Ramp(gauge, .02f, 30);
            float at100Hz = Ramp(gauge, .01f, 60);
            Require(Mathf.Abs(at50Hz - at100Hz) < .002f, "Acceleration smoothing must be independent of sample frequency.");
            gauge.SetAcceleration(100f);
            Require(gauge.SignedAmount == 1f, "Acceleration impulses must stay within the arc.");
            gauge.SetAcceleration(-100f);
            Require(gauge.SignedAmount == -1f, "Deceleration impulses must stay within the opposite arc.");
            gauge.SetAcceleration(0f);
            int dirty = 0;
            gauge.RegisterDirtyVerticesCallback(() => dirty++);
            for (int i = 0; i < 120; i++) gauge.SetAcceleration(.1f);
            Require(dirty == 0, "Near-zero noise must not rebuild the arc.");
            Debug.Log($"HUD_ACCELERATION_VALIDATION_PASS: signed speed derivative, neutral, reset, saturation, idle; 50Hz={at50Hz:F3}, 100Hz={at100Hz:F3}");
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }

    public static void RunBatch()
    {
        UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        RacingHUDFontBuilder.Build();
        Run();
        ModernUIValidation.Run();
        RacingHUDDesignPreview.CaptureHudIndicators();
    }

    private static float Ramp(RacingAccelerationGauge gauge, float dt, int count)
    {
        gauge.ResetMeasurement();
        gauge.SampleSpeed(0f, dt);
        for (int i = 1; i <= count; i++) gauge.SampleSpeed(i * dt * 10f, dt);
        return gauge.AccelerationMetersPerSecondSquared;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
