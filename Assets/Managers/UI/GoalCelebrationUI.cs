using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>ゴール専用の全画面演出と紙吹雪を、外部アセットなしで構築します。</summary>
[DisallowMultipleComponent]
public sealed class GoalCelebrationUI : MonoBehaviour
{
    private const int ConfettiCount = 54;
    private static readonly Color[] ConfettiColors =
    {
        new Color(1f, 0.82f, 0.08f, 1f),
        new Color(1f, 0.25f, 0.15f, 1f),
        new Color(0.08f, 0.88f, 1f, 1f),
        new Color(0.35f, 1f, 0.5f, 1f),
        new Color(0.78f, 0.28f, 1f, 1f)
    };

    private CanvasGroup canvasGroup;
    private RectTransform hero;
    private TMP_Text winnerText;
    private TMP_Text timeText;
    private GoalConfettiPiece[] confetti;
    private Coroutine routine;

    public static GoalCelebrationUI Create(Transform canvasRoot, TMP_FontAsset font)
    {
        Transform existing = canvasRoot.Find("Goal");
        GameObject root = existing != null
            ? existing.gameObject
            : new GameObject("Goal", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(CanvasGroup));
        root.layer = canvasRoot.gameObject.layer;
        root.transform.SetParent(canvasRoot, false);

        RectTransform rootRect = root.GetComponent<RectTransform>();
        Anchor(rootRect, Vector2.zero, Vector2.one);
        Image backdrop = root.GetComponent<Image>();
        backdrop.color = new Color(0.01f, 0.02f, 0.04f, 0.52f);
        backdrop.raycastTarget = true;

        GoalCelebrationUI view = root.GetComponent<GoalCelebrationUI>();
        if (view == null)
        {
            view = root.AddComponent<GoalCelebrationUI>();
        }
        view.Build(font);
        root.SetActive(false);
        return view;
    }

    public void Play(string winner, string finishTime, float visibleSeconds, Action onCompleted)
    {
        if (routine != null)
        {
            StopCoroutine(routine);
        }

        winnerText.text = winner;
        timeText.text = finishTime;
        gameObject.SetActive(true);
        transform.SetAsLastSibling();
        for (int index = 0; index < confetti.Length; index++)
        {
            confetti[index].Restart(index * 7919 + 17);
        }
        routine = StartCoroutine(PlayRoutine(Mathf.Max(1.2f, visibleSeconds), onCompleted));
    }

    public void HideImmediate()
    {
        if (routine != null)
        {
            StopCoroutine(routine);
            routine = null;
        }
        gameObject.SetActive(false);
    }

    private void Build(TMP_FontAsset font)
    {
        canvasGroup = GetComponent<CanvasGroup>();
        canvasGroup.alpha = 0f;
        canvasGroup.interactable = false;
        canvasGroup.blocksRaycasts = true;

        GameObject glow = GetOrCreate("VictoryGlow", transform, typeof(Image));
        Anchor(glow.GetComponent<RectTransform>(), new Vector2(0f, 0.32f), new Vector2(1f, 0.73f));
        glow.GetComponent<Image>().color = new Color(0.02f, 0.7f, 1f, 0.2f);

        GameObject heroObject = GetOrCreate("Hero", transform, typeof(CanvasGroup));
        hero = heroObject.GetComponent<RectTransform>();
        Anchor(hero, new Vector2(0.12f, 0.22f), new Vector2(0.88f, 0.82f));

        TMP_Text goalText = CreateLabel(hero, "GoalText", "GOAL!", new Vector2(0f, 0.36f), new Vector2(1f, 0.9f),
            190f, Color.white, font);
        goalText.characterSpacing = 7f;
        winnerText = CreateLabel(hero, "WinnerText", "PLAYER 1 WINS", new Vector2(0.12f, 0.2f), new Vector2(0.88f, 0.42f),
            52f, new Color(1f, 0.84f, 0.12f, 1f), font);
        timeText = CreateLabel(hero, "FinishTime", "00:00.000", new Vector2(0.25f, 0.02f), new Vector2(0.75f, 0.2f),
            42f, new Color(0.78f, 0.94f, 1f, 1f), font);

        Transform confettiRoot = transform.Find("Confetti");
        if (confettiRoot == null)
        {
            GameObject confettiObject = new GameObject("Confetti", typeof(RectTransform));
            confettiObject.layer = gameObject.layer;
            confettiObject.transform.SetParent(transform, false);
            confettiRoot = confettiObject.transform;
        }
        Anchor(confettiRoot.GetComponent<RectTransform>(), Vector2.zero, Vector2.one);

        confetti = new GoalConfettiPiece[ConfettiCount];
        for (int index = 0; index < ConfettiCount; index++)
        {
            GameObject pieceObject = GetOrCreate($"Piece{index:00}", confettiRoot, typeof(Image));
            Image pieceImage = pieceObject.GetComponent<Image>();
            pieceImage.color = ConfettiColors[index % ConfettiColors.Length];
            pieceImage.raycastTarget = false;
            GoalConfettiPiece piece = pieceObject.GetComponent<GoalConfettiPiece>();
            if (piece == null)
            {
                piece = pieceObject.AddComponent<GoalConfettiPiece>();
            }
            confetti[index] = piece;
        }
        hero.SetAsLastSibling();
    }

    private IEnumerator PlayRoutine(float visibleSeconds, Action onCompleted)
    {
        float elapsed = 0f;
        const float enterSeconds = 0.62f;
        while (elapsed < enterSeconds)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / enterSeconds);
            float eased = 1f - Mathf.Pow(1f - t, 3f);
            float overshoot = 1f + Mathf.Sin(t * Mathf.PI) * 0.12f;
            canvasGroup.alpha = eased;
            hero.localScale = Vector3.one * Mathf.Lerp(0.62f, overshoot, eased);
            hero.localRotation = Quaternion.Euler(0f, 0f, Mathf.Lerp(-7f, 0f, eased));
            yield return null;
        }

        hero.localScale = Vector3.one;
        hero.localRotation = Quaternion.identity;
        float holdSeconds = Mathf.Max(0f, visibleSeconds - enterSeconds - 0.45f);
        if (holdSeconds > 0f)
        {
            yield return new WaitForSecondsRealtime(holdSeconds);
        }

        elapsed = 0f;
        const float exitSeconds = 0.45f;
        while (elapsed < exitSeconds)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / exitSeconds);
            canvasGroup.alpha = 1f - t * t;
            hero.localScale = Vector3.one * Mathf.Lerp(1f, 1.08f, t);
            yield return null;
        }

        gameObject.SetActive(false);
        routine = null;
        onCompleted?.Invoke();
    }

    private static TMP_Text CreateLabel(Transform parent, string name, string value, Vector2 anchorMin, Vector2 anchorMax,
        float maximumFontSize, Color color, TMP_FontAsset font)
    {
        GameObject labelObject = GetOrCreate(name, parent, typeof(TextMeshProUGUI));
        Anchor(labelObject.GetComponent<RectTransform>(), anchorMin, anchorMax);
        TMP_Text label = labelObject.GetComponent<TMP_Text>();
        if (font != null)
        {
            label.font = font;
        }
        label.text = value;
        label.color = color;
        label.fontStyle = FontStyles.Bold;
        label.alignment = TextAlignmentOptions.Center;
        label.enableAutoSizing = true;
        label.fontSizeMin = 18f;
        label.fontSizeMax = maximumFontSize;
        label.raycastTarget = false;
        return label;
    }

    private static GameObject GetOrCreate(string name, Transform parent, params Type[] extraComponents)
    {
        Transform existing = parent.Find(name);
        if (existing != null)
        {
            return existing.gameObject;
        }

        Type[] components = new Type[extraComponents.Length + 2];
        components[0] = typeof(RectTransform);
        components[1] = typeof(CanvasRenderer);
        for (int index = 0; index < extraComponents.Length; index++)
        {
            components[index + 2] = extraComponents[index];
        }
        GameObject created = new GameObject(name, components);
        created.layer = parent.gameObject.layer;
        created.transform.SetParent(parent, false);
        return created;
    }

    private static void Anchor(RectTransform rectTransform, Vector2 anchorMin, Vector2 anchorMax)
    {
        rectTransform.anchorMin = anchorMin;
        rectTransform.anchorMax = anchorMax;
        rectTransform.offsetMin = Vector2.zero;
        rectTransform.offsetMax = Vector2.zero;
        rectTransform.localScale = Vector3.one;
    }
}

/// <summary>1枚の紙吹雪を軽量に循環させます。</summary>
internal sealed class GoalConfettiPiece : MonoBehaviour
{
    private RectTransform rectTransform;
    private float fallSpeed;
    private float spinSpeed;
    private float swaySpeed;
    private float swayAmount;
    private float phase;
    private float xOrigin;

    public void Restart(int seed)
    {
        if (rectTransform == null)
        {
            rectTransform = GetComponent<RectTransform>();
        }

        System.Random random = new System.Random(seed);
        xOrigin = Mathf.Lerp(-940f, 940f, (float)random.NextDouble());
        float y = Mathf.Lerp(520f, 1260f, (float)random.NextDouble());
        fallSpeed = Mathf.Lerp(310f, 670f, (float)random.NextDouble());
        spinSpeed = Mathf.Lerp(-420f, 420f, (float)random.NextDouble());
        swaySpeed = Mathf.Lerp(1.7f, 4.1f, (float)random.NextDouble());
        swayAmount = Mathf.Lerp(18f, 75f, (float)random.NextDouble());
        phase = Mathf.Lerp(0f, Mathf.PI * 2f, (float)random.NextDouble());
        rectTransform.sizeDelta = new Vector2(
            Mathf.Lerp(13f, 29f, (float)random.NextDouble()),
            Mathf.Lerp(24f, 48f, (float)random.NextDouble()));
        rectTransform.anchoredPosition = new Vector2(xOrigin, y);
        rectTransform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Lerp(0f, 360f, (float)random.NextDouble()));
    }

    private void Update()
    {
        if (rectTransform == null)
        {
            return;
        }

        Vector2 position = rectTransform.anchoredPosition;
        position.y -= fallSpeed * Time.unscaledDeltaTime;
        position.x = xOrigin + Mathf.Sin(Time.unscaledTime * swaySpeed + phase) * swayAmount;
        rectTransform.anchoredPosition = position;
        rectTransform.Rotate(0f, spinSpeed * 0.35f * Time.unscaledDeltaTime, spinSpeed * Time.unscaledDeltaTime);

        if (position.y < -620f)
        {
            position.y = 620f;
            xOrigin = -940f + Mathf.Repeat(xOrigin + phase * 137f + 1880f, 1880f);
            rectTransform.anchoredPosition = position;
        }
    }
}
