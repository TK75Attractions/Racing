using System;
using System.Collections.Generic;

/// <summary>One catalog for the F8 panel, dashboard display and accepted feature IDs.</summary>
public static class RaceDebugFeatureCatalog
{
    public sealed class Entry
    {
        public RaceFeature Feature { get; }
        public string PanelLabel { get; }
        public string DashboardLabel { get; }
        public Entry(RaceFeature feature, string panelLabel, string dashboardLabel)
        { Feature = feature; PanelLabel = panelLabel; DashboardLabel = dashboardLabel; }
    }

    public static IReadOnlyList<Entry> Entries { get; } = Array.AsReadOnly(new[] {
        new Entry(RaceFeature.ExperienceItems, "Gentle items (shield / rocket only)", "やさしいアイテム（シールド・ロケットのみ）"),
        new Entry(RaceFeature.SteeringAssist, "Course edge steering assist", "コース端のステアリング補助"),
        new Entry(RaceFeature.FastAiPace, "Fast AI pace (2 laps / 120s target)", "速いCPUペース（2周・120秒目標）"),
        new Entry(RaceFeature.PlayerOneAutodrive, "P1 test autodrive (manual on OFF)", "P1のテスト自動運転（OFFで手動）"),
        new Entry(RaceFeature.PlayerTwoAutodrive, "P2 test autodrive (manual on OFF)", "P2のテスト自動運転（OFFで手動）"),
        new Entry(RaceFeature.CampusAtmosphere, "Campus lighting / landscaping / town", "校内の照明・植栽・街並み"),
        new Entry(RaceFeature.AdaptiveAiPace, "Adaptive CPU / autodrive pace around manual players", "手動プレイヤーに合わせたCPU・自動運転のペース調整"),
    });

    public static bool TryFind(string id, out RaceFeature feature)
    {
        foreach (Entry entry in Entries)
            if (entry.Feature.ToString() == id) { feature = entry.Feature; return true; }
        feature = RaceFeature.None;
        return false;
    }
}
