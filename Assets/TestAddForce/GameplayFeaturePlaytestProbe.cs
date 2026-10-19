#if UNITY_EDITOR
using System;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

/// <summary>Opt-in assertions during the existing input-driven course playtest. Never moves cars.</summary>
public sealed class GameplayFeaturePlaytestProbe : MonoBehaviour
{
    [Serializable] private sealed class Report
    {
        public int completedPhases, driftReleases, slipstreamBoosts;
        public bool passed;
        public string error = "";
    }
    private readonly Report report = new Report();
    private int phase;
    private Gmanager race;
    private RaceFeature original;
    private int originalLaps;
    private Keyboard keyboard;
    private InputSettings.BackgroundBehavior background;
    private InputSettings.EditorInputBehaviorInPlayMode editorInput;
    private void Start()
    {
        background = InputSystem.settings.backgroundBehavior;
        editorInput = InputSystem.settings.editorInputBehaviorInPlayMode;
        InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        keyboard = InputSystem.AddDevice<Keyboard>();
        InputSystem.QueueStateEvent(keyboard, new KeyboardState());
    }
    private void Update()
    {
        if (report.error.Length > 0) return;
        race = Gmanager.Control;
        if (race == null || race.state != Gmanager.State.Game || race.Participants.Count < 4) return;
        try
        {
            RaceFeatureController features = race.Features;
            if (phase == 0 && race.time > 5f)
            {
                original = features.Settings.Enabled; originalLaps = race.GoalLap;
                features.PanelVisible = false; keyboard.MakeCurrent();
                _ = keyboard.f8Key.wasPressedThisFrame;
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.F8));
                features.SetFeature(RaceFeature.All, false);
                foreach (RaceParticipant entry in race.Participants)
                {
                    Require(!entry.Car.GetComponent<DebugMover>().IsSlipstreamBoosting, "OFF must stop the added boost during real driving.");
                    Require(entry.Effects.HitProtectionRemaining == 0, "OFF must remove additional hit protection.");
                    DriftCoachingUI coach = entry.Car.GetComponent<DriftCoachingUI>();
                    Require(coach == null || coach.Notice.Length == 0, "OFF must clear coaching.");
                }
                CompletePhase();
            }
            else if (phase == 1 && race.time > 8f)
            {
                Require(features.PanelVisible, "F8 must open the debug panel through the real player update loop.");
                features.PanelVisible = false;
                InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                features.SelectPreset(RacePreset.Experience);
                Require(race.GoalLap == originalLaps && features.Settings.ActivePreset == RacePreset.Standard,
                    "A live preset selection must not alter current lap rules.");
                features.SetFeature(RaceFeature.ExperienceItems, true);
                foreach (RaceParticipant entry in race.Participants)
                    Require(!entry.Effects.IsConfused && !entry.Effects.IsOiled && !entry.Effects.IsSpinning &&
                        !entry.Effects.ApplyConfuse(), "Gentle mode clears and blocks harmful effects for all participants.");
                CompletePhase();
            }
            else if (phase == 2 && race.time > 12f)
            {
                features.SelectPreset(RacePreset.Standard);
                features.SetFeature(RaceFeature.All, false);
                features.SetFeature(original, true);
                Require(features.Settings.Enabled == original && race.GoalLap == originalLaps, "Restoring switches preserves the active rules.");
                CompletePhase(); report.passed = true; Save();
                Debug.Log("GAMEPLAY_FEATURE_TOGGLE_PASS: live OFF, gentle items, queued rules and live restore during input-driven four-car racing.");
            }
            report.driftReleases = report.slipstreamBoosts = 0;
            foreach (RaceParticipant entry in race.Participants)
            {
                report.driftReleases += entry.Car.GetComponent<DriftCoachingUI>()?.SuccessfulReleases ?? 0;
                report.slipstreamBoosts += entry.Car.GetComponent<SlipstreamController>()?.SuccessfulBoosts ?? 0;
            }
        }
        catch (Exception error) { report.error = error.Message; Save(); Debug.LogException(error); }
    }
    private void CompletePhase() { phase++; report.completedPhases = phase; Save(); }
    private void Save()
    {
        string output = Environment.GetEnvironmentVariable("RACING_BALANCE_OUTPUT");
        if (string.IsNullOrEmpty(output)) return;
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "feature-toggles.json"), JsonUtility.ToJson(report, true));
    }
    private void OnDestroy()
    {
        Save();
        if (keyboard != null) InputSystem.RemoveDevice(keyboard);
        InputSystem.settings.backgroundBehavior = background;
        InputSystem.settings.editorInputBehaviorInPlayMode = editorInput;
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
#endif
