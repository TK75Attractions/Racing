using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class ScreenTransitionController : MonoBehaviour
{
    [Header("Title") ]
    [SerializeField] private bool useArtworkLogo;

    [Header("Transition")]
    [SerializeField, Min(0f)] private float fadeOutSeconds = 0.25f;
    [SerializeField, Min(0f)] private float fadeInSeconds = 0.35f;
    [SerializeField, Min(0f)] private float resultEnterSeconds = 0.55f;
    [SerializeField, Min(0f)] private float resultExitSeconds = 0.32f;

    private GameObject titleRoot;
    private int displayPlayerIndex;
    private GameObject onPlayRoot;
    private GameObject resultRoot;
    private CanvasGroup resultCanvasGroup;
    private RectTransform resultContent;
    private Vector2 resultContentBasePosition;
    private UIRevealSequence resultReveal;
    private TMP_Text resultRankLabel;
    private UIValuePulse finishRankPulse;
    private GoalCelebrationUI goalCelebration;
    private SpectatorOverlayUI spectatorOverlay;
    private CanvasGroup fadeCanvasGroup;
    private RectTransform fadeOverlay;
    private RacingTransitionGraphic transitionWipe;
    private Coroutine motionRoutine;
    private int motionVersion;
    private TMP_Text titlePrompt;
    private readonly PedalButtonFeedback[] titleButtonFeedback = new PedalButtonFeedback[2];
    private GameObject countdownStatusRoot;
    private GameObject finishWarningRoot;
    private TMP_Text raceStatus;
    private TMP_Text raceStatusCaption;
    private TMP_Text raceStatusEnglishCaption;
    private TMP_Text finishWarningText;
    private int lastWarningSecond = -1;
    private UIValuePulse raceStatusPulse;
    private readonly NeonRingGraphic[] countdownSignals = new NeonRingGraphic[3];
    private Material titleLogoMaterial;

    public bool IsTransitioning { get; private set; }
    public string RaceStatusText => finishWarningRoot != null && finishWarningRoot.activeSelf
        ? finishWarningText != null ? finishWarningText.text : string.Empty
        : raceStatus != null ? raceStatus.text : string.Empty;

    public void Initialize(
        Transform title,
        Transform onPlay,
        Transform result,
        string titleText,
        string promptText,
        int playerIndex)
    {
        displayPlayerIndex = Mathf.Clamp(playerIndex, 0, 1);
        ArcadeUI.Bind(transform,displayPlayerIndex);
        titleRoot = title != null ? title.gameObject : null;
        onPlayRoot = onPlay != null ? onPlay.gameObject : null;

        resultRoot = result != null ? result.gameObject : null;
        if (resultRoot != null)
        {
            resultCanvasGroup = resultRoot.GetComponent<CanvasGroup>();
            if (resultCanvasGroup == null)
            {
                resultCanvasGroup = resultRoot.AddComponent<CanvasGroup>();
            }
        }

        InitializeTitleUI(title, titleText, promptText);
        InitializeRaceStatusUI(onPlay);
        TMP_Text fontSource = result != null ? result.GetComponentInChildren<TMP_Text>(true) : null;
        if (fontSource == null && onPlay != null)
        {
            fontSource = onPlay.GetComponentInChildren<TMP_Text>(true);
        }
        goalCelebration = GoalCelebrationUI.Create(transform, fontSource != null ? fontSource.font : null);
        spectatorOverlay = SpectatorOverlayUI.Create(transform, fontSource != null ? fontSource.font : null);
        InitializeFadeOverlay();
    }

    public void ApplyStateImmediate(Gmanager.State state)
    {
        CancelTransition();
        SetScreenVisibility(state);

        if (fadeCanvasGroup != null)
        {
            fadeCanvasGroup.alpha = 0f;
            fadeCanvasGroup.blocksRaycasts = false;
            fadeCanvasGroup.interactable = false;
        }

        if (resultCanvasGroup != null)
        {
            resultCanvasGroup.alpha = state == Gmanager.State.Result ? 1f : 0f;
        }
    }

    public bool TryTransitionTo(
        Gmanager.State targetState,
        Action onScreenCovered,
        Action onCompleted = null)
    {
        if (IsTransitioning || !isActiveAndEnabled)
        {
            return false;
        }

        motionVersion++;motionRoutine=StartCoroutine(TransitionRoutine(targetState, onScreenCovered, onCompleted));
        return true;
    }

    public bool TryPlayGoal(string winnerText, string finishTime, float visibleSeconds, Action onCompleted = null)
    {
        // A close finish can arrive before the first player's own celebration ends.
        // Replace that celebration without running its stale spectating callback.
        if (!isActiveAndEnabled || goalCelebration == null || IsTransitioning && !goalCelebration.gameObject.activeSelf)
        {
            return false;
        }

        int version=++motionVersion;
        IsTransitioning = true;
        SetScreenVisibility(Gmanager.State.Goal);
        goalCelebration.Play(winnerText, finishTime, visibleSeconds, () =>
        {
            if(version!=motionVersion)return;
            IsTransitioning = false;
            onCompleted?.Invoke();
        });
        return true;
    }

    public bool TryShowResultAnimated(Action onCompleted = null)
    {
        if (IsTransitioning || !isActiveAndEnabled)
        {
            return false;
        }

        motionVersion++;motionRoutine=StartCoroutine(ShowResultRoutine(onCompleted));
        return true;
    }

    public bool TryCloseResultAndTransition(
        Gmanager.State targetState,
        Action onScreenCovered,
        Action onCompleted = null)
    {
        if (IsTransitioning || !isActiveAndEnabled)
        {
            return false;
        }

        motionVersion++;motionRoutine=StartCoroutine(CloseResultAndTransitionRoutine(targetState, onScreenCovered, onCompleted));
        return true;
    }

    public void SetTitlePrompt(string promptText)
    {
        if (promptText == "WAITING FOR THE OTHER PLAYER TO RETURN")
            promptText = "もう一人の結果操作待ち\nそのまま待っていてね";
        if (titlePrompt != null && titlePrompt.text != promptText)
        {
            titlePrompt.text = promptText;
        }
    }

    public void UpdateTitlePedals(float playerOne, float playerTwo, bool playerOneReady, bool playerTwoReady, bool armed)
    {
        float value = displayPlayerIndex == 0 ? playerOne : playerTwo;
        bool ready = displayPlayerIndex == 0 ? playerOneReady : playerTwoReady;
        UpdateTitlePedal(displayPlayerIndex, value, ready, armed);
    }

    public void RefreshLapTotal()
    {
        TMP_Text total = onPlayRoot != null ? onPlayRoot.transform.Find("CountdownLap/Total")?.GetComponent<TMP_Text>() : null;
        string value="/ "+(Gmanager.Control!=null?Gmanager.Control.GoalLap:LapManager.DefaultGoalLaps);
        if(total!=null)total.text=value;
        var hudTotal=onPlayRoot?.transform.Find("ModernHUD/Lap/Total")?.GetComponent<TMP_Text>();if(hudTotal!=null)hudTotal.text=value;
    }

    public void ShowCountdown(int seconds)
    {
        SetStatusVisibility(showCountdown: true, showWarning: false);
        SetRaceStatusValue(seconds.ToString(), "RACE START");
    }

    public void ShowGo()
    {
        SetStatusVisibility(showCountdown: true, showWarning: false);
        SetRaceStatusValue("GO!", "FULL THROTTLE");
    }

    public void ShowFinishWarning(string playerLabel, float secondsRemaining)
    {
        SetStatusVisibility(showCountdown: false, showWarning: true);
        if (finishWarningText != null)
        {
            string value = $"{playerLabel}   あと {Mathf.Max(0f, secondsRemaining):0.0}秒";
            finishWarningText.text = value;
            int displayedSecond = Mathf.CeilToInt(secondsRemaining);
            if (displayedSecond <= 10 && displayedSecond != lastWarningSecond)
            {
                finishWarningText.GetComponent<UIValuePulse>()?.Play(new Color(1f, 0.28f, 0.16f, 1f), 3f);
            }
            lastWarningSecond = displayedSecond;
        }
    }

    public void ClearRaceStatus()
    {
        lastWarningSecond = -1;
        SetStatusVisibility(showCountdown: false, showWarning: false);
    }

    public void ShowSpectator(int watchedPlayerIndex)
    {
        ClearRaceStatus();
        spectatorOverlay?.Show(watchedPlayerIndex);
    }

    public void HideSpectator()
    {
        spectatorOverlay?.Hide();
    }

    public void SetRaceStatus(string statusText)
    {
        if (raceStatus == null)
        {
            return;
        }

        string value = statusText ?? string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            ClearRaceStatus();
            return;
        }

        SetStatusVisibility(showCountdown: true, showWarning: false);
        SetRaceStatusValue(value, value == "GO!" ? "FULL THROTTLE" : "RACE START");
    }

    private IEnumerator TransitionRoutine(Gmanager.State targetState,Action onScreenCovered,Action onCompleted)
    {
        int version=motionVersion;IsTransitioning=true;SetFadeInputBlocking(true);
        try
        {
            yield return FadeTo(1f,fadeOutSeconds);
            onScreenCovered?.Invoke();
            if(version!=motionVersion)yield break;
            SetScreenVisibility(targetState);
            yield return null;
            yield return FadeTo(0f,fadeInSeconds);
        }
        finally{FinishMotion(version);}
        if(version==motionVersion)onCompleted?.Invoke();
    }

    private void FinishMotion(int version)
    {
        if(version!=motionVersion)return;
        motionRoutine=null;IsTransitioning=false;SetFadeInputBlocking(false);
        transitionWipe?.SetCoverage(0,false);if(fadeCanvasGroup!=null)fadeCanvasGroup.alpha=0;
    }

    private IEnumerator ShowResultRoutine(Action onCompleted)
    {
        int version=motionVersion;IsTransitioning=true;
        try
        {
            SetScreenVisibility(Gmanager.State.Result);RefreshResultContent();
            if(resultCanvasGroup!=null)resultCanvasGroup.alpha=0;
            resultReveal?.Play();
            double began=Time.realtimeSinceStartupAsDouble;float elapsed=0;
            while(elapsed<resultEnterSeconds)
            {
                elapsed=(float)(Time.realtimeSinceStartupAsDouble-began);
                if(resultCanvasGroup!=null)resultCanvasGroup.alpha=UIMotion.Out(resultEnterSeconds<=0?1:elapsed/resultEnterSeconds);
                yield return null;
            }
            RestoreResultContent();RestoreResultRows(true);
            if(resultCanvasGroup!=null)resultCanvasGroup.alpha=1;
        }
        finally{FinishMotion(version);}
        if(version==motionVersion)onCompleted?.Invoke();
    }

    private IEnumerator CloseResultAndTransitionRoutine(Gmanager.State targetState,Action onScreenCovered,Action onCompleted)
    {
        int version=motionVersion;IsTransitioning=true;SetFadeInputBlocking(true);RefreshResultContent();
        try
        {
            // The button already supplies a confirmation beat; do not add another hold.
            double began=Time.realtimeSinceStartupAsDouble;float elapsed=0;
            while(elapsed<resultExitSeconds)
            {
                elapsed=(float)(Time.realtimeSinceStartupAsDouble-began);float t=resultExitSeconds<=0?1:Mathf.Clamp01(elapsed/resultExitSeconds);
                resultReveal?.SampleExit(t);
                if(resultCanvasGroup!=null)resultCanvasGroup.alpha=1f-.2f*UIMotion.In(t);
                yield return null;
            }
            yield return FadeTo(1f,fadeOutSeconds);
            onScreenCovered?.Invoke();if(version!=motionVersion)yield break;
            SetScreenVisibility(targetState);yield return null;yield return FadeTo(0f,fadeInSeconds);
            RestoreResultContent();RestoreResultRows(false);
        }
        finally{FinishMotion(version);}
        if(version==motionVersion)onCompleted?.Invoke();
    }

    private IEnumerator FadeTo(float targetAlpha, float duration)
    {
        if (fadeCanvasGroup == null)
        {
            yield break;
        }

        bool revealing=targetAlpha<.5f;
        float start=transitionWipe!=null?transitionWipe.Coverage:1-targetAlpha;
        fadeCanvasGroup.alpha=1;
        float elapsed=0;double began=Time.realtimeSinceStartupAsDouble;
        while(elapsed<duration)
        {
            elapsed=(float)(Time.realtimeSinceStartupAsDouble-began);
            transitionWipe?.SetCoverage(Mathf.Lerp(start,targetAlpha,Mathf.Clamp01(elapsed/Mathf.Max(.001f,duration))),revealing);
            yield return null;
        }
        transitionWipe?.SetCoverage(targetAlpha,revealing);
        fadeCanvasGroup.alpha=targetAlpha>0?1:0;
    }

    public void CancelTransition()
    {
        motionVersion++;if(motionRoutine!=null)StopCoroutine(motionRoutine);motionRoutine=null;
        IsTransitioning=false;if(goalCelebration!=null)goalCelebration.HideImmediate();
        RestoreResultContent();RestoreResultRows(false);
        if(resultCanvasGroup!=null)resultCanvasGroup.alpha=1;
        transitionWipe?.SetCoverage(0,false);
        if(fadeCanvasGroup!=null){fadeCanvasGroup.alpha=0;fadeCanvasGroup.blocksRaycasts=false;fadeCanvasGroup.interactable=false;}
    }
    private void OnDisable()=>CancelTransition();

    private void SetScreenVisibility(Gmanager.State state)
    {
        if (titleRoot != null)
        {
            titleRoot.SetActive(state == Gmanager.State.Title);
        }

        if (state != Gmanager.State.CarSelection) GetComponent<CarSelectionUI>()?.Cancel();

        if (onPlayRoot != null)
        {
            onPlayRoot.SetActive(state == Gmanager.State.Countdown || state == Gmanager.State.Game);
        }

        if (resultRoot != null)
        {
            resultRoot.SetActive(state == Gmanager.State.Result);
        }

        if (goalCelebration != null && state != Gmanager.State.Goal)
        {
            goalCelebration.HideImmediate();
        }

        if (state != Gmanager.State.Game)
        {
            spectatorOverlay?.Hide();
        }

        if (fadeOverlay != null)
        {
            fadeOverlay.SetAsLastSibling();
        }
    }

    private void InitializeTitleUI(Transform title, string titleText, string promptText)
    {
        if (title == null)
        {
            Debug.LogWarning("Title UI root was not found.");
            return;
        }

        Image background = title.GetComponent<Image>() ?? title.gameObject.AddComponent<Image>();
        background.color = Color.clear; background.raycastTarget = false;
        ArcadeUI.Hide(title,"TitleBackground");
        MenuSceneryScrim.Create(title, "MenuSceneryScrim", false);
        ArcadeUI.Hide(title,"SceneryTitleBrand");
        ArcadeUI.Logo(title,new Vector2(.03f,.54f),new Vector2(.63f,.84f));
        title.Find("TitleBackground")?.gameObject.SetActive(false);
        TMP_Text playerBadge = NeonUI.Text(title, "PlayerBadge", $"P{displayPlayerIndex + 1} / プレイヤー{displayPlayerIndex + 1}",
            new Vector2(.69f,.90f), new Vector2(.97f,.955f), 21f, TextAlignmentOptions.Right, tint: ArcadeUI.For(displayPlayerIndex).Primary);
        foreach(Button legacy in title.GetComponentsInChildren<Button>(true)) legacy.gameObject.SetActive(false);
        Transform oldSheet=title.Find("MenuSheet"); if(oldSheet!=null)oldSheet.gameObject.SetActive(false);
        BuildTitlePedalPanel(title, displayPlayerIndex, new Vector2(.67f,.23f), new Vector2(.960f,.40f), NeonUI.Pink);
        titlePrompt = NeonUI.Text(title, "StartPrompt", promptText,
            new Vector2(.66f,.15f), new Vector2(.97f,.21f), 18f, TextAlignmentOptions.Center);
        ArcadeUI.Hide(title,"News");
        var entry=title.GetComponent<UIRevealSequence>();if(entry==null)entry=title.gameObject.AddComponent<UIRevealSequence>();
        entry.Configure(title,new[]{"CircuitLogo","PlayerBadge","StartPrompt"},new Vector2(24,0),.035f,.28f);
    }

    private void InitializeFadeOverlay()
    {
        Transform existing = transform.Find("ScreenFade");
        GameObject overlayObject;
        if (existing != null)
        {
            overlayObject = existing.gameObject;
        }
        else
        {
            overlayObject = new GameObject(
                "ScreenFade",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(CanvasGroup));
            overlayObject.layer = gameObject.layer;
            overlayObject.transform.SetParent(transform, false);
        }

        fadeOverlay = overlayObject.GetComponent<RectTransform>();
        fadeOverlay.anchorMin = Vector2.zero;
        fadeOverlay.anchorMax = Vector2.one;
        fadeOverlay.offsetMin = Vector2.zero;
        fadeOverlay.offsetMax = Vector2.zero;
        fadeOverlay.SetAsLastSibling();

        Image fadeImage = overlayObject.GetComponent<Image>();
        fadeImage.color = Color.clear;
        var wipeRect=RacingUITheme.Rect(fadeOverlay,"RacingShutters",Vector2.zero,Vector2.one);
        transitionWipe=wipeRect.GetComponent<RacingTransitionGraphic>();if(transitionWipe==null)transitionWipe=wipeRect.gameObject.AddComponent<RacingTransitionGraphic>();
        fadeImage.raycastTarget = true;

        fadeCanvasGroup = overlayObject.GetComponent<CanvasGroup>();
        fadeCanvasGroup.alpha = 0f;
        SetFadeInputBlocking(false);
    }

    private void InitializeRaceStatusUI(Transform onPlay)
    {
        if (onPlay == null)
        {
            return;
        }

        countdownStatusRoot = RacingUITheme.Rect(onPlay, "CountdownStatus", new Vector2(.40f,.32f),new Vector2(.60f,.67f)).gameObject;
        RectTransform ring = RacingUITheme.Rect(countdownStatusRoot.transform,"NeonRing",Vector2.zero,Vector2.one);
        NeonRingGraphic graphic = ring.GetComponent<NeonRingGraphic>() ?? ring.gameObject.AddComponent<NeonRingGraphic>();
        graphic.display = NeonRingGraphic.Display.Countdown;
        raceStatusCaption = NeonUI.Text(countdownStatusRoot.transform,"Caption","READY",new Vector2(.15f,.13f),new Vector2(.85f,.23f),25f,TextAlignmentOptions.Center);
        raceStatusEnglishCaption = NeonUI.Text(countdownStatusRoot.transform,"EnglishCaption","",new Vector2(.15f,.07f),new Vector2(.85f,.13f),13f,TextAlignmentOptions.Center);
        raceStatus = NeonUI.Text(countdownStatusRoot.transform,"RaceStatus","",new Vector2(.08f,.26f),new Vector2(.92f,.90f),218f,TextAlignmentOptions.Center,true);
        raceStatus.font=ArcadeUI.Digits;raceStatus.fontSharedMaterial=ArcadeUI.Digits.material;
        for(int i=0;i<countdownSignals.Length;i++)
        {
            float left=.08f+i*.30f;
            RectTransform lamp = NeonUI.Panel(countdownStatusRoot.transform,"Lamp"+i,new Vector2(left,1.06f),new Vector2(left+.24f,1.29f));
            RectTransform dot=RacingUITheme.Rect(lamp,"Signal",new Vector2(.06f,.05f),new Vector2(.94f,.95f));
            countdownSignals[i]=dot.GetComponent<NeonRingGraphic>() ?? dot.gameObject.AddComponent<NeonRingGraphic>();
            countdownSignals[i].display=NeonRingGraphic.Display.Light;
            countdownSignals[i].SetAmount(0f);
        }
        var housing=ArcadeUI.Panel(countdownStatusRoot.transform,"SignalHousing",new Vector2(.055f,1.04f),new Vector2(.945f,1.31f),ArcadeUI.For(displayPlayerIndex).Ink);housing.SetAsFirstSibling();
        for(int i=0;i<countdownSignals.Length;i++)countdownStatusRoot.transform.Find("Lamp"+i+"/ModernSurface")?.gameObject.SetActive(false);
        raceStatusPulse = raceStatus.GetComponent<UIValuePulse>() ?? raceStatus.gameObject.AddComponent<UIValuePulse>();
        RectTransform courseCard = NeonUI.Panel(onPlay,"CountdownCourse",new Vector2(.02f,.855f),new Vector2(.335f,.975f));
        RectTransform courseIcon = RacingUITheme.Rect(courseCard,"Outline",new Vector2(.02f,.09f),new Vector2(.26f,.91f));
        NeonCourseOutlineGraphic courseOutline=courseIcon.GetComponent<NeonCourseOutlineGraphic>() ?? courseIcon.gameObject.AddComponent<NeonCourseOutlineGraphic>();
        courseOutline.Configure(Gmanager.Control != null ? Gmanager.Control.course : FindFirstObjectByType<RaceCourse>());
        NeonUI.Text(courseCard,"Name","つくこまサーキット",new Vector2(.29f,.45f),new Vector2(.96f,.91f),32f,italic:true);
        NeonUI.Text(courseCard,"Caption","グランプリコース / GRAND PRIX",new Vector2(.29f,.12f),new Vector2(.96f,.46f),18f);
        RectTransform countdownLap=NeonUI.Panel(onPlay,"CountdownLap",new Vector2(.80f,.855f),new Vector2(.98f,.975f));
        NeonUI.Text(countdownLap,"Heading","LAP / 周",new Vector2(.10f,.68f),new Vector2(.88f,.93f),22f,italic:true);
        NeonUI.Text(countdownLap,"Current","1",new Vector2(.36f,.05f),new Vector2(.65f,.77f),97f,italic:true,tint:NeonUI.Pink);
        NeonUI.Text(countdownLap,"Total","/ " + (Gmanager.Control != null ? Gmanager.Control.GoalLap : LapManager.DefaultGoalLaps),new Vector2(.67f,.08f),new Vector2(.93f,.64f),44f,italic:true);

        finishWarningRoot = RacingHUDStyle.Plate(onPlay, "FinishWarningStatus",
            new Vector2(.34f, .815f), new Vector2(.66f, .95f),
            RacingHUDPlateGraphic.PlateShape.Notification, RacingHUDStyle.Amber).gameObject;
        RacingHUDStyle.Heading(finishWarningRoot.transform, "もうすぐレース終了", "TIME TO FINISH", .06f, .54f, .94f, .91f, 24f);
        finishWarningText = RacingHUDStyle.Label(finishWarningRoot.transform, "FinishWarningText", string.Empty,
            .06f, .08f, .94f, .54f, 38f, RacingHUDStyle.Text, TextAlignmentOptions.Center, bold: true);
        if (finishWarningText.GetComponent<UIValuePulse>() == null)
            finishWarningText.gameObject.AddComponent<UIValuePulse>();

        SetStatusVisibility(showCountdown: false, showWarning: false);
        var entry=onPlay.GetComponent<UIRevealSequence>();if(entry==null)entry=onPlay.gameObject.AddComponent<UIRevealSequence>();
        entry.Configure(onPlay,new[]{"ModernHUD/Position","ModernHUD/Lap","ModernHUD/Time","ModernHUD/Speed","ModernHUD/Tachometer","ModernHUD/SteeringIndicator"},new Vector2(24,0),.03f,.24f);
    }

    private void BuildTitlePedalPanel(Transform title, int playerIndex, Vector2 anchorMin, Vector2 anchorMax, Color accent)
    {
        RacingMenuButton start = NeonUI.Button(title, $"Player{playerIndex + 1}Pedal", "はじめる", "START",
            RacingIconGraphic.Icon.Flag, anchorMin, anchorMax, true, () => Gmanager.Control?.ConfirmTitleStart(playerIndex));
        PedalButtonFeedback feedback = start.GetComponent<PedalButtonFeedback>() ?? start.gameObject.AddComponent<PedalButtonFeedback>();
        feedback.Configure(NeonUI.Pink);
        start.transform.Find("ModernSurface").gameObject.SetActive(false);
        Image hit = start.GetComponent<Image>(); hit.enabled = true; hit.color = Color.clear; hit.raycastTarget = true;
        start.Configure(start.transform.Find("ButtonSurface").GetComponent<RacingPanelGraphic>());
        start.gameObject.SetActive(true);
        start.ConfigureHold(.65f, true);
        start.PlayConfirmationSound = false; // Ready cue comes from the same gate as pedal input.
        start.transform.Find("Label").GetComponent<TMP_Text>().fontSizeMax=46f;
        var caption=start.transform.Find("Caption").GetComponent<TMP_Text>();caption.fontSizeMax=25f;caption.color=ArcadeUI.For(playerIndex).Ink;caption.gameObject.SetActive(true);
        ArcadeUI.Place(start.transform,"Label",.28f,.42f,.88f,.88f);ArcadeUI.Place(start.transform,"Caption",.285f,.17f,.87f,.43f);
        titleButtonFeedback[playerIndex] = feedback;
    }

    private void UpdateTitlePedal(int playerIndex, float value, bool ready, bool armed)
    {
        if (playerIndex < 0 || playerIndex >= titleButtonFeedback.Length || titleButtonFeedback[playerIndex] == null)
        {
            return;
        }

        float amount = Mathf.Clamp01(value);
        Color accent = ArcadeUI.For(displayPlayerIndex).Primary;
        Color readyColor = ArcadeUI.For(displayPlayerIndex).Primary;
        titleButtonFeedback[playerIndex].SetState(armed, amount, ready ? readyColor : accent);
        titleButtonFeedback[playerIndex].SetConfirmed(ready);
        if (titlePrompt != null)
        {
            var input = Gmanager.Control != null ? Gmanager.Control.IManager : null;
            string connection = input != null ? DriverInputNotice.Connection(input.GetPlayerInputSource(playerIndex)) : "";
            string prompt = connection.Length > 0 ? connection + "\n係員に知らせてね" : !armed
                ? "ペダルを離して準備してください"
                : ready ? "準備完了  /  相手の準備を待っています" : "長押しで車の選択へ / ペダルを踏み込む";
            if (titlePrompt.text != prompt) titlePrompt.text = prompt;
        }
    }

    private void SetRaceStatusValue(string value, string caption)
    {
        if (raceStatus == null)
        {
            return;
        }
        bool changed = raceStatus.text != value;
        raceStatus.text = value;
        bool go = value == "GO!";
        int.TryParse(value, out int seconds);
        Color accent = go ? ArcadeUI.Blue.Secondary : ArcadeUI.For(displayPlayerIndex).Primary;
        int illuminated = go ? countdownSignals.Length : Mathf.Clamp(countdownSignals.Length - seconds + 1, 0, countdownSignals.Length);
        for (int i = 0; i < countdownSignals.Length; i++)
            if (countdownSignals[i] != null)
            {
                countdownSignals[i].color = accent;
                countdownSignals[i].SetAmount(i < illuminated ? 1f : 0f);
            }
        if (raceStatusCaption != null)
        {
            raceStatusCaption.text = go ? "スタート!" : "READY";
            raceStatusEnglishCaption.text = "";
        }
        if (changed)
        {
            raceStatusPulse?.Play(go ? accent : Color.white, -3f);
        }
    }

    private void OnDestroy()
    {
        if (titleLogoMaterial != null) Destroy(titleLogoMaterial);
    }

    private void SetStatusVisibility(bool showCountdown, bool showWarning)
    {
        countdownStatusRoot?.SetActive(showCountdown);
        finishWarningRoot?.SetActive(showWarning);
        if(onPlayRoot != null)
        {
            onPlayRoot.transform.Find("ModernHUD")?.gameObject.SetActive(true);
            onPlayRoot.transform.Find("MiniMap")?.gameObject.SetActive(false);
            onPlayRoot.transform.Find("ModernHUD/LapDelta")?.gameObject.SetActive(!showCountdown);
            onPlayRoot.transform.Find("ModernHUD/Leaderboard")?.gameObject.SetActive(!showCountdown);
            onPlayRoot.transform.Find("CountdownCourse")?.gameObject.SetActive(false);
            onPlayRoot.transform.Find("CountdownLap")?.gameObject.SetActive(false);
        }
    }

    private void SetFadeInputBlocking(bool blocksInput)
    {
        if (fadeCanvasGroup == null)
        {
            return;
        }

        fadeCanvasGroup.blocksRaycasts = blocksInput;
        fadeCanvasGroup.interactable = blocksInput;
    }

    private void RefreshResultContent()
    {
        if(resultRoot==null)return;
        Transform card=resultRoot.transform.Find("ResultPresentation/ResultCard");
        var content=card!=null?card.GetComponent<RectTransform>():resultRoot.GetComponent<RectTransform>();
        if(resultContent!=content){resultContent=content;if(content!=null)resultContentBasePosition=content.anchoredPosition;}
        resultReveal=card!=null?card.GetComponent<UIRevealSequence>():null;
        // OnEnable starts the reveal before this method runs. Cache the rank impulse
        // only after restoring the layout, rather than adopting its temporary entry pose.
        resultReveal?.Cancel();
        resultRankLabel=card!=null?card.Find("Finish/Rank")?.GetComponent<TMP_Text>():null;
        if(resultRankLabel!=null)
        {
            finishRankPulse=resultRankLabel.GetComponent<UIValuePulse>();if(finishRankPulse==null)finishRankPulse=resultRankLabel.gameObject.AddComponent<UIValuePulse>();
            finishRankPulse.ResetPose();
        }
    }
    private void RestoreResultContent()
    {
        resultReveal?.Cancel();if(finishRankPulse!=null)finishRankPulse.ResetPose();
        if(resultContent==null)return;resultContent.anchoredPosition=resultContentBasePosition;resultContent.localScale=Vector3.one;
    }
    private void RestoreResultRows(bool celebrateWinner)
    {
        resultReveal?.Cancel();
        if(celebrateWinner&&resultRankLabel!=null&&resultRankLabel.text!="DNF"&&resultRankLabel.text!="—")finishRankPulse.Play(ArcadeUI.For(transform).Accent,0);
    }

    private static GameObject InstantiateScreenBackground(Transform parent, string resourcePath, string objectName)
    {
        Transform existing = parent.Find(objectName);
        if (existing != null)
        {
            existing.SetAsFirstSibling();
            return existing.gameObject;
        }

        GameObject prefab = Resources.Load<GameObject>(resourcePath);
        if (prefab == null)
        {
            Debug.LogWarning($"UI background prefab was not found at Resources/{resourcePath}.");
            return null;
        }

        GameObject instance = Instantiate(prefab, parent, false);
        instance.name = objectName;
        instance.layer = parent.gameObject.layer;
        RectTransform rectTransform = instance.GetComponent<RectTransform>();
        if (rectTransform != null)
        {
            rectTransform.anchorMin = Vector2.zero;
            rectTransform.anchorMax = Vector2.one;
            rectTransform.offsetMin = Vector2.zero;
            rectTransform.offsetMax = Vector2.zero;
        }
        instance.transform.SetAsFirstSibling();
        return instance;
    }

    private static GameObject CreatePanel(
        Transform parent,
        string objectName,
        Vector2 anchorMin,
        Vector2 anchorMax,
        Color color)
    {
        Transform existing = parent.Find(objectName);
        GameObject panelObject;
        if (existing != null)
        {
            panelObject = existing.gameObject;
            if (panelObject.GetComponent<Image>() == null)
            {
                panelObject.AddComponent<Image>();
            }
        }
        else
        {
            panelObject = new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            panelObject.layer = parent.gameObject.layer;
            panelObject.transform.SetParent(parent, false);
        }

        RectTransform rectTransform = panelObject.GetComponent<RectTransform>();
        rectTransform.anchorMin = anchorMin;
        rectTransform.anchorMax = anchorMax;
        rectTransform.offsetMin = Vector2.zero;
        rectTransform.offsetMax = Vector2.zero;
        Image image = panelObject.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return panelObject;
    }

    private static TMP_Text CreateLabel(
        Transform parent,
        string objectName,
        string labelText,
        Vector2 anchorMin,
        Vector2 anchorMax,
        float maximumFontSize,
        Color color)
    {
        Transform existing = parent.Find(objectName);
        GameObject labelObject;
        if (existing != null)
        {
            labelObject = existing.gameObject;
        }
        else
        {
            labelObject = new GameObject(
                objectName,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(TextMeshProUGUI));
            labelObject.layer = parent.gameObject.layer;
            labelObject.transform.SetParent(parent, false);
        }

        RectTransform rectTransform = labelObject.GetComponent<RectTransform>();
        rectTransform.anchorMin = anchorMin;
        rectTransform.anchorMax = anchorMax;
        rectTransform.offsetMin = Vector2.zero;
        rectTransform.offsetMax = Vector2.zero;

        TMP_Text label = labelObject.GetComponent<TMP_Text>();
        bool japanese = false;
        foreach (char c in labelText) if (c > 255) { japanese = true; break; }
        RacingUITheme.ApplyTypography(label, japanese ? FontRole.Japanese : FontRole.English, maximumFontSize);
        label.text = labelText;
        label.alignment = TextAlignmentOptions.Center;
        label.color = color;
        label.enableAutoSizing = true;
        label.fontSizeMin = Mathf.Min(maximumFontSize, Mathf.Max(12f, maximumFontSize * 0.65f));
        label.fontSizeMax = maximumFontSize;
        label.raycastTarget = false;
        return label;
    }
}
