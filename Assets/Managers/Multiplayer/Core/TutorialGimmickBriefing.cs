using System;

/// <summary>A short, stationary course briefing. A held pedal never skips a page.</summary>
public sealed class TutorialGimmickBriefing
{
    public const int PageCount = 3;
    public const float PageSeconds = 10f;
    public const float MinimumReadSeconds = 4f;
    public int Page { get; private set; }
    public float Elapsed { get; private set; }
    public bool IsComplete => Page >= PageCount;
    public float SecondsRemaining => Math.Max(0f, PageSeconds - Elapsed);
    public bool CanAdvance => !IsComplete && Elapsed >= MinimumReadSeconds;
    private bool released, pedalDown;

    public void Tick(float dt, bool connected, float pedal)
    {
        if (IsComplete || float.IsNaN(dt) || float.IsInfinity(dt) || dt <= 0f) return;
        Elapsed += Math.Min(dt, .1f); // A stalled frame must not skip an explanation.
        bool valid = connected && !float.IsNaN(pedal) && !float.IsInfinity(pedal);
        if (!valid) released = false; // Reconnection needs a new, observed release.
        bool down = valid && pedal >= .25f;
        if (valid && Math.Abs(pedal) <= .05f) released = true;
        bool advance = CanAdvance && released && down && !pedalDown;
        pedalDown = down;
        if (!advance && Elapsed < PageSeconds) return;
        Page++; Elapsed = 0f; released = false;
    }
}
