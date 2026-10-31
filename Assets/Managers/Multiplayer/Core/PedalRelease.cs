using System;

/// <summary>Shared release band for analog pedals, including small resting sensor offsets.</summary>
public static class PedalRelease
{
    public const float Threshold = .10f;
    public static bool IsReleased(float pedal) => !float.IsNaN(pedal) && !float.IsInfinity(pedal) && Math.Abs(pedal) <= Threshold;
}
