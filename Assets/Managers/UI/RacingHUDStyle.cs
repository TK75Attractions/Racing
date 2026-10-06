using TMPro;
using UnityEngine;

/// <summary>Race-only palette and bilingual typography, at a 1920 x 1080 reference size.</summary>
public static class RacingHUDStyle
{
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
        TMP_FontAsset font = RacingUIFontCatalog.GetHUD(bold);
        if (font != null)
        {
            label.font = font;
            label.fontSharedMaterial = font.material;
        }
        label.fontStyle = FontStyles.Normal;
        label.characterSpacing = 0f;
        return label;
    }

    public static void Heading(Transform parent, string japanese, string english,
        float x0, float y0, float x1, float y1, float size = 24f)
    {
        float split = Mathf.Lerp(y0, y1, .42f);
        Label(parent, "Heading", japanese, x0, split, x1, y1, size, Text, bold: true);
        TMP_Text caption = Label(parent, "EnglishHeading", english, x0, y0, x1, split, 14f, Muted);
        caption.characterSpacing = 1.8f;
    }
}
