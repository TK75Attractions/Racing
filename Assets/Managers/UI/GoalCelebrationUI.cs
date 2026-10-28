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
    private CanvasGroup canvasGroup;
    private RectTransform hero;
    private TMP_Text winnerText;
    private TMP_Text timeText;
    private GoalConfettiPiece[] confetti;
    private Coroutine routine;
    private UIRevealSequence heroMotion;

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
        heroMotion.Play();
        routine = StartCoroutine(PlayRoutine(Mathf.Max(1.2f, visibleSeconds), onCompleted));
    }

    public void HideImmediate()
    {
        if (routine != null)
        {
            StopCoroutine(routine);
            routine = null;
        }
        heroMotion?.Cancel();
        if(hero!=null){hero.anchoredPosition=Vector2.zero;hero.localScale=Vector3.one;hero.localRotation=Quaternion.identity;}
        if(canvasGroup!=null)canvasGroup.alpha=0;
        gameObject.SetActive(false);
    }

    private void Build(TMP_FontAsset font)
    {
        var palette=ArcadeUI.For(transform);
        canvasGroup = GetComponent<CanvasGroup>();
        canvasGroup.alpha = 0f;
        canvasGroup.interactable = false;
        canvasGroup.blocksRaycasts = true;

        GameObject glow = GetOrCreate("VictoryGlow", transform, typeof(Image));
        Anchor(glow.GetComponent<RectTransform>(), new Vector2(0f, 0.32f), new Vector2(1f, 0.73f));
        glow.GetComponent<Image>().color = new Color(palette.Ink.r,palette.Ink.g,palette.Ink.b,.9f);
        RacingUITheme.Rule(glow.transform, "TopEdge", new Vector2(0f, 0.995f), Vector2.one, palette.Primary);
        RacingUITheme.Rule(glow.transform, "BottomEdge", Vector2.zero, new Vector2(1f, 0.005f), RacingPanelGraphic.Alpha(palette.Primary, 0.4f));

        GameObject heroObject = GetOrCreate("Hero", transform, typeof(CanvasGroup));
        hero = heroObject.GetComponent<RectTransform>();
        Anchor(hero, new Vector2(0.12f, 0.22f), new Vector2(0.88f, 0.82f));

        TMP_Text goalText = CreateLabel(hero, "GoalText", "GOAL!", new Vector2(0f, 0.36f), new Vector2(1f, 0.9f),
            190f, Color.white, font);
        RacingHUDStyle.Label(hero, "JapaneseGoal", "ゴール!", .12f, .84f, .88f, .97f, 38f,
            RacingHUDStyle.Teal, TextAlignmentOptions.Center, bold: true);
        goalText.font=ArcadeUI.SpeedDigits;goalText.fontSharedMaterial=goalText.font.material;goalText.fontStyle=FontStyles.Normal;
        goalText.characterSpacing = 7f;
        winnerText = CreateLabel(hero, "WinnerText", "PLAYER 1 WINS", new Vector2(0.12f, 0.2f), new Vector2(0.88f, 0.42f),
            52f, palette.Accent, font);
        timeText = CreateLabel(hero, "FinishTime", "00:00.000", new Vector2(0.25f, 0.02f), new Vector2(0.75f, 0.2f),
            42f, palette.Soft, font);

        timeText.font=ArcadeUI.Digits;timeText.fontSharedMaterial=timeText.font.material;timeText.fontStyle=FontStyles.Normal;
        heroMotion=hero.GetComponent<UIRevealSequence>();if(heroMotion==null)heroMotion=hero.gameObject.AddComponent<UIRevealSequence>();
        heroMotion.Configure(hero,new[]{"GoalText","JapaneseGoal","WinnerText","FinishTime"},new Vector2(24,0),.07f,.26f);
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
            pieceImage.color = index%4==0?palette.Primary:index%4==1?palette.Secondary:index%4==2?palette.Accent:palette.Paper;
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
        float elapsed = 0f;double began=UIMotion.Realtime;
        const float enterSeconds = 0.62f;
        while (elapsed < enterSeconds)
        {
            if(UIMotion.IsPaused){yield return null;continue;}
            elapsed=(float)(UIMotion.Realtime-began);
            float t = Mathf.Clamp01(elapsed / enterSeconds);
            float eased = 1f - Mathf.Pow(1f - t, 3f);
            canvasGroup.alpha = eased;
            hero.anchoredPosition=Vector2.left*(64f*(1-eased));
            hero.localScale=Vector3.one*(1f-.06f*(1-eased)+.025f*UIMotion.Spring(t));
            hero.localRotation=Quaternion.Euler(0,0,-1.5f*(1-eased));
            yield return null;
        }

        hero.anchoredPosition=Vector2.zero;
        hero.localScale = Vector3.one;
        hero.localRotation = Quaternion.identity;
        float holdSeconds = Mathf.Max(0f, visibleSeconds - enterSeconds - 0.45f);
        if (holdSeconds > 0f)
        {
            double holdBegan = UIMotion.Realtime;
            while (UIMotion.Realtime - holdBegan < holdSeconds) yield return null;
        }

        elapsed = 0f;began=UIMotion.Realtime;
        const float exitSeconds = 0.45f;
        while (elapsed < exitSeconds)
        {
            if(UIMotion.IsPaused){yield return null;continue;}
            elapsed=(float)(UIMotion.Realtime-began);
            float t = Mathf.Clamp01(elapsed / exitSeconds);
            canvasGroup.alpha = 1f - t * t;
            hero.anchoredPosition=Vector2.right*(28f*UIMotion.In(t));
            yield return null;
        }

        while (UIMotion.IsPaused) yield return null;
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
        RacingUITheme.ApplyTypography(label, FontRole.English, maximumFontSize);
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

        uint state=(uint)seed;
        xOrigin = Mathf.Lerp(-940f, 940f, Next01(ref state));
        float y = Mathf.Lerp(520f, 1260f, Next01(ref state));
        fallSpeed = Mathf.Lerp(310f, 670f, Next01(ref state));
        spinSpeed = Mathf.Lerp(-420f, 420f, Next01(ref state));
        swaySpeed = Mathf.Lerp(1.7f, 4.1f, Next01(ref state));
        swayAmount = Mathf.Lerp(18f, 75f, Next01(ref state));
        phase = Mathf.Lerp(0f, Mathf.PI * 2f, Next01(ref state));
        rectTransform.sizeDelta = new Vector2(
            Mathf.Lerp(13f, 29f, Next01(ref state)),
            Mathf.Lerp(24f, 48f, Next01(ref state)));
        rectTransform.anchoredPosition = new Vector2(xOrigin, y);
        rectTransform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Lerp(0f, 360f, Next01(ref state)));
    }

    private static float Next01(ref uint state){state^=state<<13;state^=state>>17;state^=state<<5;return (state>>8)*(1f/16777216f);}
    private void Update()
    {
        if (rectTransform == null)
        {
            return;
        }

        Vector2 position = rectTransform.anchoredPosition;
        position.y -= fallSpeed * UIMotion.DeltaTime;
        position.x = xOrigin + Mathf.Sin(Time.unscaledTime * swaySpeed + phase) * swayAmount;
        rectTransform.anchoredPosition = position;
        rectTransform.Rotate(0f, spinSpeed * 0.35f * UIMotion.DeltaTime, spinSpeed * UIMotion.DeltaTime);

        if (position.y < -620f)
        {
            position.y = 620f;
            xOrigin = -940f + Mathf.Repeat(xOrigin + phase * 137f + 1880f, 1880f);
            rectTransform.anchoredPosition = position;
        }
    }
}
