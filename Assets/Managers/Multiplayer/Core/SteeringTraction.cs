using System;

/// <summary>Road-plane recovery for ordinary steering. Angles are degrees, yaw rad/s, speed m/s.</summary>
public static class SteeringTraction
{
    public const float SlipAllowanceDegrees = 1.5f;
    public const float YawResponse = 24f;
    public const float MaximumYawAcceleration = 30f;
    public const float MaximumGripAcceleration = 90f;

    public static float ExcessSlip(float slip)
    {
        if (!Finite(slip)) return 0f;
        return slip - Math.Max(-SlipAllowanceDegrees,Math.Min(SlipAllowanceDegrees,slip));
    }
    public static float YawAcceleration(float current, float desired)
    {
        if (!Finite(current) || !Finite(desired)) return 0f;
        return Math.Max(-MaximumYawAcceleration,Math.Min(MaximumYawAcceleration,(desired-current)*YawResponse));
    }
    public static float RecoveryDegrees(float slip, float speed, float dt)
    {
        if (!Finite(speed) || !Finite(dt) || speed < 3f || dt <= 0f) return 0f;
        float limit = MaximumGripAcceleration / speed * dt * (180f / (float)Math.PI);
        float excess = ExcessSlip(slip);
        return Math.Max(-limit,Math.Min(limit,excess));
    }
    private static bool Finite(float x) => !float.IsNaN(x) && !float.IsInfinity(x);
}
