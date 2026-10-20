using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>Per-player draft selection. Only Confirm commits vehicle and paint to the race and preferences.</summary>
public sealed class CarSelectionUI : MonoBehaviour
{
    private int player, draftVehicle, draftPaint, committedVehicle, committedPaint;
    private RectTransform sheet;
    private Transform titleRoot;
    private CarSelectionCatalog catalog;
    private CarSelectionShowroom showroom;
    private TMP_Text modelName, grade, description, colorName, count, bodyType, titleSelection, pedalHint;
    private TMP_Text[] ratings;
    private RectTransform[] bars;
    private RacingPanelGraphic[] cards;
    private TMP_Text[] selectedLabels;
    private CarPaintSwatchGraphic[] swatches;
    private readonly List<RenderTexture> thumbnails = new List<RenderTexture>();
    private readonly Dictionary<Selectable, bool> titleControls = new Dictionary<Selectable, bool>();
    private enum Focus { Vehicle, Paint, Actions }
    private Focus focus;
    private bool backSelected, steeringLatched, confirmArmed, pedalPressed, selectionLocked;
    private float confirmHold;
    private PedalButtonFeedback confirmFeedback, backFeedback;
    private TMP_Text controls, focusHint;
    private PreparationCountdown countdown;
    private PreparationTimerUI timer;
    private float timeLimitSeconds;
    public const float ConfirmSeconds = .65f;
    public const float DefaultTimeLimitSeconds = 30f;
    public event Action Confirmed;
    public event Action Cancelled;
    public float ConfirmationProgress => Mathf.Clamp01(confirmHold / ConfirmSeconds);
    public bool IsSelectionLocked => selectionLocked;
    public float TimeRemaining => countdown?.RemainingSeconds ?? 0f;
    public bool IsChoosingPaint => focus == Focus.Paint;
    public bool IsChoosingActions => focus == Focus.Actions;
    public int PlayerIndex => player;
    public bool IsOpen => sheet != null && sheet.gameObject.activeSelf;
    public int SelectedVehicle => committedVehicle;
    public string SelectedVehicleName => catalog != null ? catalog.vehicles[committedVehicle].displayName : "SPORT CAR";
    public int SelectedPaint => committedPaint;
    public string DashboardVehicleName => catalog != null ? catalog.vehicles[IsOpen ? draftVehicle : committedVehicle].displayName : "SPORT CAR";
    public string DashboardSelectionStep => IsSelectionLocked ? "Confirmed" : IsChoosingPaint ? "Paint" : IsChoosingActions ? "Confirm" : "Vehicle";
    public int DraftVehicle => draftVehicle;
    public int DraftPaint => draftPaint;
    public CarSelectionShowroom Showroom => showroom;

    public static CarSelectionUI Create(Transform title, int playerIndex)
    {
        CarSelectionUI ui = title.parent.gameObject.AddComponent<CarSelectionUI>();
        ui.titleRoot = title;
        ui.Initialize(playerIndex);
        return ui;
    }

    public void Initialize(int playerIndex)
    {
        player = Mathf.Clamp(playerIndex, 0, 1);
        catalog = CarSelectionCatalog.Load();
        if (catalog == null || catalog.vehicles.Length == 0)
        { Debug.LogError("Car selection catalog is missing.", this); return; }
        string selectedId = PlayerPrefs.GetString("Racing.Car.P" + player, catalog.vehicles[0].id);
        committedVehicle = Math.Max(0, Array.FindIndex(catalog.vehicles, v => v.id == selectedId));
        committedPaint = Mathf.Clamp(PlayerPrefs.GetInt("Racing.Paint.P" + player, player == 0 ? 2 : 3), 0, CarSelectionCatalog.Paints.Length - 1);
        titleSelection = Text(titleRoot, "SelectedCar", "", new Vector2(.685f, .153f), new Vector2(.965f, .194f),
            17f, TextAlignmentOptions.Center, tint: NeonUI.Cyan);
        UpdateTitleSelection();
        sheet = RacingUITheme.Rect(transform, "CarSelection", Vector2.zero, Vector2.one);
        Image blocker = sheet.gameObject.AddComponent<Image>(); blocker.color = new Color(.008f, .014f, .035f); blocker.raycastTarget = true;
        showroom = sheet.gameObject.AddComponent<CarSelectionShowroom>();
        showroom.Initialize(player);
        RectTransform imageRect = RacingUITheme.Rect(sheet, "Live3DPreview", Vector2.zero, Vector2.one);
        RawImage image = imageRect.gameObject.AddComponent<RawImage>(); image.texture = showroom.Texture; image.raycastTarget = false;
        RectTransform heading = NeonUI.Panel(sheet, "Heading", new Vector2(.025f, .866f), new Vector2(.365f, .971f));
        Text(heading, "Japanese", "車 の 選 択", new Vector2(.075f, .33f), new Vector2(.93f, .98f), 53f, italic: true);
        Text(heading, "English", "C A R   S E L E C T", new Vector2(.085f, .06f), new Vector2(.94f, .35f), 17f, tint: new Color(.69f, .83f, 1));
        Text(sheet, "Player", $"PLAYER 0{player + 1}  /  YOUR GARAGE", new Vector2(.71f, .89f), new Vector2(.97f, .954f), 21f,
            TextAlignmentOptions.Right, tint: NeonUI.Cyan);
        timer = new PreparationTimerUI(sheet, new Vector2(.48f, .855f), new Vector2(.66f, .975f));
        timer.Update(DefaultTimeLimitSeconds, DefaultTimeLimitSeconds);
        Text(sheet, "GarageBrand", "TSUKUKOMA\nCIRCUIT", new Vector2(.045f, .64f), new Vector2(.34f, .815f), 58f, italic: true, tint: new Color(.39f, .44f, .71f, .45f));
        description = Text(sheet, "Description", "", new Vector2(.08f, .323f), new Vector2(.65f, .365f), 20f,
            TextAlignmentOptions.Center, tint: new Color(.76f, .84f, .96f));
        Arrow("Previous", new Vector2(.026f, .46f), new Vector2(.074f, .57f), true, () => SelectVehicle(draftVehicle - 1));
        Arrow("Next", new Vector2(.625f, .46f), new Vector2(.673f, .57f), false, () => SelectVehicle(draftVehicle + 1));
        BuildPerformance();
        BuildCards();
        RacingMenuButton back = NeonUI.Button(sheet, "Back", "戻る", "BACK / CANCEL", RacingIconGraphic.Icon.Back,
            new Vector2(.027f, .027f), new Vector2(.21f, .130f), false, Back);
        back.ConfigureHold(ConfirmSeconds, false);
        backFeedback = back.GetComponent<PedalButtonFeedback>();
        RacingMenuButton confirm = NeonUI.Button(sheet, "Confirm", "この車で決定", "ペダル長押しでチュートリアルへ", RacingIconGraphic.Icon.Flag,
            new Vector2(.724f, .027f), new Vector2(.976f, .130f), true, Confirm);
        confirm.ConfigureHold(ConfirmSeconds, false);
        confirm.PlayConfirmationSound = false;
        confirmFeedback = confirm.GetComponent<PedalButtonFeedback>();
        pedalHint = Text(sheet, "PedalHint", "ペダルを離してから踏み込む", new Vector2(.724f, .132f), new Vector2(.976f, .154f),
            13f, TextAlignmentOptions.Center, tint: NeonUI.Cyan);
        controls = Text(sheet, "Controls", "ハンドルで切替 / ペダル短押しで項目切替 / 長押しで決定",
            new Vector2(.238f, .043f), new Vector2(.70f, .109f), 17f, TextAlignmentOptions.Center, tint: new Color(.63f, .73f, .89f));
        focusHint = Text(sheet, "FocusHint", "車種を選択 / ハンドルで切替", new Vector2(.10f, .385f), new Vector2(.62f, .415f), 17f,
            TextAlignmentOptions.Center, tint: NeonUI.Cyan);
        foreach (TMP_Text label in sheet.GetComponentsInChildren<TMP_Text>(true)) ApplyFont(label);
        sheet.gameObject.SetActive(false); showroom.SetVisible(false);
    }

    private static TMP_Text Text(Transform parent, string name, string value, Vector2 min, Vector2 max, float size,
        TextAlignmentOptions alignment = TextAlignmentOptions.Left, bool italic = false, Color? tint = null)
    {
        TMP_Text text = NeonUI.Text(parent, name, value, min, max, size, alignment, italic, tint);
        ApplyFont(text);
        return text;
    }
    private static void ApplyFont(TMP_Text text)
    {
        TMP_FontAsset font = Resources.Load<TMP_FontAsset>("UI/CarSelectionFont");
        if (font != null) { text.font = font; text.fontSharedMaterial = font.material; }
    }

    private static string PerformanceSummary(CarSelectionCatalog.Vehicle vehicle)
    {
        // Compare actual tuning, including custom catalogs, rather than relying on vehicle order.
        if (vehicle.handling > vehicle.topSpeed && vehicle.handling > 1.02f)
            return "曲がりやすい / カーブが得意";
        if (vehicle.topSpeed > vehicle.handling && vehicle.topSpeed > 1.02f)
            return "直線で伸びる / カーブは早めに減速";
        return "バランス型 / 速さと曲がりやすさを両立";
    }

    private void BuildPerformance()
    {
        RectTransform panel = NeonUI.Panel(sheet, "Performance", new Vector2(.692f, .354f), new Vector2(.973f, .85f));
        grade = Text(panel, "Grade", "", new Vector2(.07f, .887f), new Vector2(.94f, .969f), 18f, tint: NeonUI.Cyan);
        modelName = Text(panel, "ModelName", "", new Vector2(.07f, .752f), new Vector2(.94f, .89f), 43f, italic: true);
        RacingUITheme.Rule(panel, "HeaderRule", new Vector2(.04f, .727f), new Vector2(.96f, .730f), NeonUI.Cyan);
        string[] names = { "最高速", "加速", "ハンドリング", "ドリフト" };
        RacingIconGraphic.Icon[] icons = { RacingIconGraphic.Icon.Course, RacingIconGraphic.Icon.DoubleChevron, RacingIconGraphic.Icon.Gear, RacingIconGraphic.Icon.Bolt };
        ratings = new TMP_Text[4]; bars = new RectTransform[4];
        for (int i = 0; i < 4; i++)
        {
            float bottom = .613f - i * .099f;
            NeonUI.Icon(panel, "StatIcon" + i, icons[i], new Vector2(.06f, bottom), new Vector2(.125f, bottom + .060f));
            Text(panel, "StatLabel" + i, names[i], new Vector2(.155f, bottom), new Vector2(.45f, bottom + .068f), 22f);
            RectTransform track = RacingUITheme.Rect(panel, "Track" + i, new Vector2(.455f, bottom + .019f), new Vector2(.845f, bottom + .052f));
            Image background = track.gameObject.AddComponent<Image>(); background.color = new Color(.052f, .081f, .15f); background.raycastTarget = false;
            bars[i] = RacingUITheme.Rect(track, "Value", Vector2.zero, Vector2.one);
            Image fill = bars[i].gameObject.AddComponent<Image>(); fill.color = NeonUI.Pink; fill.raycastTarget = false;
            ratings[i] = Text(panel, "Rating" + i, "", new Vector2(.87f, bottom), new Vector2(.965f, bottom + .073f), 24f, TextAlignmentOptions.Right, true);
        }
        RacingUITheme.Rule(panel, "DriveRule", new Vector2(.06f, .288f), new Vector2(.94f, .291f), new Color(.10f, .34f, .49f));
        Text(panel, "DriveLabel", "駆動方式", new Vector2(.07f, .212f), new Vector2(.44f, .278f), 21f);
        Text(panel, "Drive", "前輪駆動 / FWD", new Vector2(.43f, .212f), new Vector2(.94f, .278f), 21f, TextAlignmentOptions.Right);
        bodyType = Text(panel, "BodyType", "", new Vector2(.07f, .151f), new Vector2(.94f, .212f), 16f, tint: new Color(.64f, .74f, .87f));
        colorName = Text(panel, "ColorName", "", new Vector2(.07f, .085f), new Vector2(.94f, .15f), 17f);
        swatches = new CarPaintSwatchGraphic[CarSelectionCatalog.Paints.Length];
        for (int i = 0; i < swatches.Length; i++)
        {
            int index = i;
            RectTransform choice = RacingUITheme.Rect(panel, "Paint" + i, new Vector2(.31f + i * .125f, .017f), new Vector2(.413f + i * .125f, .09f));
            Image hit = choice.gameObject.AddComponent<Image>(); hit.color = Color.clear;
            Button button = choice.gameObject.AddComponent<Button>(); button.targetGraphic = hit;
            button.transition = Selectable.Transition.None; button.onClick.AddListener(() => SelectPaint(index));
            RectTransform circle = RacingUITheme.Rect(choice, "Color", Vector2.zero, Vector2.one);
            swatches[i] = circle.gameObject.AddComponent<CarPaintSwatchGraphic>();
            swatches[i].color = CarSelectionCatalog.Paints[i]; swatches[i].raycastTarget = false;
        }
        Text(panel, "ColorLabel", "カラー", new Vector2(.07f, .017f), new Vector2(.29f, .085f), 21f);
    }

    private void BuildCards()
    {
        cards = new RacingPanelGraphic[catalog.vehicles.Length]; selectedLabels = new TMP_Text[cards.Length];
        count = Text(sheet, "VehicleCount", "", new Vector2(.06f, .119f), new Vector2(.94f, .145f), 15f, TextAlignmentOptions.Center, tint: NeonUI.Cyan);
        float gap = .017f, width = (.906f - gap * (cards.Length - 1)) / cards.Length;
        for (int i = 0; i < cards.Length; i++)
        {
            int index = i;
            CarSelectionCatalog.Vehicle vehicle = catalog.vehicles[i];
            float left = .047f + i * (width + gap);
            RectTransform card = NeonUI.Panel(sheet, "Vehicle" + i, new Vector2(left, .151f), new Vector2(left + width, .308f));
            cards[i] = card.Find("ModernSurface").GetComponent<RacingPanelGraphic>();
            Image hit = card.gameObject.AddComponent<Image>(); hit.color = Color.clear;
            Button button = card.gameObject.AddComponent<Button>(); button.targetGraphic = hit; button.transition = Selectable.Transition.None;
            button.onClick.AddListener(() => SelectVehicle(index));
            RectTransform thumbnailRect = RacingUITheme.Rect(card, "Thumbnail", new Vector2(.015f, .29f), new Vector2(.59f, .97f));
            RawImage thumbnail = thumbnailRect.gameObject.AddComponent<RawImage>(); thumbnail.raycastTarget = false;
            RenderTexture texture = showroom.Thumbnail(vehicle, i == 1 ? 2 : i == 2 ? 3 : 0);
            thumbnails.Add(texture); thumbnail.texture = texture;
            Text(card, "Model", vehicle.displayName, new Vector2(.59f, .48f), new Vector2(.95f, .78f), 27f, italic: true);
            Text(card, "Grade", vehicle.grade, new Vector2(.60f, .23f), new Vector2(.95f, .46f), 15f, tint: new Color(.64f, .76f, .96f));
            selectedLabels[i] = Text(card, "Selected", "", new Vector2(.055f, .03f), new Vector2(.95f, .25f), 16f, tint: NeonUI.Pink);
        }
    }

    private void Arrow(string name, Vector2 min, Vector2 max, bool previous, UnityEngine.Events.UnityAction action)
    {
        RectTransform rect = NeonUI.Panel(sheet, name, min, max);
        RacingPanelGraphic surface = rect.Find("ModernSurface").GetComponent<RacingPanelGraphic>();
        surface.Configure(RacingPanelGraphic.SurfaceStyle.Secondary, previous ? NeonUI.Cyan : NeonUI.Pink);
        Image hit = rect.gameObject.AddComponent<Image>(); hit.color = Color.clear;
        Button button = rect.gameObject.AddComponent<Button>(); button.targetGraphic = hit; button.onClick.AddListener(action);
        NeonUI.Icon(rect, "Arrow", previous ? RacingIconGraphic.Icon.Back : RacingIconGraphic.Icon.Chevron, new Vector2(.12f, .20f), new Vector2(.88f, .80f));
    }

    public void Open(float seconds = DefaultTimeLimitSeconds)
    {
        if (IsOpen || sheet == null) return;
        timeLimitSeconds = Mathf.Max(0f, seconds);
        countdown = new PreparationCountdown(timeLimitSeconds);
        timer.Update(TimeRemaining, timeLimitSeconds);
        draftVehicle = committedVehicle; draftPaint = committedPaint;
        focus = Focus.Vehicle; backSelected = selectionLocked = pedalPressed = false;
        confirmArmed = false; confirmHold = 0f; steeringLatched = true;
        confirmFeedback.SetConfirmed(false); backFeedback.SetConfirmed(false);
        foreach (Selectable control in titleRoot.GetComponentsInChildren<Selectable>(true))
        {
            titleControls[control] = control.interactable; control.interactable = false;
        }
        EventSystem.current?.SetSelectedGameObject(null);
        sheet.gameObject.SetActive(true); showroom.SetVisible(true); sheet.SetAsLastSibling();
        pedalHint.text = "ペダルを離してから踏み込む";
        Refresh(true); UpdateFeedback();
    }
    public void SelectVehicle(int index)
    {
        if (selectionLocked) return;
        draftVehicle = (index % catalog.vehicles.Length + catalog.vehicles.Length) % catalog.vehicles.Length;
        ResetHold();
        if (IsOpen) { Refresh(true); RaceSoundEffects.PlayForPlayer(RaceSoundCue.Select, player); }
    }
    public void SelectPaint(int index)
    {
        if (selectionLocked) return;
        draftPaint = (index % CarSelectionCatalog.Paints.Length + CarSelectionCatalog.Paints.Length) % CarSelectionCatalog.Paints.Length;
        ResetHold();
        if (IsOpen) { Refresh(false); RaceSoundEffects.PlayForPlayer(RaceSoundCue.Select, player); }
    }
    public void Confirm()
    {
        if (!IsOpen || selectionLocked) return;
        selectionLocked = true; confirmHold = ConfirmSeconds;
        countdown?.Stop();
        committedVehicle = draftVehicle; committedPaint = draftPaint;
        PlayerPrefs.SetString("Racing.Car.P" + player, catalog.vehicles[committedVehicle].id);
        PlayerPrefs.SetInt("Racing.Paint.P" + player, committedPaint); PlayerPrefs.Save();
        confirmFeedback.SetState(true, 1f, NeonUI.Pink); confirmFeedback.SetConfirmed(true);
        RaceSoundEffects.PlayForPlayer(RaceSoundCue.Confirm, player);
        UpdateTitleSelection();
        if (Confirmed != null) Confirmed.Invoke(); else Cancel();
    }
    public void Back()
    {
        if (!IsOpen || selectionLocked) return;
        countdown?.Stop();
        selectionLocked = true; backFeedback.SetState(true, 1f, NeonUI.Cyan); backFeedback.SetConfirmed(true);
        if (Cancelled != null) Cancelled.Invoke(); else Cancel();
    }
    public void Cancel()
    {
        countdown?.Stop();
        if (sheet != null) sheet.gameObject.SetActive(false);
        showroom?.SetVisible(false);
        foreach (var entry in titleControls) if (entry.Key != null) entry.Key.interactable = entry.Value;
        titleControls.Clear();
        if (sheet != null && EventSystem.current != null && EventSystem.current.currentSelectedGameObject != null &&
            EventSystem.current.currentSelectedGameObject.transform.IsChildOf(sheet)) EventSystem.current.SetSelectedGameObject(null);
        selectionLocked = pedalPressed = confirmArmed = false; confirmHold = 0f;
    }
    public void ApplyToCar(GameObject car) => catalog.Apply(car, committedVehicle, committedPaint);

    // Short pedal taps cycle vehicle -> paint -> actions. A long hold commits the focused action.
    public void TickInput(DriveInputState input, bool connected, float dt)
    {
        if (!IsOpen || selectionLocked) return;
        bool expired = countdown.Tick(dt);
        timer.Update(TimeRemaining, timeLimitSeconds);
        if (expired) { Confirm(); return; }
        if (!connected)
        {
            ResetHold(); steeringLatched = true; pedalHint.text = "接続待ち / 係員に知らせてください"; return;
        }
        float steering = Mathf.Abs(input.steering);
        if (steering < 2f) steeringLatched = false;
        if (!steeringLatched && steering >= 5f)
        {
            int direction = input.steering > 0 ? 1 : -1;
            if (focus == Focus.Vehicle) SelectVehicle(draftVehicle + direction);
            else if (focus == Focus.Paint) SelectPaint(draftPaint + direction);
            else { backSelected = direction < 0; ResetHold(); }
            steeringLatched = true; UpdateFocus();
        }
        if (Mathf.Abs(input.pedal) < .2f)
        {
            if (pedalPressed)
            {
                focus = (Focus)(((int)focus + 1) % 3); backSelected = false;
                UpdateFocus(); RaceSoundEffects.PlayForPlayer(RaceSoundCue.Select, player);
            }
            confirmArmed = true; pedalPressed = false; confirmHold = 0f;
        }
        else if (confirmArmed && input.pedal >= .8f)
        {
            pedalPressed = true; confirmHold += Mathf.Max(0f, dt);
            UpdateFeedback();
            if (confirmHold >= ConfirmSeconds)
            {
                if (focus == Focus.Actions && backSelected) Back(); else Confirm();
                return;
            }
        }
        else confirmHold = 0f;
        pedalHint.text = confirmArmed ? "踏み込んでゲージを満たす" : "ペダルを離してから踏み込む";
        UpdateFeedback();
    }
    private void ResetHold()
    {
        confirmHold = 0f; pedalPressed = confirmArmed = false;
        if (confirmFeedback != null) UpdateFeedback();
    }
    private void UpdateFeedback()
    {
        bool back = focus == Focus.Actions && backSelected;
        confirmFeedback.SetState(!back, back ? 0f : ConfirmationProgress, NeonUI.Pink);
        backFeedback.SetState(back, back ? ConfirmationProgress : 0f, NeonUI.Cyan);
    }
    private void UpdateFocus()
    {
        focusHint.text = focus == Focus.Vehicle ? "車種を選択 / ハンドルで切替" :
            focus == Focus.Paint ? "カラーを選択 / ハンドルで切替" : backSelected ? "戻る / ペダル長押しでタイトルへ" : "決定 / ペダル長押しでチュートリアルへ";
        colorName.color = focus == Focus.Paint ? NeonUI.Cyan : Color.white;
        UpdateFeedback();
    }

    private void Refresh(bool changeModel)
    {
        CarSelectionCatalog.Vehicle vehicle = catalog.vehicles[draftVehicle];
        modelName.text = vehicle.displayName; grade.text = "TSUKUKOMA MOTORS  /  " + vehicle.grade;
        description.text = PerformanceSummary(vehicle); bodyType.text = vehicle.bodyType + "   /   ゲーム内性能・10段階評価";
        colorName.text = CarSelectionCatalog.PaintNames[draftPaint]; count.text = $"0{draftVehicle + 1} / 0{catalog.vehicles.Length}     SELECT YOUR MACHINE";
        float[] values = { vehicle.SpeedRating, vehicle.AccelerationRating, vehicle.HandlingRating, vehicle.DriftRating };
        for (int i = 0; i < values.Length; i++) { ratings[i].text = values[i].ToString("0.0"); bars[i].anchorMax = new Vector2(values[i] / 10f, 1); }
        for (int i = 0; i < cards.Length; i++)
        {
            bool selected = i == draftVehicle;
            cards[i].Configure(RacingPanelGraphic.SurfaceStyle.Panel, selected ? NeonUI.Pink : NeonUI.Cyan);
            cards[i].SetState(selected ? 1f : 0f, 0f, 0f, selected ? NeonUI.Pink : NeonUI.Cyan);
            selectedLabels[i].text = selected ? "選択中 / SELECTED" : catalog.vehicles[i].bodyType;
            selectedLabels[i].color = selected ? NeonUI.Pink : new Color(.63f, .73f, .89f);
        }
        for (int i = 0; i < swatches.Length; i++)
        {
            swatches[i].SetSelected(i == draftPaint);
            swatches[i].color = CarSelectionCatalog.Paints[i];
        }
        if (changeModel) showroom.Show(vehicle, draftPaint); else showroom.SetPaint(draftPaint);
        UpdateFocus();
    }
    private void UpdateTitleSelection() => titleSelection.text = catalog.vehicles[committedVehicle].displayName + "  /  " + CarSelectionCatalog.PaintNames[committedPaint];
    private void OnDisable() => Cancel();
    private void OnDestroy()
    {
        foreach (RenderTexture texture in thumbnails)
        {
            texture.Release();
            if (Application.isPlaying) Destroy(texture); else DestroyImmediate(texture);
        }
    }
}
