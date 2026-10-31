using System;

/// <summary>Optional motor/drag tuning; never changes velocity or weakens braking input.</summary>
public static class HighSpeedDriveTuning
{
    public const float TargetMetersPerSecond = 300f / 3.6f;
    public const float AccelerationMultiplier = 2f;
    public static float Motor(float normalForce, float pedal, bool enabled) => enabled && pedal > 0f
        ? normalForce * AccelerationMultiplier : normalForce;
    public static float Resistance(float normalResistance, float normalMotorForce, bool enabled) => !enabled ? normalResistance :
        Math.Max(normalResistance * 1.2f, 2f * normalMotorForce * AccelerationMultiplier / TargetMetersPerSecond);
}
