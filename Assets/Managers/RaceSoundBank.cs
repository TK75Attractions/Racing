using System;
using UnityEngine;

public enum RaceSoundCue { Select, Press, Confirm, Cancel, Ready, Countdown, Go, Finish, Results }

/// <summary>Immutable mono PCM; all cues are synthesized once, without external audio assets.</summary>
public sealed class RaceSoundClip
{
    public const int SampleRate = 48000;
    public float[] Samples { get; }
    public RaceSoundClip(float[] samples) { Samples = samples; }
}

public static class RaceSoundBank
{
    private static RaceSoundClip[] clips;
    public static RaceSoundClip Get(RaceSoundCue cue)
    {
        if (clips == null) clips = Build(); // Main-thread initialization, never from the DSP callback.
        return clips[(int)cue];
    }

    private static RaceSoundClip[] Build() => new[] {
        Tone(.055f,.10f,880f),
        Tone(.045f,.15f,520f),
        Tone(.18f,.22f,660f,990f),
        Tone(.15f,.17f,660f,440f),
        Tone(.26f,.23f,523.25f,659.25f,783.99f),
        Tone(.13f,.25f,740f),
        Tone(.48f,.27f,740f,987.77f,1480f),
        Tone(.85f,.25f,523.25f,659.25f,783.99f,1046.5f,1318.5f),
        Tone(.42f,.19f,783.99f,659.25f,1046.5f)
    };

    private static RaceSoundClip Tone(float duration,float gain,params float[] notes)
    {
        float[] pcm = new float[Mathf.CeilToInt(duration * RaceSoundClip.SampleRate)];
        double phase = 0;
        float noteSeconds = duration / notes.Length;
        for (int i=0;i<pcm.Length;i++)
        {
            float time=(float)i/RaceSoundClip.SampleRate;
            int note=Math.Min(notes.Length-1,(int)(time/noteSeconds));
            float local=time-note*noteSeconds;
            float attack=Mathf.Clamp01(local/.004f);
            float release=Mathf.Clamp01((noteSeconds-local)/.025f);
            float decay=Mathf.Exp(-local*5f);
            phase += 2*Math.PI*notes[note]/RaceSoundClip.SampleRate;
            pcm[i]=(float)(Math.Sin(phase)+.16*Math.Sin(phase*2)) * gain * attack * release * decay / 1.16f;
        }
        pcm[0]=pcm[pcm.Length-1]=0f;
        return new RaceSoundClip(pcm);
    }
}
