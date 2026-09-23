using TMPro;
using UnityEngine;

/// <summary>Replaces enlarged HUD bitmaps with live type and vector instruments.</summary>
public static class RacingHUDBuilder
{
    public static Transform Build(Transform parent, int playerIndex, int totalLaps)
    {
        foreach (string name in new[] { "Position", "Lap", "Time", "Speed" })
        {
            Transform legacy = parent.Find(name);
            if (legacy != null) legacy.gameObject.SetActive(false);
        }
        RectTransform root = RacingUITheme.Rect(parent, "ModernHUD", Vector2.zero, Vector2.one);
        root.SetAsFirstSibling();
        RectTransform position = Panel(root, "Position", new Vector2(0.025f, 0.07f), new Vector2(0.145f, 0.23f));
        Label(position, "Heading", "POSITION", 0.12f, 0.70f, 0.88f, 0.91f, 17f, RacingUITheme.Muted);
        Label(position, "Txt", "1", 0.12f, 0.12f, 0.53f, 0.73f, 72f, RacingUITheme.Text);
        Label(position, "Field", "/ 2", 0.56f, 0.17f, 0.88f, 0.51f, 24f, RacingUITheme.Muted);
        RacingUITheme.Rule(position, "Accent", new Vector2(0.12f, 0.045f), new Vector2(0.45f, 0.06f), RacingUITheme.Cyan);

        RectTransform lap = Panel(root, "Lap", new Vector2(0.155f, 0.07f), new Vector2(0.265f, 0.23f));
        Label(lap, "Heading", "LAP", 0.12f, 0.70f, 0.88f, 0.91f, 17f, RacingUITheme.Muted);
        Label(lap, "Txt", "1", 0.12f, 0.12f, 0.53f, 0.73f, 72f, RacingUITheme.Text);
        Label(lap, "Total", $"/ {totalLaps}", 0.56f, 0.17f, 0.9f, 0.51f, 24f, RacingUITheme.Muted);

        RectTransform time = Panel(root, "Time", new Vector2(0.73f, 0.77f), new Vector2(0.975f, 0.95f));
        Label(time, "Heading", "RACE TIME", 0.075f, 0.74f, 0.925f, 0.93f, 17f, RacingUITheme.Muted);
        Label(time, "Total", "00:00", 0.075f, 0.32f, 0.62f, 0.76f, 48f, RacingUITheme.Text);
        Label(time, "TotalFraction", ".00", 0.64f, 0.33f, 0.92f, 0.70f, 32f, RacingUITheme.Cyan);
        RacingUITheme.Rule(time, "Divider", new Vector2(0.075f, 0.29f), new Vector2(0.925f, 0.296f), new Color(0.3f, 0.45f, 0.55f, 0.4f));
        Label(time, "LapHeading", "LAP TIME", 0.075f, 0.055f, 0.43f, 0.255f, 15f, RacingUITheme.Muted);
        Label(time, "Lap", "00:00", 0.47f, 0.04f, 0.74f, 0.255f, 20f, RacingUITheme.Text);
        Label(time, "LapFraction", ".00", 0.75f, 0.04f, 0.925f, 0.255f, 19f, RacingUITheme.Muted);

        RectTransform speed = Panel(root, "Speed", new Vector2(0.79f, 0.06f), new Vector2(0.975f, 0.32f));
        RectTransform gauge = RacingUITheme.Rect(speed, "Gauge", new Vector2(0.08f, 0.18f), new Vector2(0.92f, 0.93f));
        if (gauge.GetComponent<RacingSpeedGauge>() == null) gauge.gameObject.AddComponent<RacingSpeedGauge>();
        Label(speed, "Txt", "0", 0.12f, 0.21f, 0.88f, 0.61f, 75f, RacingUITheme.Text, FontRole.InstrumentDigits, TextAlignmentOptions.Center);
        Label(speed, "Unit", "KM/H", 0.2f, 0.095f, 0.8f, 0.24f, 17f, RacingUITheme.Cyan, FontRole.English, TextAlignmentOptions.Center);
        Label(root, "Player", $"P{playerIndex + 1}  /  TSUKUKOMA CIRCUIT", 0.026f, 0.026f, 0.35f, 0.052f, 17f, RacingUITheme.Text);
        return root;
    }

    private static RectTransform Panel(Transform parent, string name, Vector2 min, Vector2 max)
    {
        RectTransform rect = RacingUITheme.Rect(parent, name, min, max);
        RacingUITheme.Surface(rect);
        return rect;
    }

    private static void Label(Transform parent, string name, string text, float x0, float y0, float x1, float y1,
        float size, Color color, FontRole role = FontRole.English, TextAlignmentOptions alignment = TextAlignmentOptions.Left)
    {
        RacingUITheme.Label(parent, name, text, new Vector2(x0, y0), new Vector2(x1, y1), size, color, role, alignment);
    }
}
