using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Reference composition: live car between cyan/pink instrument glass, with three bright lessons below.</summary>
public sealed class DrivingTutorialUI
{
    public static readonly Color Cyan = new Color(.05f, .69f, 1f);
    public static readonly Color Pink = new Color(1f, .10f, .31f);
    private readonly GameObject root;
    private Material logoMaterial;
    private readonly TMP_Text angle, pedal, message, status;
    private readonly DrivingTutorialGaugeGraphic gauge;
    private readonly DrivingTutorialPedalMeterGraphic meter;
    private readonly DrivingTutorialPanelGraphic[] steps = new DrivingTutorialPanelGraphic[3];
    private readonly TMP_Text[] stepNumbers = new TMP_Text[3];
    private readonly DrivingTutorialControlGraphic wheel;

    public DrivingTutorialUI(Transform canvas, int playerIndex)
    {
        RectTransform rect = RacingUITheme.Rect(canvas, "DrivingTutorial", Vector2.zero, Vector2.one);
        root = rect.gameObject;
        rect.SetAsFirstSibling();
        BuildBrand(rect);
        Label(rect, "Player", $"PLAYER {playerIndex + 1} / PRACTICE", new Vector2(.73f, .905f), new Vector2(.965f, .95f), 22f, Color.white).alignment = TextAlignmentOptions.Right;

        RectTransform steering = Panel(rect, "Steering", new Vector2(.028f, .33f), new Vector2(.329f, .757f), Cyan);
        wheel = Icon(steering, "Wheel", new Vector2(.065f, .72f), new Vector2(.29f, .975f), DrivingTutorialControlGraphic.Kind.WheelHeader);
        Label(steering, "Title", "ハンドル", new Vector2(.347f, .83f), new Vector2(.97f, .96f), 43f, Color.white);
        Label(steering, "English", "STEERING", new Vector2(.35f, .73f), new Vector2(.96f, .82f), 25f, Cyan, true).characterSpacing = 5f;
        RectTransform steeringGlass = Panel(steering, "Instrument", new Vector2(.025f, .035f), new Vector2(.975f, .705f), new Color(.05f, .38f, .6f), true);
        angle = Label(steeringGlass, "Angle", "0°", new Vector2(.05f, .70f), new Vector2(.95f, .98f), 68f, Color.white, false, true);
        angle.alignment = TextAlignmentOptions.Center;
        RectTransform gaugeRect = RacingUITheme.Rect(steeringGlass, "Arc", new Vector2(.075f, .18f), new Vector2(.925f, .71f));
        gauge = gaugeRect.gameObject.AddComponent<DrivingTutorialGaugeGraphic>(); gauge.raycastTarget = false;
        Label(steeringGlass, "Left", "−90°", new Vector2(.07f, .085f), new Vector2(.32f, .225f), 29f, Cyan);
        Label(steeringGlass, "Center", "0°", new Vector2(.37f, .19f), new Vector2(.63f, .32f), 30f, Cyan).alignment = TextAlignmentOptions.Center;
        Label(steeringGlass, "Right", "+90°", new Vector2(.69f, .085f), new Vector2(.94f, .225f), 29f, Cyan).alignment = TextAlignmentOptions.Right;

        RectTransform accelerator = Panel(rect, "Accelerator", new Vector2(.687f, .325f), new Vector2(.972f, .757f), Pink);
        Icon(accelerator, "PedalIcon", new Vector2(.08f, .735f), new Vector2(.26f, .97f), DrivingTutorialControlGraphic.Kind.PedalHeader);
        Label(accelerator, "Title", "アクセル", new Vector2(.345f, .83f), new Vector2(.975f, .96f), 43f, Color.white);
        Label(accelerator, "English", "ACCELERATOR", new Vector2(.345f, .73f), new Vector2(.985f, .82f), 25f, Pink, true).characterSpacing = 3f;
        RectTransform pedalGlass = Panel(accelerator, "Instrument", new Vector2(.03f, .035f), new Vector2(.97f, .715f), new Color(.57f, .07f, .19f), true);
        pedal = Label(pedalGlass, "Value", "0<size=58%>%</size>", new Vector2(.075f, .22f), new Vector2(.59f, .71f), 76f, Color.white, false, true);
        pedal.alignment = TextAlignmentOptions.Center;
        DrivingTutorialTypography.SetPinkGlow(pedal);
        RectTransform meterRect = RacingUITheme.Rect(pedalGlass, "Meter", new Vector2(.62f, .08f), new Vector2(.77f, .93f));
        meter = meterRect.gameObject.AddComponent<DrivingTutorialPedalMeterGraphic>(); meter.raycastTarget = false;
        for (int i = 0; i <= 7; i++)
        {
            float y = Mathf.Lerp(.10f, .90f, i / 7f);
            RacingUITheme.Rule(pedalGlass, $"Tick{i}", new Vector2(.79f, y), new Vector2(.816f, y + .005f), Color.white);
        }
        Label(pedalGlass, "Full", "100%", new Vector2(.83f, .83f), new Vector2(.99f, .98f), 27f, Color.white);
        Label(pedalGlass, "Empty", "0%", new Vector2(.83f, .025f), new Vector2(.99f, .17f), 27f, Color.white);

        RectTransform strip = Panel(rect, "Lessons", new Vector2(.036f, .06f), new Vector2(.966f, .251f), new Color(.12f, .33f, .47f));
        string[] titles = { "ペダルを踏んで\n加速しよう", "ハンドルを回して\n角を曲がってみよう", "ペダルから足を離して\n停車しよう" };
        DrivingTutorialControlGraphic.Kind[] icons = { DrivingTutorialControlGraphic.Kind.PressPedal, DrivingTutorialControlGraphic.Kind.TurnWheel, DrivingTutorialControlGraphic.Kind.ReleasePedal };
        for (int i = 0; i < 3; i++)
        {
            float x = i / 3f;
            RectTransform step = RacingUITheme.Rect(strip, $"Step{i + 1}", new Vector2(x, 0f), new Vector2(x + 1f / 3f, 1f));
            steps[i] = step.gameObject.AddComponent<DrivingTutorialPanelGraphic>();
            steps[i].Configure(Cyan, i == 0);
            Icon(step, "Icon", new Vector2(.065f, .08f), new Vector2(.41f, .92f), icons[i]);
            TMP_Text caption = Label(step, "Caption", titles[i], new Vector2(.45f, .15f), new Vector2(.98f, .85f), 29f, Color.white);
            caption.lineSpacing = 10f;
            stepNumbers[i] = Label(step, "Number", $"0{i + 1}", new Vector2(.9f, .74f), new Vector2(.98f, .93f), 16f, new Color(.59f, .8f, .91f));
            if (i < 2) Label(strip, $"Next{i}", "›", new Vector2(x + .322f, .37f), new Vector2(x + .345f, .67f), 42f, new Color(.05f, .5f, .75f)).alignment = TextAlignmentOptions.Center;
        }
        message = Label(rect, "Message", "", new Vector2(.31f, .81f), new Vector2(.685f, .91f), 27f, Color.white);
        message.alignment = TextAlignmentOptions.Center;
        message.textWrappingMode = TextWrappingModes.Normal;
        status = Label(rect, "Status", "", new Vector2(.73f, .86f), new Vector2(.965f, .895f), 19f, Cyan);
        status.alignment = TextAlignmentOptions.Right;
    }

    public void Update(DrivingTutorialProgress lesson, DriveInputState input, float metersPerSecond, bool connected)
    {
        angle.text = $"{input.steering:0}°";
        gauge.Angle = input.steering;
        wheel.Angle = input.steering;
        float pressure = Mathf.Clamp01(input.pedal);
        pedal.text = $"{pressure * 100f:0}<size=58%>%</size>";
        meter.Pressure = pressure;
        int stage = (int)lesson.CurrentStage;
        message.text = lesson.IsComplete ? "練習完了！\nもう一人のプレイヤーを待っています" : !connected ? "コントローラーの\n接続を確認してください" : !lesson.PedalReleased ? "まずペダルから\n足を離そう" : "";
        status.text = lesson.IsComplete ? "両プレイヤーの完了後にレース開始" : $"STEP {stage + 1} / 3";
        for (int i = 0; i < 3; i++)
        {
            steps[i].SetSelected(stage == i);
            stepNumbers[i].text = i < stage ? "OK" : $"0{i + 1}";
            // The reference keeps every instruction white. Selection is expressed by the neon border.
        }
    }

    public void Dispose()
    {
        if (root != null) { root.SetActive(false); Object.Destroy(root); }
        if (logoMaterial != null) Object.Destroy(logoMaterial);
    }

    private void BuildBrand(Transform parent)
    {
        Texture2D reference = Resources.Load<Texture2D>("UI/TutorialReference");
        Shader shader = Resources.Load<Shader>("UI/TutorialReferenceLogo");
        if (reference != null && shader != null)
        {
            RectTransform frame = RacingUITheme.Rect(parent, "ReferenceBrand", new Vector2(.015f, .784f), new Vector2(.25f, .995f));
            RawImage logo = frame.gameObject.AddComponent<RawImage>();
            logo.texture = reference;
            logo.uvRect = new Rect(.015f, .787f, .235f, .211f);
            logoMaterial = new Material(shader); logo.material = logoMaterial; logo.raycastTarget = false;
            return;
        }
        RectTransform brand = RacingUITheme.Rect(parent, "Brand", new Vector2(.025f, .79f), new Vector2(.24f, .99f));
        brand.localRotation = Quaternion.Euler(0f, 0f, 8f);
        Label(brand, "Name", "Tsukukoma", new Vector2(.04f, .56f), new Vector2(.88f, .84f), 43f, Color.white, true);
        Label(brand, "Circuit", "CIRCUIT", new Vector2(.03f, .14f), new Vector2(.98f, .66f), 70f, Pink, true).characterSpacing = -4f;
        TMP_Text tagline = Label(brand, "Tagline", "RACE TO THE NEXT", new Vector2(.2f, .01f), new Vector2(.99f, .14f), 12f, Color.white);
        tagline.characterSpacing = 8f;
        Icon(brand, "Crown", new Vector2(.04f, .82f), new Vector2(.22f, 1.03f), DrivingTutorialControlGraphic.Kind.Crown);
        for (int row = 0; row < 3; row++) for (int col = 0; col < 4; col++)
            if ((row + col) % 2 == 0) RacingUITheme.Rule(brand, $"Check{row}{col}", new Vector2(.80f + col * .033f, .77f + row * .042f),
                new Vector2(.833f + col * .033f, .812f + row * .042f), Color.white);
    }
    private static RectTransform Panel(Transform parent, string name, Vector2 min, Vector2 max, Color tint, bool inset = false)
    {
        RectTransform rect = RacingUITheme.Rect(parent, name, min, max);
        rect.gameObject.AddComponent<DrivingTutorialPanelGraphic>().Configure(tint, true, inset);
        return rect;
    }
    private static TMP_Text Label(Transform parent, string name, string text, Vector2 min, Vector2 max, float size, Color tint, bool italic = false, bool glow = false)
        => DrivingTutorialTypography.Label(parent, name, text, min, max, size, tint, italic, glow);
    private static DrivingTutorialControlGraphic Icon(Transform parent, string name, Vector2 min, Vector2 max, DrivingTutorialControlGraphic.Kind kind)
    {
        RectTransform rect = RacingUITheme.Rect(parent, name, min, max);
        DrivingTutorialControlGraphic graphic = rect.gameObject.AddComponent<DrivingTutorialControlGraphic>();
        graphic.Configure(kind); return graphic;
    }
}
