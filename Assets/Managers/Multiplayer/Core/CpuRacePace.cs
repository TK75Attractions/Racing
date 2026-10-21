using System;

/// <summary>Bounded pace windows create attacks and room for a counterattack; never decide a winner.</summary>
public static class CpuRacePace
{
    public static float FastRequest(float partnerSpeed, float gap, float raceSeconds, int cpuIndex, float cruise)
    {
        if (!Finite(partnerSpeed) || !Finite(gap) || !Finite(raceSeconds) || !Finite(cruise)) return 18f;
        float pace = Math.Max(8f, Math.Min(cruise, partnerSpeed > 0 ? partnerSpeed : 18f));
        float phase = (Math.Max(0f, raceSeconds) + cpuIndex * 3f) % 16f;
        float lead = cpuIndex % 2 == 0 ? 10f : 24f;
        float window = phase < 10f || gap > 15f ? 2.5f : -1.5f;
        float correction = (gap + lead) * .10f;
        // Catch up through normal driving when behind, then ease off as the CPU gets ahead.
        // Even a slow rival gets attack/counterattack windows, without a 28-38m/s floor.
        float request = pace + window + correction;
        return Math.Max(8f, Math.Min(cruise + 2f, request));
    }
    public static float CompanionRequest(float partnerSpeed, float gap, float cruise)
    {
        if (!Finite(partnerSpeed) || !Finite(gap) || !Finite(cruise)) return 18f;
        float pace = Math.Max(8f, Math.Min(cruise, partnerSpeed > 0 ? partnerSpeed : 18f));
        // P2 accompanies P1, targeting a small following distance instead of winning a solo time trial.
        return Math.Max(8f, Math.Min(cruise, pace + (gap - 10f) * .10f));
    }
    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

    // Retained behind the adaptive pace debug switch for a direct comparison.
    public static float LegacyFastRequest(float partnerSpeed, float gap, float raceSeconds, int cpuIndex, float cruise)
    {
        if (float.IsNaN(partnerSpeed) || float.IsNaN(gap) || float.IsNaN(raceSeconds)) return cruise;
        if (partnerSpeed < 8f || gap > 35f) return cruise;
        float phase = (Math.Max(0f, raceSeconds) + cpuIndex * 3f) % 16f;
        float leadAllowance = cpuIndex % 2 == 0 ? 45f : 100f;
        if (gap < -leadAllowance)
        {
            float floor = cpuIndex % 2 == 0 ? 28f : 32f;
            float excess = -gap - leadAllowance;
            float desired = partnerSpeed + (phase < 10f ? 2f : -1.5f) - Math.Min(6f, excess * .05f);
            return Math.Max(floor, Math.Min(cruise, desired));
        }
        if (Math.Abs(gap) > 35f || partnerSpeed < 30f) return cruise;
        return Math.Max(38f, Math.Min(cruise + 2f, partnerSpeed + (phase < 10f ? 4f : -1f) + gap * .08f));
    }
    public static float Pedal(float targetSpeed, float signedSpeed, float cruisePedal)
    {
        if (float.IsNaN(targetSpeed) || float.IsNaN(signedSpeed) || float.IsNaN(cruisePedal)) return 0f;
        return Math.Max(-1f, Math.Min(1f, (targetSpeed - signedSpeed) * .12f + Math.Max(0f, Math.Min(1f, cruisePedal))));
    }
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
