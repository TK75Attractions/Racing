using NUnit.Framework;

public sealed class RaceFeatureSettingsTests
{
    [Test] public void TestAutodriveStartsOffWhileGameplayImprovementsStartOn()
    {
        var settings = new RaceFeatureSettings();
        Assert.IsFalse(settings.IsEnabled(RaceFeature.PlayerOneAutodrive));
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
        Assert.IsTrue(settings.IsEnabled(RaceFeature.DailyRanking));
        Assert.IsTrue(settings.IsEnabled(RaceFeature.AdaptiveAiPace));
        Assert.IsTrue(settings.IsEnabled(RaceFeature.MenuScenery));
    }
    [Test] public void FeatureChangesAreImmediateAndIndependentWhileRulesAreLocked()
    {
        var settings = new RaceFeatureSettings();
        settings.BeginPreparation();
        settings.SetFeature(RaceFeature.SteeringAssist, false);
        Assert.IsFalse(settings.IsEnabled(RaceFeature.SteeringAssist));
        Assert.IsTrue(settings.IsEnabled(RaceFeature.HitProtection));
        settings.SetFeature(RaceFeature.All, false);
        Assert.AreEqual(RaceFeatureSettings.StandardFeatures, settings.Enabled);
        settings.SetFeature(RaceFeature.DriftCoaching, true);
        Assert.AreEqual(RaceFeatureSettings.StandardFeatures, settings.Enabled);
    }
    [Test] public void EitherAutodriveCanBeDisabledDuringABothAutoRace()
    {
        var settings = new RaceFeatureSettings();
        settings.SetFeature(RaceFeature.PlayerOneAutodrive | RaceFeature.PlayerTwoAutodrive, true);
        settings.BeginPreparation();
        settings.SetFeature(RaceFeature.PlayerOneAutodrive, false);
        Assert.IsFalse(settings.IsEnabled(RaceFeature.PlayerOneAutodrive));
        Assert.IsTrue(settings.IsEnabled(RaceFeature.PlayerTwoAutodrive));
        settings.SetFeature(RaceFeature.PlayerOneAutodrive, true);
        settings.SetFeature(RaceFeature.PlayerTwoAutodrive, false);
        Assert.IsTrue(settings.IsEnabled(RaceFeature.PlayerOneAutodrive));
        Assert.IsFalse(settings.IsEnabled(RaceFeature.PlayerTwoAutodrive));
        settings.SetFeature(RaceFeature.All, false);
        Assert.IsFalse(settings.IsEnabled(RaceFeature.PlayerOneAutodrive));
        Assert.IsFalse(settings.IsEnabled(RaceFeature.PlayerTwoAutodrive));
    }
    [Test] public void ReturningToTitleRestoresBothManualPlayersForTheNextVisitors()
    {
        var settings = new RaceFeatureSettings();
        settings.SetFeature(RaceFeature.PlayerOneAutodrive | RaceFeature.PlayerTwoAutodrive, true);
        settings.SetFeature(RaceFeature.ExperienceItems, true);
        settings.BeginPreparation();
        settings.ResetForNextGroup();
        Assert.IsFalse(settings.IsEnabled(RaceFeature.PlayerOneAutodrive));
        Assert.IsFalse(settings.IsEnabled(RaceFeature.PlayerTwoAutodrive));
        Assert.IsTrue(settings.IsEnabled(RaceFeature.ExperienceItems));
        Assert.IsFalse(settings.RulesLocked);
    }
    [Test] public void RemovedPresetCannotChangeRulesOrEnableGentleItems()
    {
        var settings = new RaceFeatureSettings();
        settings.BeginPreparation();
        settings.SelectPreset((RacePreset)999);
        Assert.AreEqual(RacePreset.Standard, settings.ActivePreset);
        Assert.AreEqual(RacePreset.Standard, settings.SelectedPreset);
        Assert.IsFalse(settings.HasQueuedPreset);
        Assert.IsFalse(settings.IsEnabled(RaceFeature.ExperienceItems));
        settings.ResetForNextGroup();
        Assert.AreEqual(RacePreset.Standard, settings.ActivePreset);
        Assert.IsFalse(settings.RulesLocked);
    }
    [Test] public void NormalRulesCapTheRaceAtThreeMinutesEvenWithLegacySceneSettings()
    {
        var rules = new RaceRuleSet(RacePreset.Standard, 2, 360, 40, 180);
        Assert.AreEqual(2, rules.GoalLaps);
        Assert.AreEqual(180, rules.MaximumSeconds);
        Assert.AreEqual(180, rules.MaximumGraceSeconds);
    }
    [Test] public void IndependentGentleItemAssistanceSurvivesPreparation()
    {
        var settings = new RaceFeatureSettings();
        settings.SetFeature(RaceFeature.ExperienceItems, true);
        settings.BeginPreparation();
        Assert.IsTrue(settings.IsEnabled(RaceFeature.ExperienceItems));
        Assert.AreEqual(RacePreset.Standard, settings.ActivePreset);
    }
    [Test] public void QuickModeSkipsPracticeAndFinishesAfterOneLap()
    {
        var rules = new RaceRuleSet(RacePreset.Quick, 2, 360, 40, 180);
        Assert.AreEqual(1, rules.GoalLaps);
        Assert.IsTrue(rules.SkipTutorial);
        Assert.IsFalse(new RaceRuleSet(RacePreset.Standard, 2, 360, 40, 180).SkipTutorial);
        Assert.AreEqual(180, rules.MaximumSeconds);
    }
    [Test] public void RaceModeChangesAreQueuedUntilTheNextGroupAndStaySelected()
    {
        var settings = new RaceFeatureSettings();
        settings.BeginPreparation();
        settings.SelectPreset(RacePreset.Quick);
        Assert.AreEqual(RacePreset.Standard, settings.ActivePreset);
        Assert.AreEqual(RacePreset.Quick, settings.SelectedPreset);
        Assert.IsTrue(settings.HasQueuedPreset);
        settings.ResetForNextGroup();
        Assert.AreEqual(RacePreset.Quick, settings.ActivePreset);
        Assert.IsFalse(settings.HasQueuedPreset);
        settings.BeginPreparation();settings.ResetForNextGroup();
        Assert.AreEqual(RacePreset.Quick, settings.ActivePreset);
        settings.SelectPreset(RacePreset.Standard);
        Assert.AreEqual(RacePreset.Standard, settings.ActivePreset);
    }
    [Test] public void SelectingCurrentModeCancelsAQueuedChange()
    {
        var settings = new RaceFeatureSettings();settings.BeginPreparation();
        settings.SelectPreset(RacePreset.Quick);Assert.IsTrue(settings.HasQueuedPreset);
        settings.SelectPreset(RacePreset.Standard);Assert.IsFalse(settings.HasQueuedPreset);
        settings.ResetForNextGroup();Assert.AreEqual(RacePreset.Standard, settings.ActivePreset);
    }
    [Test] public void NoTutorialKeepsTwoLapsAndTheNormalCourseIntroduction()
    {
        var rules=new RaceRuleSet(RacePreset.NoTutorial,2,180,40,180);
        Assert.IsTrue(rules.SkipTutorial);Assert.IsFalse(rules.SkipCourseIntro);Assert.AreEqual(2,rules.GoalLaps);
        var settings=new RaceFeatureSettings();settings.SelectPreset(RacePreset.NoTutorial);settings.BeginPreparation();
        Assert.AreEqual(RacePreset.NoTutorial,settings.ActivePreset);
        Assert.IsFalse(settings.IsEnabled(RaceFeature.HighSpeedDrive));
        settings.SetFeature(RaceFeature.HighSpeedDrive,true);
        Assert.AreEqual(RacePreset.NoTutorial,settings.ActivePreset);
        Assert.IsTrue(settings.IsEnabled(RaceFeature.HighSpeedDrive));
        settings.SelectPreset(RacePreset.Quick);Assert.IsTrue(settings.HasQueuedPreset);
        Assert.IsTrue(settings.IsEnabled(RaceFeature.HighSpeedDrive));
    }
    private static void settingsStandardCannotBeDisabled()
    {
        var settings=new RaceFeatureSettings();settings.SetFeature(RaceFeature.All,false);
        Assert.IsTrue(settings.IsEnabled(RaceFeatureSettings.StandardFeatures));
        foreach(string id in new[]{"Slipstream","BroadcastScreen","RaceReplays","SteeringStability"})Assert.IsFalse(RaceDebugFeatureCatalog.TryFind(id,out _));
    }
    [Test] public void DebugCatalogContainsExactlyTheEightConfigurableFeaturesAndRejectsStandardIds()
    {
        var found = new System.Collections.Generic.HashSet<RaceFeature>();
        foreach (RaceDebugFeatureCatalog.Entry entry in RaceDebugFeatureCatalog.Entries)
        {
            Assert.IsTrue(found.Add(entry.Feature), "A debug switch must appear once.");
            Assert.IsNotEmpty(entry.PanelLabel); Assert.IsNotEmpty(entry.DashboardLabel);
            Assert.IsTrue(RaceDebugFeatureCatalog.TryFind(entry.Feature.ToString(), out RaceFeature parsed));
            Assert.AreEqual(entry.Feature, parsed);
        }
        foreach (RaceFeature feature in System.Enum.GetValues(typeof(RaceFeature)))
            if (feature != RaceFeature.None && feature != RaceFeature.All)
                Assert.AreEqual((RaceFeatureSettings.Configurable & feature) != 0, found.Contains(feature));
        Assert.AreEqual(8, found.Count);
        settingsStandardCannotBeDisabled();
        foreach (string id in new[] { "All", "None", "1", "DriftCoaching, HitProtection", "driftcoaching", "FutureFeature", null })
            Assert.IsFalse(RaceDebugFeatureCatalog.TryFind(id, out _));
    }
}
