using UnityEngine;
using UnityEngine.UI;

/// <summary>選択時の弾性振動、踏み込み中の縁光、決定時の全体フラッシュ。</summary>
[DisallowMultipleComponent]
public sealed class PedalButtonFeedback : MonoBehaviour
{
    private RectTransform rect;
    private PedalButtonSurface surface;
    private Vector2 basePosition;
    private Vector3 baseScale;
    private Quaternion baseRotation;
    private bool selected;
    private bool confirmed;
    private float selectedAt;
    private float confirmedAt = -10f;
    private float pedalAmount;
    private float displayedPedal;
    private Color accent;

    public void Configure(Color color, RacingPanelGraphic.SurfaceStyle style = RacingPanelGraphic.SurfaceStyle.Primary)
    {
        rect = GetComponent<RectTransform>();
        basePosition = rect.anchoredPosition;
        baseScale = rect.localScale;
        baseRotation = rect.localRotation;
        GetComponent<Image>().enabled = false;
        Outline outline = GetComponent<Outline>();
        if (outline != null) outline.enabled = false;
        Transform existing = transform.Find("ButtonSurface");
        GameObject obj = existing != null ? existing.gameObject : new GameObject("ButtonSurface", typeof(RectTransform), typeof(CanvasRenderer), typeof(PedalButtonSurface));
        obj.layer = gameObject.layer;
        obj.transform.SetParent(transform, false);
        obj.transform.SetAsFirstSibling();
        RectTransform face = obj.GetComponent<RectTransform>();
        face.anchorMin = Vector2.zero;
        face.anchorMax = Vector2.one;
        face.offsetMin = face.offsetMax = Vector2.zero;
        surface = obj.GetComponent<PedalButtonSurface>();
        surface.Configure(style, color);
        accent = color;
        selected = false;
        SetState(false, 0f, color);
    }

    public void SetState(bool isSelected, float pedal, Color color)
    {
        if (rect == null) return;
        if (isSelected && !selected) selectedAt = Time.unscaledTime;
        selected = isSelected;
        pedalAmount = Mathf.Clamp01(pedal);
        accent = color;
    }

    public void SetConfirmed(bool value)
    {
        if (value && !confirmed) PlayConfirm();
        confirmed = value;
    }

    public void PlayConfirm() => confirmedAt = Time.unscaledTime;

    private void OnEnable()
    {
        if (selected) selectedAt = Time.unscaledTime;
    }

    private void Update()
    {
        if (rect == null) return;
        float age = Time.unscaledTime - selectedAt;
        float wobble = selected ? Mathf.Sin(age * 25f) * Mathf.Exp(-age * 6f) : 0f;
        float bob = selected ? Mathf.Sin(age * 3.4f) * 1.2f : 0f;
        float flashAge = Time.unscaledTime - confirmedAt;
        float flash = flashAge < 0.45f ? Mathf.Sin(Mathf.Clamp01(flashAge / 0.45f) * Mathf.PI) : 0f;
        float scale = (selected ? 1.012f : 1f) + flash * 0.035f;
        rect.anchoredPosition = basePosition + Vector2.up * bob;
        rect.localScale = Vector3.Scale(baseScale, new Vector3(scale + wobble * 0.06f, scale - wobble * 0.05f, 1f));
        rect.localRotation = baseRotation * Quaternion.Euler(0f, 0f, wobble * 1.8f);
        displayedPedal = Mathf.MoveTowards(displayedPedal, pedalAmount, Time.unscaledDeltaTime * 6f);
        surface.SetVisual(displayedPedal, selected, flash, accent);
    }

    private void OnDisable()
    {
        if (rect == null) return;
        rect.anchoredPosition = basePosition;
        rect.localScale = baseScale;
        rect.localRotation = baseRotation;
        pedalAmount = displayedPedal = 0f;
        confirmedAt = -10f;
        confirmed = selected = false;
        surface.SetVisual(0f, false, 0f, accent);
    }
}
