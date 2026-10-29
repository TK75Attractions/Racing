using System;

/// <summary>A short, stationary course briefing. A held pedal never skips a page.</summary>
public sealed class TutorialGimmickBriefing
{
    public const int PageCount = 2;
    public const float PageSeconds = 4f;
    public const float MinimumReadSeconds = 2f;
    public int Page { get; private set; }
    public float Elapsed { get; private set; }
    public bool IsComplete => Page >= PageCount;
    public float SecondsRemaining => Math.Max(0f, PageSeconds - Elapsed);
    public bool CanAdvance => !IsComplete && Elapsed >= MinimumReadSeconds;
    private bool released, pedalDown;

    public void Tick(float dt, bool connected, float pedal)
    {
        if (IsComplete || float.IsNaN(dt) || float.IsInfinity(dt) || dt <= 0f) return;
        Elapsed += dt; // Use real elapsed time so a slow frame cannot extend the group deadline.
        bool valid = connected && !float.IsNaN(pedal) && !float.IsInfinity(pedal);
        if (!valid) released = false; // Reconnection needs a new, observed release.
        bool down = valid && pedal >= .25f;
        if (valid && Math.Abs(pedal) <= .05f) released = true;
        bool advance = CanAdvance && released && down && !pedalDown;
        pedalDown = down;
        if (!advance && Elapsed < PageSeconds) return;
        if (advance) { Page++; Elapsed = 0f; }
        else while (!IsComplete && Elapsed >= PageSeconds) { Elapsed -= PageSeconds; Page++; }
        released = false;
    }
}
