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
    All = ExperienceItems | DriftCoaching | HitProtection | Slipstream | SteeringAssist | LandingFeedback | LandingBoost | TubeChargeLines | PlayerTwoAutodrive | CpuRacing | ResultHighlights
}

/// <summary>Session-local feature switches. Structural rules are frozen for a race.</summary>
public sealed class RaceFeatureSettings
{
    public RaceFeature Enabled { get; private set; } = RaceFeature.DriftCoaching | RaceFeature.HitProtection | RaceFeature.Slipstream | RaceFeature.SteeringAssist | RaceFeature.LandingFeedback | RaceFeature.LandingBoost | RaceFeature.TubeChargeLines | RaceFeature.CpuRacing | RaceFeature.ResultHighlights;
    public RacePreset SelectedPreset { get; private set; }
    public RacePreset ActivePreset { get; private set; }
    public bool RulesLocked { get; private set; }
    public bool HasQueuedPreset { get; private set; }
    public int Revision { get; private set; }
    public bool IsEnabled(RaceFeature feature) => feature != RaceFeature.None && (Enabled & feature) == feature;
    public void SetFeature(RaceFeature feature, bool enabled)
    {
        feature &= RaceFeature.All;
        RaceFeature next = enabled ? Enabled | feature : Enabled & ~feature;
        if (next == Enabled) return;
        Enabled = next; Revision++;
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
