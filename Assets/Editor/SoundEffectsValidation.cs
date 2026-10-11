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
        GameObject root=new GameObject("Sound effects validation"); root.SetActive(false);
        try
        {
            foreach (RaceSoundCue cue in Enum.GetValues(typeof(RaceSoundCue)))
            {
                float[] pcm=RaceSoundBank.Get(cue).Samples;
                Require(pcm.Length>1000 && pcm.Length<RaceSoundClip.SampleRate,"Cues must be short.");
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
            PlayerAudioValidation.Run();
            ModernUIValidation.Run();
            ExportPreview();
            Debug.Log("SOUND_EFFECTS_VALIDATION_PASS: nine synthesized cues, player routing, duplicate suppression, independent BGM/effects mute, native output compatibility and audio settings UI.");
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }

    private static void ExportPreview()
    {
        string directory=Path.Combine(Directory.GetCurrentDirectory(),"Logs/SoundEffects"); Directory.CreateDirectory(directory);
        using (MemoryStream sequence=new MemoryStream())
        using (BinaryWriter samples=new BinaryWriter(sequence))
        {
            foreach (RaceSoundCue cue in Enum.GetValues(typeof(RaceSoundCue)))
            {
                float[] pcm=RaceSoundBank.Get(cue).Samples;
                using (MemoryStream individual=new MemoryStream())
                using (BinaryWriter writer=new BinaryWriter(individual))
                {
                    foreach (float value in pcm) { short sample=(short)(Mathf.Clamp(value*.8f,-1f,1f)*32767); writer.Write(sample); samples.Write(sample); }
                    WriteWave(Path.Combine(directory,cue+".wav"),individual.ToArray());
                }
                for (int frame=0;frame<RaceSoundClip.SampleRate/4;frame++) samples.Write((short)0);
            }
            WriteWave(Path.Combine(directory,"preview.wav"),sequence.ToArray());
        }
    }
    private static void WriteWave(string path,byte[] pcm)
    {
        using (BinaryWriter writer=new BinaryWriter(File.Create(path)))
        {
            writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); writer.Write(36+pcm.Length);
            writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16); writer.Write((short)1); writer.Write((short)1);
            writer.Write(RaceSoundClip.SampleRate); writer.Write(RaceSoundClip.SampleRate*2); writer.Write((short)2); writer.Write((short)16);
            writer.Write(System.Text.Encoding.ASCII.GetBytes("data")); writer.Write(pcm.Length); writer.Write(pcm);
        }
    }
    private static void Require(bool condition,string message) { if (!condition) throw new Exception(message); }
}
