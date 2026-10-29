using System;

/// <summary>Preallocated stereo delay line. One audio thread owns each stream.</summary>
public sealed class OutputAudioDelay
{
    private readonly float[] ring;
    private readonly float[] output;
    private int write;
    public OutputAudioDelay(int maximumDelayFrames, int maximumBlockFrames)
    {
        ring = new float[(maximumDelayFrames + 1) * 2];
        output = new float[maximumBlockFrames * 2];
    }
    public void Reset() { Array.Clear(ring, 0, ring.Length); write = 0; }
    public float[] Process(float[] input, int channels, int delayFrames)
    {
        int frames = input.Length / channels;
        if (frames * 2 > output.Length) return null;
        int capacity = ring.Length / 2;
        int delay = Math.Max(0, Math.Min(capacity - 1, delayFrames));
        for (int frame = 0; frame < frames; frame++)
        {
            ring[write * 2] = input[frame * channels];
            ring[write * 2 + 1] = input[frame * channels + (channels > 1 ? 1 : 0)];
            int read = (write - delay + capacity) % capacity;
            output[frame * 2] = ring[read * 2];
            output[frame * 2 + 1] = ring[read * 2 + 1];
            write = (write + 1) % capacity;
        }
        return output;
    }
}
