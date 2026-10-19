using System;

/// <summary>Leave takeoff spacing by reducing the requested pedal speed before the launch.</summary>
public static class CpuJumpSpacing
{
    public static float Request(float requested, float leaderSpeed, float forwardGap)
    {
        if (forwardGap <= 0f || forwardGap >= 60f || float.IsNaN(forwardGap) || float.IsNaN(leaderSpeed)) return requested;
        float spacing = Math.Min(0f, (forwardGap - 20f) * .35f);
        return Math.Min(requested, Math.Max(18.5f, leaderSpeed + spacing));
    }
}
