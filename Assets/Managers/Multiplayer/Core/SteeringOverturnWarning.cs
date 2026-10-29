using System;

/// <summary>Physical wheel travel in degrees, independent of driving lock and input gates.</summary>
public sealed class SteeringOverturnWarning
{
    public const float LimitDegrees = 135f;
    public const float ReleaseDegrees = 125f;
    public bool IsActive { get; private set; }
    public bool Update(float? degrees)
    {
        if (!degrees.HasValue || float.IsNaN(degrees.Value) || float.IsInfinity(degrees.Value))
            return IsActive = false;
        float angle = Math.Abs(degrees.Value);
        return IsActive = IsActive ? angle >= ReleaseDegrees : angle >= LimitDegrees;
    }
}
