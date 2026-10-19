using NUnit.Framework;

public sealed class RaceAchievementSelectionTests
{
    [Test] public void OnlyObservedEventsCanProduceAnAchievement()
    {
        Assert.AreEqual(RaceAchievementKind.Progress, RaceAchievementSelection.Select(null));
        Assert.AreEqual(RaceAchievementKind.Progress, RaceAchievementSelection.Select(new RaceAchievementSnapshot()));
        Assert.AreEqual(RaceAchievementKind.Landing, RaceAchievementSelection.Select(new RaceAchievementSnapshot { landings = 1 }));
        Assert.AreEqual(RaceAchievementKind.TubeLine, RaceAchievementSelection.Select(new RaceAchievementSnapshot { tubeLines = 1, landings = 1 }));
    }
    [Test] public void AutodrivenResultsAreLabelledAsTestsInsteadOfHumanAchievements()
    {
        Assert.AreEqual(RaceAchievementKind.AutodriveTest,
            RaceAchievementSelection.Select(new RaceAchievementSnapshot { usedAutodrive = true, landings = 5 }));
    }
}
