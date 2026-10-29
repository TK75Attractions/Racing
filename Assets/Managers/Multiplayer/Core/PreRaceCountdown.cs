using System;

/// <summary>Shared preparation deadline, including transitions, flyover and countdown.</summary>
public sealed class PreRaceCountdown
{
    public const float MaximumSeconds = 60f;
    public const float PreparationSeconds = 45f;
    public const float FinalCountdownSeconds = 3f;
    public float Elapsed { get; private set; }
    public float Remaining => Math.Max(0f, MaximumSeconds - Elapsed);
    public bool IsRunning { get; private set; }
    public bool MustLeavePractice => IsRunning && Elapsed >= PreparationSeconds;
    public bool MustFinishIntro => IsRunning && Remaining <= FinalCountdownSeconds;
    public void Start() { if (!IsRunning) { Elapsed = 0f; IsRunning = true; } }
    public void Tick(float dt)
    {
        if (IsRunning && dt > 0 && !float.IsNaN(dt) && !float.IsInfinity(dt))
            Elapsed = Math.Min(MaximumSeconds, Elapsed + dt);
    }
    public void Stop() => IsRunning = false;
    public void Reset() { Elapsed = 0; IsRunning = false; }
}
