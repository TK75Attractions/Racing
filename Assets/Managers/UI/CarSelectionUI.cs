using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>Per-player draft selection. Only Confirm commits vehicle and paint to the race and preferences.</summary>
public sealed class CarSelectionUI : MonoBehaviour
{
    private int player, draftVehicle, draftPaint, committedVehicle, committedPaint;
    private RectTransform sheet;
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
    private bool steeringLatched, confirmArmed;
    private float confirmHold;
    public int PlayerIndex => player;
    public bool IsOpen => sheet != null && sheet.gameObject.activeSelf;
    public int SelectedVehicle => committedVehicle;
    public string SelectedVehicleName => catalog != null ? catalog.vehicles[committedVehicle].displayName : "SPORT CAR";
    public int SelectedPaint => committedPaint;
    public int DraftVehicle => draftVehicle;
    public int DraftPaint => draftPaint;
    public CarSelectionShowroom Showroom => showroom;

    public static CarSelectionUI Create(Transform title, int playerIndex)
    {
        CarSelectionUI ui = title.gameObject.AddComponent<CarSelectionUI>();
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
        RacingMenuButton entry = NeonUI.Button(transform, "SelectCar", "車の選択", "CAR SELECT",
            RacingIconGraphic.Icon.Car, new Vector2(.678f, .20f), new Vector2(.970f, .292f), false, Open);
        entry.transform.Find("Label").GetComponent<TMP_Text>().fontSizeMax = 30f;
        titleSelection = Text(transform, "SelectedCar", "", new Vector2(.685f, .153f), new Vector2(.965f, .194f),
            17f, TextAlignmentOptions.Center, tint: NeonUI.Cyan);
        UpdateTitleSelection();
        sheet = RacingUITheme.Rect(transform, "CarSelection", Vector2.zero, Vector2.one);
        Image blocker = sheet.gameObject.AddComponent<Image>(); blocker.color = new Color(.008f, .014f, .035f); blocker.raycastTarget = true;
        showroom = sheet.gameObject.AddComponent<CarSelectionShowroom>();
        showroom.Initialize(player);
        RectTransform imageRect = RacingUITheme.Rect(sheet, "Live3DPreview", Vector2.zero, Vector2.one);
        RawImage image = imageRect.gameObject.AddComponent<RawImage>(); image.texture = showroom.Texture; image.raycastTarget = true;
        RectTransform heading = NeonUI.Panel(sheet, "Heading", new Vector2(.025f, .866f), new Vector2(.365f, .971f));
        Text(heading, "Japanese", "車 の 選 択", new Vector2(.075f, .33f), new Vector2(.93f, .98f), 53f, italic: true);
        Text(heading, "English", "C A R   S E L E C T", new Vector2(.085f, .06f), new Vector2(.94f, .35f), 17f, tint: new Color(.69f, .83f, 1));
        Text(sheet, "Player", $"PLAYER 0{player + 1}  /  YOUR GARAGE", new Vector2(.71f, .89f), new Vector2(.97f, .954f), 21f,
            TextAlignmentOptions.Right, tint: NeonUI.Cyan);
        Text(sheet, "GarageBrand", "TSUKUKOMA\nCIRCUIT", new Vector2(.045f, .64f), new Vector2(.34f, .815f), 58f, italic: true, tint: new Color(.39f, .44f, .71f, .45f));
        description = Text(sheet, "Description", "", new Vector2(.08f, .323f), new Vector2(.65f, .365f), 20f,
            TextAlignmentOptions.Center, tint: new Color(.76f, .84f, .96f));
        Arrow("Previous", new Vector2(.026f, .46f), new Vector2(.074f, .57f), true, () => SelectVehicle(draftVehicle - 1));
        Arrow("Next", new Vector2(.625f, .46f), new Vector2(.673f, .57f), false, () => SelectVehicle(draftVehicle + 1));
        BuildPerformance();
        BuildCards();
        NeonUI.Button(sheet, "Back", "戻る", "BACK / CANCEL", RacingIconGraphic.Icon.Back,
            new Vector2(.027f, .035f), new Vector2(.21f, .115f), false, Cancel);
        NeonUI.Button(sheet, "Confirm", "この車で決定", "CONFIRM CAR", RacingIconGraphic.Icon.Flag,
            new Vector2(.724f, .030f), new Vector2(.976f, .119f), true, Confirm);
        pedalHint = Text(sheet, "PedalHint", "ペダル長押しでも決定できます", new Vector2(.724f, .122f), new Vector2(.976f, .146f),
            13f, TextAlignmentOptions.Center, tint: NeonUI.Cyan);
        Text(sheet, "Controls", player == 0 ? "A / D  車種     C  カラー     Enter  決定" : "Left / Right  車種     M  カラー     Right Shift  決定",
            new Vector2(.238f, .052f), new Vector2(.70f, .096f), 17f, TextAlignmentOptions.Center, tint: new Color(.63f, .73f, .89f));
        Text(sheet, "OrbitHint", player == 0 ? "ドラッグで回転  /  Q・E  回転  /  R  視点リセット" : "ドラッグで回転  /  DRAG TO ROTATE", new Vector2(.10f, .385f), new Vector2(.62f, .415f), 15f,
            TextAlignmentOptions.Center, tint: new Color(.63f, .73f, .89f));
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

    public void Open()
    {
        if (IsOpen || sheet == null || Gmanager.Control != null && Gmanager.Control.state != Gmanager.State.Title) return;
        Gmanager.Control?.CancelTitleReady(player);
        draftVehicle = committedVehicle; draftPaint = committedPaint;
        confirmArmed = false; confirmHold = 0f; steeringLatched = true;
        foreach (Selectable control in GetComponentsInChildren<Selectable>(true))
        {
            if (control.transform.IsChildOf(sheet)) continue;
            titleControls[control] = control.interactable;
            control.interactable = false;
        }
        EventSystem.current?.SetSelectedGameObject(null);
        sheet.gameObject.SetActive(true); showroom.SetVisible(true); sheet.SetAsLastSibling();
        pedalHint.text = "ペダルを離してから踏み込む";
        Refresh(true);
    }
    public void SelectVehicle(int index)
    {
        draftVehicle = (index % catalog.vehicles.Length + catalog.vehicles.Length) % catalog.vehicles.Length;
        if (IsOpen) { Refresh(true); RaceSoundEffects.PlayForPlayer(RaceSoundCue.Select, player); }
    }
    public void SelectPaint(int index)
    {
        draftPaint = (index % CarSelectionCatalog.Paints.Length + CarSelectionCatalog.Paints.Length) % CarSelectionCatalog.Paints.Length;
        if (IsOpen) { Refresh(false); RaceSoundEffects.PlayForPlayer(RaceSoundCue.Select, player); }
    }
    public void Confirm()
    {
        if (!IsOpen) return;
        committedVehicle = draftVehicle; committedPaint = draftPaint;
        PlayerPrefs.SetString("Racing.Car.P" + player, catalog.vehicles[committedVehicle].id);
        PlayerPrefs.SetInt("Racing.Paint.P" + player, committedPaint); PlayerPrefs.Save();
        UpdateTitleSelection(); Cancel();
    }
    public void Cancel()
    {
        if (IsOpen) Gmanager.Control?.CancelTitleReady(player);
        if (sheet != null) sheet.gameObject.SetActive(false);
        showroom?.SetVisible(false);
        foreach (var entry in titleControls) if (entry.Key != null) entry.Key.interactable = entry.Value;
        titleControls.Clear();
        if (sheet != null && EventSystem.current != null && EventSystem.current.currentSelectedGameObject != null &&
            EventSystem.current.currentSelectedGameObject.transform.IsChildOf(sheet)) EventSystem.current.SetSelectedGameObject(null);
        confirmHold = 0f;
    }
    public void ApplyToCar(GameObject car) => catalog.Apply(car, committedVehicle, committedPaint);

    public void TickInput(DriveInputState input, bool connected, float dt)
    {
        if (!IsOpen) return;
        if (!connected) { confirmArmed = false; confirmHold = 0f; pedalHint.text = "ボタンで決定できます"; return; }
        float steering = Mathf.Abs(input.steering);
        if (steering < 2f) steeringLatched = false;
        if (!steeringLatched && steering >= 5f) { SelectVehicle(draftVehicle + (input.steering > 0 ? 1 : -1)); steeringLatched = true; }
        if (input.pedal < .2f) { confirmArmed = true; confirmHold = 0f; }
        if (confirmArmed && input.readyPressed) { Confirm(); return; }
        if (confirmArmed && input.pedal >= .8f)
        {
            confirmHold += dt;
            if (confirmHold >= .65f) { Confirm(); Gmanager.Control?.CancelTitleReady(player); }
        }
        else confirmHold = 0f;
        if (IsOpen) pedalHint.text = confirmHold > 0f ? $"ペダルで決定  {Mathf.Clamp01(confirmHold / .65f) * 100f:0}%" :
            confirmArmed ? "ペダル長押しでも決定できます" : "ペダルを離してから踏み込む";
    }

    private void Refresh(bool changeModel)
    {
        CarSelectionCatalog.Vehicle vehicle = catalog.vehicles[draftVehicle];
        modelName.text = vehicle.displayName; grade.text = "TSUKUKOMA MOTORS  /  " + vehicle.grade;
        description.text = vehicle.description; bodyType.text = vehicle.bodyType + "   /   ゲーム内性能・10段階評価";
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
    }
    private void UpdateTitleSelection() => titleSelection.text = catalog.vehicles[committedVehicle].displayName + "  /  " + CarSelectionCatalog.PaintNames[committedPaint];
    private void Update()
    {
        if (!IsOpen || Keyboard.current == null) return;
        Keyboard keyboard = Keyboard.current;
        if (keyboard.escapeKey.wasPressedThisFrame) Cancel();
        if ((player == 0 ? keyboard.cKey : keyboard.mKey).wasPressedThisFrame) SelectPaint(draftPaint + 1);
        if (player == 0)
        {
            if (keyboard.qKey.isPressed) showroom.Rotate(60f * Time.unscaledDeltaTime);
            if (keyboard.eKey.isPressed) showroom.Rotate(-60f * Time.unscaledDeltaTime);
            if (keyboard.rKey.wasPressedThisFrame) showroom.ResetView();
        }
    }
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
