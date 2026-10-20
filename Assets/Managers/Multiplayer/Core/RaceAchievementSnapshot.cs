using System;

[Serializable]
public sealed class RaceAchievementSnapshot
{
    public int driftReleases, landings, tubeLines, slipstreamBoosts;
    public float verifiedProgress;
    public bool usedAutodrive;
}

public enum RaceAchievementKind { Progress, Drift, Landing, TubeLine, Slipstream, AutodriveTest }
public static class RaceAchievementSelection
{
    public static RaceAchievementKind Select(RaceAchievementSnapshot facts)
    {
        if (facts == null) return RaceAchievementKind.Progress;
        if (facts.usedAutodrive) return RaceAchievementKind.AutodriveTest;
        if (facts.tubeLines > 0) return RaceAchievementKind.TubeLine;
        if (facts.landings > 0) return RaceAchievementKind.Landing;
        if (facts.driftReleases > 0) return RaceAchievementKind.Drift;
        if (facts.slipstreamBoosts > 0) return RaceAchievementKind.Slipstream;
        return RaceAchievementKind.Progress;
    }
}
