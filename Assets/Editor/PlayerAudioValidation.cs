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

            RaceBackgroundMusic music = root.AddComponent<RaceBackgroundMusic>();
            Call(music, "Awake");
            AudioSource[] sources = (AudioSource[])Get(music, "sources");
            Require(sources.Length == 2 && sources[0] != sources[1], "Each player needs an independent source.");
            manager.state = Gmanager.State.Title;
            Call(music, "Update");
            Require(sources.All(s => s.clip != null && s.clip.name == "TachibanaMoroe"), "Title music must remain intact.");
            manager.state = Gmanager.State.Countdown;
            Call(music, "Update");
            Require(sources.All(s => s.clip.name == "racegame_v3"), "Countdown must use the new race track.");
            manager.state = Gmanager.State.Game;
            laps.DebugAdvanceLap(bodies[0], false);
            Call(music, "Update");
            Require(sources[0].clip.name == "racegame_v3", "One completed lap is not final in a three-lap race.");
            laps.DebugAdvanceLap(bodies[0], false);
            Call(music, "Update");
            Require(sources[0].clip.name == "racegame_v3_final lap", "P1 must switch on entering lap three.");
            Require(sources[1].clip.name == "racegame_v3", "P1 final lap must not change P2 music.");
            laps.DebugAdvanceLap(bodies[1], false);
            laps.DebugAdvanceLap(bodies[1], false);
            Call(music, "Update");
            Require(sources[1].clip.name == "racegame_v3_final lap", "P2 must switch independently.");
            laps.GetCarData(bodies[1]).lapCount = 0;
            Set(players.GetValue(0), "isSpectating", true);
            Call(music, "Update");
            Require(manager.GetAudioViewedPlayer(0) == 1 && sources[0].clip == sources[1].clip,
                "Spectator music must follow the watched player.");
            manager.state = Gmanager.State.Result;
            Call(music, "Update");
            Require(sources.All(s => s.clip.name == "TachibanaMoroe"), "Results must restore menu music.");
            laps.ResetRace();
            for (int i = 0; i < 2; i++) { laps.RegisterCar(bodies[i]); Set(players.GetValue(i), "isSpectating", false); }
            manager.state = Gmanager.State.Countdown;
            Call(music, "Update");
            Require(sources.All(s => s.clip.name == "racegame_v3"), "Retry must restore normal race music.");
            Set(laps, "goalLap", 1);
            Call(music, "Update");
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
            Debug.Log("PlayerAudioValidation PASSED: independent final laps, spectator, retry, single lap, menu tracks, stereo viewpoint and no default-output leakage.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
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
