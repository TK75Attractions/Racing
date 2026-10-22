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
        new Entry(RaceFeature.ExperienceItems, "Gentle items (shield / rocket only)", "体験向けアイテム（シールド・ロケットのみ）"),
        new Entry(RaceFeature.DriftCoaching, "Drift coaching", "ドリフト操作のコーチング"),
        new Entry(RaceFeature.HitProtection, "Anti-chain hit protection", "連続被弾の保護"),
        new Entry(RaceFeature.Slipstream, "Slipstream boost", "スリップストリーム加速"),
        new Entry(RaceFeature.SteeringAssist, "Course edge steering assist", "コース端のステアリング補助"),
        new Entry(RaceFeature.HighSpeedCornering, "High-speed cornering + turn braking", "高速コーナリング・旋回ブレーキ"),
        new Entry(RaceFeature.FastAiPace, "Fast AI pace (2 laps / 120s target)", "速いCPUペース（2周・120秒目標）"),
        new Entry(RaceFeature.LandingFeedback, "Landing success feedback", "着地成功の演出"),
        new Entry(RaceFeature.LandingBoost, "Safe landing reward boost", "安全な着地の加速ボーナス"),
        new Entry(RaceFeature.TubeChargeLines, "Tube side charge lines", "チューブ側面のチャージライン"),
        new Entry(RaceFeature.PlayerTwoAutodrive, "P2 test autodrive (manual on OFF)", "P2のテスト自動運転（OFFで手動）"),
        new Entry(RaceFeature.CpuRacing, "CPU overtaking / counterattack", "CPUの追い越し・反撃"),
        new Entry(RaceFeature.ResultHighlights, "Result achievement highlight", "結果画面の達成ハイライト"),
        new Entry(RaceFeature.FinishRun, "Short winning run after finish", "完走後の短いウイニングラン"),
        new Entry(RaceFeature.FinishApproachGrace, "One short grace near the finish line", "ゴール直前の追加猶予（1回）"),
        new Entry(RaceFeature.ClearSpeedPresentation, "Clear high-speed view (reduced blur / distortion)", "高速時の視認性改善（ぼかし・歪みを軽減）"),
        new Entry(RaceFeature.CourseIntro, "Pre-race drone course preview", "レース前のドローンによるコース紹介"),
        new Entry(RaceFeature.CampusAtmosphere, "Campus lighting / landscaping / town", "校内の照明・植栽・街並み"),
        new Entry(RaceFeature.RivalProximity, "Rival proximity: 3D arrow / rear red edges", "ライバル接近表示（3D矢印・後方の赤い縁）"),
        new Entry(RaceFeature.ItemHitFeedback, "Named outgoing item hit confirmations", "アイテム命中時に相手名を表示"),
        new Entry(RaceFeature.SteeringStability, "Stable steering: prevent sudden slip / spin", "急な横滑り・スピンを抑える操舵安定化"),
        new Entry(RaceFeature.DailyRanking, "Today ranking panel (completed times still saved)", "本日のランキング表示（OFFでも完走記録を保存）"),
        new Entry(RaceFeature.AdaptiveAiPace, "Adaptive CPU / P2 pace around manual players", "手動プレイヤーに合わせたCPU・P2のペース調整"),
    });

    public static bool TryFind(string id, out RaceFeature feature)
    {
        foreach (Entry entry in Entries)
            if (entry.Feature.ToString() == id) { feature = entry.Feature; return true; }
        feature = RaceFeature.None;
        return false;
    }
}
