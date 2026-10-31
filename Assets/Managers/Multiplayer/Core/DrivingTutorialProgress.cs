using System;

/// <summary>One player's practice progress; input alone cannot complete a driving task.</summary>
public sealed class DrivingTutorialProgress
{
    public enum Stage { Accelerate, Steer, CollectItem, Complete }
    public Stage CurrentStage { get; private set; }
    public bool PedalReleased { get; private set; }
    public bool IsComplete => CurrentStage == Stage.Complete;
    public const float DefaultTimeLimitSeconds = 35f;
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
        if (PedalRelease.IsReleased(pedal)) PedalReleased = true;
        bool satisfied;
        switch (CurrentStage)
        {
            case Stage.Accelerate:
                satisfied = PedalReleased && pedal >= 0.25f && speed >= 3f;
                break;
            case Stage.Steer:
                satisfied = Math.Abs(steering) >= 2f && speed >= 2f && Math.Abs(headingChange) >= 25f;
                break;
            default: return; // Only a real pickup contact completes the third lesson.
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
        // Recovery preserves the initial observed release.
    }

    public bool CollectItem(bool connected)
    {
        if (!connected || IsFinished || CurrentStage != Stage.CollectItem) return false;
        CurrentStage = Stage.Complete; holdSeconds = 0f; countdown.Stop(); return true;
    }
}
