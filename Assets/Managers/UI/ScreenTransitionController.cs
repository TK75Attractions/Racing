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
    private readonly RectTransform[] resultRows = new RectTransform[5];
    private readonly CanvasGroup[] resultRowGroups = new CanvasGroup[5];
    private readonly Vector2[] resultRowBasePositions = new Vector2[5];
    private TMP_Text resultWinnerLabel;
    private GoalCelebrationUI goalCelebration;
    private SpectatorOverlayUI spectatorOverlay;
    private CanvasGroup fadeCanvasGroup;
    private RectTransform fadeOverlay;
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
    private readonly Image[] countdownSignals = new Image[3];
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
        if (IsTransitioning)
        {
            return false;
        }

        StartCoroutine(TransitionRoutine(targetState, onScreenCovered, onCompleted));
        return true;
    }

    public bool TryPlayGoal(string winnerText, string finishTime, float visibleSeconds, Action onCompleted = null)
    {
        if (IsTransitioning || goalCelebration == null)
        {
            return false;
        }

        IsTransitioning = true;
        SetScreenVisibility(Gmanager.State.Goal);
        goalCelebration.Play(winnerText, finishTime, visibleSeconds, () =>
        {
            IsTransitioning = false;
            onCompleted?.Invoke();
        });
        return true;
    }

    public bool TryShowResultAnimated(Action onCompleted = null)
    {
        if (IsTransitioning)
        {
            return false;
        }

        StartCoroutine(ShowResultRoutine(onCompleted));
        return true;
    }

    public bool TryCloseResultAndTransition(
        Gmanager.State targetState,
        Action onScreenCovered,
        Action onCompleted = null)
    {
        if (IsTransitioning)
        {
            return false;
        }

        StartCoroutine(CloseResultAndTransitionRoutine(targetState, onScreenCovered, onCompleted));
        return true;
    }

    public void SetTitlePrompt(string promptText)
    {
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

    private IEnumerator TransitionRoutine(
        Gmanager.State targetState,
        Action onScreenCovered,
        Action onCompleted)
    {
        IsTransitioning = true;
        SetFadeInputBlocking(true);

        yield return FadeTo(1f, fadeOutSeconds);
        onScreenCovered?.Invoke();
        SetScreenVisibility(targetState);

        // カメラ切替を黒画面中の1フレームで反映してから表示を戻す。
        yield return null;
        yield return FadeTo(0f, fadeInSeconds);

        SetFadeInputBlocking(false);
        IsTransitioning = false;
        onCompleted?.Invoke();
    }

    private IEnumerator ShowResultRoutine(Action onCompleted)
    {
        IsTransitioning = true;
        SetScreenVisibility(Gmanager.State.Result);
        RefreshResultContent();
        if (resultCanvasGroup != null)
        {
            resultCanvasGroup.alpha = 0f;
        }
        PrepareResultRowsForEntrance();

        float elapsed = 0f;
        while (elapsed < resultEnterSeconds)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = resultEnterSeconds <= 0f ? 1f : Mathf.Clamp01(elapsed / resultEnterSeconds);
            float eased = 1f - Mathf.Pow(1f - t, 3f);
            if (resultCanvasGroup != null)
            {
                resultCanvasGroup.alpha = eased;
            }
            if (resultContent != null)
            {
                resultContent.anchoredPosition = resultContentBasePosition + Vector2.right * Mathf.Lerp(170f, 0f, eased);
                resultContent.localScale = Vector3.one * Mathf.Lerp(0.94f, 1f, eased);
            }
            AnimateResultRowsIn(t);
            yield return null;
        }

        RestoreResultContent();
        RestoreResultRows(celebrateWinner: true);
        if (resultCanvasGroup != null)
        {
            resultCanvasGroup.alpha = 1f;
        }
        IsTransitioning = false;
        onCompleted?.Invoke();
    }

    private IEnumerator CloseResultAndTransitionRoutine(
        Gmanager.State targetState,
        Action onScreenCovered,
        Action onCompleted)
    {
        IsTransitioning = true;
        SetFadeInputBlocking(true);
        RefreshResultContent();

        // 決定時の全体フラッシュを見せてから退出を開始する。
        yield return new WaitForSecondsRealtime(0.25f);

        float elapsed = 0f;
        while (elapsed < resultExitSeconds)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = resultExitSeconds <= 0f ? 1f : Mathf.Clamp01(elapsed / resultExitSeconds);
            float eased = t * t;
            if (resultCanvasGroup != null)
            {
                resultCanvasGroup.alpha = 1f - eased;
            }
            if (resultContent != null)
            {
                resultContent.anchoredPosition = resultContentBasePosition + Vector2.left * Mathf.Lerp(0f, 150f, eased);
                resultContent.localScale = Vector3.one * Mathf.Lerp(1f, 0.96f, eased);
            }
            AnimateResultRowsOut(t);
            yield return null;
        }

        yield return FadeTo(1f, fadeOutSeconds);
        onScreenCovered?.Invoke();
        SetScreenVisibility(targetState);
        yield return null;
        yield return FadeTo(0f, fadeInSeconds);

        RestoreResultContent();
        RestoreResultRows(celebrateWinner: false);
        SetFadeInputBlocking(false);
        IsTransitioning = false;
        onCompleted?.Invoke();
    }

    private IEnumerator FadeTo(float targetAlpha, float duration)
    {
        if (fadeCanvasGroup == null)
        {
            yield break;
        }

        float startAlpha = fadeCanvasGroup.alpha;
        if (duration <= 0f)
        {
            fadeCanvasGroup.alpha = targetAlpha;
            yield break;
        }

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            fadeCanvasGroup.alpha = Mathf.Lerp(startAlpha, targetAlpha, Mathf.Clamp01(elapsed / duration));
            yield return null;
        }

        fadeCanvasGroup.alpha = targetAlpha;
    }

    private void SetScreenVisibility(Gmanager.State state)
    {
        if (titleRoot != null)
        {
            titleRoot.SetActive(state == Gmanager.State.Title);
        }

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
        background.color = Color.black; background.raycastTarget = false;
        NeonUI.Background(title, "TitleBackground", "UI/Neon/TitleBackground");
        TMP_Text playerBadge = NeonUI.Text(title, "PlayerBadge", $"PLAYER 0{displayPlayerIndex + 1}  /  プレイヤー {displayPlayerIndex + 1}",
            new Vector2(.69f,.90f), new Vector2(.97f,.955f), 21f, TextAlignmentOptions.Right, tint: NeonUI.Cyan);
        BuildTitlePedalPanel(title, displayPlayerIndex, new Vector2(.678f,.645f), new Vector2(.970f,.748f), NeonUI.Pink);
        NeonTitleMenu menu = title.GetComponent<NeonTitleMenu>() ?? title.gameObject.AddComponent<NeonTitleMenu>();
        menu.Build(title, displayPlayerIndex);
        titlePrompt = NeonUI.Text(title, "StartPrompt", promptText,
            new Vector2(.678f,.12f), new Vector2(.970f,.174f), 18f, TextAlignmentOptions.Center);
        RectTransform news = NeonUI.Panel(title, "News", new Vector2(.02f,.035f), new Vector2(.977f,.078f));
        NeonUI.Text(news,"Tag","NEWS",new Vector2(.018f,0),new Vector2(.07f,1),19f,TextAlignmentOptions.Center);
        NeonUI.Text(news,"Welcome","ようこそ、ツクコマ・サーキットへ！",new Vector2(.09f,0),new Vector2(.78f,1),18f);
        NeonUI.Text(news,"Version","Ver. " + Application.version,new Vector2(.85f,0),new Vector2(.985f,1),17f,TextAlignmentOptions.Right);
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
        fadeImage.color = Color.black;
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

        countdownStatusRoot = RacingHUDStyle.Plate(onPlay, "CountdownStatus",
            new Vector2(.415f, .43f), new Vector2(.585f, .735f),
            RacingHUDPlateGraphic.PlateShape.Speed, RacingHUDStyle.Teal).gameObject;
        raceStatusCaption = RacingHUDStyle.Label(countdownStatusRoot.transform, "Caption", "スタートまで",
            .12f, .73f, .88f, .85f, 25f, RacingHUDStyle.Text, TextAlignmentOptions.Center, bold: true);
        raceStatusEnglishCaption = RacingHUDStyle.Label(countdownStatusRoot.transform, "EnglishCaption", "RACE START",
            .12f, .655f, .88f, .735f, 14f, RacingHUDStyle.Muted, TextAlignmentOptions.Center);
        raceStatus = RacingHUDStyle.Label(countdownStatusRoot.transform, "RaceStatus", string.Empty,
            .08f, .22f, .92f, .66f, 132f, RacingHUDStyle.Text, TextAlignmentOptions.Center, bold: true);
        for (int i = 0; i < countdownSignals.Length; i++)
        {
            float left = .30f + i * .15f;
            countdownSignals[i] = CreatePanel(countdownStatusRoot.transform, $"Signal{i + 1}",
                new Vector2(left, .16f), new Vector2(left + .10f, .177f), new Color(.30f, .38f, .39f, 1f)).GetComponent<Image>();
        }
        raceStatusPulse = raceStatus.GetComponent<UIValuePulse>() ?? raceStatus.gameObject.AddComponent<UIValuePulse>();
        finishWarningRoot = RacingHUDStyle.Plate(onPlay, "FinishWarningStatus",
            new Vector2(.34f, .815f), new Vector2(.66f, .95f),
            RacingHUDPlateGraphic.PlateShape.Notification, RacingHUDStyle.Amber).gameObject;
        RacingHUDStyle.Heading(finishWarningRoot.transform, "もうすぐレース終了", "TIME TO FINISH", .06f, .54f, .94f, .91f, 24f);
        finishWarningText = RacingHUDStyle.Label(finishWarningRoot.transform, "FinishWarningText", string.Empty,
            .06f, .08f, .94f, .54f, 38f, RacingHUDStyle.Text, TextAlignmentOptions.Center, bold: true);
        if (finishWarningText.GetComponent<UIValuePulse>() == null)
            finishWarningText.gameObject.AddComponent<UIValuePulse>();

        SetStatusVisibility(showCountdown: false, showWarning: false);
    }

    private void BuildTitlePedalPanel(Transform title, int playerIndex, Vector2 anchorMin, Vector2 anchorMax, Color accent)
    {
        RacingMenuButton start = NeonUI.Button(title, $"Player{playerIndex + 1}Pedal", "ゲームをはじめる", "START RACE",
            RacingIconGraphic.Icon.Flag, anchorMin, anchorMax, true, () => Gmanager.Control?.ConfirmTitleStart(playerIndex));
        PedalButtonFeedback feedback = start.GetComponent<PedalButtonFeedback>() ?? start.gameObject.AddComponent<PedalButtonFeedback>();
        feedback.Configure(NeonUI.Pink);
        start.transform.Find("ModernSurface").gameObject.SetActive(false);
        start.Configure(start.transform.Find("ButtonSurface").GetComponent<RacingPanelGraphic>());
        titleButtonFeedback[playerIndex] = feedback;
    }

    private void UpdateTitlePedal(int playerIndex, float value, bool ready, bool armed)
    {
        if (playerIndex < 0 || playerIndex >= titleButtonFeedback.Length || titleButtonFeedback[playerIndex] == null)
        {
            return;
        }

        float amount = Mathf.Clamp01(value);
        Color accent = NeonUI.Pink;
        Color readyColor = NeonUI.Pink;
        titleButtonFeedback[playerIndex].SetState(armed, amount, ready ? readyColor : accent);
        titleButtonFeedback[playerIndex].SetConfirmed(ready);
        if (titlePrompt != null)
        {
            titlePrompt.text = !armed
                ? "ペダルを離して準備してください"
                : ready ? "準備完了  /  相手の準備を待っています" : "ペダルを踏み込んでスタート";
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
        Color accent = go ? RacingHUDStyle.Teal : RacingHUDStyle.Amber;
        for (int i = 0; i < countdownSignals.Length; i++)
            if (countdownSignals[i] != null)
                countdownSignals[i].color = go || i < Mathf.Clamp(4 - seconds, 0, 3) ? accent : new Color(.30f, .38f, .39f, 1f);
        if (raceStatusCaption != null)
        {
            raceStatusCaption.text = go ? "スタート!" : "スタートまで";
            raceStatusEnglishCaption.text = caption;
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
        if (resultRoot == null)
        {
            return;
        }

        Transform presentation = resultRoot.transform.Find("ResultPresentation");
        Transform card = presentation != null ? presentation.Find("ResultCard") : null;
        resultContent = card != null
            ? card.GetComponent<RectTransform>()
            : resultRoot.GetComponent<RectTransform>();
        if (resultContent != null)
        {
            resultContentBasePosition = resultContent.anchoredPosition;
        }

        for (int index = 0; index < resultRows.Length; index++)
        {
            Transform row = card != null ? card.Find($"ResultRow{index + 1}") : null;
            resultRows[index] = row != null ? row.GetComponent<RectTransform>() : null;
            if (resultRows[index] == null)
            {
                continue;
            }
            resultRowBasePositions[index] = resultRows[index].anchoredPosition;
            resultRowGroups[index] = row.GetComponent<CanvasGroup>();
            if (resultRowGroups[index] == null)
            {
                resultRowGroups[index] = row.gameObject.AddComponent<CanvasGroup>();
            }
        }

        Transform winner = card != null ? card.Find("Winner") : null;
        resultWinnerLabel = winner != null ? winner.GetComponent<TMP_Text>() : null;
    }

    private void RestoreResultContent()
    {
        if (resultContent == null)
        {
            return;
        }

        resultContent.anchoredPosition = resultContentBasePosition;
        resultContent.localScale = Vector3.one;
    }

    private void PrepareResultRowsForEntrance()
    {
        for (int index = 0; index < resultRows.Length; index++)
        {
            if (resultRows[index] == null)
            {
                continue;
            }
            resultRows[index].anchoredPosition = resultRowBasePositions[index] + Vector2.right * (240f + index * 70f);
            resultRows[index].localScale = Vector3.one * 0.9f;
            resultRowGroups[index].alpha = 0f;
        }
    }

    private void AnimateResultRowsIn(float normalizedTime)
    {
        for (int index = 0; index < resultRows.Length; index++)
        {
            if (resultRows[index] == null)
            {
                continue;
            }
            float rowTime = Mathf.Clamp01((normalizedTime - 0.08f - index * 0.09f) / 0.56f);
            float eased = 1f - Mathf.Pow(1f - rowTime, 3f);
            float overshoot = Mathf.Sin(rowTime * Mathf.PI) * 0.045f;
            resultRowGroups[index].alpha = eased;
            resultRows[index].anchoredPosition = resultRowBasePositions[index] + Vector2.right * Mathf.Lerp(240f + index * 70f, 0f, eased);
            resultRows[index].localScale = Vector3.one * (Mathf.Lerp(0.9f, 1f, eased) + overshoot);
        }
    }

    private void AnimateResultRowsOut(float normalizedTime)
    {
        for (int index = 0; index < resultRows.Length; index++)
        {
            if (resultRows[index] == null)
            {
                continue;
            }
            float rowTime = Mathf.Clamp01((normalizedTime - (resultRows.Length - 1 - index) * 0.08f) / 0.8f);
            resultRowGroups[index].alpha = 1f - rowTime;
            resultRows[index].anchoredPosition = resultRowBasePositions[index] + Vector2.left * (130f * rowTime);
            resultRows[index].localScale = Vector3.one * Mathf.Lerp(1f, 0.94f, rowTime);
        }
    }

    private void RestoreResultRows(bool celebrateWinner)
    {
        for (int index = 0; index < resultRows.Length; index++)
        {
            if (resultRows[index] == null)
            {
                continue;
            }
            resultRows[index].anchoredPosition = resultRowBasePositions[index];
            resultRows[index].localScale = Vector3.one;
            resultRowGroups[index].alpha = 1f;
        }

        if (celebrateWinner && resultWinnerLabel != null)
        {
            UIValuePulse pulse = resultWinnerLabel.GetComponent<UIValuePulse>();
            if (pulse == null)
            {
                pulse = resultWinnerLabel.gameObject.AddComponent<UIValuePulse>();
            }
            pulse.Play(new Color(1f, 0.82f, 0.12f, 1f), -4f);
        }
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
