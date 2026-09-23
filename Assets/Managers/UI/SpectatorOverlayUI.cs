using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>ゴール済みプレイヤーの画面に、観戦対象を示す枠とラベルを重ねます。</summary>
[DisallowMultipleComponent]
public sealed class SpectatorOverlayUI : MonoBehaviour
{
    private static readonly Color FrameColor = new Color(0.08f, 0.82f, 1f, 0.96f);
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
        playerLabel.text = $"WATCHING  P{watchedPlayerIndex + 1}";
        gameObject.SetActive(true);
        transform.SetAsLastSibling();
    }

    public void Hide()
    {
        gameObject.SetActive(false);
    }

    private void Build(TMP_FontAsset font)
    {
        CreateBar("Top", new Vector2(0f, 0.976f), Vector2.one);
        CreateBar("Bottom", Vector2.zero, new Vector2(1f, 0.024f));
        CreateBar("Left", Vector2.zero, new Vector2(0.014f, 1f));
        CreateBar("Right", new Vector2(0.986f, 0f), Vector2.one);

        GameObject plate = GetOrCreate(
            "PlayerPlate", transform, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        Anchor(plate.GetComponent<RectTransform>(), new Vector2(0.035f, 0.875f), new Vector2(0.255f, 0.955f));
        Image plateImage = plate.GetComponent<Image>();
        plateImage.color = new Color(0.005f, 0.025f, 0.045f, 0.9f);
        plateImage.raycastTarget = false;

        GameObject labelObject = GetOrCreate(
            "PlayerLabel", plate.transform, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        Anchor(labelObject.GetComponent<RectTransform>(), new Vector2(0.08f, 0.08f), new Vector2(0.92f, 0.92f));
        playerLabel = labelObject.GetComponent<TMP_Text>();
        if (font != null) playerLabel.font = font;
        playerLabel.text = "WATCHING  P2";
        playerLabel.fontSize = 30f;
        playerLabel.enableAutoSizing = true;
        playerLabel.fontSizeMin = 14f;
        playerLabel.fontSizeMax = 30f;
        playerLabel.fontStyle = FontStyles.Bold | FontStyles.Italic;
        playerLabel.alignment = TextAlignmentOptions.Center;
        playerLabel.color = Color.white;
        playerLabel.raycastTarget = false;
    }

    private void CreateBar(string name, Vector2 anchorMin, Vector2 anchorMax)
    {
        GameObject bar = GetOrCreate(
            name, transform, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        Anchor(bar.GetComponent<RectTransform>(), anchorMin, anchorMax);
        Image image = bar.GetComponent<Image>();
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
