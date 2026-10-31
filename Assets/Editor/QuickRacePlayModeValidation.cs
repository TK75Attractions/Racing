#if UNITY_EDITOR
using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Exercises both title gates, one-lap completion, queued mode changes and normal practice.</summary>
[InitializeOnLoad]
public static class QuickRacePlayModeValidation
{
    private const string Key = "Racing.QuickRaceValidation";
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static int stage;
    private static double started;
    static QuickRacePlayModeValidation() { EditorApplication.update += Tick; }
    [MenuItem("Racing/Validation/Quick Race Flow")]
    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        var input = UnityEngine.Object.FindFirstObjectByType<InputManager>();
        input.isDebugMode = true; input.isP1SerialP2KeyboardDebugMode = false;
        stage = 0; started = 0;
        SessionState.SetBool(Key, true); EditorApplication.isPlaying = true;
    }
    private static void Tick()
    {
        if (!SessionState.GetBool(Key, false) || !EditorApplication.isPlaying || Gmanager.Control == null) return;
        try
        {
            if (started == 0) started = EditorApplication.timeSinceStartup;
            if (EditorApplication.timeSinceStartup - started > 120) throw new Exception("Quick race validation timed out at " + stage);
            var race = Gmanager.Control;
            foreach (var publisher in UnityEngine.Object.FindObjectsByType<RaceRemoteFirebasePublisher>(FindObjectsSortMode.None)) publisher.enabled = false;
            foreach (var transition in UnityEngine.Object.FindObjectsByType<ScreenTransitionController>(FindObjectsSortMode.None)) if (transition.IsTransitioning) return;
            if (stage == 0 && Time.frameCount > 12)
            {
                race.Features.SetValidationFeature(RaceFeature.FinishRun, false);
                race.Features.SelectPreset(RacePreset.Quick);
                race.ConfirmTitleStart(0);
                Require(race.state == Gmanager.State.Title && race.GoalLap == 1, "P1 readiness keeps P2's title and selects one lap.");
                Require(race.GetComponent<DrivingTutorialController>()?.HasAnyStarted != true, "Quick mode never starts practice.");
                race.GetPlayerDashboardProgress(0, out _, out _, out string ready, out _, out _);
                Require(ready == "Ready", "Dashboard reports the first player as ready while waiting for P2.");
                race.Features.SelectPreset(RacePreset.Standard);
                Require(race.Features.Settings.ActivePreset == RacePreset.Quick && race.Features.Settings.HasQueuedPreset, "The first player's readiness freezes the mode.");
                race.ConfirmTitleStart(1); stage++;
            }
            else if (stage == 1 && race.state == Gmanager.State.Game)
            {
                Require(race.GoalLap == 1 && !race.IsCourseIntroPlaying, "Both ready players start one lap without course introduction.");
                Call(race, "AdvanceDebugLap", 0, false); stage++;
            }
            else if (stage == 2 && race.LatestSessionResult?.GetPlayerResult(0)?.didFinish == true)
            {
                var result = race.LatestSessionResult.GetPlayerResult(0);
                Require(result.goalLap == 1 && result.completedLaps == 1, "One normal lap completion finishes Quick mode.");
                Require(race.IsPlayerStillRacing(1), "P1's finish does not finish P2.");
                Call(race, "AdvanceDebugLap", 1, false); stage++;
            }
            else if (stage == 3 && race.state == Gmanager.State.Result)
            {
                Require(race.LatestSessionResult.GetPlayerResult(1).didFinish, "P2 also finishes after one lap.");
                race.ResetGame(); stage++;
            }
            else if (stage == 4 && race.state == Gmanager.State.Title)
            {
                Require(race.Features.Settings.ActivePreset == RacePreset.Standard && race.GoalLap == 2, "The queued normal mode applies to the next group.");
                race.ConfirmTitleStart(0); stage++;
            }
            else if (stage == 5 && race.GetComponent<DrivingTutorialController>()?.HasStarted(0) == true)
            {
                Require(race.state == Gmanager.State.Tutorial && race.GoalLap == 2, "Normal mode still starts independent practice and uses two laps.");
                Debug.Log("QUICK_RACE_PLAYMODE_PASS: independent title readiness; no tutorial or introduction; one-lap finishes for both players; frozen current rules; next-group normal practice restored.");
                Finish(0);
            }
        }
        catch (Exception error) { Debug.LogException(error); Finish(1); }
    }
    private static void Call(object owner, string method, params object[] args) => owner.GetType().GetMethod(method, Private).Invoke(owner, args);
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void Finish(int code) { SessionState.SetBool(Key, false); EditorApplication.isPlaying = false; if (Application.isBatchMode) EditorApplication.Exit(code); }
}
#endif
