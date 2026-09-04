using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[Serializable]
public class TitleUIManager
{
    private GameObject root;
    private TMP_Text titleText;
    private TMP_Text captionText;
    private TMP_Text promptText;

    public void Init(Transform parent)
    {
        if (parent == null)
        {
            Debug.LogWarning("TitleUIManager requires a parent Transform.");
            return;
        }

        root = parent.gameObject;

        Image background = GetOrCreateImage(parent, "Background");
        background.color = new Color(0.015f, 0.04f, 0.07f, 0.94f);
        SetFullStretch(background.rectTransform);
        background.transform.SetAsFirstSibling();

        Image topLine = GetOrCreateImage(parent, "TopLine");
        topLine.color = new Color(0.12f, 0.82f, 0.95f, 0.9f);
        SetRect(topLine.rectTransform, new Vector2(0.1f, 0.76f), new Vector2(0.9f, 0.76f), Vector2.zero, new Vector2(0f, 4f));

        Image bottomLine = GetOrCreateImage(parent, "BottomLine");
        bottomLine.color = new Color(1f, 0.68f, 0.22f, 0.9f);
        SetRect(bottomLine.rectTransform, new Vector2(0.1f, 0.22f), new Vector2(0.9f, 0.22f), Vector2.zero, new Vector2(0f, 2f));

        captionText = GetOrCreateText(parent, "Caption", 24f, TextAlignmentOptions.Center);
        captionText.text = "RACING EXPERIENCE";
        captionText.color = new Color(0.55f, 0.8f, 0.86f, 1f);
        captionText.characterSpacing = 6f;
        SetRect(captionText.rectTransform, new Vector2(0.5f, 0.7f), new Vector2(0.5f, 0.7f), Vector2.zero, new Vector2(1200f, 60f));

        titleText = GetOrCreateText(parent, "TitleText", 96f, TextAlignmentOptions.Center);
        titleText.text = "Tsukukoma Circuit";
        titleText.color = Color.white;
        titleText.fontStyle = FontStyles.Bold;
        titleText.characterSpacing = 3f;
        titleText.enableAutoSizing = true;
        titleText.fontSizeMin = 48f;
        titleText.fontSizeMax = 108f;
        SetRect(titleText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1600f, 190f));

        promptText = GetOrCreateText(parent, "StartPrompt", 34f, TextAlignmentOptions.Center);
        promptText.text = "PRESS PEDAL TO START";
        promptText.color = new Color(1f, 0.78f, 0.3f, 1f);
        promptText.characterSpacing = 4f;
        promptText.enableAutoSizing = true;
        promptText.fontSizeMin = 24f;
        promptText.fontSizeMax = 42f;
        SetRect(promptText.rectTransform, new Vector2(0.5f, 0.3f), new Vector2(0.5f, 0.3f), Vector2.zero, new Vector2(1100f, 90f));
    }

    public void SetActive(bool isActive)
    {
        if (root != null)
        {
            root.SetActive(isActive);
        }
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
