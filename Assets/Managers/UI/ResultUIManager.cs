using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Neon classification, local-driver medal and four functional result actions.</summary>
public class ResultUIManager
{
    private RaceResultRecord currentResult;
    private RaceSessionResult currentSessionResult;
    private Transform card;
    private int localPlayerNumber=1;
    private readonly TMP_Text[] ranks=new TMP_Text[2],drivers=new TMP_Text[2],cars=new TMP_Text[2],times=new TMP_Text[2],gaps=new TMP_Text[2];
    private readonly RacingPanelGraphic[] rowSurfaces=new RacingPanelGraphic[2];
    private readonly RacingIconGraphic[] carIcons=new RacingIconGraphic[2];
    private TMP_Text winnerLabel,medalNumber,ordinal,bestLap,lastLap,completedLaps;
    private RawImage medalImage;
    private ResultMenuAnimator menuAnimator;
    public RaceResultRecord CurrentResult=>currentResult;
    public RaceSessionResult CurrentSessionResult=>currentSessionResult;

    public void Init(Transform parent,int playerNumber=1)
    {
        if(parent==null){Debug.LogWarning("ResultUIManager requires a Result root.");return;}
        localPlayerNumber=playerNumber;
        Transform legacy=parent.Find("Panel");if(legacy!=null)legacy.gameObject.SetActive(false);
        RectTransform presentation=RacingUITheme.Rect(parent,"ResultPresentation",Vector2.zero,Vector2.one);
        NeonUI.Background(presentation,"ResultBackground","UI/Neon/ResultBackground");
        card=RacingUITheme.Rect(presentation,"ResultCard",Vector2.zero,Vector2.one);
        if(card.GetComponent<CanvasGroup>()==null)card.gameObject.AddComponent<CanvasGroup>();
        RectTransform heading=NeonUI.Panel(card,"Header",new Vector2(.03f,.82f),new Vector2(.465f,.955f));
        RacingPanelGraphic headingFace=heading.Find("ModernSurface").GetComponent<RacingPanelGraphic>();headingFace.Configure(RacingPanelGraphic.SurfaceStyle.Panel,NeonUI.Pink);
        TMP_Text resultTitle=NeonUI.Text(heading,"Title","RACE RESULT",new Vector2(.035f,.29f),new Vector2(.94f,.98f),85f,italic:true);
        NeonUI.GlowText(resultTitle,NeonUI.Cyan);
        NeonUI.Text(heading,"Subtitle","レース結果",new Vector2(.04f,.065f),new Vector2(.85f,.33f),27f,italic:true);
        BuildCourseCard(card);
        NeonUI.Panel(card,"Table",new Vector2(.03f,.17f),new Vector2(.625f,.80f));
        RectTransform tableHead=RacingUITheme.Rect(card,"TableHeader",new Vector2(.04f,.727f),new Vector2(.614f,.785f));
        NeonUI.Text(tableHead,"Rank","POS / 順位",new Vector2(.02f,0),new Vector2(.15f,1),22f,TextAlignmentOptions.Center,true,new Color(.6f,.83f,1));
        NeonUI.Text(tableHead,"Driver","ドライバー",new Vector2(.18f,0),new Vector2(.52f,1),24f);
        NeonUI.Text(tableHead,"Time","タイム",new Vector2(.60f,0),new Vector2(.79f,1),24f,TextAlignmentOptions.Center);
        NeonUI.Text(tableHead,"Gap","ギャップ",new Vector2(.80f,0),new Vector2(.98f,1),24f,TextAlignmentOptions.Center);
        for(int i=0;i<2;i++)
        {
            float top=.72f-i*.164f;
            RectTransform row=NeonUI.Panel(card,"ResultRow"+(i+1),new Vector2(.04f,top-.154f),new Vector2(.614f,top));
            rowSurfaces[i]=row.Find("ModernSurface").GetComponent<RacingPanelGraphic>();
            ranks[i]=NeonUI.Text(row,"Rank",(i+1).ToString(),new Vector2(.01f,.08f),new Vector2(.145f,.92f),77f,TextAlignmentOptions.Center,true);
            drivers[i]=NeonUI.Text(row,"Player","プレイヤー "+(i+1),new Vector2(.18f,.43f),new Vector2(.53f,.89f),35f);
            cars[i]=NeonUI.Text(row,"CarName","SPORT CAR",new Vector2(.18f,.12f),new Vector2(.53f,.43f),21f,tint:new Color(.73f,.82f,.93f));
            carIcons[i]=NeonUI.Icon(row,"Car",RacingIconGraphic.Icon.Car,new Vector2(.47f,.25f),new Vector2(.60f,.76f));
            times[i]=NeonUI.Text(row,"Time","--'--.---",new Vector2(.60f,.21f),new Vector2(.80f,.79f),42f,TextAlignmentOptions.Center,true);
            gaps[i]=NeonUI.Text(row,"Gap","—",new Vector2(.81f,.21f),new Vector2(.98f,.79f),31f,TextAlignmentOptions.Center,true);
            RacingUITheme.Rule(row,"Divider",new Vector2(.015f,.01f),new Vector2(.985f,.016f),new Color(.23f,.37f,.70f,.5f));
        }
        winnerLabel=NeonUI.Text(card,"Winner","RACE COMPLETE",new Vector2(.052f,.32f),new Vector2(.60f,.38f),27f,italic:true,tint:NeonUI.Cyan);
        RectTransform summary=RacingUITheme.Rect(card,"RaceSummary",new Vector2(.053f,.20f),new Vector2(.602f,.315f));
        NeonUI.Text(summary,"BestLabel","ベストラップ / BEST LAP",new Vector2(.01f,.62f),new Vector2(.48f,.97f),18f,tint:new Color(.6f,.83f,1));
        bestLap=NeonUI.Text(summary,"Best","--'--.---",new Vector2(.01f,.05f),new Vector2(.46f,.62f),36f,italic:true);
        NeonUI.Text(summary,"LastLabel","最終ラップ / LAST LAP",new Vector2(.50f,.62f),new Vector2(.98f,.97f),18f,tint:new Color(.6f,.83f,1));
        lastLap=NeonUI.Text(summary,"Last","--'--.---",new Vector2(.50f,.05f),new Vector2(.94f,.62f),36f,italic:true);
        RectTransform medal=RacingUITheme.Rect(card,"Medal",new Vector2(.665f,.315f),new Vector2(.96f,.79f));
        RectTransform artwork=RacingUITheme.Rect(medal,"Artwork",Vector2.zero,Vector2.one);
        medalImage=artwork.GetComponent<RawImage>() ?? artwork.gameObject.AddComponent<RawImage>();medalImage.raycastTarget=false;
        AspectRatioFitter fit=artwork.GetComponent<AspectRatioFitter>() ?? artwork.gameObject.AddComponent<AspectRatioFitter>();fit.aspectMode=AspectRatioFitter.AspectMode.FitInParent;fit.aspectRatio=1f;
        medalNumber=NeonUI.Text(medal,"Number","1",new Vector2(.20f,.34f),new Vector2(.80f,.78f),234f,TextAlignmentOptions.Center,true,new Color(1,.94f,.73f));
        RectTransform ribbon=NeonUI.Panel(card,"PlacementRibbon",new Vector2(.665f,.215f),new Vector2(.97f,.37f),true);
        ordinal=NeonUI.Text(ribbon,"Ordinal","1ST",new Vector2(.04f,.23f),new Vector2(.96f,.98f),113f,TextAlignmentOptions.Center,true);
        NeonUI.GlowText(ordinal,NeonUI.Pink);
        completedLaps=NeonUI.Text(ribbon,"Completed","1 位 / FINISH",new Vector2(.04f,.03f),new Vector2(.96f,.30f),22f,TextAlignmentOptions.Center);
        BuildMenu(card);
    }
    private void BuildCourseCard(Transform parent)
    {
        RectTransform course=NeonUI.Panel(parent,"CourseCard",new Vector2(.527f,.82f),new Vector2(.974f,.955f));
        RectTransform photo=RacingUITheme.Rect(course,"Photo",new Vector2(.02f,.08f),new Vector2(.365f,.92f));
        RawImage image=photo.GetComponent<RawImage>() ?? photo.gameObject.AddComponent<RawImage>();image.texture=Resources.Load<Texture2D>("UI/Neon/ResultBackground");image.raycastTarget=false;image.uvRect=new Rect(.26f,.3f,.62f,.49f);
        NeonUI.Text(course,"Name","つくこまサーキット",new Vector2(.40f,.45f),new Vector2(.78f,.92f),31f,italic:true);
        NeonUI.Text(course,"EnglishName","Tsukukoma Circuit",new Vector2(.40f,.15f),new Vector2(.78f,.46f),22f,italic:true);
        RectTransform outline=RacingUITheme.Rect(course,"Outline",new Vector2(.80f,.07f),new Vector2(.97f,.93f));
        NeonCourseOutlineGraphic map=outline.GetComponent<NeonCourseOutlineGraphic>() ?? outline.gameObject.AddComponent<NeonCourseOutlineGraphic>();
        map.Configure(Gmanager.Control!=null?Gmanager.Control.course:Object.FindFirstObjectByType<RaceCourse>());
    }
    private void BuildMenu(Transform parent)
    {
        RectTransform menu=RacingUITheme.Rect(parent,"ResultMenu",new Vector2(.03f,.048f),new Vector2(.97f,.14f));
        string[] labels={"もう一度レースする","車の選択に戻る","コース選択に戻る","メインメニューへ"};
        string[] english={"RACE AGAIN","SELECT CAR","SELECT COURSE","MAIN MENU"};
        RacingIconGraphic.Icon[] icons={RacingIconGraphic.Icon.Retry,RacingIconGraphic.Icon.Car,RacingIconGraphic.Icon.Course,RacingIconGraphic.Icon.Home};
        RectTransform[] buttons=new RectTransform[4];
        for(int i=0;i<4;i++)
        {
            int option=i;
            RacingMenuButton button=NeonUI.Button(menu,"Option"+(i+1),labels[i],english[i],icons[i],
                new Vector2(i*.252f,0),new Vector2(i*.252f+.240f,1),i==0,()=>Gmanager.Control?.SelectResultOption(localPlayerNumber-1,option));
            buttons[i]=button.GetComponent<RectTransform>();
            button.transform.Find("Label").GetComponent<TMP_Text>().fontSizeMax=28f;
        }
        menuAnimator=menu.GetComponent<ResultMenuAnimator>() ?? menu.gameObject.AddComponent<ResultMenuAnimator>();menuAnimator.Configure(buttons);
        foreach(RectTransform button in buttons)
        {
            button.Find("ModernSurface").gameObject.SetActive(false);
            Image hit=button.GetComponent<Image>();hit.enabled=true;hit.color=Color.clear;hit.raycastTarget=true;
            button.GetComponent<RacingMenuButton>().Configure(button.Find("ButtonSurface").GetComponent<RacingPanelGraphic>());
        }
        NeonUI.Text(parent,"MenuHint","ハンドルで選択 / ペダルで決定",new Vector2(.28f,.008f),new Vector2(.72f,.042f),17f,TextAlignmentOptions.Center);
    }
    public void ShowResults()=>ShowResults((RaceResultRecord)null);
    public void ShowResults(RaceResultRecord record)
    {
        var session=new RaceSessionResult();
        if(record!=null)session.SetPlayerResult(Mathf.Clamp(record.playerNumber-1,0,1),record);
        ShowResults(session);
    }
    public void ShowResults(RaceSessionResult session)
    {
        currentSessionResult=session;currentResult=session?.GetResultAtPosition(1);
        float winnerTime=currentResult!=null&&currentResult.didFinish?currentResult.totalRaceTime:0f;
        for(int i=0;i<2;i++)ApplyRow(i,session?.GetResultAtPosition(i+1),winnerTime);
        winnerLabel.text=currentResult!=null?$"PLAYER {currentResult.playerNumber}  WINNER / 優勝":"RACE COMPLETE / レース終了";
        RaceResultRecord local=session?.GetPlayerResult(localPlayerNumber-1);
        int position=local!=null?Mathf.Clamp(local.finishPosition,1,2):localPlayerNumber;
        bool finished=local==null||local.didFinish;
        medalImage.texture=Resources.Load<Texture2D>(position==1?"UI/Neon/MedalGold":"UI/Neon/MedalSilver");
        medalImage.gameObject.SetActive(finished);medalNumber.gameObject.SetActive(finished);
        medalNumber.text=position.ToString();medalNumber.color=Color.white;
        medalNumber.enableVertexGradient=true;
        Color highlight=position==1?new Color(1,.97f,.78f):new Color(.96f,.99f,1f);
        Color shadow=position==1?new Color(.66f,.29f,.10f):new Color(.35f,.45f,.58f);
        medalNumber.colorGradient=new VertexGradient(highlight,highlight,shadow,shadow);
        Material metal=medalNumber.fontMaterial;
        metal.SetColor("_OutlineColor",position==1?new Color(.32f,.14f,.05f):new Color(.12f,.17f,.25f));
        metal.SetFloat("_OutlineWidth",.12f);
        metal.EnableKeyword("UNDERLAY_ON");metal.SetColor("_UnderlayColor",new Color(.04f,.02f,.01f,.95f));
        metal.SetFloat("_UnderlayOffsetX",.75f);metal.SetFloat("_UnderlayOffsetY",-.75f);
        medalNumber.UpdateMeshPadding();
        ordinal.text=finished?(position==1?"1ST":"2ND"):"DNF";
        completedLaps.text=finished?$"{position} 位 / FINISH": "未完走 / DID NOT FINISH";
        bestLap.text=local!=null&&local.bestLapTime>0?FormatTime(local.bestLapTime):"--'--.---";
        lastLap.text=local!=null&&local.finalLapTime>0?FormatTime(local.finalLapTime):"--'--.---";
        SetMenuState(0,0);
    }
    private void ApplyRow(int index,RaceResultRecord result,float winnerTime)
    {
        bool local=result!=null&&result.playerNumber==localPlayerNumber;
        rowSurfaces[index].Configure(local?RacingPanelGraphic.SurfaceStyle.Primary:RacingPanelGraphic.SurfaceStyle.Panel,local?NeonUI.Pink:NeonUI.Cyan);
        ranks[index].text=result!=null?(index+1).ToString():"—";
        drivers[index].text=result!=null?"プレイヤー "+result.playerNumber:"—";
        cars[index].text=result!=null?(string.IsNullOrEmpty(result.carName)?"SPORT CAR":result.carName):"";
        carIcons[index].gameObject.SetActive(result!=null);
        if(result!=null)carIcons[index].color=Color.Lerp(PlayerCarPaint.GetPlayerColor(result.playerNumber-1),Color.white,.3f);
        times[index].text=result==null?"—":result.didFinish?FormatTime(result.totalRaceTime):"DNF";
        gaps[index].text=result==null||index==0||!result.didFinish?"—":"+"+Mathf.Max(0,result.totalRaceTime-winnerTime).ToString("0.000");
        times[index].color=local?new Color(1,.62f,.78f):Color.white;
        gaps[index].color=local?new Color(1,.62f,.78f):new Color(.74f,.80f,.95f);
    }
    public void SetMenuState(int selectedIndex,float amount)=>menuAnimator?.SetState(selectedIndex,amount);
    public void PlayConfirm(int selectedIndex)=>menuAnimator?.PlayConfirm(selectedIndex);
    public void HideResults(){currentResult=null;currentSessionResult=null;SetMenuState(0,0);}
    private static string FormatTime(float time)=>$"{Mathf.FloorToInt(time/60f)}'{Mathf.FloorToInt(time%60f):00}.{Mathf.FloorToInt(time*1000f)%1000:000}";
}
