using System;
using UnityEditor;
using UnityEngine;

public static class RaceFeatureValidation
{
    [MenuItem("Racing/Validation/Gameplay Features")]
    public static void Run()
    {
        var settings = new RaceFeatureSettings();
        settings.SelectPreset(RacePreset.Experience);
        settings.BeginPreparation();
        settings.SelectPreset(RacePreset.Standard);
        Require(settings.ActivePreset == RacePreset.Experience, "Live presets must not change lap rules.");
        settings.SetFeature(RaceFeature.All, false);
        Require(settings.Enabled == RaceFeature.None, "All switches must be independently disabled during a race.");
        settings.BeginPreparation();
        Require(settings.ActivePreset == RacePreset.Standard, "Queued rules must apply at the next start.");
        var rules = new RaceRuleSet(RacePreset.Experience, 2, 360, 40, 180);
        Require(rules.GoalLaps == 1 && rules.MaximumSeconds == 180 && rules.MaximumGraceSeconds == 90,
            "Experience limits must match the UI and race session.");
        Debug.Log("RACE_FEATURE_VALIDATION_PASS: presets, live switches and frozen race rules.");
    }
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
