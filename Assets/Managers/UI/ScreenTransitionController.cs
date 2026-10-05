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
            string value = $"{playerLabel}   {Mathf.Max(0f, secondsRemaining):0.0}s";
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

        Image background = title.GetComponent<Image>();
        if (background == null)
        {
            background = title.gameObject.AddComponent<Image>();
        }

        background.color = Color.black;
        background.raycastTarget = false;

        InstantiateScreenBackground(title, "UI/TitleScreenBackground", "TitleBackground");
        CreatePanel(title, "TitleBackgroundTint", Vector2.zero, Vector2.one,
            new Color(0.012f, 0.024f, 0.040f, 0.68f));

        GameObject topLine = CreatePanel(title, "TopLine", new Vector2(0.025f, 0.92f), new Vector2(0.19f, 0.924f),
            new Color(0.15f, 0.85f, 1f, 0.9f));
        topLine.GetComponent<Image>().raycastTarget = false;

        TMP_Text circuitLabel = CreateLabel(
            title, "CircuitLabel", "TSUKUKOMA CIRCUIT   /   RACING",
            new Vector2(0.025f, 0.925f), new Vector2(0.48f, 0.975f), 22f,
            new Color(0.73f, 0.82f, 0.9f, 1f));
        circuitLabel.alignment = TextAlignmentOptions.Left;
        circuitLabel.characterSpacing = 2.5f;
        TMP_Text playerBadge = CreateLabel(title, "PlayerBadge", $"PLAYER 0{displayPlayerIndex + 1}   /   LOCAL VERSUS",
            new Vector2(0.65f, 0.925f), new Vector2(0.975f, 0.975f), 19f, RacingUITheme.Cyan);
        playerBadge.alignment = TextAlignmentOptions.Right;
        RacingUITheme.Rule(title, "HeaderRule", new Vector2(0.025f, 0.915f), new Vector2(0.975f, 0.916f), new Color(0.4f, 0.6f, 0.7f, 0.25f));

        TMP_Text mainTitle = CreateLabel(
            title,
            "TitleText",
            useArtworkLogo ? titleText : "CIRCUIT",
            new Vector2(0.12f, 0.53f),
            new Vector2(0.88f, 0.72f),
            172f,
            Color.white);
        mainTitle.fontStyle = FontStyles.Bold | FontStyles.Italic;
        mainTitle.characterSpacing = 5f;
        if (!useArtworkLogo)
        {
            TMP_Text wordmark = CreateLabel(title, "Wordmark", "TSUKUKOMA", new Vector2(0.2f, 0.73f), new Vector2(0.8f, 0.80f), 44f, RacingUITheme.Cyan);
            wordmark.characterSpacing = 14f;
        }
        Texture2D logoTexture = useArtworkLogo ? Resources.Load<Texture2D>("UI/TsukukomaCircuitLogo") : null;
        Shader logoShader = useArtworkLogo ? Resources.Load<Shader>("UI/LogoWhiteKey") : null;
        if (useArtworkLogo && logoTexture != null && logoShader != null)
        {
            mainTitle.gameObject.SetActive(false);
            GameObject logoContainer = new GameObject("TitleLogo", typeof(RectTransform));
            logoContainer.layer = title.gameObject.layer;
            logoContainer.transform.SetParent(title, false);
            RectTransform logoRect = logoContainer.GetComponent<RectTransform>();
            logoRect.anchorMin = new Vector2(0.09f, 0.51f);
            logoRect.anchorMax = new Vector2(0.91f, 0.80f);
            logoRect.offsetMin = logoRect.offsetMax = Vector2.zero;
            GameObject logoObject = new GameObject("Artwork", typeof(RectTransform), typeof(RawImage), typeof(AspectRatioFitter));
            logoObject.layer = title.gameObject.layer;
            logoObject.transform.SetParent(logoContainer.transform, false);
            AspectRatioFitter aspect = logoObject.GetComponent<AspectRatioFitter>();
            aspect.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            aspect.aspectRatio = logoTexture.width / (logoTexture.height * 0.42f);
            RawImage logo = logoObject.GetComponent<RawImage>();
            logo.texture = logoTexture;
            logo.uvRect = new Rect(0f, 0.30f, 1f, 0.42f);
            titleLogoMaterial = new Material(logoShader);
            logo.material = titleLogoMaterial;
            logo.raycastTarget = false;
        }

        TMP_Text subtitle = CreateLabel(
            title, "TitleSubtitle", "SPEED  /  CONTROL  /  VICTORY",
            new Vector2(0.25f, 0.465f), new Vector2(0.75f, 0.535f), 27f,
            new Color(0.35f, 0.88f, 1f, 1f));
        subtitle.characterSpacing = 5f;

        titlePrompt = CreateLabel(
            title,
            "StartPrompt",
            promptText,
            new Vector2(0.24f, 0.405f),
            new Vector2(0.76f, 0.475f),
            25f,
            new Color(0.82f, 0.87f, 0.92f, 1f));
        titlePrompt.characterSpacing = 1f;
        RacingUITheme.ApplyTypography(titlePrompt, FontRole.Japanese, 24f);

        Color playerAccent = PlayerCarPaint.GetPlayerColor(displayPlayerIndex);
        BuildTitlePedalPanel(title, displayPlayerIndex, new Vector2(0.35f, 0.235f), new Vector2(0.65f, 0.35f),
            playerAccent);

        TMP_Text footer = CreateLabel(
            title, "TitleFooter", "ハンドルで操作   /   ペダルを踏み込んで決定",
            new Vector2(0.2f, 0.085f), new Vector2(0.8f, 0.145f), 19f,
            new Color(0.46f, 0.55f, 0.64f, 1f));
        footer.characterSpacing = 1.5f;
        RacingUITheme.Rule(title, "FooterRule", new Vector2(0.35f, 0.16f), new Vector2(0.65f, 0.161f), new Color(0.4f, 0.6f, 0.7f, 0.3f));
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

        countdownStatusRoot = CreatePanel(onPlay, "CountdownStatus",
            new Vector2(0.37f, 0.49f), new Vector2(0.63f, 0.84f),
            new Color(0.006f, 0.018f, 0.034f, 0.78f));
        Outline countdownOutline = countdownStatusRoot.GetComponent<Outline>();
        if (countdownOutline == null)
        {
            countdownOutline = countdownStatusRoot.AddComponent<Outline>();
        }
        countdownOutline.effectColor = new Color(0.2f, 0.53f, 0.68f, 0.25f);
        countdownOutline.effectDistance = new Vector2(1f, -1f);
        RacingUITheme.Surface(countdownStatusRoot.transform);
        CreatePanel(countdownStatusRoot.transform, "TopAccent", new Vector2(0.30f, 0.985f), new Vector2(0.70f, 1f), new Color(0.15f, 0.8f, 1f, 1f));

        raceStatusCaption = CreateLabel(
            countdownStatusRoot.transform, "Caption", "RACE START",
            new Vector2(0.08f, 0.79f), new Vector2(0.92f, 0.93f), 19f,
            new Color(0.6f, 0.79f, 0.88f, 1f));
        raceStatusCaption.characterSpacing = 5f;
        raceStatus = CreateLabel(
            countdownStatusRoot.transform, "RaceStatus", string.Empty,
            new Vector2(0.05f, 0.19f), new Vector2(0.95f, 0.80f), 164f,
            Color.white);
        raceStatus.fontStyle = FontStyles.Bold | FontStyles.Italic;
        TMP_FontAsset countdownFont = RacingUIFontCatalog.Get(FontRole.English);
        if (countdownFont != null)
        {
            raceStatus.font = countdownFont;
            raceStatusCaption.font = countdownFont;
        }
        for (int i = 0; i < countdownSignals.Length; i++)
        {
            float left = 0.24f + i * 0.18f;
            countdownSignals[i] = CreatePanel(countdownStatusRoot.transform, $"Signal{i + 1}",
                new Vector2(left, 0.12f), new Vector2(left + 0.15f, 0.14f), new Color(0.12f, 0.22f, 0.29f, 1f)).GetComponent<Image>();
        }
        raceStatusPulse = raceStatus.GetComponent<UIValuePulse>();
        if (raceStatusPulse == null)
        {
            raceStatusPulse = raceStatus.gameObject.AddComponent<UIValuePulse>();
        }
        finishWarningRoot = CreatePanel(onPlay, "FinishWarningStatus",
            new Vector2(0.31f, 0.78f), new Vector2(0.69f, 0.94f),
            new Color(0.12f, 0.025f, 0.018f, 0.94f));
        RacingUITheme.Surface(finishWarningRoot.transform);
        GameObject warningAccent = CreatePanel(finishWarningRoot.transform, "WarningAccent",
            new Vector2(0f, 0f), new Vector2(0.018f, 1f), new Color(1f, 0.24f, 0.12f, 1f));
        warningAccent.GetComponent<Image>().raycastTarget = false;
        TMP_Text warningCaption = CreateLabel(
            finishWarningRoot.transform, "Caption", "FINAL CHANCE  /  TIME TO FINISH",
            new Vector2(0.08f, 0.57f), new Vector2(0.92f, 0.9f), 21f,
            new Color(1f, 0.45f, 0.25f, 1f));
        warningCaption.characterSpacing = 5f;
        finishWarningText = CreateLabel(
            finishWarningRoot.transform, "FinishWarningText", string.Empty,
            new Vector2(0.08f, 0.08f), new Vector2(0.92f, 0.62f), 45f,
            Color.white);
        finishWarningText.fontStyle = FontStyles.Bold;
        if (finishWarningText.GetComponent<UIValuePulse>() == null)
        {
            finishWarningText.gameObject.AddComponent<UIValuePulse>();
        }

        SetStatusVisibility(showCountdown: false, showWarning: false);
    }

    private void BuildTitlePedalPanel(Transform title, int playerIndex, Vector2 anchorMin, Vector2 anchorMax, Color accent)
    {
        GameObject panel = CreatePanel(title, $"Player{playerIndex + 1}Pedal", anchorMin, anchorMax,
            new Color(0.018f, 0.035f, 0.06f, 0.91f));
        TMP_Text instruction = CreateLabel(panel.transform, "Instruction", "スタート",
            new Vector2(0.21f, 0.43f), new Vector2(0.89f, 0.86f), 36f, RacingUITheme.Text);
        instruction.fontStyle = FontStyles.Bold;
        TMP_Text caption = CreateLabel(panel.transform, "Caption", "START",
            new Vector2(0.21f, 0.19f), new Vector2(0.89f, 0.41f), 17f, RacingUITheme.Muted);
        caption.characterSpacing = 4f;

        PedalButtonFeedback feedback = panel.GetComponent<PedalButtonFeedback>();
        if (feedback == null) feedback = panel.AddComponent<PedalButtonFeedback>();
        feedback.Configure(accent);
        titleButtonFeedback[playerIndex] = feedback;

    }

    private void UpdateTitlePedal(int playerIndex, float value, bool ready, bool armed)
    {
        if (playerIndex < 0 || playerIndex >= titleButtonFeedback.Length || titleButtonFeedback[playerIndex] == null)
        {
            return;
        }

        float amount = Mathf.Clamp01(value);
        Color accent = PlayerCarPaint.GetPlayerColor(playerIndex);
        Color readyColor = new Color(0.2f, 1f, 0.58f, 1f);
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
        Color accent = go ? new Color(0.25f, 0.9f, 1f, 1f) : new Color(0.18f, 0.75f, 1f, 1f);
        for (int i = 0; i < countdownSignals.Length; i++)
            if (countdownSignals[i] != null)
                countdownSignals[i].color = go || i < Mathf.Clamp(4 - seconds, 0, 3) ? accent : new Color(0.12f, 0.22f, 0.29f, 1f);
        if (raceStatusCaption != null)
        {
            raceStatusCaption.text = caption;
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
