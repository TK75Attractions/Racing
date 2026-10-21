using NUnit.Framework;

public sealed class RaceFeatureSettingsTests
{
    [Test] public void TestAutodriveStartsOffWhileGameplayImprovementsStartOn()
    {
        var settings = new RaceFeatureSettings();
        Assert.IsFalse(settings.IsEnabled(RaceFeature.PlayerTwoAutodrive));
        Assert.IsTrue(settings.IsEnabled(RaceFeature.CpuRacing));
        Assert.IsTrue(settings.IsEnabled(RaceFeature.ResultHighlights));
        Assert.IsTrue(settings.IsEnabled(RaceFeature.HighSpeedCornering));
        Assert.IsTrue(settings.IsEnabled(RaceFeature.FastAiPace));
        Assert.IsTrue(settings.IsEnabled(RaceFeature.FinishRun));
        Assert.IsTrue(settings.IsEnabled(RaceFeature.FinishApproachGrace));
        Assert.IsTrue(settings.IsEnabled(RaceFeature.ClearSpeedPresentation));
        Assert.IsTrue(settings.IsEnabled(RaceFeature.CourseIntro));
        Assert.IsTrue(settings.IsEnabled(RaceFeature.CampusAtmosphere));
        Assert.IsTrue(settings.IsEnabled(RaceFeature.RivalProximity));
        Assert.IsTrue(settings.IsEnabled(RaceFeature.ItemHitFeedback));
        Assert.IsTrue(settings.IsEnabled(RaceFeature.SteeringStability));
    }
    [Test] public void FeatureChangesAreImmediateAndIndependentWhileRulesAreLocked()
    {
        var settings = new RaceFeatureSettings();
        settings.BeginPreparation();
        settings.SetFeature(RaceFeature.Slipstream, false);
        Assert.IsFalse(settings.IsEnabled(RaceFeature.Slipstream));
        Assert.IsTrue(settings.IsEnabled(RaceFeature.HitProtection));
        settings.SetFeature(RaceFeature.All, false);
        Assert.AreEqual(RaceFeature.None, settings.Enabled);
        settings.SetFeature(RaceFeature.DriftCoaching, true);
        Assert.AreEqual(RaceFeature.DriftCoaching, settings.Enabled);
    }
    [Test] public void AQueuedPresetCannotChangeTheCurrentRace()
    {
        var settings = new RaceFeatureSettings();
        settings.BeginPreparation();
        settings.SelectPreset(RacePreset.Experience);
        Assert.AreEqual(RacePreset.Standard, settings.ActivePreset);
        Assert.IsFalse(settings.IsEnabled(RaceFeature.ExperienceItems));
        settings.BeginPreparation();
        Assert.AreEqual(RacePreset.Experience, settings.ActivePreset);
        Assert.IsTrue(settings.IsEnabled(RaceFeature.ExperienceItems));
        settings.ResetForNextGroup();
        Assert.AreEqual(RacePreset.Standard, settings.ActivePreset);
        Assert.IsFalse(settings.RulesLocked);
    }
    [Test] public void AnExplicitQueuedPresetSurvivesNextGroupWhileUnqueuedExperienceResets()
    {
        var settings = new RaceFeatureSettings();
        settings.BeginPreparation();
        settings.SelectPreset(RacePreset.Experience);
        settings.ResetForNextGroup();
        Assert.AreEqual(RacePreset.Experience, settings.ActivePreset);
        Assert.IsFalse(settings.HasQueuedPreset);
        settings.BeginPreparation();
        settings.ResetForNextGroup();
        Assert.AreEqual(RacePreset.Standard, settings.ActivePreset);
    }
    [Test] public void ExperienceRulesAndCustomStandardRulesDoNotMutateEachOther()
    {
        var standard = new RaceRuleSet(RacePreset.Standard, 3, 240, 25, 120);
        var experience = new RaceRuleSet(RacePreset.Experience, 3, 240, 25, 120);
        Assert.AreEqual(3, standard.GoalLaps);
        Assert.AreEqual(240, standard.MaximumSeconds);
        Assert.AreEqual(1, experience.GoalLaps);
        Assert.AreEqual(180, experience.MaximumSeconds);
        Assert.AreEqual(90, experience.MaximumGraceSeconds);
    }
}
