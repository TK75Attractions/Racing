using System;

/// <summary>Continuous pursuit of the same car is required; changing targets cannot bank charge.</summary>
public sealed class SlipstreamCharge
{
    public const float ChargeSeconds = .9f;
    public const float CooldownSeconds = 3f;
    private int target = -1;
    public float Charge { get; private set; }
    public float Cooldown { get; private set; }
    public bool Tick(int eligibleTarget, float dt)
    {
        if (float.IsNaN(dt) || float.IsInfinity(dt) || dt <= 0f) return false;
        // A long frame cannot award a complete boost without observing a continuous chase.
        dt = Math.Min(.1f, dt);
        if (Cooldown > 0f)
        {
            Cooldown = Math.Max(0f, Cooldown - dt); Charge = 0f; target = -1; return false;
        }
        if (eligibleTarget < 0) { Charge = 0f; target = -1; return false; }
        if (target != eligibleTarget) { target = eligibleTarget; Charge = 0f; }
        Charge += dt;
        if (Charge + .00001f < ChargeSeconds) return false;
        Charge = 0f; Cooldown = CooldownSeconds; target = -1;
        return true;
    }
    public void Clear() { Charge = Cooldown = 0f; target = -1; }
}
