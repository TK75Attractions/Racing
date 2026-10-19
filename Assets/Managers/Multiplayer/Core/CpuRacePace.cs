using System;

/// <summary>Bounded pace windows create attacks and room for a counterattack; never decide a winner.</summary>
public static class CpuRacePace
{
    public static float Request(float partnerSpeed, float gap, float raceSeconds, int cpuIndex, float cruise, float catchup)
    {
        if (float.IsNaN(partnerSpeed) || float.IsNaN(gap) || float.IsNaN(raceSeconds)) return cruise;
        float phase = (Math.Max(0f, raceSeconds) + cpuIndex * 3f) % 16f;
        float desired;
        if (gap > 22f) desired = cruise + catchup;
        else if (gap < -45f) desired = partnerSpeed + .8f + (gap + 45f) * .04f;
        else desired = partnerSpeed + (phase < 10f || gap > 12f ? 5f : -2f) + gap * .1f;
        return Math.Max(10f, Math.Min(cruise + catchup, desired));
    }
}
