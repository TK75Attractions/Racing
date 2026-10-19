using System;

/// <summary>One affliction at a time, capped at three seconds, followed by one second of protection.</summary>
public sealed class HitProtectionWindow
{
    public const float MaximumEffectSeconds = 3f;
    public const float ProtectionSeconds = 1f;
    public float EffectRemaining { get; private set; }
    public float ProtectionRemaining { get; private set; }
    public bool CanAccept => EffectRemaining <= 0f && ProtectionRemaining <= 0f;
    public bool TryBegin(float duration)
    {
        if (!CanAccept || float.IsNaN(duration) || float.IsInfinity(duration) || duration <= 0f) return false;
        EffectRemaining = Math.Min(MaximumEffectSeconds, duration);
        return true;
    }
    public void Tick(float dt)
    {
        if (float.IsNaN(dt) || float.IsInfinity(dt) || dt <= 0f) return;
        if (EffectRemaining > 0f)
        {
            float consumed = Math.Min(dt, EffectRemaining);
            EffectRemaining = Math.Max(0f, EffectRemaining - consumed);
            dt -= consumed;
            if (EffectRemaining <= 0f) ProtectionRemaining = ProtectionSeconds;
        }
        ProtectionRemaining = Math.Max(0f, ProtectionRemaining - dt);
    }
    public void Clear() { EffectRemaining = ProtectionRemaining = 0f; }
}
