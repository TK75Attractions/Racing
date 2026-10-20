using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Live instruments and one instruction card for the player's current action.</summary>
public sealed class DrivingTutorialUI
{
    public static readonly Color Cyan = new Color(.01f, .78f, 1f);
    public static readonly Color Pink = new Color(1f, .025f, .39f);
    private readonly GameObject root;
    private Material logoMaterial;
    private readonly TMP_Text angle, pedal, message, status;
    private readonly DrivingTutorialGaugeGraphic gauge;
    private readonly DrivingTutorialPedalMeterGraphic meter;
    private readonly DrivingTutorialControlGraphic wheel;
    private readonly DrivingTutorialControlGraphic instructionIcon;
    private readonly RectTransform instructionCard;
    private readonly PreparationTimerUI timer;
    private readonly RectTransform turnCue;
    private readonly RacingIconGraphic stateIcon;
    private readonly DrivingTutorialController tutorial;
    private readonly int playerIndex;
    private float popupTime;
    private int shownAngle=int.MinValue,shownPedal=-1,shownStage=-1;
    private string shownMessage;

    public DrivingTutorialUI(Transform canvas, int playerIndex)
    {
        this.playerIndex = playerIndex;
        tutorial = Object.FindFirstObjectByType<DrivingTutorialController>();
        RectTransform rect = RacingUITheme.Rect(canvas, "DrivingTutorial", Vector2.zero, Vector2.one);
        root = rect.gameObject;
        rect.SetAsFirstSibling();
        BuildBrand(rect);
        Label(rect, "Player", $"PLAYER {playerIndex + 1} / PRACTICE", new Vector2(.73f, .905f), new Vector2(.965f, .95f), 22f, Color.white).alignment = TextAlignmentOptions.Right;

        RacingUITheme.Rule(rect,"PlayerUnderline",new Vector2(.855f,.898f),new Vector2(.965f,.901f),Pink);
        timer = new PreparationTimerUI(rect, new Vector2(.78f, .765f), new Vector2(.965f, .885f), true);
        RectTransform steering = Panel(rect, "Steering", new Vector2(.028f, .025f), new Vector2(.255f, .315f), Cyan);
        wheel = Icon(steering, "Wheel", new Vector2(.065f, .72f), new Vector2(.29f, .975f), DrivingTutorialControlGraphic.Kind.WheelHeader);
        Label(steering, "Title", "ハンドル", new Vector2(.347f, .815f), new Vector2(.97f, .98f), 43f, Color.white);
        Label(steering, "English", "STEERING", new Vector2(.35f, .73f), new Vector2(.96f, .82f), 25f, Cyan, true).characterSpacing = 5f;
        RectTransform steeringGlass = Panel(steering, "Instrument", new Vector2(.025f, .035f), new Vector2(.975f, .705f), new Color(.05f, .38f, .6f), true);
        angle = Label(steeringGlass, "Angle", "0°", new Vector2(.05f, .66f), new Vector2(.95f, 1f), 68f, Color.white, false, true);
        angle.alignment = TextAlignmentOptions.Center;
        RectTransform gaugeRect = RacingUITheme.Rect(steeringGlass, "Arc", new Vector2(.075f, .18f), new Vector2(.925f, .71f));
        gauge = gaugeRect.gameObject.AddComponent<DrivingTutorialGaugeGraphic>(); gauge.raycastTarget = false;
        Label(steeringGlass, "Left", "−90°", new Vector2(.07f, .085f), new Vector2(.32f, .225f), 29f, Cyan);
        Label(steeringGlass, "Center", "0°", new Vector2(.37f, .18f), new Vector2(.63f, .34f), 30f, Cyan).alignment = TextAlignmentOptions.Center;
        Label(steeringGlass, "Right", "+90°", new Vector2(.69f, .085f), new Vector2(.94f, .225f), 29f, Cyan).alignment = TextAlignmentOptions.Right;

        RectTransform accelerator = Panel(rect, "Accelerator", new Vector2(.745f, .025f), new Vector2(.972f, .315f), Pink);
        Icon(accelerator, "PedalIcon", new Vector2(.08f, .735f), new Vector2(.26f, .97f), DrivingTutorialControlGraphic.Kind.PedalHeader);
        Label(accelerator, "Title", "アクセル", new Vector2(.345f, .815f), new Vector2(.975f, .98f), 43f, Color.white);
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

        instructionCard = Panel(rect, "CurrentInstruction", new Vector2(.275f, .035f), new Vector2(.725f, .215f), Cyan);
        instructionIcon = Icon(instructionCard, "ActionIcon", new Vector2(.045f, .14f), new Vector2(.26f, .77f), DrivingTutorialControlGraphic.Kind.PressPedal);
        status = Label(instructionCard, "Status", "STEP 1 / 3", new Vector2(.28f, .75f), new Vector2(.95f, .94f), 19f, Cyan);
        status.alignment = TextAlignmentOptions.Center;
        message = Label(instructionCard, "Message", "", new Vector2(.28f, .10f), new Vector2(.96f, .74f), 31f, Color.white);
        message.alignment = TextAlignmentOptions.Center;
        message.textWrappingMode = TextWrappingModes.Normal;
        stateIcon = NeonUI.Icon(instructionCard, "StateIcon", RacingIconGraphic.Icon.Success,
            new Vector2(.045f, .18f), new Vector2(.23f, .72f));
        turnCue = Panel(rect, "TurnCue", new Vector2(.43f, .48f), new Vector2(.63f, .58f), Cyan);
        NeonUI.Icon(turnCue, "Arrow", RacingIconGraphic.Icon.Chevron, new Vector2(.02f, .15f), new Vector2(.23f, .85f));
        Label(turnCue, "Direction", "右カーブ\nハンドルを右へ", new Vector2(.25f, .08f), new Vector2(.96f, .92f), 23f, Color.white);
        turnCue.gameObject.SetActive(false);
    }

    public void Update(DrivingTutorialProgress lesson, DriveInputState input, float metersPerSecond, bool connected)
    {
        timer.Update(lesson.TimeRemaining, lesson.TimeLimitSeconds);
        int degrees=Mathf.RoundToInt(input.steering);
        if(shownAngle!=degrees){angle.SetText("{0}°",degrees);shownAngle=degrees;}
        gauge.Angle = input.steering;
        wheel.Angle = input.steering;
        float pressure = Mathf.Clamp01(input.pedal);
        int percent=Mathf.RoundToInt(pressure*100f);
        if(shownPedal!=percent){pedal.SetText("{0}<size=58%>%</size>",percent);shownPedal=percent;}
        meter.Pressure = pressure;
        int stage = lesson.HasTimedOut ? 4 : (int)lesson.CurrentStage;
        string prompt=lesson.HasTimedOut ? "時間終了 / 相手の準備待ち\n操作不要・そろうとレースへ" : lesson.IsComplete ? "準備完了 / 相手の準備待ち\n操作不要・そろうとレースへ" : !connected ? "接続待ち / 操作できません\n係員に知らせてください" : !lesson.PedalReleased ? "まずペダルから\n足を離そう" : stage == 0 ? "ペダルを踏んで\n加速しよう" : stage == 1 ? "右カーブでハンドルを右へ\n矢印に沿って曲がろう" : "ペダルから足を離して\n停車しよう";
        UpdateTurnCue(connected && !lesson.IsFinished && stage == 1);
        if(shownMessage!=prompt)
        {
            message.text=prompt;shownMessage=prompt;popupTime=0f;
            instructionIcon.Configure(!lesson.PedalReleased || stage >= 2
                ? DrivingTutorialControlGraphic.Kind.ReleasePedal : stage == 1
                ? DrivingTutorialControlGraphic.Kind.TurnWheel : DrivingTutorialControlGraphic.Kind.PressPedal);
            instructionIcon.gameObject.SetActive(connected && !lesson.IsFinished);
            stateIcon.gameObject.SetActive(!connected || lesson.IsFinished);
            stateIcon.symbol = !connected && !lesson.IsFinished ? RacingIconGraphic.Icon.Warning : RacingIconGraphic.Icon.Success;
            stateIcon.SetVerticesDirty();
        }
        popupTime = Mathf.Min(1f, popupTime + Time.unscaledDeltaTime * 5f);
        instructionCard.localScale = Vector3.one * Mathf.Lerp(.94f, 1f, Mathf.SmoothStep(0f, 1f, popupTime));
        if(shownStage==stage)return;
        shownStage=stage;
        status.text = lesson.HasTimedOut ? "TIME UP" : lesson.IsComplete ? "PRACTICE COMPLETE" : $"STEP {stage + 1} / 3";
    }

    public void Dispose()
    {
        if (root != null) { root.SetActive(false); Object.Destroy(root); }
        if (logoMaterial != null) Object.Destroy(logoMaterial);
    }

    private void UpdateTurnCue(bool visible)
    {
        GameObject car = tutorial != null ? tutorial.GetPlayerCar(playerIndex) : null;
        Camera camera = Gmanager.Control != null ? Gmanager.Control.GetPlayerViewCamera(playerIndex) : null;
        if (visible && car != null && camera != null)
        {
            // The practice road has one right-hand quarter circle; mark its actual entry.
            DrivingTutorialRoad.Sample(80f, out Vector3 point, out _);
            Vector3 view = camera.WorldToViewportPoint(car.transform.parent.TransformPoint(point + Vector3.up * 1.8f));
            visible = view.z > 0f && view.x > .1f && view.x < .9f && view.y > .32f && view.y < .8f;
            if (visible)
            {
                Vector2 center = new Vector2(view.x, view.y);
                turnCue.anchorMin = center - new Vector2(.10f, .05f);
                turnCue.anchorMax = center + new Vector2(.10f, .05f);
            }
        }
        else visible = false;
        turnCue.gameObject.SetActive(visible);
    }

    private void BuildBrand(Transform parent)
    {
        Texture2D reference = Resources.Load<Texture2D>("UI/TutorialReferenceDecorated");
        Shader shader = Resources.Load<Shader>("UI/TutorialReferenceLogo");
        if (reference != null && shader != null)
        {
            RectTransform frame = RacingUITheme.Rect(parent, "ReferenceBrand", new Vector2(0f, .774f), new Vector2(.329f, .995f));
            RectTransform stripes=RacingUITheme.Rect(frame,"RacingBands",Vector2.zero,Vector2.one);
            stripes.gameObject.AddComponent<DrivingTutorialBrandGraphic>();
            RectTransform artwork=RacingUITheme.Rect(frame,"Artwork",Vector2.zero,Vector2.one);
            RawImage logo = artwork.gameObject.AddComponent<RawImage>();
            logo.texture = reference;
            logo.uvRect = new Rect(0f, .765f, .329f, .23f);
            logoMaterial = new Material(shader); logo.material = logoMaterial; logo.raycastTarget = false;
            stripes.SetAsFirstSibling();
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
