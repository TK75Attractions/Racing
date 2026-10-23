using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Highlight film, local finish and a compact classification; daily records are disclosed on demand.</summary>
public class ResultUIManager
{
    private RaceResultRecord currentResult;
    private RaceSessionResult currentSessionResult;
    private int localPlayerNumber=1;
    private Transform card;
    private readonly TMP_Text[] ranks=new TMP_Text[4],drivers=new TMP_Text[4],times=new TMP_Text[4];
    private readonly ArcadeSurfaceGraphic[] surfaces=new ArcadeSurfaceGraphic[4];
    private TMP_Text localRank,localTime,rankingLabel,menuHint;
    private ResultMenuAnimator menuAnimator;
    private DailyRankingPanel dailyRankingPanel;
    private GameObject classification;
    private bool rankingOpen;
    public string AchievementText=>FormatHighlight(currentSessionResult?.GetPlayerResult(localPlayerNumber-1));
    public RaceResultRecord CurrentResult=>currentResult;
    public RaceSessionResult CurrentSessionResult=>currentSessionResult;
    public void Init(Transform parent,int playerNumber=1)
    {
        if(parent==null)return;localPlayerNumber=playerNumber;ArcadeUI.Bind(parent,playerNumber-1);
        var p=ArcadeUI.For(playerNumber-1);ArcadeUI.Hide(parent,"Panel");
        var presentation=RacingUITheme.Rect(parent,"ResultPresentation",Vector2.zero,Vector2.one);
        var background=parent.GetComponent<Image>();if(background!=null)background.color=Color.clear;
        card=RacingUITheme.Rect(presentation,"ResultCard",Vector2.zero,Vector2.one);
        if(card.GetComponent<CanvasGroup>()==null)card.gameObject.AddComponent<CanvasGroup>();
        var header=ArcadeUI.Panel(card,"Header",new Vector2(.03f,.865f),new Vector2(.97f,.96f),p.Ink);
        ArcadeUI.Label(header,"Title","レース結果",new Vector2(.025f,.1f),new Vector2(.6f,.90f),44,p.Paper);
        ArcadeUI.Label(header,"Course","つくこまサーキット / 2周",new Vector2(.60f,.1f),new Vector2(.98f,.9f),21,p.Soft,false,TextAlignmentOptions.Right);
        RacingUITheme.Rule(header,"Accent",Vector2.zero,new Vector2(.005f,1),p.Primary);
        var finish=ArcadeUI.Panel(card,"Finish",new Vector2(.625f,.63f),new Vector2(.97f,.83f),p.Paper);
        ArcadeUI.Label(finish,"Player","P"+playerNumber,new Vector2(.055f,.73f),new Vector2(.18f,.94f),24,p.Ink,true);
        localRank=ArcadeUI.Label(finish,"Rank","—",new Vector2(.055f,.10f),new Vector2(.30f,.80f),110,p.Ink,true);localRank.fontStyle=FontStyles.Italic;
        ArcadeUI.Label(finish,"TimeLabel","タイム",new Vector2(.50f,.65f),new Vector2(.94f,.87f),21,p.Ink);
        localTime=ArcadeUI.Label(finish,"Time","—",new Vector2(.38f,.18f),new Vector2(.94f,.65f),66,p.Ink,true,TextAlignmentOptions.Right);
        RacingUITheme.Rule(finish,"Underline",Vector2.zero,new Vector2(1,.03f),p.Primary);
        var table=ArcadeUI.Panel(card,"Classification",new Vector2(.625f,.27f),new Vector2(.97f,.61f),p.Ink);classification=table.gameObject;
        ArcadeUI.Label(table,"Header","順位     ドライバー",new Vector2(.06f,.87f),new Vector2(.65f,.98f),20,p.Paper);
        ArcadeUI.Label(table,"Time","タイム",new Vector2(.70f,.87f),new Vector2(.96f,.98f),20,p.Paper,false,TextAlignmentOptions.Right);
        for(int i=0;i<4;i++)
        {
            float top=.85f-i*.20f;
            var row=ArcadeUI.Panel(table,"ResultRow"+(i+1),new Vector2(.025f,top-.19f),new Vector2(.975f,top),p.Ink);surfaces[i]=row.GetComponent<ArcadeSurfaceGraphic>();
            ranks[i]=ArcadeUI.Label(row,"Rank","—",new Vector2(.01f,.06f),new Vector2(.13f,.94f),39,p.Paper,true,TextAlignmentOptions.Center);
            drivers[i]=ArcadeUI.Label(row,"Driver","—",new Vector2(.17f,.06f),new Vector2(.53f,.94f),26,p.Paper,true);
            times[i]=ArcadeUI.Label(row,"Time","—",new Vector2(.53f,.06f),new Vector2(.98f,.94f),39,p.Paper,true,TextAlignmentOptions.Right);
        }
        dailyRankingPanel=new DailyRankingPanel(card);dailyRankingPanel.SetExpanded(false);
        var disclosure=ArcadeUI.Panel(card,"RankingDisclosure",new Vector2(.625f,.17f),new Vector2(.97f,.25f),p.Ink);
        var hit=disclosure.GetComponent<ArcadeSurfaceGraphic>();hit.raycastTarget=true;
        var button=disclosure.gameObject.AddComponent<Button>();button.targetGraphic=hit;button.onClick.AddListener(ToggleRanking);
        rankingLabel=ArcadeUI.Label(disclosure,"Label","本日のランキング  +",new Vector2(.045f,.1f),new Vector2(.96f,.9f),22,p.Soft);
        BuildMenu();
        (card.GetComponent<ResultReplayPanel>()??card.gameObject.AddComponent<ResultReplayPanel>()).Build(localPlayerNumber);
    }
    private void BuildMenu()
    {
        var menu=RacingUITheme.Rect(card,"ResultMenu",new Vector2(.28f,.045f),new Vector2(.72f,.13f));
        var buttons=new RectTransform[2];
        for(int i=0;i<2;i++){int option=i;var button=NeonUI.Button(menu,"Option"+(i+1),i==0?"もう一度":"タイトルへ","",i==0?RacingIconGraphic.Icon.Retry:RacingIconGraphic.Icon.Home,new Vector2(i*.53f,0),new Vector2(i*.53f+.47f,1),i==0,()=>Gmanager.Control?.SelectResultOption(localPlayerNumber-1,option));buttons[i]=button.GetComponent<RectTransform>();button.PlayConfirmationSound=false;button.ConfigureHold(1,i==0);}
        menuAnimator=menu.GetComponent<ResultMenuAnimator>()??menu.gameObject.AddComponent<ResultMenuAnimator>();menuAnimator.Configure(buttons);
        menuHint=ArcadeUI.Label(card,"MenuHint","",new Vector2(.28f,.005f),new Vector2(.72f,.04f),18,ArcadeUI.For(card).Paper,false,TextAlignmentOptions.Center);
    }
    private void ToggleRanking(){rankingOpen=!rankingOpen;dailyRankingPanel.SetExpanded(rankingOpen);classification.SetActive(!rankingOpen);rankingLabel.text=rankingOpen?"本日のランキング  -":"本日のランキング  +";}
    public void ShowResults()=>ShowResults((RaceResultRecord)null);
    public void ShowResults(RaceResultRecord record){var session=new RaceSessionResult();if(record!=null)session.SetPlayerResult(Mathf.Clamp(record.playerNumber-1,0,1),record);ShowResults(session);}
    public void ShowResults(RaceSessionResult session)
    {
        currentSessionResult=session;currentResult=session?.GetResultAtPosition(1);var p=ArcadeUI.For(localPlayerNumber-1);
        for(int i=0;i<4;i++)
        {
            var record=session?.GetResultAtPosition(i+1);bool own=record!=null&&!record.isCpu&&record.playerNumber==localPlayerNumber;
            surfaces[i].color=own?p.Primary:p.Ink;Color ink=own?p.Ink:p.Paper;
            ranks[i].text=record?.didFinish==true?(i+1).ToString():"—";drivers[i].text=record==null?"—":record.isCpu?record.DriverLabel:"P"+record.playerNumber;
            times[i].text=record==null?"—":record.didFinish?FormatTime(record.totalRaceTime):"DNF";
            ranks[i].color=drivers[i].color=times[i].color=ink;
        }
        var local=session?.GetPlayerResult(localPlayerNumber-1);
        localRank.text=local==null?"—":local.didFinish?local.finishPosition+"<size=32%>位</size>":"DNF";
        localTime.text=local==null?"—":local.didFinish?FormatTime(local.totalRaceTime):"未完走";
        rankingOpen=false;classification.SetActive(true);dailyRankingPanel.SetExpanded(false);rankingLabel.text="本日のランキング  +";
        RefreshDailyRanking();SetMenuState(0,0);SetPedalConfirmationReady(false);
    }
    public void RefreshHighlight() { }
    public void RefreshDailyRanking()=>dailyRankingPanel?.Refresh(currentSessionResult?.dailyRanking,currentSessionResult?.GetPlayerResult(localPlayerNumber-1),localPlayerNumber);
    public void SetMenuState(int index,float amount)=>menuAnimator?.SetState(index,amount);
    public void SetPedalConfirmationReady(bool ready){menuHint.text=ready?"":"まずペダルから足を離してください";}
    public void PlayConfirm(int index)=>menuAnimator?.PlayConfirm(index);
    public void HideResults(){currentResult=null;currentSessionResult=null;SetMenuState(0,0);}
    private static string FormatTime(float seconds){int ms=DailyRankingBook.Milliseconds(seconds);return $"{ms/60000}:{ms/1000%60:00}.{ms%1000:000}";}
    public static string FormatHighlight(RaceResultRecord local)
    {
        if (local == null) return "";
        RaceAchievementSnapshot facts = local.achievements;
        string text;
        switch (RaceAchievementSelection.Select(facts))
        {
            case RaceAchievementKind.AutodriveTest: return "自動運転テストの記録";
            case RaceAchievementKind.TubeLine: text=$"ライン達成 {facts.tubeLines}回！"; break;
            case RaceAchievementKind.Landing: text=$"着地成功 {facts.landings}回！"; break;
            case RaceAchievementKind.Drift: text=$"ドリフト加速 {facts.driftReleases}回！"; break;
            case RaceAchievementKind.Slipstream: text=$"追走加速 {facts.slipstreamBoosts}回！"; break;
            default: text=local.completedLaps>0 ? $"{local.completedLaps}周を走行！" : $"走行進捗 {Mathf.Max(0f,facts?.verifiedProgress ?? 0f):F0}m"; break;
        }
        return local.didFinish ? text : "未完走 / " + text;
    }

}
