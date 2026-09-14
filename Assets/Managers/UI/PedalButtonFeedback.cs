using UnityEngine;
using UnityEngine.UI;

/// <summary>全てのペダル操作カードに共通の背景塗り・選択時の弾性振動を適用します。</summary>
[DisallowMultipleComponent]
public sealed class PedalButtonFeedback : MonoBehaviour
{
    private RectTransform rect;
    private RectTransform fill;
    private Image background;
    private Image fillImage;
    private Outline outline;
    private Vector2 basePosition;
    private Vector3 baseScale;
    private Quaternion baseRotation;
    private Color baseColor;
    private Color accent;
    private bool selected;
    private float selectedAt;

    public void Configure(Color color)
    {
        rect = GetComponent<RectTransform>();
        background = GetComponent<Image>();
        outline = GetComponent<Outline>();
        basePosition = rect.anchoredPosition;
        baseScale = rect.localScale;
        baseRotation = rect.localRotation;
        baseColor = background.color;
        accent = color;
        Transform existing = transform.Find("PedalBackgroundFill");
        GameObject obj = existing != null ? existing.gameObject : new GameObject("PedalBackgroundFill", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        obj.layer = gameObject.layer;
        obj.transform.SetParent(transform, false);
        obj.transform.SetAsFirstSibling();
        fill = obj.GetComponent<RectTransform>();
        fill.anchorMin = Vector2.zero;
        fill.anchorMax = new Vector2(0f, 1f);
        fill.offsetMin = Vector2.zero;
        fill.offsetMax = Vector2.zero;
        fillImage = obj.GetComponent<Image>();
        fillImage.raycastTarget = false;
        selected = false;
        SetState(false, 0f, color);
    }

    public void SetState(bool isSelected, float pedal, Color color)
    {
        if (rect == null) return;
        if (isSelected && !selected) selectedAt = Time.unscaledTime;
        selected = isSelected;
        accent = color;
        fill.anchorMax = new Vector2(Mathf.Clamp01(pedal), 1f);
        // 半透明の塗りで文字のコントラストを保ち、左から面全体を染めます。
        fillImage.color = new Color(accent.r, accent.g, accent.b, 0.42f);
    }

    private void OnEnable()
    {
        if (selected) selectedAt = Time.unscaledTime;
    }

    private void Update()
    {
        if (rect == null) return;
        float age = Time.unscaledTime - selectedAt;
        float wobble = selected ? Mathf.Sin(age * 25f) * Mathf.Exp(-age * 6f) : 0f;
        float bob = selected ? Mathf.Sin(age * 3.4f) * 7f : 0f;
        float scale = selected ? 1.025f : 1f;
        rect.anchoredPosition = basePosition + Vector2.up * bob;
        rect.localScale = Vector3.Scale(baseScale, new Vector3(scale + wobble * 0.075f, scale - wobble * 0.065f, 1f));
        rect.localRotation = baseRotation * Quaternion.Euler(0f, 0f, wobble * 2.5f);
        Color target = selected ? Color.Lerp(baseColor, new Color(accent.r, accent.g, accent.b, baseColor.a), 0.18f) : baseColor;
        background.color = Color.Lerp(background.color, target, 1f - Mathf.Exp(-Time.unscaledDeltaTime * 12f));
        if (outline != null)
        {
            Color border = selected ? accent : new Color(0.35f, 0.43f, 0.52f, 0.55f);
            outline.effectColor = Color.Lerp(outline.effectColor, border, 1f - Mathf.Exp(-Time.unscaledDeltaTime * 12f));
            outline.effectDistance = selected ? new Vector2(3f, -3f) : new Vector2(2f, -2f);
        }
    }

    private void OnDisable()
    {
        if (rect == null) return;
        rect.anchoredPosition = basePosition;
        rect.localScale = baseScale;
        rect.localRotation = baseRotation;
        background.color = baseColor;
        fill.anchorMax = new Vector2(0f, 1f);
        selected = false;
    }
}
