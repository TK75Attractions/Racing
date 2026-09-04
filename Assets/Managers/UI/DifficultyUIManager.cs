using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[Serializable]
public class DifficultyUIManager
{
    private GameObject root;
    private TMP_Text titleText;
    private TMP_Text instructionText;
    private TMP_Text[] optionTexts = new TMP_Text[0];
    private Image[] optionBackgrounds = new Image[0];
    private string[] labels = new string[0];
    private int selectedIndex;

    public int SelectedIndex => selectedIndex;

    public void Init(Transform parent, IReadOnlyList<string> optionLabels, int initialIndex)
    {
        if (parent == null)
        {
            Debug.LogWarning("DifficultyUIManager requires a parent Transform.");
            return;
        }

        root = parent.gameObject;
        labels = CopyLabels(optionLabels);

        Image background = GetOrCreateImage(parent, "Background");
        background.color = new Color(0.015f, 0.04f, 0.07f, 0.96f);
        SetFullStretch(background.rectTransform);
        background.transform.SetAsFirstSibling();

        Image topLine = GetOrCreateImage(parent, "TopLine");
        topLine.color = new Color(0.12f, 0.82f, 0.95f, 0.9f);
        SetRect(topLine.rectTransform, new Vector2(0.1f, 0.76f), new Vector2(0.9f, 0.76f), Vector2.zero, new Vector2(0f, 4f));

        Image bottomLine = GetOrCreateImage(parent, "BottomLine");
        bottomLine.color = new Color(1f, 0.68f, 0.22f, 0.9f);
        SetRect(bottomLine.rectTransform, new Vector2(0.1f, 0.22f), new Vector2(0.9f, 0.22f), Vector2.zero, new Vector2(0f, 2f));

        titleText = GetOrCreateText(parent, "TitleText", 62f, TextAlignmentOptions.Center);
        titleText.text = "SELECT DIFFICULTY";
        titleText.color = Color.white;
        titleText.fontStyle = FontStyles.Bold;
        titleText.characterSpacing = 4f;
        titleText.enableAutoSizing = true;
        titleText.fontSizeMin = 36f;
        titleText.fontSizeMax = 72f;
        SetRect(titleText.rectTransform, new Vector2(0.5f, 0.68f), new Vector2(0.5f, 0.68f), Vector2.zero, new Vector2(1300f, 100f));

        instructionText = GetOrCreateText(parent, "Instruction", 25f, TextAlignmentOptions.Center);
        instructionText.text = "TURN HANDLE TO SELECT    PRESS PEDAL TO CONFIRM";
        instructionText.color = new Color(0.55f, 0.8f, 0.86f, 1f);
        instructionText.characterSpacing = 2f;
        instructionText.enableAutoSizing = true;
        instructionText.fontSizeMin = 18f;
        instructionText.fontSizeMax = 30f;
        SetRect(instructionText.rectTransform, new Vector2(0.5f, 0.29f), new Vector2(0.5f, 0.29f), Vector2.zero, new Vector2(1400f, 70f));

        Transform options = GetOrCreateOptions(parent);
        optionTexts = new TMP_Text[labels.Length];
        optionBackgrounds = new Image[labels.Length];

        for (int childIndex = 0; childIndex < options.childCount; childIndex++)
        {
            options.GetChild(childIndex).gameObject.SetActive(false);
        }

        float verticalSpacing = 112f;
        float firstPosition = (labels.Length - 1) * verticalSpacing * 0.5f;
        for (int index = 0; index < labels.Length; index++)
        {
            float y = firstPosition - index * verticalSpacing;
            Image optionBackground = GetOrCreateImage(options, "OptionBackground" + index);
            optionBackgrounds[index] = optionBackground;
            optionBackground.gameObject.SetActive(true);
            SetRect(optionBackground.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, y), new Vector2(780f, 86f));

            TMP_Text optionText = GetOrCreateText(options, "Option" + index, 48f, TextAlignmentOptions.Center);
            optionTexts[index] = optionText;
            optionText.gameObject.SetActive(true);
            optionText.enableAutoSizing = true;
            optionText.fontSizeMin = 30f;
            optionText.fontSizeMax = 56f;
            SetRect(optionText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, y), new Vector2(780f, 86f));
            optionText.transform.SetAsLastSibling();
        }

        SetSelectedIndex(initialIndex);
    }

    public void SetSelectedIndex(int index)
    {
        if (labels.Length == 0)
        {
            selectedIndex = 0;
            return;
        }

        selectedIndex = Mathf.Clamp(index, 0, labels.Length - 1);
        for (int optionIndex = 0; optionIndex < labels.Length; optionIndex++)
        {
            bool isSelected = optionIndex == selectedIndex;
            if (optionTexts[optionIndex] != null)
            {
                optionTexts[optionIndex].text = isSelected ? "> " + labels[optionIndex] + " <" : "  " + labels[optionIndex];
                optionTexts[optionIndex].color = isSelected
                    ? new Color(0.25f, 0.95f, 1f, 1f)
                    : new Color(0.82f, 0.88f, 0.92f, 0.78f);
                optionTexts[optionIndex].fontStyle = isSelected ? FontStyles.Bold : FontStyles.Normal;
                optionTexts[optionIndex].rectTransform.localScale = isSelected ? Vector3.one * 1.06f : Vector3.one;
            }

            if (optionBackgrounds[optionIndex] != null)
            {
                optionBackgrounds[optionIndex].color = isSelected
                    ? new Color(0.1f, 0.76f, 0.9f, 0.2f)
                    : new Color(0.15f, 0.2f, 0.25f, 0.2f);
            }
        }
    }

    public void SetActive(bool isActive)
    {
        if (root != null)
        {
            root.SetActive(isActive);
        }
    }

    private static string[] CopyLabels(IReadOnlyList<string> source)
    {
        int count = source == null || source.Count == 0 ? 1 : source.Count;
        string[] copy = new string[count];
        for (int index = 0; index < count; index++)
        {
            string label = source != null && index < source.Count ? source[index] : null;
            copy[index] = string.IsNullOrEmpty(label) ? "OPTION " + (index + 1) : label;
        }

        return copy;
    }

    private static Transform GetOrCreateOptions(Transform parent)
    {
        Transform options = parent.Find("Options");
        if (options == null)
        {
            GameObject optionsObject = new GameObject("Options", typeof(RectTransform));
            optionsObject.transform.SetParent(parent, false);
            options = optionsObject.transform;
        }

        RectTransform rect = options.GetComponent<RectTransform>();
        SetRect(rect, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 0f), new Vector2(900f, 500f));
        return options;
    }

    private static Image GetOrCreateImage(Transform parent, string objectName)
    {
        Transform existing = parent.Find(objectName);
        Image image = existing != null ? existing.GetComponent<Image>() : null;
        if (image == null)
        {
            GameObject imageObject = new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer));
            imageObject.transform.SetParent(parent, false);
            image = imageObject.AddComponent<Image>();
        }

        if (image.sprite == null)
        {
            image.sprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0f, 0f, 1f, 1f), new Vector2(0.5f, 0.5f), 1f);
            image.type = Image.Type.Simple;
        }

        image.raycastTarget = false;
        return image;
    }

    private static TMP_Text GetOrCreateText(Transform parent, string objectName, float fontSize, TextAlignmentOptions alignment)
    {
        Transform existing = parent.Find(objectName);
        TMP_Text text = existing != null ? existing.GetComponent<TMP_Text>() : null;
        if (text == null)
        {
            GameObject textObject = new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer));
            textObject.transform.SetParent(parent, false);
            text = textObject.AddComponent<TextMeshProUGUI>();
        }

        if (text.font == null)
        {
            text.font = TMP_Settings.defaultFontAsset;
        }

        text.fontSize = fontSize;
        text.alignment = alignment;
        text.raycastTarget = false;
        return text;
    }

    private static void SetFullStretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        rect.localScale = Vector3.one;
    }

    private static void SetRect(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax, Vector2 position, Vector2 size)
    {
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        rect.localScale = Vector3.one;
    }
}
