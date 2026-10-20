using System;

/// <summary>Bounds only the extra reward, accounting for legacy boosts already queued in this physics step.</summary>
public static class RoadRewardBudget
{
    public static float SpeedDelta(float dt, float draftSeconds, float landingSeconds, float legacyDelta, float speed)
    {
        if (!Finite(dt) || !Finite(draftSeconds) || !Finite(landingSeconds) || !Finite(legacyDelta) || !Finite(speed) || dt <= 0f) return 0f;
        float requested = Math.Max(Math.Min(dt, Math.Max(0f, draftSeconds)) * 4f,
            Math.Min(dt, Math.Max(0f, landingSeconds)) * 2.5f);
        float committed = Math.Max(0f, legacyDelta);
        return Math.Max(0f, Math.Min(requested, Math.Min(18f * dt - committed, 32f - speed - committed)));
    }
    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}
