using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class ScreenTransitionController : MonoBehaviour
{
    [Header("Transition")]
    [SerializeField, Min(0f)] private float fadeOutSeconds = 0.25f;
    [SerializeField, Min(0f)] private float fadeInSeconds = 0.35f;
    [SerializeField, Min(0f)] private float resultEnterSeconds = 0.55f;
    [SerializeField, Min(0f)] private float resultExitSeconds = 0.32f;

    private GameObject titleRoot;
    private GameObject onPlayRoot;
    private GameObject resultRoot;
    private CanvasGroup resultCanvasGroup;
    private RectTransform resultContent;
    private Vector2 resultContentBasePosition;
    private GoalCelebrationUI goalCelebration;
    private CanvasGroup fadeCanvasGroup;
    private RectTransform fadeOverlay;
    private TMP_Text titlePrompt;
    private readonly Image[] titlePedalFills = new Image[2];
    private readonly RectTransform[] titlePedalFillRects = new RectTransform[2];
    private readonly TMP_Text[] titlePedalValues = new TMP_Text[2];
    private readonly TMP_Text[] titlePedalStates = new TMP_Text[2];
    private GameObject countdownStatusRoot;
    private GameObject finishWarningRoot;
    private TMP_Text raceStatus;
    private TMP_Text raceStatusCaption;
    private TMP_Text finishWarningText;
    private int lastWarningSecond = -1;
    private UIValuePulse raceStatusPulse;

    public bool IsTransitioning { get; private set; }
    public string RaceStatusText => finishWarningRoot != null && finishWarningRoot.activeSelf
        ? finishWarningText != null ? finishWarningText.text : string.Empty
        : raceStatus != null ? raceStatus.text : string.Empty;

    public void Initialize(
        Transform title,
        Transform onPlay,
        Transform result,
        string titleText,
        string promptText)
    {
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
        UpdateTitlePedal(0, playerOne, playerOneReady, armed);
        UpdateTitlePedal(1, playerTwo, playerTwoReady, armed);
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
            yield return null;
        }

        RestoreResultContent();
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
            yield return null;
        }

        yield return FadeTo(1f, fadeOutSeconds);
        onScreenCovered?.Invoke();
        SetScreenVisibility(targetState);
        yield return null;
        yield return FadeTo(0f, fadeInSeconds);

        RestoreResultContent();
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

        background.color = new Color(0.008f, 0.016f, 0.03f, 0.76f);
        background.raycastTarget = false;

        GameObject topLine = CreatePanel(title, "TopLine", new Vector2(0.025f, 0.92f), new Vector2(0.19f, 0.924f),
            new Color(0.15f, 0.85f, 1f, 0.9f));
        topLine.GetComponent<Image>().raycastTarget = false;

        TMP_Text circuitLabel = CreateLabel(
            title, "CircuitLabel", "ENNICH CIRCUIT  /  TWO PLAYER RACING",
            new Vector2(0.025f, 0.925f), new Vector2(0.48f, 0.975f), 22f,
            new Color(0.73f, 0.82f, 0.9f, 1f));
        circuitLabel.alignment = TextAlignmentOptions.Left;
        circuitLabel.characterSpacing = 9f;

        TMP_Text mainTitle = CreateLabel(
            title,
            "TitleText",
            titleText,
            new Vector2(0.12f, 0.51f),
            new Vector2(0.88f, 0.8f),
            172f,
            Color.white);
        mainTitle.fontStyle = FontStyles.Bold | FontStyles.Italic;
        mainTitle.characterSpacing = 5f;

        TMP_Text subtitle = CreateLabel(
            title, "TitleSubtitle", "SPEED  /  CONTROL  /  VICTORY",
            new Vector2(0.25f, 0.465f), new Vector2(0.75f, 0.535f), 27f,
            new Color(0.35f, 0.88f, 1f, 1f));
        subtitle.characterSpacing = 13f;

        titlePrompt = CreateLabel(
            title,
            "StartPrompt",
            promptText,
            new Vector2(0.24f, 0.405f),
            new Vector2(0.76f, 0.475f),
            25f,
            new Color(0.82f, 0.87f, 0.92f, 1f));
        titlePrompt.characterSpacing = 6f;

        BuildTitlePedalPanel(title, 0, new Vector2(0.17f, 0.19f), new Vector2(0.49f, 0.38f),
            new Color(0.05f, 0.78f, 1f, 1f));
        BuildTitlePedalPanel(title, 1, new Vector2(0.51f, 0.19f), new Vector2(0.83f, 0.38f),
            new Color(1f, 0.28f, 0.36f, 1f));

        TMP_Text footer = CreateLabel(
            title, "TitleFooter", "PRESS AND HOLD BOTH PEDALS TO JOIN THE GRID",
            new Vector2(0.2f, 0.085f), new Vector2(0.8f, 0.145f), 19f,
            new Color(0.46f, 0.55f, 0.64f, 1f));
        footer.characterSpacing = 5f;
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
            new Vector2(0.39f, 0.57f), new Vector2(0.61f, 0.86f),
            new Color(0.015f, 0.03f, 0.055f, 0.9f));
        Outline countdownOutline = countdownStatusRoot.GetComponent<Outline>();
        if (countdownOutline == null)
        {
            countdownOutline = countdownStatusRoot.AddComponent<Outline>();
        }
        countdownOutline.effectColor = new Color(0.08f, 0.82f, 1f, 0.8f);
        countdownOutline.effectDistance = new Vector2(3f, -3f);

        raceStatusCaption = CreateLabel(
            countdownStatusRoot.transform, "Caption", "RACE START",
            new Vector2(0.08f, 0.72f), new Vector2(0.92f, 0.91f), 24f,
            new Color(0.22f, 0.88f, 1f, 1f));
        raceStatusCaption.characterSpacing = 8f;
        raceStatus = CreateLabel(
            countdownStatusRoot.transform, "RaceStatus", string.Empty,
            new Vector2(0.05f, 0.08f), new Vector2(0.95f, 0.78f), 154f,
            Color.white);
        raceStatus.fontStyle = FontStyles.Bold;
        raceStatusPulse = raceStatus.GetComponent<UIValuePulse>();
        if (raceStatusPulse == null)
        {
            raceStatusPulse = raceStatus.gameObject.AddComponent<UIValuePulse>();
        }
        finishWarningRoot = CreatePanel(onPlay, "FinishWarningStatus",
            new Vector2(0.31f, 0.78f), new Vector2(0.69f, 0.94f),
            new Color(0.12f, 0.025f, 0.018f, 0.94f));
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
        Outline outline = panel.GetComponent<Outline>();
        if (outline == null)
        {
            outline = panel.AddComponent<Outline>();
        }
        outline.effectColor = new Color(accent.r, accent.g, accent.b, 0.65f);
        outline.effectDistance = new Vector2(2f, -2f);

        GameObject accentBar = CreatePanel(panel.transform, "Accent",
            new Vector2(0f, 0f), new Vector2(0.012f, 1f), accent);
        accentBar.GetComponent<Image>().raycastTarget = false;

        TMP_Text player = CreateLabel(panel.transform, "Player", $"P{playerIndex + 1}",
            new Vector2(0.07f, 0.48f), new Vector2(0.25f, 0.88f), 49f, accent);
        player.fontStyle = FontStyles.Bold | FontStyles.Italic;
        TMP_Text instruction = CreateLabel(panel.transform, "Instruction", "PRESS PEDAL",
            new Vector2(0.27f, 0.57f), new Vector2(0.72f, 0.85f), 24f,
            new Color(0.82f, 0.86f, 0.92f, 1f));
        instruction.alignment = TextAlignmentOptions.Left;
        instruction.characterSpacing = 4f;

        titlePedalValues[playerIndex] = CreateLabel(panel.transform, "Value", "0%",
            new Vector2(0.73f, 0.57f), new Vector2(0.93f, 0.85f), 25f, accent);
        titlePedalValues[playerIndex].alignment = TextAlignmentOptions.Right;
        titlePedalValues[playerIndex].fontStyle = FontStyles.Bold;

        GameObject track = CreatePanel(panel.transform, "GaugeTrack",
            new Vector2(0.27f, 0.31f), new Vector2(0.93f, 0.42f),
            new Color(0.22f, 0.27f, 0.33f, 0.9f));
        GameObject fill = CreatePanel(track.transform, "Fill", Vector2.zero, Vector2.one, accent);
        Image fillImage = fill.GetComponent<Image>();
        titlePedalFills[playerIndex] = fillImage;
        titlePedalFillRects[playerIndex] = fill.GetComponent<RectTransform>();

        GameObject threshold = CreatePanel(track.transform, "Threshold",
            new Vector2(0.79f, -0.2f), new Vector2(0.805f, 1.2f), Color.white);
        threshold.GetComponent<Image>().raycastTarget = false;

        titlePedalStates[playerIndex] = CreateLabel(panel.transform, "State", "RELEASE PEDAL",
            new Vector2(0.27f, 0.06f), new Vector2(0.93f, 0.27f), 18f,
            new Color(0.55f, 0.63f, 0.72f, 1f));
        titlePedalStates[playerIndex].alignment = TextAlignmentOptions.Left;
        titlePedalStates[playerIndex].characterSpacing = 3f;
    }

    private void UpdateTitlePedal(int playerIndex, float value, bool ready, bool armed)
    {
        if (playerIndex < 0 || playerIndex >= titlePedalFills.Length || titlePedalFills[playerIndex] == null)
        {
            return;
        }

        float amount = Mathf.Clamp01(value);
        Color accent = playerIndex == 0
            ? new Color(0.05f, 0.78f, 1f, 1f)
            : new Color(1f, 0.28f, 0.36f, 1f);
        Color readyColor = new Color(0.2f, 1f, 0.58f, 1f);
        RectTransform fillRect = titlePedalFillRects[playerIndex];
        fillRect.anchorMax = new Vector2(amount, 1f);
        fillRect.offsetMin = Vector2.zero;
        fillRect.offsetMax = Vector2.zero;
        titlePedalFills[playerIndex].color = ready ? readyColor : accent;
        titlePedalValues[playerIndex].text = ready ? "READY" : $"{Mathf.RoundToInt(amount * 100f):00}%";
        titlePedalValues[playerIndex].color = ready ? readyColor : accent;
        titlePedalStates[playerIndex].text = !armed ? "RELEASE TO ARM" : ready ? "ON THE GRID" : "HOLD TO READY";
        titlePedalStates[playerIndex].color = ready ? readyColor : new Color(0.55f, 0.63f, 0.72f, 1f);
    }

    private void SetRaceStatusValue(string value, string caption)
    {
        if (raceStatus == null)
        {
            return;
        }
        bool changed = raceStatus.text != value;
        raceStatus.text = value;
        if (raceStatusCaption != null)
        {
            raceStatusCaption.text = caption;
        }
        if (changed)
        {
            raceStatusPulse?.Play(value == "GO!" ? new Color(0.2f, 1f, 0.55f, 1f) : Color.white, -5f);
        }
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
        label.text = labelText;
        label.alignment = TextAlignmentOptions.Center;
        label.color = color;
        label.enableAutoSizing = true;
        label.fontSizeMin = 18f;
        label.fontSizeMax = maximumFontSize;
        label.raycastTarget = false;
        return label;
    }
}
