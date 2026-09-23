using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>プレイヤー単位の中断メニューを実行時に構築します。</summary>
public sealed class InterruptionMenuUI : MonoBehaviour
{
    private GameObject root;
    private TMP_Text directionStatus;
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
        SetDirectionStatus(isReverse);
    }

    public void Hide()
    {
        if (root != null) root.SetActive(false);
    }

    public void SetDirectionStatus(bool isReverse)
    {
        if (directionStatus != null)
            directionStatus.text = isReverse ? "現在: 後退" : "現在: 前進";
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
        Stretch(card.GetComponent<RectTransform>(), new Vector2(0.18f, 0.14f), new Vector2(0.82f, 0.86f));
        Image cardImage = card.GetComponent<Image>();
        cardImage.color = new Color(0.018f, 0.045f, 0.072f, 0.98f);
        Outline outline = card.GetComponent<Outline>() ?? card.AddComponent<Outline>();
        outline.effectColor = new Color(0.18f, 0.75f, 1f, 0.85f);
        outline.effectDistance = new Vector2(2f, -2f);

        TMP_Text title = CreateLabel(card.transform, "Title", "PAUSE", new Vector2(0.08f, 0.79f), new Vector2(0.92f, 0.95f), 58f, FontRole.English);
        title.fontStyle = FontStyles.Bold | FontStyles.Italic;
        TMP_Text subtitle = CreateLabel(card.transform, "Subtitle", "操作を一時停止しています", new Vector2(0.08f, 0.72f), new Vector2(0.92f, 0.80f), 22f, FontRole.Japanese);
        subtitle.color = new Color(0.66f, 0.76f, 0.84f, 1f);

        CreateButton(card.transform, "Interrupt", "中断する", new Vector2(0.11f, 0.53f), new Vector2(0.89f, 0.67f), () => interruptAction?.Invoke());
        CreateButton(card.transform, "ToggleDirection", "前進後退切り替え", new Vector2(0.11f, 0.34f), new Vector2(0.89f, 0.48f), () => toggleDirectionAction?.Invoke());
        directionStatus = CreateLabel(card.transform, "DirectionStatus", string.Empty, new Vector2(0.11f, 0.285f), new Vector2(0.89f, 0.34f), 16f, FontRole.Japanese);
        directionStatus.color = new Color(0.35f, 0.85f, 1f, 1f);
        CreateButton(card.transform, "Restart", "スタートから", new Vector2(0.11f, 0.10f), new Vector2(0.89f, 0.24f), () => restartAction?.Invoke());

        TMP_Text hint = CreateLabel(card.transform, "Hint", "ESCでもどる", new Vector2(0.11f, 0.025f), new Vector2(0.89f, 0.085f), 16f, FontRole.Japanese);
        hint.color = new Color(0.52f, 0.62f, 0.70f, 1f);
    }

    private static void CreateButton(Transform parent, string name, string caption, Vector2 min, Vector2 max, UnityEngine.Events.UnityAction action)
    {
        GameObject buttonObject = GetOrCreate(name, parent, typeof(Image), typeof(Button));
        Stretch(buttonObject.GetComponent<RectTransform>(), min, max);
        Image image = buttonObject.GetComponent<Image>();
        image.color = new Color(0.035f, 0.12f, 0.18f, 1f);
        Button button = buttonObject.GetComponent<Button>();
        ColorBlock colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(0.45f, 0.9f, 1f, 1f);
        colors.pressedColor = new Color(1f, 0.76f, 0.18f, 1f);
        colors.selectedColor = colors.highlightedColor;
        button.colors = colors;
        button.onClick.RemoveAllListeners();
        button.onClick.AddListener(action);

        TMP_Text label = CreateLabel(buttonObject.transform, "Label", caption, new Vector2(0.06f, 0.08f), new Vector2(0.94f, 0.92f), 28f, FontRole.Japanese);
        label.fontStyle = FontStyles.Bold;
    }

    private static TMP_Text CreateLabel(Transform parent, string name, string value, Vector2 min, Vector2 max, float maxSize, FontRole role)
    {
        GameObject labelObject = GetOrCreate(name, parent, typeof(TextMeshProUGUI));
        Stretch(labelObject.GetComponent<RectTransform>(), min, max);
        TMP_Text label = labelObject.GetComponent<TMP_Text>();
        TMP_FontAsset font = RacingUIFontCatalog.Get(role);
        if (font != null) label.font = font;
        label.text = value;
        label.color = Color.white;
        label.alignment = TextAlignmentOptions.Center;
        label.enableAutoSizing = true;
        label.fontSizeMin = 11f;
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
