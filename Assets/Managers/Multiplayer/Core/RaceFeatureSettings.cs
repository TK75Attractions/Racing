using System;

public enum RacePreset { Standard, Experience }

[Flags]
public enum RaceFeature
{
    None = 0,
    ExperienceItems = 1,
    DriftCoaching = 2,
    HitProtection = 4,
    Slipstream = 8,
    SteeringAssist = 16,
    LandingFeedback = 32,
    LandingBoost = 64,
    TubeChargeLines = 128,
    PlayerTwoAutodrive = 256,
    CpuRacing = 512,
    ResultHighlights = 1024,
    HighSpeedCornering = 2048,
    FastAiPace = 4096,
    FinishRun = 8192,
    FinishApproachGrace = 16384,
    ClearSpeedPresentation = 32768,
    CourseIntro = 65536,
    CampusAtmosphere = 131072,
    RivalProximity = 262144,
    ItemHitFeedback = 524288,
    SteeringStability = 1048576,
    DailyRanking = 2097152,
    AdaptiveAiPace = 4194304,
    MenuScenery = 8388608,
    BroadcastScreen = 16777216,
    RaceReplays = 33554432,
    All = ExperienceItems | DriftCoaching | HitProtection | Slipstream | SteeringAssist | LandingFeedback | LandingBoost | TubeChargeLines | PlayerTwoAutodrive | CpuRacing | ResultHighlights | HighSpeedCornering | FastAiPace | FinishRun | FinishApproachGrace | ClearSpeedPresentation | CourseIntro | CampusAtmosphere | RivalProximity | ItemHitFeedback | SteeringStability | DailyRanking | AdaptiveAiPace | MenuScenery | BroadcastScreen | RaceReplays
}

/// <summary>Session-local feature switches. Structural rules are frozen for a race.</summary>
public sealed class RaceFeatureSettings
{
    public const RaceFeature Configurable = RaceFeature.ExperienceItems | RaceFeature.SteeringAssist | RaceFeature.FastAiPace |
        RaceFeature.PlayerTwoAutodrive | RaceFeature.CampusAtmosphere | RaceFeature.AdaptiveAiPace;
    public const RaceFeature StandardFeatures = RaceFeature.All & ~Configurable;
    private bool validationOverrides;
    public RaceFeature Enabled { get; private set; } = RaceFeature.DriftCoaching | RaceFeature.HitProtection | RaceFeature.Slipstream | RaceFeature.SteeringAssist | RaceFeature.LandingFeedback | RaceFeature.LandingBoost | RaceFeature.TubeChargeLines | RaceFeature.CpuRacing | RaceFeature.ResultHighlights | RaceFeature.HighSpeedCornering | RaceFeature.FastAiPace | RaceFeature.FinishRun | RaceFeature.FinishApproachGrace | RaceFeature.ClearSpeedPresentation | RaceFeature.CourseIntro | RaceFeature.CampusAtmosphere | RaceFeature.RivalProximity | RaceFeature.ItemHitFeedback | RaceFeature.SteeringStability | RaceFeature.DailyRanking | RaceFeature.AdaptiveAiPace | RaceFeature.MenuScenery | RaceFeature.BroadcastScreen | RaceFeature.RaceReplays;
    public RacePreset SelectedPreset { get; private set; }
    public RacePreset ActivePreset { get; private set; }
    public bool RulesLocked { get; private set; }
    public bool HasQueuedPreset { get; private set; }
    public int Revision { get; private set; }
    public bool IsEnabled(RaceFeature feature) => feature != RaceFeature.None && (Enabled & feature) == feature;
    public void SetFeature(RaceFeature feature, bool enabled)
    {
        feature &= validationOverrides ? RaceFeature.All : Configurable;
        RaceFeature next = (enabled ? Enabled | feature : Enabled & ~feature) | (validationOverrides ? RaceFeature.None : StandardFeatures);
        if (next == Enabled) return;
        Enabled = next; Revision++;
    }
    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    public void SetValidationFeature(RaceFeature feature, bool enabled)
    {
        validationOverrides = true; SetFeature(feature, enabled);
    }
    public void SelectPreset(RacePreset preset)
    {
        if (preset != RacePreset.Standard && preset != RacePreset.Experience) return;
        SelectedPreset = preset;
        if (!RulesLocked) ApplyPreset();
        else HasQueuedPreset = true;
    }
    public void BeginPreparation()
    {
        RulesLocked = false;
        ApplyPreset();
        RulesLocked = true;
    }
    public void UnlockAtTitle()
    {
        RulesLocked = false;
        ApplyPreset();
    }
    public void ResetForNextGroup()
    {
        if (!HasQueuedPreset) SelectedPreset = RacePreset.Standard;
        UnlockAtTitle();
    }
    private void ApplyPreset()
    {
        ActivePreset = SelectedPreset;
        HasQueuedPreset = false;
        SetFeature(RaceFeature.ExperienceItems, ActivePreset == RacePreset.Experience);
    }
}

public sealed class RaceRuleSet
{
    public readonly RacePreset Preset;
    public readonly int GoalLaps;
    public readonly float MaximumSeconds, GraceSeconds, MaximumGraceSeconds;
    public RaceRuleSet(RacePreset preset, int standardLaps, float standardMaximum, float grace, float maximumGrace)
    {
        Preset = preset;
        GoalLaps = preset == RacePreset.Experience ? 1 : Math.Max(1, standardLaps);
        MaximumSeconds = preset == RacePreset.Experience ? 180f : Math.Max(1f, standardMaximum);
        GraceSeconds = Math.Max(0f, grace);
        MaximumGraceSeconds = preset == RacePreset.Experience ? 90f : Math.Max(GraceSeconds, maximumGrace);
    }
}
