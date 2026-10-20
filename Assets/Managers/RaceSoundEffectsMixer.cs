using System.Threading;
using UnityEngine;

/// <summary>Mixes cues and low-level tire feedback before the player's independent audio capture.</summary>
[DisallowMultipleComponent, RequireComponent(typeof(AudioSource))]
public sealed class RaceSoundEffectsMixer : MonoBehaviour
{
    private const int QueueSize=32, VoiceCount=8;
    private readonly RaceSoundClip[] queue=new RaceSoundClip[QueueSize];
    private readonly float[] queueLeft=new float[QueueSize],queueRight=new float[QueueSize];
    private readonly Voice[] voices=new Voice[VoiceCount];
    private int read,write,nextVoice;
    private volatile int outputSampleRate=48000;
    private volatile float musicGain=1f,effectsGain=1f,skidLeft,skidRight;
    private volatile int skidSurface;
    private uint skidRandom=0x981f3a7u;
    private float skidNoise,skidLeftSmooth,skidRightSmooth;
    private struct Voice { public RaceSoundClip clip; public double cursor; public float left,right; }

    private void OnEnable()
    {
        outputSampleRate=AudioSettings.outputSampleRate;
        AudioSettings.OnAudioConfigurationChanged+=OnAudioConfigurationChanged;
    }
    private void OnDisable() => AudioSettings.OnAudioConfigurationChanged-=OnAudioConfigurationChanged;
    private void OnAudioConfigurationChanged(bool changed) => outputSampleRate=AudioSettings.outputSampleRate;
    public void SetMusicGain(float gain) => musicGain=Mathf.Clamp01(gain);
    public void SetEffectsGain(float gain) => effectsGain=Mathf.Clamp01(gain);
    public void SetTireSkid(float left,float right,int surface)
    { skidLeft=Mathf.Clamp01(left); skidRight=Mathf.Clamp01(right); skidSurface=Mathf.Clamp(surface,0,2); }

    /// <summary>Main thread is the sole producer; a full queue drops excess requests.</summary>
    public bool Enqueue(RaceSoundClip clip) => Enqueue(clip,1f,1f);
    public bool Enqueue(RaceSoundClip clip,float left,float right)
    {
        if (clip==null) return false;
        int next=(write+1)%QueueSize;
        if (next==Volatile.Read(ref read)) return false;
        queue[write]=clip; queueLeft[write]=Mathf.Clamp01(left); queueRight[write]=Mathf.Clamp01(right);
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
            voices[nextVoice]=new Voice { clip=queue[read],left=queueLeft[read],right=queueRight[read] };
            queue[read]=null;
            nextVoice=(nextVoice+1)%VoiceCount;
            Volatile.Write(ref read,(read+1)%QueueSize);
        }
        float music=musicGain,effects=effectsGain,tireLeft=skidLeft,tireRight=skidRight;
        int surface=skidSurface;
        double step=(double)RaceSoundClip.SampleRate/System.Math.Max(1,outputSampleRate);
        float skidSmooth=1f-(float)System.Math.Exp(-1.0/(System.Math.Max(1,outputSampleRate)*.02));
        for (int offset=0;offset+channels<=data.Length;offset+=channels)
        {
            float effectLeft=0f,effectRight=0f;
            for (int voice=0;voice<voices.Length;voice++)
            {
                RaceSoundClip clip=voices[voice].clip;
                if (clip==null) continue;
                int index=(int)voices[voice].cursor;
                if (index>=clip.Samples.Length-1) { voices[voice].clip=null; continue; }
                float fraction=(float)(voices[voice].cursor-index);
                float sample=clip.Samples[index]+(clip.Samples[index+1]-clip.Samples[index])*fraction;
                effectLeft += sample*voices[voice].left; effectRight += sample*voices[voice].right;
                voices[voice].cursor+=step;
            }
            skidRandom^=skidRandom<<13; skidRandom^=skidRandom>>17; skidRandom^=skidRandom<<5;
            skidLeftSmooth+=(tireLeft-skidLeftSmooth)*skidSmooth;
            skidRightSmooth+=(tireRight-skidRightSmooth)*skidSmooth;
            float white=(skidRandom&0xffff)*(2f/65535f)-1f;
            float cutoff=surface==0?.045f:surface==1?.12f:.075f;
            skidNoise+=(white-skidNoise)*cutoff;
            float rough=skidNoise*(surface==1?1.35f:surface==2?.72f:1f)*.12f;
            for (int channel=0;channel<channels;channel++)
            {
                float pan=channel==0?skidLeftSmooth:(channel==1?skidRightSmooth:(skidLeftSmooth+skidRightSmooth)*.5f);
                float cue=channel==0?effectLeft:(channel==1?effectRight:(effectLeft+effectRight)*.5f);
                float mixed=data[offset+channel]*music+cue*effects+rough*pan;
                // Smooth limiting prevents sustained full-scale output when cues overlap music.
                data[offset+channel]=mixed/Mathf.Sqrt(1f+mixed*mixed);
            }
        }
    }
}
