using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

/// <summary>Reference artwork palette, chamfered controls and bilingual menu typography.</summary>
public static class NeonUI
{
    public static readonly Color Pink = new Color(1f, .025f, .39f, 1f);
    public static readonly Color Cyan = new Color(.015f, .78f, 1f, 1f);
    public static readonly Color Violet = new Color(.47f, .29f, 1f, 1f);
    public static readonly Color Red = new Color(1f, .045f, .21f, 1f);

    public static RectTransform Panel(Transform parent, string name, Vector2 min, Vector2 max, bool primary = false)
    {
        RectTransform r = RacingUITheme.Rect(parent, name, min, max);
        RacingUITheme.Surface(r, primary ? RacingPanelGraphic.SurfaceStyle.Primary : RacingPanelGraphic.SurfaceStyle.Panel);
        return r;
    }

    public static TMP_Text Text(Transform parent, string name, string value, Vector2 min, Vector2 max, float size,
        TextAlignmentOptions alignment = TextAlignmentOptions.Left, bool italic = false, Color? tint = null)
    {
        TMP_Text text = RacingHUDStyle.Label(parent, name, value, min.x, min.y, max.x, max.y, size,
            tint ?? Color.white, alignment, true);
        if (italic) text.fontStyle = FontStyles.Italic;
        return text;
    }

    public static void GlowText(TMP_Text text, Color tint)
    {
        Material material = text.fontMaterial;
        material.EnableKeyword("GLOW_ON");
        if (material.HasProperty("_GlowColor"))
        {
            material.SetColor("_GlowColor", tint);
            material.SetFloat("_GlowOuter", .16f);
            material.SetFloat("_GlowPower", .55f);
        }
        if (material.HasProperty("_OutlineColor"))
        {
            material.SetColor("_OutlineColor", tint);
            material.SetFloat("_OutlineWidth", .035f);
        }
        text.UpdateMeshPadding();
    }

    public static RacingIconGraphic Icon(Transform parent, string name, RacingIconGraphic.Icon symbol, Vector2 min, Vector2 max)
    {
        RectTransform r = RacingUITheme.Rect(parent, name, min, max);
        RacingIconGraphic icon = r.GetComponent<RacingIconGraphic>() ?? r.gameObject.AddComponent<RacingIconGraphic>();
        icon.symbol = symbol; icon.color = new Color(.86f, .93f, 1f); icon.raycastTarget = false;
        return icon;
    }

    public static RacingMenuButton Button(Transform parent, string name, string japanese, string english,
        RacingIconGraphic.Icon icon, Vector2 min, Vector2 max, bool primary, UnityAction action)
    {
        RectTransform r = Panel(parent, name, min, max, primary);
        RacingUITheme.Surface(r, primary ? RacingPanelGraphic.SurfaceStyle.Primary : RacingPanelGraphic.SurfaceStyle.Secondary);
        Image hit = r.GetComponent<Image>() ?? r.gameObject.AddComponent<Image>();
        hit.enabled = true; hit.color = Color.clear; hit.raycastTarget = true;
        RacingMenuButton button = r.GetComponent<RacingMenuButton>() ?? r.gameObject.AddComponent<RacingMenuButton>();
        button.targetGraphic = hit;
        button.Configure(r.Find("ModernSurface").GetComponent<RacingPanelGraphic>());
        button.onClick.RemoveAllListeners();
        if (action != null) button.onClick.AddListener(action);
        Icon(r, "Icon", icon, new Vector2(.085f, .23f), new Vector2(.24f, .79f));
        Icon(r, "Chevron", primary ? RacingIconGraphic.Icon.DoubleChevron : RacingIconGraphic.Icon.Chevron, new Vector2(.875f, .26f), new Vector2(.96f, .74f));
        Text(r, "Label", japanese, new Vector2(.28f, .30f), new Vector2(.88f, .84f), 34f);
        Text(r, "Caption", english, new Vector2(.285f, .095f), new Vector2(.87f, .32f), 13f,
            tint: new Color(.76f, .84f, .96f));
        button.GetComponent<PedalButtonFeedback>().CacheContent();
        return button;
    }

    public static void Background(Transform parent, string name, string resource)
    {
        RectTransform r = RacingUITheme.Rect(parent, name, Vector2.zero, Vector2.one);
        r.SetAsFirstSibling();
        RawImage image = r.GetComponent<RawImage>() ?? r.gameObject.AddComponent<RawImage>();
        image.texture = Resources.Load<Texture2D>(resource); image.raycastTarget = false;
        // Fill the viewport without stretching the cars or logo on other aspect ratios.
        AspectRatioFitter fit = r.GetComponent<AspectRatioFitter>() ?? r.gameObject.AddComponent<AspectRatioFitter>();
        fit.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
        fit.aspectRatio = image.texture != null ? (float)image.texture.width / image.texture.height : 16f / 9f;
    }
}
