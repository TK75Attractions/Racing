using System;
using System.Collections.Generic;

/// <summary>A continuous forward pass through either side lane. One reward per player/lap, not per side.</summary>
public sealed class TubeLineChallenge
{
    public const float Length = 18f;
    private readonly HashSet<int> awardedLaps = new HashSet<int>();
    private int lane, activeLap;
    private float previous, elapsed;
    public bool IsPending => lane != 0;
    public bool WasAwarded(int lap) => awardedLaps.Contains(lap);
    public bool Tick(int lap, int currentLane, float distance, bool eligible, float dt)
    {
        if (!eligible || currentLane == 0 || awardedLaps.Contains(lap) || float.IsNaN(distance) ||
            float.IsInfinity(distance) || float.IsNaN(dt) || float.IsInfinity(dt) || dt <= 0f)
        { Clear(); return false; }
        if (lane == 0)
        {
            if (distance < 0f || distance > 2f) return false;
            lane = currentLane; activeLap = lap; previous = distance; elapsed = 0f;
        }
        if (currentLane != lane || activeLap != lap || distance < previous - .1f || distance - previous > 4f)
        { Clear(); return false; }
        previous = distance; elapsed += Math.Min(.1f, dt);
        if (elapsed > 3.5f) { Clear(); return false; }
        if (distance < Length) return false;
        awardedLaps.Add(lap); Clear(); return true;
    }
    public void Clear() { lane = 0; previous = elapsed = 0f; }
}
