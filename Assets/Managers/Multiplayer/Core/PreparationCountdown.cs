using System;

/// <summary>A per-screen countdown; expiry is reported once and stopped timers retain their value.</summary>
public sealed class PreparationCountdown
{
    public float RemainingSeconds { get; private set; }
    public bool HasExpired { get; private set; }
    public bool IsRunning { get; private set; } = true;

    public PreparationCountdown(float seconds)
    {
        RemainingSeconds = Math.Max(0f, seconds);
    }

    public bool Tick(float deltaTime)
    {
        if (!IsRunning) return false;
        RemainingSeconds = Math.Max(0f, RemainingSeconds - Math.Max(0f, deltaTime));
        if (RemainingSeconds > 0f) return false;
        HasExpired = true;
        IsRunning = false;
        return true;
    }

    public void Stop() => IsRunning = false;
}
