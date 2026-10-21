using System;

/// <summary>Counts frame intervals against a monotonic, unscaled clock without per-frame allocations.</summary>
public sealed class FrameRateCounter
{
    private const double SampleSeconds = .5;
    private double startedAt = double.NaN;
    private int frames;

    public float FramesPerSecond { get; private set; }
    public float FrameMilliseconds { get; private set; }

    public void Reset()
    {
        startedAt = double.NaN;
        frames = 0;
        FramesPerSecond = FrameMilliseconds = 0f;
    }

    public void RecordFrame(double realtimeSeconds)
    {
        if (double.IsNaN(startedAt) || realtimeSeconds < startedAt)
        {
            Reset();
            startedAt = realtimeSeconds;
            return; // The first frame establishes a baseline, not a completed interval.
        }
        frames++;
        double elapsed = realtimeSeconds - startedAt;
        if (elapsed < SampleSeconds) return;
        FramesPerSecond = (float)(frames / elapsed);
        FrameMilliseconds = (float)(elapsed * 1000 / frames);
        frames = 0;
        startedAt = realtimeSeconds;
    }
}
