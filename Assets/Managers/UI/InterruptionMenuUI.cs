using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

/// <summary>プレイヤー単位の中断メニューを実行時に構築します。</summary>
public sealed class InterruptionMenuUI : MonoBehaviour
{
    private GameObject root;
    private TMP_Text directionStatus;
    private RacingMenuButton firstButton;
    private GameObject optionsPanel;
    private RacingMenuButton optionsButton;
    private Slider masterSlider;
    private Slider bgmSlider;
    private Slider engineSlider;
    private Slider effectsSlider;
    private readonly TMP_Dropdown[] deviceDropdowns = new TMP_Dropdown[2];
    private readonly List<int>[] deviceIndices = { new List<int>(), new List<int>() };
    private string[] shownDevices;
    private readonly string[] shownDeviceStatus = new string[2];
    private readonly int[] shownSelectedDevices = { int.MinValue, int.MinValue };
    private Action interruptAction;
    private Action toggleDirectionAction;
    private Action restartAction;

    public bool IsOpen => root != null && root.activeSelf;

    public static InterruptionMenuUI Create(
        Transform canvasRoot,
        Action interruptAction,
        Action toggleDirectionAction,
        Action restartAction)
    {
        if (canvasRoot == null) return null;

        InterruptionMenuUI menu = canvasRoot.GetComponent<InterruptionMenuUI>();
        if (menu == null) menu = canvasRoot.gameObject.AddComponent<InterruptionMenuUI>();
        menu.interruptAction = interruptAction;
        menu.toggleDirectionAction = toggleDirectionAction;
        menu.restartAction = restartAction;
        menu.Build(canvasRoot);
        menu.Hide();
        return menu;
    }

    public void Show(bool isReverse)
    {
        if (root == null) return;
        root.transform.SetAsLastSibling();
        root.SetActive(true);
        ShowOptions(false);
        SetDirectionStatus(isReverse);
        if (EventSystem.current != null && firstButton != null)
            EventSystem.current.SetSelectedGameObject(firstButton.gameObject);
    }

    public void Hide()
    {
        if (root == null) return;
        GameObject selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
        if (selected != null && selected.transform.IsChildOf(root.transform))
            EventSystem.current.SetSelectedGameObject(null);
        root.SetActive(false);
    }

    private void ShowOptions(bool visible)
    {
        if (optionsPanel == null) return;
        optionsPanel.SetActive(visible);
        foreach (string name in new[] { "Restart", "ToggleDirection", "Options", "Interrupt", "Subtitle" })
            root.transform.Find("MenuCard/" + name)?.gameObject.SetActive(!visible);
        if (visible)
        {
            masterSlider.SetValueWithoutNotify(RaceAudioSettings.Master);
            bgmSlider.SetValueWithoutNotify(RaceAudioSettings.Bgm);
            engineSlider.SetValueWithoutNotify(RaceAudioSettings.Engine);
            effectsSlider.SetValueWithoutNotify(RaceAudioSettings.Effects);
            RefreshDeviceLabels();
        }
        GameObject selected = visible ? masterSlider.gameObject : optionsButton.gameObject;
        if (root.activeInHierarchy && EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(selected);
    }

    public void SetDirectionStatus(bool isReverse)
    {
        if (directionStatus == null) return;
        directionStatus.text = isReverse ? "後退" : "前進";
        directionStatus.color = isReverse ? RacingUITheme.Gold : RacingUITheme.Cyan;
    }

    private void Build(Transform canvasRoot)
    {
        Transform existing = canvasRoot.Find("InterruptionMenu");
        root = existing != null
            ? existing.gameObject
            : CreateObject("InterruptionMenu", canvasRoot, typeof(Image));
        Stretch(root.GetComponent<RectTransform>(), Vector2.zero, Vector2.one);
        Image backdrop = root.GetComponent<Image>();
        backdrop.color = new Color(0.003f, 0.008f, 0.015f, 0.82f);
        backdrop.raycastTarget = true;

        GameObject card = GetOrCreate("MenuCard", root.transform, typeof(Image));
        Stretch(card.GetComponent<RectTransform>(), new Vector2(0.27f, 0.12f), new Vector2(0.73f, 0.88f));
        RacingUITheme.Surface(card.transform);
        RacingUITheme.Rule(card.transform, "HeaderAccent", new Vector2(0.08f, 0.926f), new Vector2(0.125f, 0.93f), RacingUITheme.Cyan);
        TMP_Text eyebrow = CreateLabel(card.transform, "Eyebrow", "RACE MENU", new Vector2(0.15f, 0.895f), new Vector2(0.92f, 0.957f), 16f, FontRole.English);
        eyebrow.alignment = TextAlignmentOptions.Left;
        eyebrow.color = RacingUITheme.Muted;
        eyebrow.characterSpacing = 3f;
        TMP_Text title = CreateLabel(card.transform, "Title", "PAUSE", new Vector2(0.08f, 0.78f), new Vector2(0.92f, 0.89f), 58f, FontRole.English);
        title.alignment = TextAlignmentOptions.Left;
        title.fontStyle = FontStyles.Bold | FontStyles.Italic;
        TMP_Text subtitle = CreateLabel(card.transform, "Subtitle", "あなたの操作を一時停止しています", new Vector2(0.08f, 0.72f), new Vector2(0.92f, 0.78f), 21f, FontRole.Japanese);
        subtitle.alignment = TextAlignmentOptions.Left;
        subtitle.color = RacingUITheme.Muted;

        firstButton = CreateButton(card.transform, "Restart", "スタートから", "RETURN TO START", "01", new Vector2(0.08f, 0.595f), new Vector2(0.92f, 0.705f),
            () => restartAction?.Invoke(), RacingPanelGraphic.SurfaceStyle.Primary);
        RacingMenuButton direction = CreateButton(card.transform, "ToggleDirection", "走行方向を切り替え", "CHANGE DIRECTION", "02", new Vector2(0.08f, 0.455f), new Vector2(0.92f, 0.565f),
            () => toggleDirectionAction?.Invoke());
        directionStatus = CreateLabel(direction.transform, "DirectionStatus", string.Empty, new Vector2(0.78f, 0.35f), new Vector2(0.95f, 0.73f), 22f, FontRole.Japanese);
        direction.transform.Find("Label").GetComponent<RectTransform>().anchorMax = new Vector2(0.75f, 0.85f);
        direction.GetComponent<PedalButtonFeedback>().CacheContent();
        optionsButton = CreateButton(card.transform, "Options", "設定", "AUDIO SETTINGS", "03", new Vector2(0.08f, 0.315f), new Vector2(0.92f, 0.425f),
            () => ShowOptions(true));
        CreateButton(card.transform, "Interrupt", "中断する", "LEAVE RACE", "04", new Vector2(0.08f, 0.175f), new Vector2(0.92f, 0.285f),
            () => interruptAction?.Invoke());
        RacingUITheme.Rule(card.transform, "FooterDivider", new Vector2(0.08f, 0.125f), new Vector2(0.92f, 0.1265f), new Color(0.3f, 0.45f, 0.55f, 0.4f));
        TMP_Text hint = CreateLabel(card.transform, "Hint", "ESC  /  走行にもどる", new Vector2(0.08f, 0.035f), new Vector2(0.92f, 0.105f), 18f, FontRole.Japanese);
        hint.color = RacingUITheme.Muted;

        optionsPanel = GetOrCreate("OptionsPanel", card.transform, typeof(Image));
        Stretch(optionsPanel.GetComponent<RectTransform>(), new Vector2(0.04f, 0.02f), new Vector2(0.96f, 0.78f));
        Image optionsBackground = optionsPanel.GetComponent<Image>();
        optionsBackground.color = new Color(0.012f, 0.035f, 0.055f, 0.98f);
        optionsBackground.raycastTarget = true;
        masterSlider = CreateVolumeSlider(optionsPanel.transform, "Master", "Master", 0.82f, RaceAudioSettings.SetMaster);
        bgmSlider = CreateVolumeSlider(optionsPanel.transform, "BGM", "BGM", 0.68f, RaceAudioSettings.SetBgm);
        engineSlider = CreateVolumeSlider(optionsPanel.transform, "Engine", "エンジン音", 0.54f, RaceAudioSettings.SetEngine);
        effectsSlider = CreateVolumeSlider(optionsPanel.transform, "Effects", "効果音", 0.40f, RaceAudioSettings.SetEffects);
        for (int player = 0; player < 2; player++)
        {
            int capturedPlayer = player;
            float bottom = player == 0 ? 0.25f : 0.125f;
            CreateLabel(optionsPanel.transform, $"P{player + 1}OutputLabel", $"P{player + 1} 出力", new Vector2(.08f, bottom), new Vector2(.30f, bottom + .105f), 21f, FontRole.Japanese);
            TMP_Dropdown dropdown = CreateDeviceDropdown(optionsPanel.transform, $"P{player + 1}Output", bottom);
            deviceDropdowns[player] = dropdown;
            dropdown.onValueChanged.AddListener(value =>
                {
                    if (value < 0 || value >= deviceIndices[capturedPlayer].Count) return;
                    RaceAudioOutput.Instance?.SelectDevice(capturedPlayer, deviceIndices[capturedPlayer][value]);
                    shownDevices = null;
                    RefreshDeviceLabels();
                });
        }
        RacingMenuButton refresh = CreateButton(optionsPanel.transform, "RefreshDevices", "再検出", "", "", new Vector2(0.08f, 0.02f), new Vector2(0.48f, 0.11f), () =>
        {
            RaceAudioOutput.Instance?.RefreshDevices();
            RefreshDeviceLabels();
        });
        RacingMenuButton back = CreateButton(optionsPanel.transform, "Back", "戻る", "BACK", "", new Vector2(0.52f, 0.02f), new Vector2(0.92f, 0.11f),
            () => ShowOptions(false));
        foreach (RacingMenuButton button in new[] { refresh, back })
        {
            TMP_Text caption = button.transform.Find("Label").GetComponent<TMP_Text>();
            caption.fontSizeMin = 14f; caption.fontSizeMax = 22f;
            caption.rectTransform.anchorMin = new Vector2(.28f,.18f);
            caption.rectTransform.anchorMax = new Vector2(.87f,.82f);
            button.transform.Find("Caption").GetComponent<TMP_Text>().text = string.Empty;
        }
        optionsPanel.SetActive(false);
    }

    private void RefreshDeviceLabels()
    {
        RaceAudioOutput output = RaceAudioOutput.Instance;
        string[] names = output != null ? output.DeviceNames : Array.Empty<string>();
        bool changed = shownDevices != names;
        for (int player = 0; player < 2; player++)
            changed |= shownSelectedDevices[player] != (output?.GetSelectedDevice(player) ?? -1) ||
                shownDeviceStatus[player] != (output?.GetDeviceLabel(player) ?? "未接続");
        if (!changed) return;
        shownDevices = names;
        for (int player = 0; player < 2; player++)
        {
            TMP_Dropdown dropdown = deviceDropdowns[player];
            if (dropdown == null) continue;
            dropdown.Hide();
            var options = new List<TMP_Dropdown.OptionData> { new TMP_Dropdown.OptionData("出力なし") };
            List<int> indices = deviceIndices[player];
            indices.Clear(); indices.Add(-1);
            int selected = output?.GetSelectedDevice(player) ?? -1;
            for (int device = 0; device < names.Length; device++)
                if (output.CanSelectDevice(player, device))
                {
                    options.Add(new TMP_Dropdown.OptionData(names[device]));
                    indices.Add(device);
                }
            dropdown.ClearOptions(); dropdown.AddOptions(options);
            dropdown.SetValueWithoutNotify(Mathf.Max(0, indices.IndexOf(selected)));
            shownSelectedDevices[player] = selected;
            shownDeviceStatus[player] = output?.GetDeviceLabel(player) ?? "未接続";
            dropdown.captionText.text = shownDeviceStatus[player];
            dropdown.interactable = output != null && output.CanSelectDevice(player, -1);
        }
    }

    private static TMP_Dropdown CreateDeviceDropdown(Transform parent, string name, float bottom)
    {
        GameObject control = GetOrCreate(name, parent, typeof(Image), typeof(RaceAudioDeviceDropdown));
        Stretch(control.GetComponent<RectTransform>(), new Vector2(.31f, bottom + .015f), new Vector2(.92f, bottom + .095f));
        Image background = control.GetComponent<Image>(); background.color = new Color(.06f, .14f, .20f);
        TMP_Dropdown dropdown = control.GetComponent<TMP_Dropdown>(); dropdown.targetGraphic = background;
        TMP_Text caption = CreateLabel(control.transform, "Caption", "出力なし", new Vector2(.04f, .08f), new Vector2(.88f, .92f), 21f, FontRole.Japanese);
        caption.alignment = TextAlignmentOptions.Left;
        CreateLabel(control.transform, "Arrow", "▾", new Vector2(.88f, .05f), new Vector2(.98f, .95f), 24f, FontRole.English);
        GameObject template = GetOrCreate("Template", control.transform, typeof(Image), typeof(ScrollRect));
        RectTransform templateRect = template.GetComponent<RectTransform>();
        Stretch(templateRect, Vector2.zero, new Vector2(1f, 0f));
        templateRect.pivot = new Vector2(.5f, 1f); templateRect.sizeDelta = new Vector2(0f, 240f);
        template.GetComponent<Image>().color = new Color(.025f, .06f, .10f, 1f);
        GameObject viewport = GetOrCreate("Viewport", template.transform, typeof(RectMask2D));
        Stretch(viewport.GetComponent<RectTransform>(), Vector2.zero, Vector2.one);
        GameObject content = GetOrCreate("Content", viewport.transform);
        RectTransform contentRect = content.GetComponent<RectTransform>();
        Stretch(contentRect, new Vector2(0f, 1f), Vector2.one);
        contentRect.pivot = new Vector2(.5f, 1f); contentRect.sizeDelta = new Vector2(0f, 44f);
        GameObject item = GetOrCreate("Item", content.transform, typeof(Image), typeof(Toggle));
        RectTransform itemRect = item.GetComponent<RectTransform>();
        Stretch(itemRect, new Vector2(0f, .5f), new Vector2(1f, .5f)); itemRect.sizeDelta = new Vector2(0f, 44f);
        Image itemBackground = item.GetComponent<Image>(); itemBackground.color = new Color(.08f, .18f, .24f);
        Toggle toggle = item.GetComponent<Toggle>(); toggle.targetGraphic = itemBackground;
        GameObject check = GetOrCreate("Check", item.transform, typeof(Image));
        Stretch(check.GetComponent<RectTransform>(), new Vector2(.02f, .25f), new Vector2(.035f, .75f));
        check.GetComponent<Image>().color = RacingUITheme.Cyan; toggle.graphic = check.GetComponent<Image>();
        TMP_Text itemLabel = CreateLabel(item.transform, "Label", "", new Vector2(.055f, .08f), new Vector2(.96f, .92f), 21f, FontRole.Japanese);
        itemLabel.alignment = TextAlignmentOptions.Left;
        ScrollRect scroll = template.GetComponent<ScrollRect>();
        scroll.content = contentRect; scroll.viewport = viewport.GetComponent<RectTransform>();
        scroll.horizontal = false; scroll.vertical = true; scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 30f;
        dropdown.template = templateRect; dropdown.captionText = caption; dropdown.itemText = itemLabel;
        template.SetActive(false);
        return dropdown;
    }

    private void Update()
    {
        if (IsOpen && optionsPanel != null && optionsPanel.activeSelf) RefreshDeviceLabels();
    }

    private static Slider CreateVolumeSlider(Transform parent, string name, string caption, float top,
        UnityEngine.Events.UnityAction<float> onChanged)
    {
        TMP_Text label = CreateLabel(parent, name + "Label", caption, new Vector2(0.08f, top), new Vector2(0.45f, top + 0.15f), 23f, FontRole.Japanese);
        label.alignment = TextAlignmentOptions.Left;
        GameObject sliderObject = GetOrCreate(name + "Slider", parent, typeof(Slider));
        Stretch(sliderObject.GetComponent<RectTransform>(), new Vector2(0.45f, top + 0.015f), new Vector2(0.92f, top + 0.125f));
        Image hit = sliderObject.GetComponent<Image>() ?? sliderObject.AddComponent<Image>();
        hit.color = Color.clear; hit.raycastTarget = true;
        GameObject track = GetOrCreate("Track", sliderObject.transform, typeof(Image));
        Stretch(track.GetComponent<RectTransform>(), new Vector2(0f, 0.45f), new Vector2(1f, 0.55f));
        track.GetComponent<Image>().color = new Color(0.12f, 0.21f, 0.27f);
        GameObject fillArea = GetOrCreate("FillArea", sliderObject.transform);
        Stretch(fillArea.GetComponent<RectTransform>(), new Vector2(0f, 0.45f), new Vector2(1f, 0.55f));
        GameObject fill = GetOrCreate("Fill", fillArea.transform, typeof(Image));
        Stretch(fill.GetComponent<RectTransform>(), Vector2.zero, Vector2.one);
        fill.GetComponent<Image>().color = RacingUITheme.Cyan;
        GameObject handleArea = GetOrCreate("HandleArea", sliderObject.transform);
        Stretch(handleArea.GetComponent<RectTransform>(), new Vector2(0f, 0.3f), new Vector2(1f, 0.7f));
        GameObject handle = GetOrCreate("Handle", handleArea.transform, typeof(Image));
        RectTransform handleRect = handle.GetComponent<RectTransform>();
        Stretch(handleRect, Vector2.zero, new Vector2(0f, 1f));
        handleRect.sizeDelta = new Vector2(10f, 0f);
        handle.GetComponent<Image>().color = Color.white;
        Slider slider = sliderObject.GetComponent<Slider>();
        slider.direction = Slider.Direction.LeftToRight;
        slider.minValue = 0f;
        slider.maxValue = 1f;
        slider.fillRect = fill.GetComponent<RectTransform>();
        slider.handleRect = handleRect;
        slider.targetGraphic = handle.GetComponent<Image>();
        slider.onValueChanged.RemoveAllListeners();
        slider.onValueChanged.AddListener(onChanged);
        return slider;
    }

    private static RacingMenuButton CreateButton(Transform parent, string name, string caption, string english, string number,
        Vector2 min, Vector2 max, UnityEngine.Events.UnityAction action, RacingPanelGraphic.SurfaceStyle style = RacingPanelGraphic.SurfaceStyle.Secondary)
    {
        bool danger=name=="Interrupt";
        RacingIconGraphic.Icon icon=name=="Restart"?RacingIconGraphic.Icon.Retry:danger?RacingIconGraphic.Icon.Power:name.Contains("Back")?RacingIconGraphic.Icon.Back:RacingIconGraphic.Icon.Gear;
        RacingMenuButton button=NeonUI.Button(parent,name,caption,english,icon,min,max,style==RacingPanelGraphic.SurfaceStyle.Primary,action);
        if(danger)
        {
            RacingPanelGraphic surface=button.transform.Find("ModernSurface").GetComponent<RacingPanelGraphic>();
            surface.Configure(RacingPanelGraphic.SurfaceStyle.Danger,NeonUI.Red);button.Configure(surface);button.ConfigureHold(.65f,false);
        }
        button.GetComponent<PedalButtonFeedback>().CacheContent();
        return button;
    }

    private static TMP_Text CreateLabel(Transform parent, string name, string value, Vector2 min, Vector2 max, float maxSize, FontRole role)
    {
        GameObject labelObject = GetOrCreate(name, parent, typeof(TextMeshProUGUI));
        Stretch(labelObject.GetComponent<RectTransform>(), min, max);
        TMP_Text label = labelObject.GetComponent<TMP_Text>();
        RacingUITheme.ApplyTypography(label, role, maxSize);
        label.text = value;
        label.color = Color.white;
        label.alignment = TextAlignmentOptions.Center;
        label.enableAutoSizing = true;
        label.fontSizeMin = Mathf.Min(maxSize, Mathf.Max(14f, maxSize * 0.7f));
        label.fontSizeMax = maxSize;
        label.raycastTarget = false;
        return label;
    }

    private static GameObject GetOrCreate(string name, Transform parent, params Type[] components)
    {
        Transform existing = parent.Find(name);
        return existing != null ? existing.gameObject : CreateObject(name, parent, components);
    }

    private static GameObject CreateObject(string name, Transform parent, params Type[] components)
    {
        Type[] types = new Type[components.Length + 2];
        types[0] = typeof(RectTransform);
        types[1] = typeof(CanvasRenderer);
        Array.Copy(components, 0, types, 2, components.Length);
        GameObject created = new GameObject(name, types);
        created.layer = parent.gameObject.layer;
        created.transform.SetParent(parent, false);
        return created;
    }

    private static void Stretch(RectTransform rect, Vector2 min, Vector2 max)
    {
        rect.anchorMin = min;
        rect.anchorMax = max;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        rect.localScale = Vector3.one;
    }
}
