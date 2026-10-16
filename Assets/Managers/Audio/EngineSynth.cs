using System;

/// <summary>Continuous four-stroke inline-four. Owned exclusively by the audio callback.</summary>
public sealed class EngineSynth
{
    private const double Tau = Math.PI * 2.0;
    private readonly int sampleRate;
    private readonly float smooth, gainSmooth, roomSmooth;
    private readonly Biquad exhaustBody, exhaustBark, intake;
    private readonly TunnelReverb reverb;
    private double firePhase, crankPhase;
    private float rpm = AutomaticGearbox.IdleRpm, load, gain, cut = 1f, room, boost;
    private float pulse, pop, noiseLow, dcIn, dcOut, lastLoad;
    private int cylinder, lastShift;
    private uint random = 0x51f15e7u;
    private float popCooldown;
    public int SampleRate => sampleRate;

    public EngineSynth(int rate)
    {
        sampleRate = Math.Max(8000, rate);
        smooth = Coefficient(.035f); gainSmooth = Coefficient(.012f); roomSmooth = Coefficient(.24f);
        exhaustBody = new Biquad(sampleRate, 105, 1.1);
        exhaustBark = new Biquad(sampleRate, 420, .85);
        intake = new Biquad(sampleRate, 1150, .7);
        reverb = new TunnelReverb(sampleRate);
    }

    // No allocation, locks, scene access or Unity API calls on this path.
    public void Render(float[] data, int channels, float targetRpm, float targetLoad, float targetGain,
        float combustion, int shiftSerial, float targetBoost, float tunnel)
    {
        if (channels <= 0) return;
        targetRpm = Clamp(targetRpm, AutomaticGearbox.IdleRpm, AutomaticGearbox.RedlineRpm);
        targetLoad = Clamp(targetLoad, 0f, 1f); targetGain = Clamp(targetGain, 0f, 1f);
        tunnel = Clamp(tunnel, 0f, 1f); combustion = Clamp(combustion, 0f, 1f);
        if (shiftSerial != lastShift)
        {
            lastShift = shiftSerial;
            if (lastLoad > .25f) pop = .65f;
        }
        if (lastLoad - targetLoad > .16f && rpm > 3600f && popCooldown <= 0f)
        { pop = .45f; popCooldown = .2f; }
        lastLoad = targetLoad;
        float pulseDecay = (float)Math.Exp(-1.0 / (sampleRate * .0028));
        float popDecay = (float)Math.Exp(-1.0 / (sampleRate * .022));
        float noiseCoefficient = 1f - (float)Math.Exp(-Tau * 1900 / sampleRate);
        float dcCoefficient = (float)Math.Exp(-Tau * 28 / sampleRate);
        for (int i = 0; i + channels <= data.Length; i += channels)
        {
            rpm += (targetRpm - rpm) * smooth;
            load += (targetLoad - load) * smooth;
            gain += (targetGain - gain) * gainSmooth;
            cut += (combustion - cut) * gainSmooth;
            room += (tunnel - room) * roomSmooth;
            boost += (targetBoost - boost) * smooth;
            // Integrate frequency, never multiply elapsed time by a changing RPM.
            double crankIncrement = rpm / (60.0 * sampleRate);
            crankPhase += crankIncrement;
            if (crankPhase >= 1.0) crankPhase -= 1.0;
            firePhase += crankIncrement * 2.0; // 4 cylinders / 2 revolutions.
            if (firePhase >= 1.0)
            {
                firePhase -= 1.0;
                cylinder = (cylinder + 1) & 3;
                // Small cylinder imbalance and cycle variation, without detuning the firing order.
                pulse += (cylinder == 0 ? 1.04f : cylinder == 2 ? .96f : 1f) * (1f + Noise() * .035f);
            }
            pulse *= pulseDecay;
            float noise = Noise();
            noiseLow += (noise - noiseLow) * noiseCoefficient;
            double angle = firePhase * Tau;
            float harmonics = 0f;
            double fireHz = rpm / 30.0;
            for (int h = 1; h <= 8; h++)
            {
                double rolloff = Math.Max(0, Math.Min(1, (sampleRate * .43 - fireHz * h) / 1000));
                harmonics += (float)(Math.Sin(angle * h) * rolloff / (h * h)) * (h <= 2 ? .32f : .20f + load * .16f);
            }
            float body = exhaustBody.Process(pulse) * 1.9f;
            float bark = exhaustBark.Process(pulse + noiseLow * (.05f + load * .20f));
            float induction = intake.Process(noise) * load * (.08f + rpm / 7800f * .15f);
            float mechanical = (float)(Math.Sin(crankPhase * Tau) * .055 + Math.Sin(crankPhase * Tau * 4) * .025);
            // Brief fuel cut at the limiter and during upshifts, with a loaded exhaust growl.
            float limiter = rpm > 7600f && crankPhase < .28 ? .18f : 1f;
            float engine = ((body * .58f + bark * .65f + harmonics) * (.48f + load * .75f) * cut * limiter +
                induction + mechanical + noiseLow * .025f) * (1f + boost * .12f);
            pop *= popDecay;
            engine += pop * (noiseLow * .7f + (float)Math.Sin(angle * .5) * .3f);
            popCooldown -= 1f / sampleRate;
            // Remove pulse DC, then soft exhaust saturation. Retain headroom for four cars and BGM.
            float highpass = engine - dcIn + dcCoefficient * dcOut;
            dcIn = engine; dcOut = highpass;
            float dry = (float)Math.Tanh(highpass * 1.55f) * .62f;
            reverb.Process(dry, room, out float left, out float right);
            // Gain is after the reverb so Engine=0 also silences the residual tail.
            if (channels == 1) data[i] = (left + right) * .5f * gain;
            else
            {
                data[i] = left * gain; data[i + 1] = right * gain;
                for (int c = 2; c < channels; c++) data[i + c] = (left + right) * .5f * gain;
            }
        }
    }

    private float Noise()
    {
        random ^= random << 13; random ^= random >> 17; random ^= random << 5;
        return (random & 0xffffff) * (2f / 16777215f) - 1f;
    }
    private float Coefficient(float seconds) => 1f - (float)Math.Exp(-1.0 / (sampleRate * seconds));
    private static float Clamp(float x, float min, float max) => Math.Max(min, Math.Min(max, x));

    private sealed class Biquad
    {
        private readonly float b0, b2, a1, a2;
        private float x1, x2, y1, y2;
        public Biquad(int rate, double frequency, double q)
        {
            double w = Tau * frequency / rate, alpha = Math.Sin(w) / (2 * q), a0 = 1 + alpha;
            b0 = (float)(alpha / a0); b2 = -b0;
            a1 = (float)(-2 * Math.Cos(w) / a0); a2 = (float)((1 - alpha) / a0);
        }
        public float Process(float x)
        {
            float y = b0 * x + b2 * x2 - a1 * y1 - a2 * y2;
            x2 = x1; x1 = x; y2 = y1; y1 = y;
            return y;
        }
    }
}
