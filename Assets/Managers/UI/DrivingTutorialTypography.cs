using TMPro;
using UnityEngine;

/// <summary>Practice uses a real heavy font master rather than dilating the thin variable-font atlas.</summary>
public static class DrivingTutorialTypography
{
    private static TMP_FontAsset font;
    private static Material face, cyanGlow, pinkGlow;

    public static TMP_Text Label(Transform parent, string name, string text, Vector2 min, Vector2 max,
        float size, Color tint, bool italic = false, bool glow = false)
    {
        RectTransform rect = RacingUITheme.Rect(parent, name, min, max);
        TMP_Text label = rect.gameObject.AddComponent<TextMeshProUGUI>();
        EnsureFont();
        label.font = font;
        label.fontSharedMaterial = glow ? (tint.r > .8f && tint.g < .4f ? pinkGlow : cyanGlow) : face;
        label.fontStyle = italic ? FontStyles.Italic : FontStyles.Normal;
        label.fontSize = size;
        label.enableAutoSizing = false;
        label.color = tint;
        label.text = text;
        label.alignment = TextAlignmentOptions.MidlineLeft;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.overflowMode = TextOverflowModes.Overflow;
        label.extraPadding = true;
        label.raycastTarget = false;
        return label;
    }

    public static void SetPinkGlow(TMP_Text label) { EnsureFont(); label.fontSharedMaterial = pinkGlow; }

    private static void EnsureFont()
    {
        if (font != null) return;
        font = Resources.Load<TMP_FontAsset>("UI/HUD/Tutorial ExtraBold");
        if (font == null)
            throw new System.InvalidOperationException("Build the static tutorial font using Racing/UI/Build HUD Fonts.");
        face = new Material(font.material) { name = "Tutorial bold face" };
        face.EnableKeyword("UNDERLAY_ON");
        face.SetColor(ShaderUtilities.ID_UnderlayColor, new Color(0f, .01f, .025f, .9f));
        face.SetFloat(ShaderUtilities.ID_UnderlayOffsetY, -.6f);
        face.SetFloat(ShaderUtilities.ID_UnderlayDilate, .15f);
        face.SetFloat(ShaderUtilities.ID_UnderlaySoftness, .12f);
        cyanGlow = Glow(face, new Color(.02f, .55f, 1f, .6f));
        pinkGlow = Glow(face, new Color(1f, .015f, .18f, .6f));
    }

    private static Material Glow(Material original, Color tint)
    {
        Material material = new Material(original);
        material.SetColor(ShaderUtilities.ID_UnderlayColor, tint);
        material.SetFloat(ShaderUtilities.ID_UnderlayDilate, .25f);
        material.SetFloat(ShaderUtilities.ID_UnderlaySoftness, .75f);
        return material;
    }
}
