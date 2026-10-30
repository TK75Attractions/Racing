using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

/// <summary>USB/HDMI実機を鳴らさず、周回・観戦・視点別ミックスを検証します。</summary>
public static class PlayerAudioValidation
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    [MenuItem("Racing/Validate Player Audio")]
    public static void Run()
    {
        GameObject root = new GameObject("Player audio validation");
        root.SetActive(false); // Avoid Gmanager.Awake and opening physical output devices.
        try
        {
            Gmanager manager = root.AddComponent<Gmanager>();
            LapManager laps = root.AddComponent<LapManager>();
            Set(manager, "lapManager", laps);
            Array players = (Array)Get(manager, "players");
            Type runtimeType = players.GetType().GetElementType();
            Rigidbody[] bodies = new Rigidbody[2];
            Transform[] cameras = new Transform[2];
            for (int i = 0; i < 2; i++)
            {
                GameObject car = Child(root.transform, "Car" + i);
                bodies[i] = car.AddComponent<Rigidbody>();
                laps.RegisterCar(bodies[i]);
                object player = Activator.CreateInstance(runtimeType, true);
                Set(player, "playerIndex", i); Set(player, "car", car); Set(player, "rigidbody", bodies[i]);
                GameObject cameraRoot = Child(root.transform, "CameraRoot" + i);
                GameObject cameraObject = Child(cameraRoot.transform, "MainCamera");
                cameraObject.AddComponent<Camera>();
                cameras[i] = cameraObject.transform;
                Set(player, "displayRig", new PlayerDisplayRig(i, cameraRoot, null, null, null, false));
                players.SetValue(player, i);
            }

            RaceAudioOutput audioOutput = root.AddComponent<RaceAudioOutput>();
            Set(audioOutput, "sampleRate", AudioSettings.outputSampleRate);
            Set(audioOutput, "accepting", true);
            typeof(RaceAudioOutput).GetMethod("OnAudioConfigurationChanged", Private)
                .Invoke(audioOutput, new object[] { true });
            Require((bool)Get(audioOutput, "accepting"),
                "A default-device notification at the same DSP rate must not interrupt independent outputs.");
            Set(audioOutput, "accepting", false);

            RaceBackgroundMusic music = root.AddComponent<RaceBackgroundMusic>();
            Call(music, "Awake");
            AudioSource[] sources = (AudioSource[])Get(music, "sources");
            Require(sources.Length == 2 && sources[0] != sources[1], "Each player needs an independent source.");
            manager.state = Gmanager.State.Title;
            AdvanceMusic(music);
            ValidateTransport(music, sources);
            Require(sources.All(s => s.clip != null && s.clip.name == "Race_Opening_v3"), "Title must use the opening track.");
            Set(players.GetValue(0), "preparationState", Gmanager.State.CarSelection);
            manager.state = Gmanager.State.CarSelection;
            AdvanceMusic(music);
            Require(sources.All(s => s.clip.name == "Race_Opening_v3"), "Car selection and the other title screen must keep the opening track.");
            Set(players.GetValue(0), "preparationState", Gmanager.State.Tutorial);
            manager.state = Gmanager.State.Tutorial;
            AdvanceMusic(music);
            Require(sources[0].clip.name == "Race_Opening_v3" && sources[1].clip.name == "Race_Opening_v3",
                "Each screen must select tutorial or opening music independently of the shared state.");
            Set(players.GetValue(1), "preparationState", Gmanager.State.Tutorial);
            AdvanceMusic(music);
            Require(sources.All(s => s.clip.name == "Race_Opening_v3"), "Both tutorial screens must keep the opening track.");
            manager.state = Gmanager.State.Countdown;
            typeof(RaceBackgroundMusic).GetMethod("Tick", Private).Invoke(music, new object[] { 0f });
            Require(sources.All(s => s.clip.name == "Race_Opening_v3"), "Track changes must fade out before replacing the current clip.");
            AdvanceMusic(music);
            Require(sources.All(s => s.clip.name == "racegame_v3"), "Countdown must use the new race track.");
            manager.state = Gmanager.State.Game;
            Require(laps.GoalLap == 2, "The default race must be two laps.");
            AdvanceMusic(music);
            Require(sources[0].clip.name == "racegame_v3", "The first lap must use normal race music.");
            laps.DebugAdvanceLap(bodies[0], false);
            AdvanceMusic(music);
            Require(sources[0].clip.name == "racegame_v3_final lap", "P1 must switch on entering lap two.");
            Require(sources[1].clip.name == "racegame_v3", "P1 final lap must not change P2 music.");
            laps.DebugAdvanceLap(bodies[1], false);
            AdvanceMusic(music);
            Require(sources[1].clip.name == "racegame_v3_final lap", "P2 must switch independently.");
            laps.GetCarData(bodies[1]).lapCount = 0;
            Set(players.GetValue(0), "result", new RaceResultRecord { didFinish = true });
            AdvanceMusic(music);
            Require(sources[0].clip.name == "finishv4" && sources[1].clip.name == "racegame_v3",
                "The goal celebration must start finish music only for the finisher.");
            Set(players.GetValue(0), "isSpectating", true);
            AdvanceMusic(music);
            Require(manager.GetAudioViewedPlayer(0) == 1 && sources[0].clip.name == "finishv4" && !sources[0].loop,
                "The full finish cue plays once, including while spectating.");
            FinishMusicSequence[] sequences=(FinishMusicSequence[])Get(music,"finishSequences");
            // Simulate the DSP boundary after that one shot has finished, without playing real devices.
            sequences[0].Reset();sequences[0].Begin(-100,1);
            AdvanceMusic(music);
            Require(sources[0].clip==sources[1].clip && sources[0].loop,
                "After the cue, the spectator follows the opponent's race transport.");
            Set(players.GetValue(0), "result", new RaceResultRecord { didFinish = false });
            AdvanceMusic(music);
            Require(sources[0].clip == sources[1].clip, "A DNF spectator still follows the watched player's race music.");
            manager.state = Gmanager.State.Goal;
            AdvanceMusic(music);
            Require(sources.All(s => s.clip.name == "result_v3"), "DNF screens go directly to result music.");
            AudioClip resultTrack=sources[0].clip;
            var epochs=(System.Collections.Generic.Dictionary<AudioClip,double>)Get(music,"clipEpochs");
            double resultEpoch=epochs[resultTrack];
            manager.state = Gmanager.State.Result;
            AdvanceMusic(music);
            Require(sources.All(s => s.clip.name == "result_v3") && epochs[resultTrack]==resultEpoch,
                "Entering results continues the existing result transport without restarting.");
            Set(players.GetValue(0), "returnedToTitle", true);
            AdvanceMusic(music);
            Require(sources[0].clip.name == "Race_Opening_v3" && sources[1].clip.name == "result_v3",
                "Returning one screen to the title must not change the other result screen.");
            laps.ResetRace();
            for (int i = 0; i < 2; i++)
            {
                laps.RegisterCar(bodies[i]);
                Set(players.GetValue(i), "isSpectating", false);
                Set(players.GetValue(i), "result", null);
                Set(players.GetValue(i), "returnedToTitle", false);
            }
            AudioClip[] beforeRetry = sources.Select(source => source.clip).ToArray();
            manager.state = Gmanager.State.Countdown;
            typeof(RaceBackgroundMusic).GetMethod("Tick", Private).Invoke(music, new object[] { 0f });
            Require(sources.Select(source => source.clip).SequenceEqual(beforeRetry), "Retry must fade the two independently selected previous tracks before replacing them.");
            AdvanceMusic(music);
            Require(sources.All(s => s.clip.name == "racegame_v3"), "Retry must restore normal race music.");
            Set(laps, "goalLap", 1);
            AdvanceMusic(music);
            Require(sources.All(s => s.clip.name == "racegame_v3_final lap"), "Single-lap races start on the final lap.");
            Set(laps, "goalLap", 0);
            Require(!manager.IsPlayerOnFinalLap(0) && !manager.IsPlayerOnFinalLap(-1), "Unlimited or invalid laps must not be final.");

            PlayerAudioCapture capture = Child(root.transform, "Engine").AddComponent<PlayerAudioCapture>();
            capture.ConfigureEngine(root.GetComponent<RaceAudioOutput>(), manager, bodies[0].transform, 0);
            bodies[0].transform.position = new Vector3(4, 0, 4);
            cameras[0].position = Vector3.zero;
            cameras[1].position = new Vector3(0, 0, 100);
            object[] arguments = { 0, 1f, 0f, 0f };
            typeof(PlayerAudioCapture).GetMethod("GetPerspectiveGains", Private).Invoke(capture, arguments);
            Require((float)arguments[3] > (float)arguments[2], "An engine to the right must pan to the right.");
            arguments[0] = 1;
            typeof(PlayerAudioCapture).GetMethod("GetPerspectiveGains", Private).Invoke(capture, arguments);
            Require((float)arguments[2] == 0 && (float)arguments[3] == 0, "Distant engines must be silent in the other viewpoint.");
            foreach (Gmanager.State silentState in new[] { Gmanager.State.Goal, Gmanager.State.Result, Gmanager.State.Title })
            {
                manager.state = silentState;
                arguments[0] = 0;
                typeof(PlayerAudioCapture).GetMethod("GetPerspectiveGains", Private).Invoke(capture, arguments);
                Require((float)arguments[2] == 0 && (float)arguments[3] == 0,
                    "Engine PCM must be silent during " + silentState);
            }
            float[] pcm = { .5f, -.5f, 1f, -1f };
            typeof(PlayerAudioCapture).GetMethod("OnAudioFilterRead", Private).Invoke(capture, new object[] { pcm, 2 });
            Require(pcm.All(sample => sample == 0), "Captured audio must never leak into Unity's default output.");
            if (Application.isBatchMode)
            {
                // Check Unity's platform importer and P/Invoke name resolution, without playing audio.
                BindingFlags nativeFlags = BindingFlags.Static | BindingFlags.NonPublic;
                Type outputType = typeof(RaceAudioOutput);
                PluginImporter importer = AssetImporter.GetAtPath("Assets/Plugins/RaceAudio/macOS/libRaceAudio.dylib") as PluginImporter;
                Debug.Log($"Race audio importer: {importer != null}, Editor={importer?.GetCompatibleWithEditor()}, CPU={importer?.GetEditorData("CPU")}, OS={importer?.GetEditorData("OS")}");
                int count = (int)outputType.GetMethod("ra_refresh_devices", nativeFlags).Invoke(null, null);
                outputType.GetMethod("ra_shutdown", nativeFlags).Invoke(null, null);
                Require(count >= 0, "Unity must load the native plugin and enumerate real audio endpoints.");
            }
            Debug.Log("PlayerAudioValidation PASSED: opening/selection, independent tutorials and final laps, one-shot finish/opponent transport/continuous result music, individual title return, retry, single lap, stereo viewpoint, unchanged-rate device notification and no default-output leakage.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    private static void ValidateTransport(RaceBackgroundMusic music, AudioSource[] sources)
    {
        var epochs = (System.Collections.Generic.Dictionary<AudioClip, double>)Get(music, "clipEpochs");
        AudioClip clip = sources[0].clip;
        double epoch = epochs[clip];
        var start = typeof(RaceBackgroundMusic).GetMethod("StartOnTransport", Private);
        // Join several seconds later, then after a complete loop. Both must use
        // the same transport rather than restarting their local track at zero.
        foreach (double offset in new[] { 4.0, (double)clip.samples / clip.frequency + 7.0 })
        {
            double at = epoch + offset;
            start.Invoke(music, new object[] { 1, clip, at });
            int expected = (int)(Math.Floor(offset * clip.frequency) % clip.samples);
            Require(Math.Abs(sources[1].timeSamples - expected) <= 1, "Late join/loop must seek to the shared sample position.");
            Require(epochs[clip] == epoch, "Joining must not reset the transport for the other output.");
        }
        double boundary = AudioSettings.dspTime + .2;
        start.Invoke(music, new object[] { 0, clip, boundary });
        start.Invoke(music, new object[] { 1, clip, boundary });
        Require(sources[0].timeSamples == sources[1].timeSamples, "Same-boundary starts must select the identical PCM sample.");
    }

    private static void AdvanceMusic(RaceBackgroundMusic music)
    {
        // Advance the real fade deterministically; edit-mode Update has no reliable frame delta.
        MethodInfo tick = typeof(RaceBackgroundMusic).GetMethod("Tick", Private);
        for (int frame = 0; frame < 20; frame++)
        {
            // Sources are inactive in this fixture; simulate passing their DSP start.
            var starts = (double[])Get(music, "playbackStarts");
            starts[0] = starts[1] = 0;
            tick.Invoke(music, new object[] { .05f });
        }
    }

    private static GameObject Child(Transform parent, string name)
    {
        GameObject obj = new GameObject(name);
        obj.transform.SetParent(parent, false);
        return obj;
    }
    private static object Get(object target, string name) => target.GetType().GetField(name, Private | BindingFlags.Public).GetValue(target);
    private static void Set(object target, string name, object value) => target.GetType().GetField(name, Private | BindingFlags.Public).SetValue(target, value);
    private static void Call(object target, string name) => target.GetType().GetMethod(name, Private).Invoke(target, null);
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
