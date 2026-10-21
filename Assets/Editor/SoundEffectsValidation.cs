using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>Offline PCM/mute/routing regression and audition exports; never plays physical audio.</summary>
public static class SoundEffectsValidation
{
    [MenuItem("Racing/Validation/Sound Effects")]
    public static void Run()
    {
        RunAudioCore();
        PlayerAudioValidation.Run();
        ModernUIValidation.Run();
        Debug.Log("SOUND_EFFECTS_VALIDATION_PASS: file-backed cues, independent player routing, native output compatibility and audio settings UI.");
    }

    // Can also run in a small Unity project with just the audio sources and WAV resources.
    public static void RunAudioCore()
    {
        GameObject root=new GameObject("Sound effects validation"); root.SetActive(false);
        try
        {
            foreach (RaceSoundCue cue in Enum.GetValues(typeof(RaceSoundCue)))
            {
                RaceSoundClip clip=RaceSoundBank.Get(cue);
                Require(clip!=null,"Missing or unreadable audio asset: "+cue);
                AudioClip asset=Resources.Load<AudioClip>(RaceSoundBank.ResourcePath(cue));
                AudioImporter importer=(AudioImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(asset));
                Require(importer.defaultSampleSettings.loadType==AudioClipLoadType.DecompressOnLoad &&
                    importer.defaultSampleSettings.compressionFormat==AudioCompressionFormat.PCM &&
                    importer.defaultSampleSettings.preloadAudioData && !importer.loadInBackground && !importer.forceToMono,
                    "SFX must preload PCM and preserve stereo: "+cue);
                float[] pcm=clip.Samples;
                Require(clip.Channels==2 && clip.Frequency==48000,"Expected authored 48 kHz stereo audio: "+cue);
                Require(clip.Frames>1000 && clip.Frames<clip.Frequency*3.5f,"Cues must have bounded duration.");
                Require(pcm.All(value=>!float.IsNaN(value)&&!float.IsInfinity(value)&&Math.Abs(value)<.4f),"Cue PCM must be finite and have headroom.");
                Require(pcm.Any(value=>Math.Abs(value)>.02f)&&pcm[0]==0&&pcm[pcm.Length-1]==0,"Each cue must be audible with silent attack/release endpoints.");
            }
            RaceSoundEffects effects=root.AddComponent<RaceSoundEffects>();
            RaceSoundEffectsMixer[] mixers=new RaceSoundEffectsMixer[2];
            for (int i=0;i<2;i++)
            {
                GameObject source=new GameObject("Sound source "+i); source.transform.SetParent(root.transform,false);
                mixers[i]=source.AddComponent<RaceSoundEffectsMixer>(); effects.RegisterMixer(i,mixers[i]);
                mixers[i].SetMusicGain(0f); mixers[i].SetEffectsGain(1f);
            }
            int[] queued=new int[2]; effects.CueQueued+=(cue,player)=>queued[player]++;
            Require(effects.Play(RaceSoundCue.Confirm,1),"P2 confirmation must enqueue.");
            Require(!effects.Play(RaceSoundCue.Confirm,1),"Duplicate requests in the same frame must not double cues.");
            float[] p1=new float[2048],p2=new float[2048];
            mixers[0].Mix(p1,2); mixers[1].Mix(p2,2);
            Require(p1.All(value=>value==0f)&&p2.Any(value=>Math.Abs(value)>.01f),"P2-only effects must not enter P1's mix.");
            Require(queued[0]==0&&queued[1]==1,"A cue must be queued only once for its owner.");
            effects.PlayForAll(RaceSoundCue.Go);
            Require(queued[0]==1&&queued[1]==2,"A shared announcement must enqueue once per destination.");
            mixers[0].Mix(p1,2);
            Require(p1.Any(value=>Math.Abs(value)>.01f),"BGM mute must not mute sound effects.");
            mixers[0].SetEffectsGain(0f); mixers[0].Mix(p1,2);
            Require(p1.All(value=>value==0f),"Effects mute must silence active voices immediately.");
            mixers[0].SetMusicGain(.5f); Array.Fill(p1,.25f); mixers[0].Mix(p1,2);
            Require(p1.All(value=>Math.Abs(value-.125f)<.00001f),"Effects mute must preserve the independent BGM gain.");
            ValidateStereoAndResampling(root.transform);
            ExportPreview();
            Debug.Log("SOUND_EFFECTS_AUDIO_CORE_PASS: "+RaceSoundBank.CueCount+" imported WAVs, PCM settings, stereo, resampling, overlap headroom, player routing, duplicate suppression and independent mute.");
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }

    private static RaceSoundEffectsMixer CreateMixer(Transform root)
    {
        GameObject child=new GameObject("PCM test"); child.transform.SetParent(root,false);
        return child.AddComponent<RaceSoundEffectsMixer>();
    }

    private static void ValidateStereoAndResampling(Transform root)
    {
        RaceSoundClip stereo=new RaceSoundClip(new float[] { 0,.2f, .2f,0, .4f,-.2f, 0,0 },2,24000);
        RaceSoundEffectsMixer mixer=CreateMixer(root);
        mixer.Enqueue(stereo);
        float[] pcm=new float[16]; mixer.Mix(pcm,2);
        Require(Math.Abs(pcm[1]-.2f)<.00001f && Math.Abs(pcm[2]-.1f)<.00001f && Math.Abs(pcm[3]-.1f)<.00001f,
            "Stereo samples must interpolate independently at the source rate.");
        mixer=CreateMixer(root); mixer.Enqueue(stereo);
        float[] mono=new float[8]; mixer.Mix(mono,1);
        Require(Math.Abs(mono[0]-.1f)<.00001f,"Mono output must average left and right.");
        RaceSoundEffectsMixer whole=CreateMixer(root), split=CreateMixer(root);
        whole.Enqueue(stereo); split.Enqueue(stereo);
        float[] continuous=new float[16], first=new float[6], second=new float[10];
        whole.Mix(continuous,2); split.Mix(first,2); split.Mix(second,2);
        Require(continuous.SequenceEqual(first.Concat(second)),"Callback boundaries must preserve the playback cursor.");
        mixer=CreateMixer(root);
        for(int i=0;i<8;i++) mixer.Enqueue(new RaceSoundClip(Enumerable.Repeat(.35f,128).ToArray()));
        float[] overlap=Enumerable.Repeat(.8f,64).ToArray(); mixer.Mix(overlap,2);
        Require(overlap.All(value=>value>.9f && value<1f),"Eight simultaneous voices plus BGM must remain below clipping.");
        Require(!mixer.Enqueue(new RaceSoundClip(Array.Empty<float>())),"Empty audio must not enter the DSP queue.");
    }

    private static void ExportPreview()
    {
        string directory=Path.Combine(Directory.GetCurrentDirectory(),"Logs/SoundEffects"); Directory.CreateDirectory(directory);
        using (MemoryStream sequence=new MemoryStream())
        using (BinaryWriter samples=new BinaryWriter(sequence))
        {
            foreach (RaceSoundCue cue in Enum.GetValues(typeof(RaceSoundCue)))
            {
                RaceSoundClip clip=RaceSoundBank.Get(cue);
                float[] pcm=clip.Samples;
                using (MemoryStream individual=new MemoryStream())
                using (BinaryWriter writer=new BinaryWriter(individual))
                {
                    foreach (float value in pcm) { short sample=(short)(Mathf.Clamp(value*.8f,-1f,1f)*32767); writer.Write(sample); samples.Write(sample); }
                    WriteWave(Path.Combine(directory,cue+".wav"),individual.ToArray());
                }
                for (int frame=0;frame<RaceSoundClip.SampleRate/4;frame++) { samples.Write((short)0); samples.Write((short)0); }
            }
            WriteWave(Path.Combine(directory,"preview.wav"),sequence.ToArray());
        }
    }
    private static void WriteWave(string path,byte[] pcm)
    {
        using (BinaryWriter writer=new BinaryWriter(File.Create(path)))
        {
            writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); writer.Write(36+pcm.Length);
            writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16); writer.Write((short)1); writer.Write((short)2);
            writer.Write(RaceSoundClip.SampleRate); writer.Write(RaceSoundClip.SampleRate*4); writer.Write((short)4); writer.Write((short)16);
            writer.Write(System.Text.Encoding.ASCII.GetBytes("data")); writer.Write(pcm.Length); writer.Write(pcm);
        }
    }
    private static void Require(bool condition,string message) { if (!condition) throw new Exception(message); }
}
