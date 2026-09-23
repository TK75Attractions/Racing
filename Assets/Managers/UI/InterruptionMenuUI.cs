using System;
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
        if (visible)
        {
            masterSlider.SetValueWithoutNotify(RaceAudioSettings.Master);
            bgmSlider.SetValueWithoutNotify(RaceAudioSettings.Bgm);
            engineSlider.SetValueWithoutNotify(RaceAudioSettings.Engine);
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
        optionsButton = CreateButton(card.transform, "Options", "設定", "AUDIO SETTINGS", "03", new Vector2(0.08f, 0.315f), new Vector2(0.92f, 0.425f),
            () => ShowOptions(true));
        CreateButton(card.transform, "Interrupt", "中断する", "LEAVE RACE", "04", new Vector2(0.08f, 0.175f), new Vector2(0.92f, 0.285f),
            () => interruptAction?.Invoke());
        RacingUITheme.Rule(card.transform, "FooterDivider", new Vector2(0.08f, 0.125f), new Vector2(0.92f, 0.1265f), new Color(0.3f, 0.45f, 0.55f, 0.4f));
        TMP_Text hint = CreateLabel(card.transform, "Hint", "ESC  /  走行にもどる", new Vector2(0.08f, 0.035f), new Vector2(0.92f, 0.105f), 18f, FontRole.Japanese);
        hint.color = RacingUITheme.Muted;

        optionsPanel = GetOrCreate("OptionsPanel", card.transform, typeof(Image));
        Stretch(optionsPanel.GetComponent<RectTransform>(), new Vector2(0.04f, 0.14f), new Vector2(0.96f, 0.73f));
        Image optionsBackground = optionsPanel.GetComponent<Image>();
        optionsBackground.color = new Color(0.012f, 0.035f, 0.055f, 0.98f);
        optionsBackground.raycastTarget = true;
        masterSlider = CreateVolumeSlider(optionsPanel.transform, "Master", "Master", 0.74f, RaceAudioSettings.SetMaster);
        bgmSlider = CreateVolumeSlider(optionsPanel.transform, "BGM", "BGM", 0.52f, RaceAudioSettings.SetBgm);
        engineSlider = CreateVolumeSlider(optionsPanel.transform, "Engine", "エンジン音", 0.30f, RaceAudioSettings.SetEngine);
        CreateButton(optionsPanel.transform, "Back", "戻る", "BACK", "", new Vector2(0.08f, 0.04f), new Vector2(0.92f, 0.19f),
            () => ShowOptions(false));
        optionsPanel.SetActive(false);
    }

    private static Slider CreateVolumeSlider(Transform parent, string name, string caption, float top,
        UnityEngine.Events.UnityAction<float> onChanged)
    {
        TMP_Text label = CreateLabel(parent, name + "Label", caption, new Vector2(0.08f, top), new Vector2(0.45f, top + 0.15f), 23f, FontRole.Japanese);
        label.alignment = TextAlignmentOptions.Left;
        GameObject sliderObject = GetOrCreate(name + "Slider", parent, typeof(Slider));
        Stretch(sliderObject.GetComponent<RectTransform>(), new Vector2(0.45f, top + 0.015f), new Vector2(0.92f, top + 0.125f));
        GameObject track = GetOrCreate("Track", sliderObject.transform, typeof(Image));
        Stretch(track.GetComponent<RectTransform>(), new Vector2(0f, 0.34f), new Vector2(1f, 0.66f));
        track.GetComponent<Image>().color = new Color(0.12f, 0.21f, 0.27f);
        GameObject fill = GetOrCreate("Fill", sliderObject.transform, typeof(Image));
        Stretch(fill.GetComponent<RectTransform>(), new Vector2(0f, 0.34f), new Vector2(1f, 0.66f));
        fill.GetComponent<Image>().color = RacingUITheme.Cyan;
        GameObject handle = GetOrCreate("Handle", sliderObject.transform, typeof(Image));
        RectTransform handleRect = handle.GetComponent<RectTransform>();
        Stretch(handleRect, new Vector2(0f, 0.1f), new Vector2(0f, 0.9f));
        handleRect.sizeDelta = new Vector2(16f, 0f);
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
        GameObject buttonObject = GetOrCreate(name, parent, typeof(Image), typeof(RacingMenuButton));
        Stretch(buttonObject.GetComponent<RectTransform>(), min, max);
        RacingPanelGraphic surface = RacingUITheme.Surface(buttonObject.transform, style);
        // Keep a transparent hit target on the button; decorative graphics never intercept input.
        Image hitTarget = buttonObject.GetComponent<Image>();
        hitTarget.enabled = true;
        hitTarget.color = Color.clear;
        hitTarget.raycastTarget = true;
        RacingMenuButton button = buttonObject.GetComponent<RacingMenuButton>();
        button.targetGraphic = hitTarget;
        button.Configure(surface);
        button.onClick.RemoveAllListeners();
        button.onClick.AddListener(action);
        if (style != RacingPanelGraphic.SurfaceStyle.Primary)
        {
            TMP_Text index = CreateLabel(buttonObject.transform, "Index", number, new Vector2(0.04f, 0.27f), new Vector2(0.14f, 0.8f), 20f, FontRole.English);
            index.color = RacingUITheme.Muted;
        }
        TMP_Text label = CreateLabel(buttonObject.transform, "Label", caption, new Vector2(0.18f, 0.4f), new Vector2(0.93f, 0.85f), 29f, FontRole.Japanese);
        label.alignment = TextAlignmentOptions.Left;
        TMP_Text detail = CreateLabel(buttonObject.transform, "Caption", english, new Vector2(0.18f, 0.18f), new Vector2(0.93f, 0.40f), 14f, FontRole.English);
        detail.alignment = TextAlignmentOptions.Left;
        detail.characterSpacing = 2f;
        detail.color = RacingUITheme.Muted;
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
