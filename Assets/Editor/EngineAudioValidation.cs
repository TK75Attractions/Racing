using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Debug = UnityEngine.Debug;

/// <summary>Renders the shipping engine DSP offline and checks gearbox, acoustics and routing integration.</summary>
public static class EngineAudioValidation
{
    private const int Rate = 48000;
    [MenuItem("Racing/Validation/Engine Audio")]
    public static void Run()
    {
        string output = Path.Combine(Directory.GetCurrentDirectory(), "Artifacts/EngineAudio");
        Directory.CreateDirectory(output);
        ValidateGearbox();
        ValidateDsp(output);
        ValidateScene();
        PlayerAudioValidation.Run();
        ModernUIValidation.Run();
        Debug.Log("ENGINE_AUDIO_VALIDATION_PASS: six gears, hysteresis, kickdown, reverse, reset, PCM continuity, mute, sample rates, reverb tails, actual tube and prefab integration.");
    }

    private static void ValidateGearbox()
    {
        // Regression for premature economy-style changes: use the low gears up to racing speeds.
        AutomaticGearbox racing = new AutomaticGearbox();
        float previousSpeed = 0;
        foreach (var checkpoint in new[] { (Kph: 55f, Gear: 1), (Kph: 80f, Gear: 2), (Kph: 110f, Gear: 3) })
        {
            float speed = checkpoint.Kph / 3.6f;
            for (int i = 0; i < 240; i++) racing.Step(1f / 60, Mathf.Lerp(previousSpeed, speed, i / 239f), 1, true, 0);
            Require(racing.Gear == checkpoint.Gear, $"Must retain gear {checkpoint.Gear} at {checkpoint.Kph} km/h.");
            previousSpeed = speed;
        }
        racing.Reset();
        for (int i = 0; i < 240; i++) racing.Step(1f / 60, i / 239f * 16, .4f, true, 0);
        Require(racing.Gear == 1, "Part throttle must still hold first near 58 km/h.");
        for (int i = 0; i < 120; i++) racing.Step(1f / 60, 16, 0, true, 0);
        Require(racing.Gear == 1, "Lifting before a corner must not trigger an early upshift.");
        AutomaticGearbox box = new AutomaticGearbox();
        int highest = 1, previous = 1;
        for (int i = 0; i < 1200; i++)
        {
            box.Step(1f / 60, i / 1200f * 70, 1f, true, 0);
            Require(box.Rpm >= 950 && box.Rpm <= 7800 && Math.Abs(box.Gear - previous) <= 1, "Invalid RPM or skipped gear.");
            highest = Math.Max(highest, box.Gear); previous = box.Gear;
        }
        Require(highest == 6, "A full acceleration must reach sixth.");
        int serial = box.ShiftSerial;
        for (int i = 0; i < 240; i++) box.Step(1f / 60, 68 + Mathf.Sin(i) * .1f, 1, true, 0);
        Require(box.ShiftSerial == serial, "Steady speed must not hunt between gears.");
        for (int i = 0; i < 180; i++) box.Step(1f / 60, 18, 0, true, 0);
        int coastGear = box.Gear;
        for (int i = 0; i < 90; i++) box.Step(1f / 60, 18, 1, true, 0);
        Require(box.Gear < coastGear, "A loaded low-RPM engine must kick down.");
        int airborneGear = box.Gear;
        for (int i = 0; i < 120; i++) box.Step(1f / 60, 60, 1, false, 0);
        Require(box.Gear == airborneGear && box.Rpm > 7000, "Airborne free revs must not shift road gears.");
        box.Step(.02f, 0, 0, true, 0);
        Require(box.Gear == 1, "Stopping/respawning must restore first.");
        for (int i = 0; i < 120; i++) box.Step(1f / 60, -20, 1, true, 0);
        Require(box.Gear == 1, "Reverse must never shift through forward gears.");
        float rpm = box.Rpm; box.Step(0, 50, 1, true, 0);
        Require(box.Rpm == rpm, "Pause must freeze the transmission.");
        box.Reset(); Require(box.Rpm == 950 && box.Gear == 1, "Retry must reset to idle first gear.");
    }

    private static void ValidateDsp(string output)
    {
        float[] gt = new float[Rate * 2], rs = new float[Rate * 2], gtr = new float[Rate * 2];
        new EngineSynth(Rate, 123u, 0).Render(gt, 2, 4200, .7f, .2f, 1, 0, 0, 0);
        new EngineSynth(Rate, 123u, 1).Render(rs, 2, 4200, .7f, .2f, 1, 0, 0, 0);
        new EngineSynth(Rate, 123u, 2).Render(gtr, 2, 4200, .7f, .2f, 1, 0, 0, 0);
        Require(!gt.SequenceEqual(rs) && !rs.SequenceEqual(gtr) && !gt.SequenceEqual(gtr),
            "Each vehicle profile must produce a distinct but valid exhaust resonance.");
        foreach (int rate in new[] { 44100, 48000, 96000 })
        {
            EngineSynth synth = new EngineSynth(rate);
            float[] pcm = new float[rate * 2];
            synth.Render(pcm, 2, 7800, 1, .2f, 1, 0, 1, 1);
            Require(pcm.All(x => !float.IsNaN(x) && !float.IsInfinity(x) && Math.Abs(x) < .5f), "PCM must be finite with mix headroom.");
            synth.Render(pcm, 2, 950, 0, 0, 1, 0, 0, 0);
            Require(pcm.Skip(pcm.Length - 1000).All(x => Math.Abs(x) < .000001f), "Mute must also silence reverb tails.");
        }
        // Identical target automation must be independent of callback block size.
        float[] whole = new float[Rate * 2], chunks = new float[whole.Length], block = new float[512];
        new EngineSynth(Rate).Render(whole, 2, 4200, .8f, .2f, 1, 0, 0, 0);
        EngineSynth split = new EngineSynth(Rate);
        for (int i = 0; i < chunks.Length; i += block.Length)
        {
            int length = Math.Min(block.Length, chunks.Length - i);
            float[] part = length == block.Length ? block : new float[length];
            split.Render(part, 2, 4200, .8f, .2f, 1, 0, 0, 0);
            Array.Copy(part, 0, chunks, i, length);
        }
        Require(whole.SequenceEqual(chunks), "Callback boundaries must not reset phase or filter memory.");
        TunnelReverb reverb = new TunnelReverb(Rate);
        double earlyEnergy = 0, lateEnergy = 0, stereo = 0;
        for (int i = 0; i < Rate * 3; i++)
        {
            reverb.Process(i == 0 ? .5f : 0, i == 0 ? 1 : 0, out float l, out float r);
            if (i > Rate / 10 && i < Rate / 2) earlyEnergy += l * l + r * r;
            if (i > Rate * 2) lateEnergy += l * l + r * r;
            stereo += Math.Abs(l - r);
        }
        Require(earlyEnergy > .0001 && lateEnergy < earlyEnergy * .001 && stereo > .01, "Reflections must have stereo width and a decaying exit tail.");
        WriteWave(Path.Combine(output, "01-idle.wav"), Constant(950, .05f, 0, 3));
        WriteWave(Path.Combine(output, "03-open-road.wav"), Constant(5200, .85f, 0, 5));
        WriteWave(Path.Combine(output, "04-tunnel.wav"), Constant(5200, .85f, 1, 5));
        ExportDrive(output);
        // Four continuous cars on this machine; report instead of a hardware-dependent pass threshold.
        EngineSynth[] engines = Enumerable.Range(0, 4).Select(_ => new EngineSynth(Rate)).ToArray();
        float[] buffer = new float[1024];
        foreach (EngineSynth engine in engines) engine.Render(buffer, 2, 6000, 1, .2f, 1, 0, 1, 1);
        Stopwatch watch = new Stopwatch();
        long allocation = GC.GetAllocatedBytesForCurrentThread();
        watch.Start();
        for (int i = 0; i < 1000; i++) foreach (EngineSynth engine in engines) engine.Render(buffer, 2, 6000, 1, .2f, 1, 0, 1, 1);
        watch.Stop();
        long allocated = GC.GetAllocatedBytesForCurrentThread() - allocation;
        Require(allocated == 0, "DSP rendering must allocate no garbage.");
        File.WriteAllText(Path.Combine(output, "performance.txt"), $"Four cars / 10.667 seconds each: {watch.Elapsed.TotalMilliseconds:F1} ms offline, allocation delta (excluding Stopwatch): {allocated} bytes\n");
    }

    private static float[] Constant(float rpm, float load, float room, int seconds)
    {
        float[] data = new float[Rate * seconds * 2];
        new EngineSynth(Rate).Render(data, 2, rpm, load, .2f, 1, 0, 0, room);
        return data;
    }
    private static void ExportDrive(string output)
    {
        const int seconds = 30, frames = Rate / 60;
        float[] pcm = new float[Rate * seconds * 2], block = new float[frames * 2];
        AutomaticGearbox box = new AutomaticGearbox(); EngineSynth synth = new EngineSynth(Rate);
        using (StreamWriter csv = new StreamWriter(Path.Combine(output, "drive.csv")))
        {
            csv.WriteLine("seconds,speed_mps,throttle,gear,rpm,load,shift,tunnel");
            for (int i = 0; i < seconds * 60; i++)
            {
                float t = i / 60f;
                float speed = t < 2 ? 0 : t < 19 ? (t - 2) / 17 * 46 : Mathf.Max(0, 46 - (t - 19) * 5.5f);
                float pedal = t < 2 || t > 19 ? 0 : 1;
                float room = t > 10 && t < 18 ? 1 : 0;
                float boost = t > 13 && t < 16 ? 1 : 0;
                box.Step(1f / 60, speed, pedal, true, boost);
                synth.Render(block, 2, box.Rpm, box.Load, .2f, box.Combustion, box.ShiftSerial, boost, room);
                Array.Copy(block, 0, pcm, i * block.Length, block.Length);
                csv.WriteLine(FormattableString.Invariant($"{t:F4},{speed:F3},{pedal},{box.Gear},{box.Rpm:F1},{box.Load:F3},{box.IsShifting},{room}"));
            }
        }
        WriteWave(Path.Combine(output, "02-acceleration-tunnel-braking.wav"), pcm);
    }
    public static void WriteWave(string path, float[] samples, int sampleRate = Rate)
    {
        using (BinaryWriter writer = new BinaryWriter(File.Create(path)))
        {
            writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + samples.Length * 2);
            writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16); writer.Write((short)1); writer.Write((short)2);
            writer.Write(sampleRate); writer.Write(sampleRate * 4); writer.Write((short)4); writer.Write((short)16);
            writer.Write(System.Text.Encoding.ASCII.GetBytes("data")); writer.Write(samples.Length * 2);
            foreach (float value in samples) writer.Write((short)(Mathf.Clamp(value, -1, 1) * 32767));
        }
    }
    private static void ValidateScene()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        RaceCourse course = UnityEngine.Object.FindFirstObjectByType<RaceCourse>();
        course.RebuildRoad();
        Physics.SyncTransforms();
        Vector3[] inside = { new Vector3(673.90f, -24.0f, 343.36f), new Vector3(577.20f, -24.0f, 173.96f) };
        foreach (Vector3 p in inside)
        {
            float enclosure = TunnelAcoustics.EnclosureAt(p);
            Debug.Log($"ENGINE_TUNNEL_SAMPLE: {p} enclosure={enclosure:F2}");
            Require(enclosure > .5f, "Actual driven tunnel points must be enclosed.");
        }
        foreach (Vector3 p in new[] { new Vector3(674, 5, 343), new Vector3(684, 50, 320), new Vector3(550, -15, 400) })
            Require(TunnelAcoustics.EnclosureAt(p) == 0, "Land above/between tube arms must remain dry: " + p);
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefab/car/car2.prefab");
        CarSoundController controller = prefab.GetComponentInChildren<CarSoundController>(true);
        Require(controller != null && controller.audioCore != null && controller.carRigidbody != null, "Actual car must wire the engine and body.");
        Require(prefab.GetComponentsInChildren<EngineSoundSynthesizer>(true).Length == 0, "Legacy oscillator must not double the new engine.");
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
