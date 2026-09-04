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

    private GameObject titleRoot;
    private GameObject onPlayRoot;
    private GameObject resultPanel;
    private CanvasGroup fadeCanvasGroup;
    private RectTransform fadeOverlay;
    private TMP_Text titlePrompt;
    private TMP_Text raceStatus;

    public bool IsTransitioning { get; private set; }
    public string RaceStatusText => raceStatus != null ? raceStatus.text : string.Empty;

    public void Initialize(
        Transform title,
        Transform onPlay,
        Transform result,
        string titleText,
        string promptText)
    {
        titleRoot = title != null ? title.gameObject : null;
        onPlayRoot = onPlay != null ? onPlay.gameObject : null;

        Transform panel = result != null ? result.Find("Panel") : null;
        resultPanel = panel != null ? panel.gameObject : result != null ? result.gameObject : null;

        InitializeTitleUI(title, titleText, promptText);
        InitializeRaceStatusUI(onPlay);
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

    public void SetTitlePrompt(string promptText)
    {
        if (titlePrompt != null && titlePrompt.text != promptText)
        {
            titlePrompt.text = promptText;
        }
    }

    public void SetRaceStatus(string statusText)
    {
        if (raceStatus == null)
        {
            return;
        }

        string value = statusText ?? string.Empty;
        raceStatus.text = value;
        raceStatus.gameObject.SetActive(!string.IsNullOrWhiteSpace(value));
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

        if (resultPanel != null)
        {
            resultPanel.SetActive(state == Gmanager.State.Result);
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

        background.color = new Color(0.015f, 0.025f, 0.045f, 0.72f);
        background.raycastTarget = false;

        CreateLabel(
            title,
            "TitleText",
            titleText,
            new Vector2(0.1f, 0.52f),
            new Vector2(0.9f, 0.82f),
            120f,
            Color.white);

        titlePrompt = CreateLabel(
            title,
            "StartPrompt",
            promptText,
            new Vector2(0.15f, 0.2f),
            new Vector2(0.85f, 0.42f),
            42f,
            new Color(0.2f, 0.9f, 1f, 1f));
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

        raceStatus = CreateLabel(
            onPlay,
            "RaceStatus",
            string.Empty,
            new Vector2(0.12f, 0.58f),
            new Vector2(0.88f, 0.82f),
            120f,
            new Color(0.2f, 0.9f, 1f, 1f));
        raceStatus.fontStyle = FontStyles.Bold;
        raceStatus.gameObject.SetActive(false);
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
