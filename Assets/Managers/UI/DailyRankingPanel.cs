using TMPro;
using UnityEngine;

/// <summary>The local daily placement and the best time for today's matching course rules.</summary>
public sealed class DailyRankingPanel
{
    private readonly RectTransform root;
    private readonly TMP_Text ownRank, bestTime;
    private readonly DailyRankCelebration celebration;
    public string OwnRankText => ownRank.text;
    public string BestTimeText => bestTime.text;
    public DailyRankingPanel(Transform parent)
    {
        var p = ArcadeUI.For(parent);
        root = ArcadeUI.Panel(parent, "DailyRanking", new Vector2(.625f,.15f), new Vector2(.97f,.32f), p.Ink);
        var effect = RacingUITheme.Rect(root, "Celebration", Vector2.zero, Vector2.one);
        celebration = effect.gameObject.AddComponent<DailyRankCelebration>();
        celebration.raycastTarget = false;
        ownRank = ArcadeUI.Label(root, "OwnRank", "", new Vector2(.04f,.36f), new Vector2(.96f,.98f), 64, p.Paper, false, TextAlignmentOptions.Center);
        bestTime = ArcadeUI.Label(root, "BestTime", "", new Vector2(.045f,.04f), new Vector2(.955f,.32f), 27, p.Secondary, false, TextAlignmentOptions.Center);
        celebration.Configure(ownRank, p.Primary);
    }
    public void Refresh(DailyRankingView view, RaceResultRecord local, int player)
    {
        bool visible = Gmanager.Control?.Features == null || RaceFeatureController.Enabled(RaceFeature.DailyRanking);
        root.gameObject.SetActive(visible);
        if (!visible) return;
        var own = view?.FindPlayer(player);
        string message = view?.available == false ? "本日の順位を確認できません" : view?.saved == false ? "記録を保存できません" :
            view?.recording == false || local?.dailyRankingExcluded == true || local?.achievements?.usedAutodrive == true ? "テスト走行 / 記録対象外" :
            local?.didFinish == false ? "未完走 / 記録なし" :
            own != null ? null :
            !string.IsNullOrEmpty(local?.dailyRankingDay) && local.dailyRankingDay != view?.day ? "前日の記録" : "本日の記録なし";
        ownRank.text = message != null ? "<size=46%>" + message + "</size>" :
            $"<size=38%>本日 </size>{own.rank}位<size=38%> / {view.rows.Count}人中</size>";
        int ms = view?.BestMilliseconds ?? 0;
        bestTime.text = view?.available == false ? "本日のベスト —" : ms > 0 ?
            $"本日のベスト  {ms / 60000}:{ms / 1000 % 60:00}.{ms % 1000:000}" : "本日のベスト —";
        celebration.SetCelebrating(message == null && view.IsTopTwentyPercent(own));
    }
}
