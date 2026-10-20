using System;

/// <summary>Bounded pace windows create attacks and room for a counterattack; never decide a winner.</summary>
public static class CpuRacePace
{
    // A leading CPU used to revert to full cruise as soon as its rival was >35m
    // away or below 30m/s. Keep one close rival and one more competitive rival,
    // using physical throttle/brake only. A stopped player never parks the CPUs.
    public static float FastRequest(float partnerSpeed, float gap, float raceSeconds, int cpuIndex, float cruise)
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
