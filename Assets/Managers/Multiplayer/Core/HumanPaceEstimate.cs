using System;

/// <summary>Seconds-scale observation, independent of render rate and temporary item/stall impulses.</summary>
public sealed class HumanPaceEstimate
{
    public float Pace { get; private set; } = 18f;
    private float slowSeconds;
    public void Clear() { Pace = 18f; slowSeconds = 0; }
    public void Observe(float forwardSpeed, bool temporarilySlowed, float dt)
    {
        if (float.IsNaN(forwardSpeed) || float.IsInfinity(forwardSpeed) || float.IsNaN(dt) || float.IsInfinity(dt) || dt <= 0) return;
        dt = Math.Min(.25f,dt);
        bool slow = temporarilySlowed || forwardSpeed < 3f;
        slowSeconds = slow ? slowSeconds + dt : 0;
        if (slow && slowSeconds < 3f) return;
        float target = Math.Max(8f,Math.Min(50f,forwardSpeed));
        float step = dt * (target > Pace ? 3f : 1.5f);
        Pace += Math.Max(-step,Math.Min(step,target-Pace));
    }
}
