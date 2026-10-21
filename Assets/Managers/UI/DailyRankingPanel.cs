using TMPro;
using UnityEngine;

/// <summary>Top five attempts plus the local driver's rank, even outside the top five.</summary>
public sealed class DailyRankingPanel
{
    private readonly RectTransform root;
    private readonly TMP_Text context, ownRank, empty;
    private readonly TMP_Text[] ranks = new TMP_Text[5], drivers = new TMP_Text[5], times = new TMP_Text[5];
    private readonly RectTransform[] rows = new RectTransform[5];
    public string OwnRankText => ownRank.text;
    public DailyRankingPanel(Transform parent)
    {
        root = NeonUI.Panel(parent, "DailyRanking", new Vector2(.65f,.215f), new Vector2(.974f,.50f));
        NeonUI.Text(root, "Title", "本日のランキング", new Vector2(.045f,.81f), new Vector2(.96f,.98f), 31f, tint: NeonUI.Cyan);
        context = NeonUI.Text(root, "Context", "", new Vector2(.045f,.70f), new Vector2(.96f,.82f), 17f, tint: new Color(.70f,.83f,.95f));
        empty = NeonUI.Text(root, "Empty", "本日の完走記録はまだありません", new Vector2(.07f,.28f), new Vector2(.93f,.65f), 23f, TextAlignmentOptions.Center);
        for (int i = 0; i < 5; i++)
        {
            float top = .69f - i * .096f;
            rows[i] = RacingUITheme.Rect(root, "DailyRow" + i, new Vector2(.045f,top-.091f), new Vector2(.96f,top));
            ranks[i] = NeonUI.Text(rows[i], "Rank", "", new Vector2(0,0), new Vector2(.13f,1), 24f, TextAlignmentOptions.Center);
            drivers[i] = NeonUI.Text(rows[i], "Driver", "", new Vector2(.16f,0), new Vector2(.65f,1), 21f);
            times[i] = NeonUI.Text(rows[i], "Time", "", new Vector2(.66f,0), new Vector2(1,1), 23f, TextAlignmentOptions.Right);
        }
        RacingUITheme.Rule(root, "Divider", new Vector2(.045f,.18f), new Vector2(.96f,.184f), new Color(.23f,.37f,.70f,.7f));
        ownRank = NeonUI.Text(root, "OwnRank", "", new Vector2(.045f,.025f), new Vector2(.96f,.17f), 22f, TextAlignmentOptions.Center, tint: NeonUI.Pink);
    }
    public void Refresh(DailyRankingView view, RaceResultRecord local, int player)
    {
        // This debug switch hides the presentation only; completed records remain saved.
        bool visible = Gmanager.Control?.Features == null || RaceFeatureController.Enabled(RaceFeature.DailyRanking);
        root.gameObject.SetActive(visible);
        if (!visible) return;
        string date = view?.day?.Replace('-', '/') ?? System.DateTime.Now.ToString("yyyy/MM/dd");
        string direction = view?.reversed == true ? "逆走" : "正走";
        string preset = view?.experience == true ? "体験" : "標準";
        context.text = view == null ? date + " / この端末の記録" :
            $"{date} / {view.goalLaps}周 / {direction} / {preset} / {view.rows.Count}件";
        int count = view?.available == true ? view.rows.Count : 0;
        empty.gameObject.SetActive(count == 0);
        empty.text = view?.available == false ? "履歴を読み込めません" : "本日の完走記録はまだありません";
        for (int i = 0; i < rows.Length; i++)
        {
            rows[i].gameObject.SetActive(i < count);
            if (i >= count) continue;
            var row = view.rows[i]; var entry = row.entry;
            bool current = entry.raceId == view.raceId;
            Color color = current ? NeonUI.Cyan : Color.white;
            ranks[i].text = row.rank.ToString();
            drivers[i].text = $"第{entry.playNumber}レース P{entry.playerNumber}" + (current ? " 今回" : "");
            times[i].text = FormatTime(entry.milliseconds);
            ranks[i].color = drivers[i].color = times[i].color = color;
        }
        var own = view?.FindPlayer(player);
        ownRank.text = view?.available == false ? "記録ファイルを確認してください" : view?.saved == false ? "保存失敗 / 再起動すると消えます" :
            view?.recording == false || local?.dailyRankingExcluded == true || local?.achievements?.usedAutodrive == true ? "テスト走行 / 記録対象外" :
            local?.didFinish == false ? "未完走 / ランキング対象外" :
            own != null ? $"今回のP{player} : 本日 {own.rank}位 / {view.rows.Count}件" :
            !string.IsNullOrEmpty(local?.dailyRankingDay) && local.dailyRankingDay != view?.day ? "今回のタイムは前日の記録です" : "プレイヤーの完走タイムを記録";
    }
    private static string FormatTime(int milliseconds) => $"{milliseconds/60000}:{milliseconds/1000%60:00}.{milliseconds%1000:000}";
}
