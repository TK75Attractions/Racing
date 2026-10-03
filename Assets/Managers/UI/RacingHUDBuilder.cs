using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Builds the bilingual race instruments. Node names are the live race-state bindings.</summary>
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
        RectTransform overlay = RacingUITheme.Rect(root, "BoostOverlay", Vector2.zero, Vector2.one);
        overlay.SetAsFirstSibling();
        RacingBoostGraphic screen = overlay.GetComponent<RacingBoostGraphic>() ?? overlay.gameObject.AddComponent<RacingBoostGraphic>();
        screen.Configure(RacingBoostGraphic.DisplayMode.Screen);
        overlay.gameObject.SetActive(false);

        RectTransform position = RacingHUDStyle.Plate(root, "Position", new Vector2(.025f, .065f), new Vector2(.143f, .285f),
            RacingHUDPlateGraphic.PlateShape.Position, RacingHUDStyle.Teal);
        RacingHUDStyle.Heading(position, "順位", "POSITION", .13f, .69f, .86f, .91f, 26f);
        RacingHUDStyle.Label(position, "Txt", "1", .12f, .17f, .60f, .69f, 112f, RacingHUDStyle.Text, bold: true);
        RacingHUDStyle.Label(position, "PlaceUnit", "位", .61f, .23f, .87f, .45f, 28f, RacingHUDStyle.Text, bold: true);
        RacingHUDStyle.Label(position, "Field", "/ 2人", .14f, .045f, .80f, .19f, 20f, RacingHUDStyle.Muted);
        Hide(position, "Accent");

        RectTransform lap = RacingHUDStyle.Plate(root, "Lap", new Vector2(.151f, .065f), new Vector2(.281f, .245f),
            RacingHUDPlateGraphic.PlateShape.Lap, RacingHUDStyle.Amber);
        RacingHUDStyle.Heading(lap, "いまの周", "LAP", .11f, .62f, .88f, .89f);
        RacingHUDStyle.Label(lap, "Txt", "1", .10f, .09f, .43f, .62f, 78f, RacingHUDStyle.Text, bold: true);
        RacingHUDStyle.Label(lap, "Total", $"/ {Mathf.Max(1, totalLaps)}", .44f, .14f, .74f, .47f, 34f, RacingHUDStyle.Muted);
        RacingHUDStyle.Label(lap, "LapUnit", "周", .77f, .15f, .94f, .41f, 24f, RacingHUDStyle.Text);

        RectTransform time = RacingHUDStyle.Plate(root, "Time", new Vector2(.755f, .775f), new Vector2(.975f, .955f),
            RacingHUDPlateGraphic.PlateShape.Timer, RacingHUDStyle.Teal);
        RacingHUDStyle.Heading(time, "走行タイム", "RACE TIME", .09f, .65f, .92f, .91f);
        RacingHUDStyle.Label(time, "Total", "00:00", .09f, .30f, .56f, .67f, 53f, RacingHUDStyle.Text, bold: true);
        RacingHUDStyle.Label(time, "TotalFraction", ".00", .57f, .33f, .92f, .61f, 32f, RacingHUDStyle.Teal, bold: true);
        RacingUITheme.Rule(time, "Divider", new Vector2(.09f, .28f), new Vector2(.91f, .284f), new Color(.70f, .80f, .79f, .19f));
        RacingHUDStyle.Label(time, "LapHeading", "この周", .09f, .035f, .29f, .23f, 20f, RacingHUDStyle.Muted);
        RacingHUDStyle.Label(time, "LapEnglish", "LAP", .29f, .04f, .42f, .23f, 12f, RacingHUDStyle.Muted);
        RacingHUDStyle.Label(time, "Lap", "00:00", .46f, .03f, .75f, .24f, 25f, RacingHUDStyle.Text);
        RacingHUDStyle.Label(time, "LapFraction", ".00", .76f, .03f, .91f, .24f, 20f, RacingHUDStyle.Muted);

        RectTransform speed = RacingHUDStyle.Plate(root, "Speed", new Vector2(.785f, .04f), new Vector2(.975f, .335f),
            RacingHUDPlateGraphic.PlateShape.Speed, RacingHUDStyle.Teal);
        RectTransform gauge = RacingUITheme.Rect(speed, "Gauge", Vector2.zero, Vector2.one);
        if (gauge.GetComponent<RacingSpeedGauge>() == null) gauge.gameObject.AddComponent<RacingSpeedGauge>();
        RacingHUDStyle.Label(speed, "Heading", "スピード", .24f, .64f, .76f, .74f, 23f, RacingHUDStyle.Muted, TextAlignmentOptions.Center);
        RacingHUDStyle.Label(speed, "EnglishHeading", "SPEED", .24f, .575f, .76f, .65f, 13f, RacingHUDStyle.Muted, TextAlignmentOptions.Center);
        RacingHUDStyle.Label(speed, "Txt", "0", .14f, .26f, .86f, .58f, 106f, RacingHUDStyle.Text, TextAlignmentOptions.Center, bold: true);
        RacingHUDStyle.Label(speed, "Unit", "km/h", .30f, .17f, .70f, .28f, 21f, RacingHUDStyle.Teal, TextAlignmentOptions.Center);
        RacingHUDStyle.Label(speed, "Minimum", "0", .10f, .17f, .24f, .26f, 13f, RacingHUDStyle.Muted, TextAlignmentOptions.Center);
        RacingHUDStyle.Label(speed, "Maximum", "180", .76f, .17f, .90f, .26f, 13f, RacingHUDStyle.Muted, TextAlignmentOptions.Center);

        RectTransform boost = RacingHUDStyle.Plate(root, "PadBoost", new Vector2(.755f, .355f), new Vector2(.975f, .465f),
            RacingHUDPlateGraphic.PlateShape.Boost, RacingHUDStyle.Amber);
        RacingHUDStyle.Label(boost, "Heading", "加速中", .07f, .50f, .36f, .88f, 24f, RacingHUDStyle.Amber, bold: true);
        RacingHUDStyle.Label(boost, "EnglishHeading", "BOOST", .38f, .53f, .65f, .86f, 14f, RacingHUDStyle.Muted);
        RacingHUDStyle.Label(boost, "Remaining", "3.0秒", .69f, .50f, .93f, .88f, 24f, RacingHUDStyle.Text, TextAlignmentOptions.Right);
        RacingUITheme.Rule(boost, "Track", new Vector2(.07f, .18f), new Vector2(.93f, .35f), new Color(.22f, .26f, .27f, 1f));
        RectTransform glow = RacingUITheme.Rect(boost, "Glow", new Vector2(.065f, .14f), new Vector2(.935f, .39f));
        Image glowImage = glow.GetComponent<Image>() ?? glow.gameObject.AddComponent<Image>();
        glowImage.color = new Color(.96f, .70f, .38f, .12f);
        glowImage.raycastTarget = false;
        RectTransform fill = RacingUITheme.Rect(boost, "Fill", new Vector2(.07f, .18f), new Vector2(.93f, .35f));
        RacingBoostGraphic bar = fill.GetComponent<RacingBoostGraphic>() ?? fill.gameObject.AddComponent<RacingBoostGraphic>();
        bar.Configure(RacingBoostGraphic.DisplayMode.Bar);
        boost.gameObject.SetActive(false);

        RacingHUDStyle.Label(root, "Player", $"プレイヤー {playerIndex + 1}  /  PLAYER {playerIndex + 1}",
            .027f, .022f, .30f, .052f, 18f, RacingHUDStyle.Text);
        return root;
    }

    private static void Hide(Transform parent, string name)
    {
        Transform old = parent.Find(name);
        if (old != null) old.gameObject.SetActive(false);
    }
}
