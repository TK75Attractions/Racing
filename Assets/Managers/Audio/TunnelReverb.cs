using System;

/// <summary>Damped parallel reflections with stereo early echoes, sized once at DSP initialization.</summary>
public sealed class TunnelReverb
{
    private readonly Delay[] combs;
    private readonly float[] early;
    private readonly int tapLeft, tapRight, tapFar;
    private int cursor;
    public TunnelReverb(int rate)
    {
        // Incommensurate paths avoid a metallic, periodically repeating single echo.
        combs = new[] { new Delay(rate, .0671f), new Delay(rate, .0797f),
            new Delay(rate, .1013f), new Delay(rate, .1279f) };
        tapLeft = (int)(rate * .023f); tapRight = (int)(rate * .037f); tapFar = (int)(rate * .053f);
        early = new float[Math.Max(1, tapFar + 1)];
    }
    public void Process(float dry, float amount, out float left, out float right)
    {
        // Only enclosed engine energy enters the room; the tail decays naturally after the exit.
        float send = dry * amount;
        early[cursor] = send;
        float l = Read(tapLeft), r = Read(tapRight), far = Read(tapFar);
        cursor = (cursor + 1) % early.Length;
        float a = combs[0].Process(send + far * .18f);
        float b = combs[1].Process(send + l * .18f);
        float c = combs[2].Process(send + r * .18f);
        float d = combs[3].Process(send + far * .18f);
        left = dry + l * .22f + far * .11f + (a + c + d * .5f) * .16f;
        right = dry + r * .22f + far * .11f + (b + d + c * .5f) * .16f;
    }
    private float Read(int distance) => early[(cursor - distance + early.Length) % early.Length];
    private sealed class Delay
    {
        private readonly float[] buffer;
        private readonly float feedback;
        private int cursor;
        private float damped;
        public Delay(int rate, float seconds)
        {
            buffer = new float[Math.Max(1, (int)(rate * seconds))];
            feedback = (float)Math.Pow(.001, seconds / 1.35); // RT60 ~= 1.35 seconds.
        }
        public float Process(float input)
        {
            float value = buffer[cursor];
            damped += (value - damped) * .32f;
            buffer[cursor] = input + damped * feedback;
            cursor = (cursor + 1) % buffer.Length;
            return value;
        }
    }
}
