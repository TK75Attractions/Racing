using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Shared countdown display for the two preparation screens.</summary>
public sealed class PreparationTimerUI
{
    private readonly TMP_Text value;
    private readonly RectTransform fill;
    private readonly Image fillImage;
    private int shownSeconds = -1;

    public PreparationTimerUI(Transform parent, Vector2 min, Vector2 max, bool tutorial = false)
    {
        RectTransform panel = NeonUI.Panel(parent, "PreparationTimer", min, max);
        TMP_Text caption = Label(panel, "Caption", "TIME LEFT", new Vector2(.08f, .64f), new Vector2(.92f, .91f), 15f, tutorial);
        caption.characterSpacing = 2f;
        value = Label(panel, "Time", "", new Vector2(.05f, .13f), new Vector2(.95f, .67f), 42f, tutorial);
        RectTransform track = RacingUITheme.Rect(panel, "Track", new Vector2(.08f, .07f), new Vector2(.92f, .11f));
        Image background = track.gameObject.AddComponent<Image>();
        background.color = new Color(.08f, .16f, .25f); background.raycastTarget = false;
        fill = RacingUITheme.Rect(track, "Remaining", Vector2.zero, Vector2.one);
        fillImage = fill.gameObject.AddComponent<Image>(); fillImage.raycastTarget = false;
    }

    public void Update(float remainingSeconds, float durationSeconds)
    {
        int seconds = Mathf.CeilToInt(Mathf.Max(0f, remainingSeconds));
        if (shownSeconds != seconds)
        {
            value.text = $"{seconds / 60:00}:{seconds % 60:00}";
            shownSeconds = seconds;
        }
        Color tint = remainingSeconds <= 10f ? NeonUI.Pink : NeonUI.Cyan;
        value.color = fillImage.color = tint;
        fill.anchorMax = new Vector2(durationSeconds > 0f ? Mathf.Clamp01(remainingSeconds / durationSeconds) : 0f, 1f);
    }

    private static TMP_Text Label(Transform parent, string name, string text, Vector2 min, Vector2 max, float size, bool tutorial)
    {
        TMP_Text label = tutorial
            ? DrivingTutorialTypography.Label(parent, name, text, min, max, size, Color.white)
            : NeonUI.Text(parent, name, text, min, max, size);
        if (!tutorial)
        {
            TMP_FontAsset font = Resources.Load<TMP_FontAsset>("UI/CarSelectionFont");
            if (font != null) { label.font = font; label.fontSharedMaterial = font.material; }
        }
        label.alignment = TextAlignmentOptions.Center;
        return label;
    }
}
