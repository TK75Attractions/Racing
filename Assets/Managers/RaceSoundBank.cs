using System;
using UnityEngine;

// Keep the original values: existing scene/UI references depend on their order.
public enum RaceSoundCue
{
    Select, Press, Confirm, Cancel, Ready, Countdown, Go, Finish, Results,
    TireSkid, DriftCharge, DriftBoost, Boost, Landing, Charge, ItemPickup, Shield, Impact, FinalLap, Rocket
}

/// <summary>Decoded, interleaved PCM, shared by all players without Unity access on the audio thread.</summary>
public sealed class RaceSoundClip
{
    public const int SampleRate = 48000;
    public float[] Samples { get; }
    public int Channels { get; }
    public int Frequency { get; }
    public int Frames => Samples.Length / Channels;

    public RaceSoundClip(float[] samples, int channels = 1, int frequency = SampleRate)
    {
        if (samples == null || channels < 1 || channels > 2 || frequency <= 0 || samples.Length % channels != 0)
            throw new ArgumentException("Expected mono or stereo PCM with a valid sample rate.");
        Samples = samples; Channels = channels; Frequency = frequency;
    }
}

public static class RaceSoundBank
{
    public static readonly int CueCount = Enum.GetValues(typeof(RaceSoundCue)).Length;
    private static RaceSoundClip[] clips;
    public static string ResourcePath(RaceSoundCue cue) => "Audio/SFX/" + cue;

    /// <summary>Call on the main thread. Audio files are decoded once, never synthesized at runtime.</summary>
    public static RaceSoundClip Get(RaceSoundCue cue)
    {
        if ((uint)(int)cue >= (uint)CueCount) return null;
        if (clips == null) clips = Load();
        return clips[(int)cue];
    }

    private static RaceSoundClip[] Load()
    {
        var result = new RaceSoundClip[CueCount];
        foreach (RaceSoundCue cue in Enum.GetValues(typeof(RaceSoundCue)))
        {
            AudioClip asset = Resources.Load<AudioClip>(ResourcePath(cue));
            if (asset == null || asset.channels < 1 || asset.channels > 2 ||
                asset.loadType != AudioClipLoadType.DecompressOnLoad ||
                asset.loadState != AudioDataLoadState.Loaded && !asset.LoadAudioData())
            {
                Debug.LogError($"RaceSoundBank: {ResourcePath(cue)} must be a preloaded mono/stereo audio file (Decompress On Load).");
                continue;
            }
            float[] pcm = new float[asset.samples * asset.channels];
            if (!asset.GetData(pcm, 0))
            {
                Debug.LogError($"RaceSoundBank: could not decode {ResourcePath(cue)}.");
                continue;
            }
            result[(int)cue] = new RaceSoundClip(pcm, asset.channels, asset.frequency);
        }
        return result;
    }
}
