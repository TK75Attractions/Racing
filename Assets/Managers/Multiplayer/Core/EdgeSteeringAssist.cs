using System;

/// <summary>Only counters a predicted departure; the centre and an inward trajectory stay untouched.</summary>
public static class EdgeSteeringAssist
{
    public static float Correction(float lateral, float predictedLateral, float halfWidth, float angleLimit)
    {
        if (!Finite(lateral) || !Finite(predictedLateral) || !Finite(halfWidth) || !Finite(angleLimit) ||
            halfWidth < 2f || angleLimit <= 0f) return 0f;
        float side = Math.Sign(predictedLateral);
        float safe = halfWidth - 1.5f; // car footprint plus an edge margin
        float outwardTravel = Math.Abs(predictedLateral) - lateral * side;
        if (outwardTravel <= .05f || Math.Abs(predictedLateral) <= safe) return 0f;
        float risk = Math.Min(1f, (Math.Abs(predictedLateral) - safe) / 1.5f);
        return -side * risk * Math.Min(18f, angleLimit);
    }
    public static float Blend(float driverAngle, float roadAngle, float lateral, float predicted, float halfWidth, float limit)
    {
        float correction = Correction(lateral, predicted, halfWidth, limit);
        if (correction == 0f || !Finite(driverAngle) || !Finite(roadAngle)) return driverAngle;
        float risk = Math.Min(1f, Math.Abs(correction) / Math.Max(.01f, Math.Min(18f, limit)));
        return driverAngle + (Math.Max(-limit, Math.Min(limit, roadAngle)) - driverAngle) * risk;
    }
    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}
