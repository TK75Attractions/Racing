using System;

/// <summary>Continuous curvature from centered steering to full lock, including the speed safety envelope.</summary>
public static class SteeringResponse
{
    public const float FullLockUnits = 30f;
    private const double Radians = Math.PI / 180.0;
    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

    public static float CanonicalInput(float sourceInput, float fullLockInput)
    {
        if (!Finite(sourceInput)) return 0f;
        if (!Finite(fullLockInput) || fullLockInput <= .0001f) fullLockInput = FullLockUnits;
        return sourceInput / fullLockInput * FullLockUnits;
    }

    public static float WheelAngle(float input, float maximumAngle, float speedMultiplier,
        float speed, float wheelbase, float maximumYawRate, float lateralAcceleration, bool protect)
    {
        if (!Finite(input) || !Finite(speed)) return 0f;
        double limit = Math.Max(0, Math.Min(60, maximumAngle * Math.Max(0, Math.Min(1, speedMultiplier))));
        double velocity = Math.Abs(speed);
        if (protect && velocity >= 3 && wheelbase > .01f)
        {
            double yaw = Math.Min(Math.Max(0, maximumYawRate), Math.Max(0, lateralAcceleration) / velocity);
            limit = Math.Min(limit, Math.Atan(yaw * wheelbase / velocity) / Radians);
        }
        double amount = Math.Max(-1, Math.Min(1, input / FullLockUnits));
        // Interpolate curvature rather than clipping intermediate inputs to the same safe angle.
        return (float)(Math.Atan(Math.Tan(limit * Radians) * amount) / Radians);
    }

    public static float YawRate(float wheelAngle, float forwardSpeed, float wheelbase)
    {
        if (!Finite(wheelAngle) || !Finite(forwardSpeed) || wheelbase < .01f) return 0f;
        return (float)(forwardSpeed * Math.Tan(wheelAngle * Radians) / wheelbase);
    }
}
