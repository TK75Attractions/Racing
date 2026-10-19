using System;
using System.Reflection;
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
        ValidateCoachingAndItems();
        SlipstreamValidation.Run();
        RacingHUDFontBuilder.Build();
        foreach (bool bold in new[] { false, true })
            Require(RacingUIFontCatalog.GetHUD(bold).HasCharacters("ドリフト加速！少し逆に切って加速曲がってためる保護中着地成功！ライン成功！チャージ増加横の光るラインでチャージ"),
                "All new Japanese notices must be baked into the HUD fonts.");
        TubeChargeLineValidation.Run();
        ResultHighlightValidation.Run();
        Debug.Log("RACE_FEATURE_VALIDATION_PASS: presets, live switches, coaching, anti-chain protection, steering assist, landing rewards, tube lines and fonts.");
    }
    private static void ValidateCoachingAndItems()
    {
        Gmanager previous = Gmanager.Control;
        var root = new GameObject("Gameplay feature validation");
        root.SetActive(false);
        try
        {
            Gmanager race = root.AddComponent<Gmanager>();
            Gmanager.Control = race;
            RaceFeatureController features = root.AddComponent<RaceFeatureController>(); features.Configure(race);
            typeof(Gmanager).GetField("<Features>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(race, features);
            var car = new GameObject("Feature test car"); car.transform.SetParent(root.transform);
            car.AddComponent<Rigidbody>();
            DebugMover mover = car.AddComponent<DebugMover>();
            CarItemEffects effects = car.AddComponent<CarItemEffects>();
            typeof(CarItemEffects).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(effects, null);
            root.SetActive(true);
            features.SetFeature(RaceFeature.HitProtection, true);
            Require(effects.ApplyOil(), "The first affliction must apply.");
            Require(!effects.ApplyConfuse(), "A second effect cannot stack or prolong the first.");
            features.SetFeature(RaceFeature.HitProtection, false);
            Require(effects.ApplyConfuse(), "Disabling protection restores legacy hit behavior immediately.");
            features.SetFeature(RaceFeature.ExperienceItems, true);
            effects.ApplyFeatureSwitches();
            Require(!effects.IsOiled && !effects.IsConfused && !effects.ApplyConfuse(), "Gentle mode must clear and reject harmful effects.");
            effects.GrantRecoveryProtection(2f);
            features.SetFeature(RaceFeature.HitProtection, false); effects.ApplyFeatureSwitches();
            Require(effects.IsRecoveryProtected, "Debugging hit protection must not remove recovery safety.");
            effects.ClearAll();
            features.SetFeature(RaceFeature.ExperienceItems, false);
            race.state = Gmanager.State.Game;
            DriftCoachingUI coach = car.AddComponent<DriftCoachingUI>(); coach.Configure(race, mover, 0);
            typeof(DebugMover).GetMethod("StartDriftBoost", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(mover, new object[] { 3f });
            Require(coach.SuccessfulReleases == 1 && coach.Notice.Length > 0, "An actual boost release must provide feedback.");
            features.SetFeature(RaceFeature.DriftCoaching, false);
            Require(coach.Notice.Length == 0, "Turning coaching off hides existing guidance immediately.");
        }
        finally { UnityEngine.Object.DestroyImmediate(root); Gmanager.Control = previous; }
    }
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
