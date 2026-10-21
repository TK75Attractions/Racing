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
        if (clip == null || clip.Frames < 2) return false;
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
        for (int offset=0;offset+channels<=data.Length;offset+=channels)
        {
            float left=0f, right=0f;
            for (int voice=0;voice<voices.Length;voice++)
            {
                RaceSoundClip clip=voices[voice].clip;
                if (clip==null) continue;
                int index=(int)voices[voice].cursor;
                if (index>=clip.Frames) { voices[voice].clip=null; continue; }
                float fraction=(float)(voices[voice].cursor-index);
                int first=index*clip.Channels;
                int second=System.Math.Min(index+1,clip.Frames-1)*clip.Channels;
                int r=clip.Channels-1;
                left += clip.Samples[first]+(clip.Samples[second]-clip.Samples[first])*fraction;
                right += clip.Samples[first+r]+(clip.Samples[second+r]-clip.Samples[first+r])*fraction;
                voices[voice].cursor+=(double)clip.Frequency/System.Math.Max(1,outputSampleRate);
            }
            for (int channel=0;channel<channels;channel++)
            {
                float effect=channels==1 || channel>1 ? (left+right)*.5f : channel==0 ? left : right;
                data[offset+channel]=Limit(data[offset+channel]*music+effect*effects);
            }
        }
    }

    // Transparent below -0.9 dBFS; softly contain overlapping fanfares/impacts above that.
    private static float Limit(float sample)
    {
        float magnitude=System.Math.Abs(sample);
        if (magnitude<=.9f) return sample;
        float excess=magnitude-.9f;
        float limited=.9f+.1f*excess/(.1f+excess);
        return sample<0f ? -limited : limited;
    }
}
