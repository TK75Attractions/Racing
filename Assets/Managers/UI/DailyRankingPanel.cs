using TMPro;
using UnityEngine;

/// <summary>Only the local driver's daily placement; the race classification stays visible.</summary>
public sealed class DailyRankingPanel
{
    private readonly RectTransform root;
    private readonly TMP_Text ownRank;
    public string OwnRankText=>ownRank.text;
    public DailyRankingPanel(Transform parent)
    {
        var p=ArcadeUI.For(parent);
        root=ArcadeUI.Panel(parent,"DailyRanking",new Vector2(.625f,.17f),new Vector2(.97f,.25f),p.Ink);
        ownRank=ArcadeUI.Label(root,"OwnRank","",new Vector2(.045f,.06f),new Vector2(.96f,.94f),35,p.Secondary,false,TextAlignmentOptions.Center);
    }
    public void Refresh(DailyRankingView view,RaceResultRecord local,int player)
    {
        bool visible=Gmanager.Control?.Features==null||RaceFeatureController.Enabled(RaceFeature.DailyRanking);
        root.gameObject.SetActive(visible);
        if(!visible)return;
        var own=view?.FindPlayer(player);
        ownRank.text=view?.available==false?"本日の順位を確認できません":view?.saved==false?"記録を保存できません":
            view?.recording==false||local?.dailyRankingExcluded==true||local?.achievements?.usedAutodrive==true?"テスト走行 / 記録対象外":
            local?.didFinish==false?"未完走 / 記録なし":
            own!=null?$"本日{own.rank}位！":
            !string.IsNullOrEmpty(local?.dailyRankingDay)&&local.dailyRankingDay!=view?.day?"前日の記録":"本日の記録なし";
    }
}
