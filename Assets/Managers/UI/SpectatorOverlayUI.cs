using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>ゴール済みプレイヤーの画面に、観戦対象を示す枠とラベルを重ねます。</summary>
[DisallowMultipleComponent]
public sealed class SpectatorOverlayUI : MonoBehaviour
{
    private static readonly Color FrameColor = RacingPanelGraphic.Alpha(RacingUITheme.Cyan, 0.75f);
    private TMP_Text playerLabel;

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
        transform.SetAsLastSibling();
    }

    public void Hide()
    {
        gameObject.SetActive(false);
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
        RacingUITheme.Surface(plate);
        RacingUITheme.Rule(plate, "LiveAccent", new Vector2(0.07f, 0.67f), new Vector2(0.09f, 0.73f), RacingUITheme.Cyan);
        TMP_Text caption = RacingUITheme.Label(plate, "Caption", "LIVE  /  SPECTATOR", new Vector2(0.12f, 0.57f), new Vector2(0.93f, 0.84f),
            17f, RacingUITheme.Cyan);
        caption.characterSpacing = 2f;
        playerLabel = RacingUITheme.Label(plate, "PlayerLabel", "プレイヤー 2 を観戦中", new Vector2(0.07f, 0.13f), new Vector2(0.93f, 0.54f),
            28f, RacingUITheme.Text, FontRole.Japanese);
        RectTransform footer = RacingUITheme.Rect(transform, "FinishStatus", new Vector2(0.32f, 0.025f), new Vector2(0.68f, 0.087f));
        RacingUITheme.Surface(footer);
        RacingUITheme.Label(footer, "Status", "ゴール済み  /  レース終了を待っています", new Vector2(0.05f, 0.12f), new Vector2(0.95f, 0.88f),
            20f, RacingUITheme.Muted, FontRole.Japanese, TextAlignmentOptions.Center);
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
