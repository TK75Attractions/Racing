using System;

/// <summary>Shared release band for analog pedals, matching the title screen and tolerating resting sensor offsets.</summary>
public static class PedalRelease
{
    public const float Threshold = .20f;
    public static bool IsReleased(float pedal) => !float.IsNaN(pedal) && !float.IsInfinity(pedal) && Math.Abs(pedal) <= Threshold;
}
