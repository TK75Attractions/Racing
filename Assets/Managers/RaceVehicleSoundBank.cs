using UnityEngine;

public enum RaceVehicleAudioCue { LightImpact, HeavyImpact, Scrape, Landing }

/// <summary>Short cached impact sounds generated once, outside the audio thread.</summary>
public static class RaceVehicleSoundBank
{
    private static readonly RaceSoundClip[] clips=new RaceSoundClip[4];
    public static RaceSoundClip Get(RaceVehicleAudioCue cue)
    {
        int index=(int)cue;
        if (clips[index]==null) clips[index]=Build(cue);
        return clips[index];
    }

    private static RaceSoundClip Build(RaceVehicleAudioCue cue)
    {
        float duration=cue==RaceVehicleAudioCue.HeavyImpact?.28f:cue==RaceVehicleAudioCue.Scrape?.32f:.20f;
        float level=cue==RaceVehicleAudioCue.HeavyImpact?.16f:cue==RaceVehicleAudioCue.LightImpact?.13f:
            cue==RaceVehicleAudioCue.Landing?.14f:.09f;
        float[] pcm=new float[Mathf.CeilToInt(duration*RaceSoundClip.SampleRate)];
        uint seed=(uint)(0x83b5d9u+(int)cue*817u);
        float low=0f;
        double phase=0;
        for (int i=0;i<pcm.Length;i++)
        {
            float t=(float)i/RaceSoundClip.SampleRate;
            float envelope=Mathf.Exp(-t*(cue==RaceVehicleAudioCue.Scrape?8f:cue==RaceVehicleAudioCue.HeavyImpact?15f:22f));
            seed^=seed<<13; seed^=seed>>17; seed^=seed<<5;
            float noise=(seed&0xffff)*(2f/65535f)-1f;
            low+=(noise-low)*(cue==RaceVehicleAudioCue.Scrape?.16f:.055f);
            float baseHz=cue==RaceVehicleAudioCue.HeavyImpact?82f:cue==RaceVehicleAudioCue.Landing?118f:cue==RaceVehicleAudioCue.Scrape?310f:185f;
            phase+=2.0*Mathf.PI*baseHz/RaceSoundClip.SampleRate;
            float transient=cue==RaceVehicleAudioCue.Scrape?noise*.18f:noise*.45f;
            pcm[i]=(Mathf.Sin((float)phase)+low*.8f+transient)*level*envelope;
        }
        pcm[0]=pcm[pcm.Length-1]=0f;
        return new RaceSoundClip(pcm);
    }
}
