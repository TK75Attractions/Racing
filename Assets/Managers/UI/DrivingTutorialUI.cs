using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Neon control readouts over the live practice car, with an ordered lesson strip.</summary>
public sealed class DrivingTutorialUI
{
    private static readonly Color Pink = new Color(1f, 0.12f, 0.36f);
    private readonly GameObject root;
    private readonly TMP_Text angle, pedal, prompt, speed, status;
    private readonly RectTransform pedalFill, needle;
    private readonly Image progress;
    private readonly RacingPanelGraphic[] steps = new RacingPanelGraphic[3];
    private readonly TMP_Text[] captions = new TMP_Text[3];
    private readonly DrivingTutorialControlGraphic wheel;

    public DrivingTutorialUI(Transform canvas, int playerIndex)
    {
        RectTransform rect = RacingUITheme.Rect(canvas, "DrivingTutorial", Vector2.zero, Vector2.one);
        root = rect.gameObject;
        // The transition overlay remains the last sibling and can cover the tutorial during changes.
        rect.SetAsFirstSibling();
        RacingUITheme.Label(rect, "Brand", "TSUKUKOMA  /  CIRCUIT", new Vector2(.035f, .9f), new Vector2(.6f, .98f),
            38f, RacingUITheme.Text);
        RacingUITheme.Label(rect, "Player", $"PLAYER {playerIndex + 1}   •   PRACTICE", new Vector2(.66f, .91f), new Vector2(.965f, .97f),
            22f, RacingUITheme.Cyan, alignment: TextAlignmentOptions.Right);
        RectTransform steering = Panel(rect, "Steering", new Vector2(.035f, .34f), new Vector2(.315f, .74f), RacingUITheme.Cyan);
        wheel = Icon(steering, "Wheel", new Vector2(.04f, .73f), new Vector2(.26f, .96f), true, RacingUITheme.Cyan);
        Label(steering, "Title", "ハンドル", new Vector2(.32f, .79f), new Vector2(.94f, .95f), 36f, RacingUITheme.Text, true);
        Label(steering, "English", "STEERING", new Vector2(.32f, .69f), new Vector2(.94f, .79f), 19f, RacingUITheme.Cyan);
        angle = Label(steering, "Angle", "0°", new Vector2(.1f, .42f), new Vector2(.9f, .67f), 54f, RacingUITheme.Cyan);
        angle.alignment = TextAlignmentOptions.Center;
        // A semicircular gauge with a live needle and physical input angle ticks.
        for (int i = 0; i <= 12; i++)
        {
            float radians = Mathf.Lerp(160f, 20f, i / 12f) * Mathf.Deg2Rad;
            Vector2 point = new Vector2(.5f + Mathf.Cos(radians) * .37f, .12f + Mathf.Sin(radians) * .27f);
            RacingUITheme.Rule(steering, $"Tick{i}", point - new Vector2(.004f, .016f), point + new Vector2(.004f, .016f), RacingUITheme.Cyan);
        }
        needle = RacingUITheme.Rect(steering, "NeedlePivot", new Vector2(.5f, .12f), new Vector2(.5f, .12f));
        needle.sizeDelta = new Vector2(8f, 90f);
        needle.pivot = new Vector2(.5f, 0f);
        Image needleImage = needle.gameObject.AddComponent<Image>();
        needleImage.color = RacingUITheme.Cyan;
        needleImage.raycastTarget = false;
        Label(steering, "Left", "−90°", new Vector2(.05f, .01f), new Vector2(.3f, .12f), 22f, RacingUITheme.Cyan);
        Label(steering, "Right", "+90°", new Vector2(.7f, .01f), new Vector2(.95f, .12f), 22f, RacingUITheme.Cyan);

        RectTransform accelerator = Panel(rect, "Accelerator", new Vector2(.705f, .34f), new Vector2(.965f, .74f), Pink);
        Icon(accelerator, "PedalIcon", new Vector2(.04f, .73f), new Vector2(.26f, .96f), false, Pink);
        Label(accelerator, "Title", "アクセル", new Vector2(.32f, .79f), new Vector2(.94f, .95f), 36f, RacingUITheme.Text, true);
        Label(accelerator, "English", "ACCELERATOR", new Vector2(.32f, .69f), new Vector2(.97f, .79f), 19f, Pink);
        pedal = Label(accelerator, "Value", "0%", new Vector2(.07f, .22f), new Vector2(.64f, .58f), 64f, Pink);
        pedal.alignment = TextAlignmentOptions.Center;
        RacingUITheme.Rule(accelerator, "Meter", new Vector2(.68f, .1f), new Vector2(.83f, .64f), new Color(.2f, .04f, .09f, .9f));
        pedalFill = RacingUITheme.Rect(accelerator, "Fill", new Vector2(.685f, .105f), new Vector2(.825f, .105f));
        Image fillImage = pedalFill.gameObject.AddComponent<Image>();
        fillImage.color = Pink;
        fillImage.raycastTarget = false;
        Label(accelerator, "Full", "100%", new Vector2(.84f, .56f), new Vector2(.99f, .67f), 18f, RacingUITheme.Text);
        Label(accelerator, "Empty", "0%", new Vector2(.85f, .06f), new Vector2(.99f, .17f), 18f, RacingUITheme.Text);
        speed = Label(rect, "Speed", "0 km/h", new Vector2(.38f, .26f), new Vector2(.62f, .34f), 30f, RacingUITheme.Text);
        speed.alignment = TextAlignmentOptions.Center;
        prompt = Label(rect, "Prompt", "", new Vector2(.1f, .77f), new Vector2(.9f, .86f), 34f, RacingUITheme.Text, true);
        prompt.alignment = TextAlignmentOptions.Center;
        status = Label(rect, "Status", "", new Vector2(.16f, .215f), new Vector2(.84f, .27f), 23f, RacingUITheme.Cyan, true);
        status.alignment = TextAlignmentOptions.Center;
        string[] titles = { "ペダルを踏んで加速", "ハンドルを回して曲がる", "ペダルを離して停車" };
        for (int i = 0; i < 3; i++)
        {
            float x = .04f + i * .31f;
            RectTransform step = Panel(rect, $"Step{i + 1}", new Vector2(x, .055f), new Vector2(x + .30f, .205f), RacingUITheme.Cyan);
            steps[i] = step.Find("ModernSurface").GetComponent<RacingPanelGraphic>();
            Icon(step, "Icon", new Vector2(.04f, .12f), new Vector2(.23f, .83f), i == 1, i == 0 ? Pink : RacingUITheme.Cyan);
            Label(step, "Number", $"0{i + 1}", new Vector2(.28f, .61f), new Vector2(.92f, .9f), 19f, RacingUITheme.Muted);
            captions[i] = Label(step, "Caption", titles[i], new Vector2(.28f, .15f), new Vector2(.97f, .65f), 25f, RacingUITheme.Text, true);
        }
        RectTransform progressRect = RacingUITheme.Rect(rect, "Progress", new Vector2(.04f, .038f), new Vector2(.96f, .043f));
        progress = progressRect.gameObject.AddComponent<Image>();
        progress.color = RacingUITheme.Cyan;
        progress.raycastTarget = false;
    }

    public void Update(DrivingTutorialProgress lesson, DriveInputState input, float metersPerSecond, bool connected)
    {
        angle.text = $"{input.steering:0}°";
        needle.localRotation = Quaternion.Euler(0f, 0f, -Mathf.Clamp(input.steering, -90f, 90f));
        wheel.Angle = input.steering;
        float pressure = Mathf.Clamp01(input.pedal);
        pedal.text = $"{pressure * 100f:0}%";
        pedalFill.anchorMax = new Vector2(.825f, .105f + .53f * pressure);
        speed.text = $"{metersPerSecond * 3.6f:0} km/h";
        int stage = (int)lesson.CurrentStage;
        string[] prompts = { lesson.PedalReleased ? "ペダルを踏んで加速しよう" : "まずペダルから足を離そう",
            "ペダルを踏みながらハンドルを回そう", "ペダルから足を離して停車しよう", "練習完了！ もう一人のプレイヤーを待っています" };
        prompt.text = connected || lesson.IsComplete ? prompts[stage] : "コントローラーの接続を確認してください";
        status.text = lesson.IsComplete ? "両プレイヤーの練習完了後にレースが始まります" : $"STEP {stage + 1} / 3  •  自分専用の練習コース";
        for (int i = 0; i < 3; i++)
        {
            steps[i].SetState(stage == i ? 1f : .0f, 0f, 0f, i < stage ? RacingUITheme.Gold : RacingUITheme.Cyan);
            captions[i].color = i < stage ? RacingUITheme.Gold : i == stage ? RacingUITheme.Text : RacingUITheme.Muted;
        }
        progress.rectTransform.anchorMax = new Vector2(.04f + .92f * Mathf.Clamp01((stage + lesson.HoldProgress) / 3f), .043f);
    }

    public void Dispose() { if (root != null) Object.Destroy(root); }

    private static RectTransform Panel(Transform parent, string name, Vector2 min, Vector2 max, Color tint)
    {
        RectTransform rect = RacingUITheme.Rect(parent, name, min, max);
        RacingPanelGraphic surface = RacingUITheme.Surface(rect, RacingPanelGraphic.SurfaceStyle.Secondary);
        surface.SetState(1f, 0f, 0f, tint);
        return rect;
    }

    private static TMP_Text Label(Transform parent, string name, string text, Vector2 min, Vector2 max, float size, Color color, bool japanese = false)
        => RacingUITheme.Label(parent, name, text, min, max, size, color, japanese ? FontRole.Japanese : FontRole.English);

    private static DrivingTutorialControlGraphic Icon(Transform parent, string name, Vector2 min, Vector2 max, bool steering, Color color)
    {
        RectTransform rect = RacingUITheme.Rect(parent, name, min, max);
        DrivingTutorialControlGraphic graphic = rect.gameObject.AddComponent<DrivingTutorialControlGraphic>();
        graphic.Configure(steering, color);
        return graphic;
    }
}
