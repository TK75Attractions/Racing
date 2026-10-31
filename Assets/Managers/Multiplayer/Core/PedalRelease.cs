using System;

/// <summary>Shared release band for analog pedals, tolerating resting sensor offsets when resuming driving.</summary>
public static class PedalRelease
{
    public const float Threshold = .20f;
    public static bool IsReleased(float pedal) => !float.IsNaN(pedal) && !float.IsInfinity(pedal) && Math.Abs(pedal) <= Threshold;
}
