using System.Threading;
using UnityEngine;

/// <summary>Mixes UI/race cues before PlayerAudioCapture on the player's music source.</summary>
[DisallowMultipleComponent, RequireComponent(typeof(AudioSource))]
public sealed class RaceSoundEffectsMixer : MonoBehaviour
{
    private const int QueueSize=32, VoiceCount=8;
    private readonly RaceSoundClip[] queue=new RaceSoundClip[QueueSize];
    private readonly Voice[] voices=new Voice[VoiceCount];
    private int read,write,nextVoice;
    private volatile int outputSampleRate=48000;
    private volatile float musicGain=1f,effectsGain=1f;
    private struct Voice { public RaceSoundClip clip; public double cursor; }

    private void OnEnable()
    {
        outputSampleRate=AudioSettings.outputSampleRate;
        AudioSettings.OnAudioConfigurationChanged+=OnAudioConfigurationChanged;
    }
    private void OnDisable() => AudioSettings.OnAudioConfigurationChanged-=OnAudioConfigurationChanged;
    private void OnAudioConfigurationChanged(bool changed) => outputSampleRate=AudioSettings.outputSampleRate;
    public void SetMusicGain(float gain) => musicGain=Mathf.Clamp01(gain);
    public void SetEffectsGain(float gain) => effectsGain=Mathf.Clamp01(gain);

    /// <summary>Main thread is the sole producer; a full queue drops excess UI requests.</summary>
    public bool Enqueue(RaceSoundClip clip)
    {
        if (clip == null) return false;
        int next=(write+1)%QueueSize;
        if (next==Volatile.Read(ref read)) return false;
        queue[write]=clip;
        Volatile.Write(ref write,next);
        return true;
    }

    private void OnAudioFilterRead(float[] data,int channels) => Mix(data,channels);

    /// <summary>DSP/offline entry point. No Unity objects, allocations, locks or preferences here.</summary>
    public void Mix(float[] data,int channels)
    {
        if (channels<=0 || data==null) return;
        while (read!=Volatile.Read(ref write))
        {
            voices[nextVoice]=new Voice { clip=queue[read] };
            queue[read]=null;
            nextVoice=(nextVoice+1)%VoiceCount;
            Volatile.Write(ref read,(read+1)%QueueSize);
        }
        float music=musicGain, effects=effectsGain;
        double step=(double)RaceSoundClip.SampleRate/System.Math.Max(1,outputSampleRate);
        for (int offset=0;offset+channels<=data.Length;offset+=channels)
        {
            float effect=0f;
            for (int voice=0;voice<voices.Length;voice++)
            {
                RaceSoundClip clip=voices[voice].clip;
                if (clip==null) continue;
                int index=(int)voices[voice].cursor;
                if (index>=clip.Samples.Length-1) { voices[voice].clip=null; continue; }
                float fraction=(float)(voices[voice].cursor-index);
                effect += clip.Samples[index]+(clip.Samples[index+1]-clip.Samples[index])*fraction;
                voices[voice].cursor+=step;
            }
            for (int channel=0;channel<channels;channel++)
                data[offset+channel]=data[offset+channel]*music+effect*effects;
        }
    }
}
