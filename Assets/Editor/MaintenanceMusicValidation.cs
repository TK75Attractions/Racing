using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

/// <summary>Exercise the real maintenance toggle and BGM routing without opening output devices.</summary>
public static class MaintenanceMusicValidation
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    [MenuItem("Racing/Validation/Maintenance Music %#m")]
    public static void Run()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play mode before validation.");
        var root = new GameObject("Maintenance music QA"); root.SetActive(false);
        float oldScale = Time.timeScale;
        bool oldPause = AudioListener.pause, oldMuted = RaceAudioSettings.MaintenanceMuted;
        try
        {
            var manager = root.AddComponent<Gmanager>();
            root.AddComponent<RaceAudioOutput>();
            var music = root.AddComponent<RaceBackgroundMusic>();
            typeof(RaceBackgroundMusic).GetMethod("Awake", Private).Invoke(music, null);
            var tick = typeof(RaceBackgroundMusic).GetMethod("Tick", Private);
            tick.Invoke(music, new object[] { 1f });
            var normal = (AudioSource[])Get(music, "sources");
            var original = new[] { normal[0].clip, normal[1].clip };
            Require(original[0] != null && original[1] != null, "Original BGM must be loaded.");

            var maintenance = root.AddComponent<RaceMaintenanceController>();
            typeof(Gmanager).GetField("<Maintenance>k__BackingField", Private).SetValue(manager, maintenance);
            maintenance.Configure(manager, Array.Empty<PlayerDisplayRig>(), null);
            maintenance.SetActive(true, persist: false);
            Require(Time.timeScale == 0 && AudioListener.pause && RaceAudioSettings.MaintenanceMuted,
                "Maintenance must keep physics and ordinary game audio paused.");
            var service = (AudioSource[])Get(music, "maintenanceSources");
            for (int player = 0; player < 2; player++)
            {
                Require(service[player] != null && service[player].clip.name == "TachibanaMoroe" && service[player].loop,
                    "Both maintenance outputs must loop 橘諸兄.");
                Require(service[player].ignoreListenerPause && !normal[player].ignoreListenerPause,
                    "Only service music may bypass the listener pause.");
                Require((bool)Get(service[player].GetComponent<PlayerAudioCapture>(), "maintenanceMusic") &&
                    !(bool)Get(normal[player].GetComponent<PlayerAudioCapture>(), "maintenanceMusic"),
                    "Only service capture may pass the independent output maintenance gate.");
            }
            tick.Invoke(music, new object[] { 100f });
            Require(normal[0].clip == original[0] && normal[1].clip == original[1],
                "Maintenance must preserve both normal tracks, including across unscaled updates.");
            maintenance.SetActive(false, persist: false);
            Require(Time.timeScale == oldScale && AudioListener.pause == oldPause && !RaceAudioSettings.MaintenanceMuted,
                "Leaving maintenance must restore the original pause state.");
            tick.Invoke(music, new object[] { 0f });
            Require(normal[0].clip == original[0] && normal[1].clip == original[1], "Normal tracks must resume unchanged.");
            maintenance.SetActive(true, persist: false);
            Require(ReferenceEquals(service, Get(music, "maintenanceSources")), "Repeated entry reuses the two music sources.");
            maintenance.SetActive(false, persist: false);
            Directory.CreateDirectory("Artifacts/MaintenanceMusic");
            File.WriteAllText("Artifacts/MaintenanceMusic/verification.txt",
                "PASS: real maintenance toggle; both outputs loop TachibanaMoroe; only service audio bypasses pause/mute; normal tracks preserved; pause state restored; repeated entry reuses sources. No physical audio output opened.\n");
            Debug.Log("MAINTENANCE_MUSIC_VALIDATION_PASS");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
            Time.timeScale = oldScale; AudioListener.pause = oldPause; RaceAudioSettings.MaintenanceMuted = oldMuted;
        }
    }

    private static object Get(object target, string field) => target.GetType().GetField(field, Private).GetValue(target);
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
