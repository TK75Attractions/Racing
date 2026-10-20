using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>ゴール済みプレイヤーの画面に、観戦対象を示す枠とラベルを重ねます。</summary>
[DisallowMultipleComponent]
public sealed class SpectatorOverlayUI : MonoBehaviour
{
    private static readonly Color FrameColor = RacingPanelGraphic.Alpha(RacingHUDStyle.Teal, .60f);
    private TMP_Text playerLabel;
    private TMP_Text remainingLabel;
    private int shownSeconds = int.MinValue;

    public static SpectatorOverlayUI Create(Transform canvasRoot, TMP_FontAsset font)
    {
        Transform existing = canvasRoot.Find("SpectatorOverlay");
        GameObject root = existing != null
            ? existing.gameObject
            : new GameObject("SpectatorOverlay", typeof(RectTransform));
        root.layer = canvasRoot.gameObject.layer;
        root.transform.SetParent(canvasRoot, false);

        RectTransform rootRect = root.GetComponent<RectTransform>();
        Anchor(rootRect, Vector2.zero, Vector2.one);

        SpectatorOverlayUI view = root.GetComponent<SpectatorOverlayUI>();
        if (view == null) view = root.AddComponent<SpectatorOverlayUI>();
        view.Build(font);
        root.SetActive(false);
        return view;
    }

    public void Show(int watchedPlayerIndex)
    {
        playerLabel.text = $"プレイヤー {Mathf.Clamp(watchedPlayerIndex, 0, 1) + 1} を観戦中";
        gameObject.SetActive(true);
        Update();
        transform.SetAsLastSibling();
    }

    public void Hide()
    {
        gameObject.SetActive(false);
    }

    private void Update()
    {
        Gmanager manager = Gmanager.Control;
        SetRemaining(manager != null && manager.WaitingForSecondPlace ? manager.SecondPlaceTimeRemaining : -1f);
    }

    public void SetRemaining(float seconds)
    {
        int value = seconds < 0f ? -1 : Mathf.CeilToInt(Mathf.Max(0f, seconds));
        if (shownSeconds == value) return;
        shownSeconds = value;
        RacingHUDStyle.SetText(remainingLabel, value < 0 ? "終了を確認中 / 操作不要・結果へ自動で進みます"
            : $"残り猶予 {value} 秒 / 操作不要・終了後は結果へ");
    }

    private void Build(TMP_FontAsset font)
    {
        // Short corner marks preserve the racing view and leave both HUD corners clear.
        for (int x = 0; x < 2; x++)
        for (int y = 0; y < 2; y++)
        {
            Vector2 anchor = new Vector2(x, y);
            CreateCorner($"Corner{x}{y}H", anchor, new Vector2(52f, 2f));
            CreateCorner($"Corner{x}{y}V", anchor, new Vector2(2f, 52f));
        }
        RectTransform plate = RacingUITheme.Rect(transform, "PlayerPlate", new Vector2(0.34f, 0.845f), new Vector2(0.66f, 0.955f));
        RacingHUDStyle.Surface(plate, RacingHUDPlateGraphic.PlateShape.Notification, RacingHUDStyle.Teal);
        RacingUITheme.Rule(plate, "LiveAccent", new Vector2(0.07f, 0.67f), new Vector2(0.09f, 0.73f), RacingHUDStyle.Teal);
        TMP_Text caption = RacingHUDStyle.Label(plate, "Caption", "LIVE / SPECTATOR", .12f, .57f, .93f, .84f, 15f, RacingHUDStyle.Teal);
        caption.characterSpacing = 2f;
        playerLabel = RacingHUDStyle.Label(plate, "PlayerLabel", "プレイヤー 2 を観戦中", .07f, .13f, .93f, .54f,
            28f, RacingHUDStyle.Text, bold: true);
        RectTransform footer = RacingUITheme.Rect(transform, "FinishStatus", new Vector2(0.27f, 0.025f), new Vector2(0.73f, 0.12f));
        RacingHUDStyle.Surface(footer, RacingHUDPlateGraphic.PlateShape.Notification, RacingHUDStyle.Teal);
        RacingHUDStyle.Label(footer, "Status", "ゴール済み / 相手のゴール待ち", .05f, .45f, .95f, .92f,
            20f, RacingHUDStyle.Text, TextAlignmentOptions.Center);
        remainingLabel = RacingHUDStyle.Label(footer, "EnglishStatus", "", .05f, .04f, .95f, .43f,
            18f, RacingHUDStyle.Muted, TextAlignmentOptions.Center);
        shownSeconds = int.MinValue;
    }

    private void CreateCorner(string name, Vector2 anchor, Vector2 size)
    {
        RectTransform rect = RacingUITheme.Rect(transform, name, anchor, anchor);
        rect.pivot = anchor;
        rect.sizeDelta = size;
        rect.anchoredPosition = new Vector2(anchor.x == 0f ? 18f : -18f, anchor.y == 0f ? 18f : -18f);
        Image image = rect.GetComponent<Image>() ?? rect.gameObject.AddComponent<Image>();
        image.color = FrameColor;
        image.raycastTarget = false;
    }

    private static GameObject GetOrCreate(string name, Transform parent, params System.Type[] components)
    {
        Transform existing = parent.Find(name);
        if (existing != null) return existing.gameObject;

        GameObject created = new GameObject(name, components);
        created.layer = parent.gameObject.layer;
        created.transform.SetParent(parent, false);
        return created;
    }

    private static void Anchor(RectTransform rect, Vector2 min, Vector2 max)
    {
        rect.anchorMin = min;
        rect.anchorMax = max;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }
}
