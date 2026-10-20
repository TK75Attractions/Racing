using TMPro;
using UnityEngine;

/// <summary>Race-only palette and bilingual typography, at a 1920 x 1080 reference size.</summary>
public static class RacingHUDStyle
{
    // Reference pixels. Japanese : English : number : unit = 1 : .60 : 2.8 : .65.
    public const float JapaneseSize = 24f;
    public const float EnglishRatio = .60f, NumberRatio = 2.8f, UnitRatio = .65f;
    public const float CaptionSize = JapaneseSize * EnglishRatio;
    public const float NumberSize = JapaneseSize * NumberRatio;
    public const float PositionNumberSize = NumberSize * 1.6f;
    public const float SpeedNumberSize = NumberSize * 1.4f;
    public const float SmallNumberSize = NumberSize * .60f;
    public const float FractionSize = SmallNumberSize * .65f;
    public const float UnitSize = JapaneseSize * UnitRatio;
    public const float SecondarySize = 18f, NoticeSize = 32f;
    public const float SecondaryOpacity = .68f;
    public const FontStyles TextStyle = FontStyles.Normal;
    // Slant is reserved for the primary speed and position numbers.
    public const FontStyles PrimaryNumberStyle = FontStyles.Italic;
    public static readonly Color Text = new Color(.96f, .97f, .94f, 1f);
    public static readonly Color Muted = new Color(.69f, .76f, .77f, 1f);
    public static readonly Color Teal = NeonUI.Cyan;
    public static readonly Color Amber = NeonUI.Pink;
    public static readonly Color Alert = new Color(.96f, .47f, .38f, 1f);

    public static RectTransform Plate(Transform parent, string name, Vector2 min, Vector2 max,
        RacingHUDPlateGraphic.PlateShape shape, Color accent)
    {
        RectTransform rect = RacingUITheme.Rect(parent, name, min, max);
        Surface(rect, shape, accent);
        return rect;
    }

    public static void Surface(Transform target, RacingHUDPlateGraphic.PlateShape shape, Color accent)
    {
        UnityEngine.UI.Image image = target.GetComponent<UnityEngine.UI.Image>();
        if (image != null) image.enabled = false;
        UnityEngine.UI.Outline outline = target.GetComponent<UnityEngine.UI.Outline>();
        if (outline != null) outline.enabled = false;
        RectTransform face = RacingUITheme.Rect(target, "ModernSurface", Vector2.zero, Vector2.one);
        face.SetAsFirstSibling();
        RacingPanelGraphic oldFace = face.GetComponent<RacingPanelGraphic>();
        if (oldFace != null) oldFace.enabled = false;
        RacingHUDPlateGraphic graphic = face.GetComponent<RacingHUDPlateGraphic>();
        if (graphic == null) graphic = face.gameObject.AddComponent<RacingHUDPlateGraphic>();
        graphic.Configure(shape, accent);
    }

    public static TMP_Text Label(Transform parent, string name, string text,
        float x0, float y0, float x1, float y1, float size, Color tint,
        TextAlignmentOptions alignment = TextAlignmentOptions.Left, bool bold = false)
    {
        // Static medium/bold masters keep Japanese copy and large digits readable at 720p.
        TMP_Text label = RacingUITheme.Label(parent, name, text, new Vector2(x0, y0), new Vector2(x1, y1),
            size, tint, FontRole.Japanese, alignment);
        TMP_FontAsset font = RacingUIFontCatalog.GetHUDForText(text, bold);
        if (font != null)
        {
            label.font = font;
            label.fontSharedMaterial = font == RacingUIFontCatalog.Get(FontRole.Japanese)
                ? RacingUIFontCatalog.GetJapaneseUIMaterial() : font.material;
        }
        label.fontStyle = TextStyle;
        label.characterSpacing = 0f;
        return label;
    }

    public static void Heading(Transform parent, string japanese, string english,
        float x0, float y0, float x1, float y1, float size = JapaneseSize)
    {
        float split = Mathf.Lerp(y0, y1, .42f);
        Label(parent, "Heading", japanese, x0, split, x1, y1, size, Text, bold: true);
        TMP_Text caption = Label(parent, "EnglishHeading", english, x0, y0, x1, split, size * EnglishRatio, Muted);
        caption.characterSpacing = 1.8f;
    }
    public static void Secondary(Transform target)
    {
        CanvasGroup group = target.GetComponent<CanvasGroup>() ?? target.gameObject.AddComponent<CanvasGroup>();
        group.alpha = SecondaryOpacity;
        group.blocksRaycasts = false;
    }
    public static void SetText(TMP_Text label, string text, bool bold = false)
    {
        TMP_FontAsset font = RacingUIFontCatalog.GetHUDForText(text, bold);
        if (font != null && label.font != font)
        {
            label.font = font;
            label.fontSharedMaterial = font == RacingUIFontCatalog.Get(FontRole.Japanese)
                ? RacingUIFontCatalog.GetJapaneseUIMaterial() : font.material;
        }
        label.text = text;
    }
}
