using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class FastRacePaceValidation
{
    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        RaceCourse course = UnityEngine.Object.FindFirstObjectByType<RaceCourse>(); course.RebuildCache();
        Gmanager previous = Gmanager.Control;
        var root = new GameObject("Fast pace validation"); root.SetActive(false);
        try
        {
            var manager = root.AddComponent<Gmanager>(); Gmanager.Control = manager;
            var features = root.AddComponent<RaceFeatureController>(); features.Configure(manager);
            typeof(Gmanager).GetField("<Features>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(manager, features);
            Require(RaceSpeedPlanner.Enabled, "Fast pace must start enabled.");
            float maximum = 0f, minimum = 100f, idealSeconds = 0f, straight = -1f;
            for (float p = 0; p < course.TotalLength; p += 5f)
            {
                float speed = RaceSpeedPlanner.Limit(course, p, 1, RaceSpeedPlanner.Cruise);
                Require(speed > 0 && speed <= RaceSpeedPlanner.Cruise, "Road planning must emit finite bounded speeds.");
                minimum = Mathf.Min(minimum, speed); maximum = Mathf.Max(maximum, speed);
                idealSeconds += 10f / speed; // Two laps, excluding acceleration/traffic; full physical races cover those.
                if (speed >= RaceSpeedPlanner.Cruise - .01f) straight = p;
            }
            Require(straight >= 0 && idealSeconds < 120f, "The road plan must permit the 120-second target before traffic/acceleration.");
            course.TryGetSampleAtProgress(straight, out var road);
            var car = new GameObject("Default observation driver", typeof(Rigidbody)); car.transform.SetParent(root.transform);
            Rigidbody body = car.GetComponent<Rigidbody>(); body.position = road.position; body.rotation = Quaternion.LookRotation(road.forward, road.up);
            body.linearVelocity = road.forward * 35f;
            var bot = new RoadObservationBot();
            Require(bot.Decide(course, body, 1f, 4, 1f).pedal > .9f, "The default bot must accelerate above the old 80-100km/h range.");
            features.SetValidationFeature(RaceFeature.FastAiPace, false);
            Require(!RaceSpeedPlanner.Enabled && features.Settings.IsEnabled(RaceFeature.HighSpeedCornering), "OFF must be live and independent of cornering control.");
            bot.Clear();
            Require(bot.Decide(course, body, 1f, 4, 2f, cruise: 24f).pedal < 0, "Explicit legacy pace must remain available for comparison.");
            features.SetValidationFeature(RaceFeature.FastAiPace, true);
            Require(RaceSpeedPlanner.Enabled, "Live ON must restore fast pace.");
            Require(RaceSpeedPlanner.CpuCruise(31, -100, 0, 2) < 31f, "Fast CPUs must retain a response to an outdistanced rival.");
            features.SetValidationFeature(RaceFeature.CpuRacing, false);
            Require(RaceSpeedPlanner.CpuCruise(31, -100, 0, 2) == RaceSpeedPlanner.Cruise && RaceSpeedPlanner.Enabled,
                "CPU contest OFF must restore full cruise without disabling fast road planning.");
            features.SetValidationFeature(RaceFeature.CpuRacing, true);
            Debug.Log($"FAST_RACE_PACE_VALIDATION_PASS: min={minimum:F2}m/s max={maximum:F2}m/s idealTwoLaps={idealSeconds:F2}s, default driver, live independent OFF/ON.");
        }
        finally { UnityEngine.Object.DestroyImmediate(root); Gmanager.Control = previous; }
        RaceFeatureValidation.Run();
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
