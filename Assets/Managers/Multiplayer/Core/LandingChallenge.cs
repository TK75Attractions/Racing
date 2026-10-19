using System;
using System.Collections.Generic;

/// <summary>A designated gap, genuine flight and stable landing are all required. OFF keeps the spent ledger.</summary>
public sealed class LandingChallenge
{
    private readonly HashSet<long> spent = new HashSet<long>();
    private long key;
    private float elapsed, airborne, stable;
    private bool flying;
    public bool Armed { get; private set; }
    public bool TryArm(int jump, int lap)
    {
        long next = ((long)lap << 32) | (uint)jump;
        if (Armed || spent.Contains(next)) return false;
        key = next; Armed = true; elapsed = airborne = stable = 0f; flying = false; return true;
    }
    public bool Tick(bool valid, int contacts, bool overGap, bool landingEligible, float dt)
    {
        if (!Armed) return false;
        if (!valid || float.IsNaN(dt) || float.IsInfinity(dt) || dt <= 0f) { Clear(); return false; }
        dt = Math.Min(.1f, dt); elapsed += dt;
        if (elapsed > 6f) { Clear(); return false; }
        if (!flying && contacts == 0 && overGap) { flying = true; spent.Add(key); }
        if (!flying) return false;
        if (contacts == 0) { airborne += dt; stable = 0f; return false; }
        if (contacts < 2) { stable = 0f; return false; }
        if (!landingEligible || airborne < .15f) { Clear(); return false; }
        stable += dt;
        if (stable + .00001f < .2f) return false;
        Clear(); return true;
    }
    public void Clear() { Armed = flying = false; elapsed = airborne = stable = 0f; }
}
