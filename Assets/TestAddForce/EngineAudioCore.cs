using System;
using System.Threading;
using UnityEngine;

[RequireComponent(typeof(AudioSource))]
public class EngineAudioCore : MonoBehaviour
{
    private EngineSynth synth;
    private volatile float targetRpm = AutomaticGearbox.IdleRpm, targetLoad, targetGain, combustion = 1f, boost, tunnel;
    private volatile int shiftSerial;
    private AudioSource source;
    private AudioClip clock;
#if UNITY_EDITOR
    private PreviewRecording recording;
    private sealed class PreviewRecording
    {
        public readonly float[] Samples;
        public readonly int Rate;
        public int Count;
        public PreviewRecording(int rate, int seconds) { Rate = rate; Samples = new float[rate * seconds * 2]; }
        public void Append(float[] data, int channels)
        {
            int count = Count;
            for (int i = 0; i + channels <= data.Length && count + 2 <= Samples.Length; i += channels)
            {
                Samples[count++] = data[i];
                Samples[count++] = data[i + (channels > 1 ? 1 : 0)];
            }
            Volatile.Write(ref Count, count);
        }
    }
    public void BeginPreviewRecording(int seconds) => Volatile.Write(ref recording,
        new PreviewRecording(Volatile.Read(ref synth).SampleRate, seconds));
    public float[] EndPreviewRecording(out int rate)
    {
        PreviewRecording capture = Interlocked.Exchange(ref recording, null);
        rate = capture?.Rate ?? AudioSettings.outputSampleRate;
        if (capture == null) return Array.Empty<float>();
        int count = Volatile.Read(ref capture.Count);
        float[] samples = new float[count];
        Array.Copy(capture.Samples, samples, count);
        return samples;
    }
#endif

    private void OnEnable()
    {
        source = GetComponent<AudioSource>();
        AudioSettings.OnAudioConfigurationChanged += RefreshSampleRate;
        RefreshSampleRate(false);
    }
    private void OnDisable()
    {
        AudioSettings.OnAudioConfigurationChanged -= RefreshSampleRate;
        source.Stop();
    }
    private void OnDestroy()
    {
        if (clock != null) Destroy(clock);
    }
    private void RefreshSampleRate(bool deviceWasChanged)
    {
        int rate = Math.Max(8000, AudioSettings.outputSampleRate);
        // Publish a fully initialized processor; the old callback may finish on the previous instance.
        Volatile.Write(ref synth, new EngineSynth(rate, unchecked((uint)GetInstanceID())));
        source.spatialBlend = 0f; source.dopplerLevel = 0f;
        // A silent looping clock keeps OnAudioFilterRead active even when the prefab has no clip.
        if (clock != null) Destroy(clock);
        clock = AudioClip.Create("Procedural engine clock", 1024, 1, rate, false);
        source.clip = clock; source.loop = true;
        source.Play();
    }
    public void SetEngineState(float rpm, float load, float gain, float fuel, int shift, float boostAmount, float enclosure)
    {
        targetRpm = rpm; targetLoad = load; targetGain = gain;
        combustion = fuel; shiftSerial = shift; boost = boostAmount; tunnel = enclosure;
    }
    private void OnAudioFilterRead(float[] data, int channels)
    {
        EngineSynth processor = Volatile.Read(ref synth);
        if (processor == null) { Array.Clear(data, 0, data.Length); return; }
        processor.Render(data, channels, targetRpm, targetLoad, targetGain, combustion, shiftSerial, boost, tunnel);
#if UNITY_EDITOR
        Volatile.Read(ref recording)?.Append(data, channels);
#endif
    }
}
