using System;

/// <summary>One player's practice progress; input alone cannot complete a driving task.</summary>
public sealed class DrivingTutorialProgress
{
    public enum Stage { Accelerate, Steer, Stop, Complete }
    public Stage CurrentStage { get; private set; }
    public bool PedalReleased { get; private set; }
    public bool IsComplete => CurrentStage == Stage.Complete;
    public const float DefaultTimeLimitSeconds = 60f;
    public bool HasTimedOut => countdown.HasExpired;
    public bool IsFinished => IsComplete || HasTimedOut;
    public float TimeRemaining => countdown.RemainingSeconds;
    public float TimeLimitSeconds { get; }
    public float HoldProgress => Math.Min(1f, holdSeconds / 0.6f);
    private float holdSeconds;
    private readonly PreparationCountdown countdown;

    public DrivingTutorialProgress(float timeLimitSeconds = DefaultTimeLimitSeconds)
    {
        TimeLimitSeconds = Math.Max(0f, timeLimitSeconds);
        countdown = new PreparationCountdown(TimeLimitSeconds);
    }

    public void Tick(float deltaTime, bool connected, float pedal, float steering, float speed, float headingChange)
    {
        if (IsFinished) return;
        if (countdown.Tick(deltaTime)) { holdSeconds = 0f; return; }
        if (!connected)
        {
            holdSeconds = 0f;
            return;
        }
        float dt = Math.Max(0f, deltaTime);
        if (Math.Abs(pedal) <= 0.05f) PedalReleased = true;
        bool satisfied;
        switch (CurrentStage)
        {
            case Stage.Accelerate:
                satisfied = PedalReleased && pedal >= 0.25f && speed >= 3f;
                break;
            case Stage.Steer:
                satisfied = Math.Abs(steering) >= 2f && speed >= 2f && Math.Abs(headingChange) >= 25f;
                break;
            default:
                // Losing a device must never count as releasing the pedal and stopping.
                satisfied = Math.Abs(pedal) <= 0.05f && speed <= 0.5f;
                break;
        }
        holdSeconds = satisfied ? holdSeconds + dt : 0f;
        if (holdSeconds < 0.6f) return;
        CurrentStage++;
        holdSeconds = 0f;
        if (IsComplete) countdown.Stop();
    }

    public void ResetCurrentAttempt()
    {
        // Recovery preserves completed lessons but never carries a partial hold through a teleport.
        holdSeconds = 0f;
        if (CurrentStage == Stage.Accelerate) PedalReleased = false;
    }
}
