using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Shared palette, typography and native-resolution UI construction.</summary>
public static class RacingUITheme
{
    public static readonly Color Cyan = new Color(0.20f, 0.82f, 1f, 1f);
    public static readonly Color Gold = new Color(1f, 0.84f, 0.24f, 1f);
    public static readonly Color Text = new Color(0.93f, 0.96f, 0.99f, 1f);
    public static readonly Color Muted = new Color(0.57f, 0.67f, 0.76f, 1f);

    public static RectTransform Rect(Transform parent, string name, Vector2 min, Vector2 max)
    {
        Transform existing = parent.Find(name);
        RectTransform rect = existing != null ? existing.GetComponent<RectTransform>()
            : new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.gameObject.layer = parent.gameObject.layer;
        rect.SetParent(parent, false);
        rect.anchorMin = min;
        rect.anchorMax = max;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        rect.localScale = Vector3.one;
        return rect;
    }

    public static RacingPanelGraphic Surface(Transform target, RacingPanelGraphic.SurfaceStyle style = RacingPanelGraphic.SurfaceStyle.Panel)
    {
        Image legacy = target.GetComponent<Image>();
        if (legacy != null) legacy.enabled = false;
        Outline outline = target.GetComponent<Outline>();
        if (outline != null) outline.enabled = false;
        RectTransform face = Rect(target, "ModernSurface", Vector2.zero, Vector2.one);
        face.SetAsFirstSibling();
        RacingPanelGraphic graphic = face.GetComponent<RacingPanelGraphic>();
        if (graphic == null) graphic = face.gameObject.AddComponent<RacingPanelGraphic>();
        graphic.Configure(style, Cyan);
        return graphic;
    }

    public static TMP_Text Label(Transform parent, string name, string value, Vector2 min, Vector2 max,
        float size, Color tint, FontRole role = FontRole.English, TextAlignmentOptions alignment = TextAlignmentOptions.Left)
    {
        RectTransform rect = Rect(parent, name, min, max);
        TMP_Text label = rect.GetComponent<TMP_Text>();
        if (label == null) label = rect.gameObject.AddComponent<TextMeshProUGUI>();
        ApplyTypography(label, role, size);
        label.text = value;
        label.color = tint;
        label.alignment = alignment;
        return label;
    }

    public static void ApplyTypography(TMP_Text label, FontRole role, float size)
    {
        TMP_FontAsset font = RacingUIFontCatalog.Get(role);
        if (font != null) label.font = font;
        if (role == FontRole.Japanese)
        {
            Material material = RacingUIFontCatalog.GetJapaneseUIMaterial();
            if (material != null) label.fontSharedMaterial = material;
        }
        label.enableAutoSizing = true;
        label.fontSize = size;
        label.fontSizeMin = Mathf.Min(size, Mathf.Max(12f, size * 0.65f));
        label.fontSizeMax = size;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.overflowMode = TextOverflowModes.Ellipsis;
        label.extraPadding = true;
        label.raycastTarget = false;
        label.fontStyle = FontStyles.Normal;
    }

    public static void Rule(Transform parent, string name, Vector2 min, Vector2 max, Color tint)
    {
        RectTransform rect = Rect(parent, name, min, max);
        Image line = rect.GetComponent<Image>();
        if (line == null) line = rect.gameObject.AddComponent<Image>();
        line.color = tint;
        line.raycastTarget = false;
    }
}
