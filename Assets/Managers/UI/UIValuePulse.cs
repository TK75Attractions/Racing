using System.Collections;
using TMPro;
using UnityEngine;

/// <summary>HUDの数値更新を、読みやすさを保った短いパンチアニメーションで通知します。</summary>
[DisallowMultipleComponent]
public sealed class UIValuePulse : MonoBehaviour
{
    [SerializeField, Min(0.05f)] private float duration = 0.42f;
    [SerializeField, Range(1f, 2f)] private float peakScale = 1.34f;

    private TMP_Text label;
    private Coroutine routine;
    private Vector3 baseScale;
    private Quaternion baseRotation;
    private Color baseColor;

    private void Awake()
    {
        CacheBaseState();
    }

    public void Play(Color accentColor, float peakRotation)
    {
        CacheBaseState();
        if (label == null)
        {
            return;
        }

        if (routine != null)
        {
            StopCoroutine(routine);
            RestoreBaseState();
        }

        routine = StartCoroutine(PlayRoutine(accentColor, peakRotation));
    }

    private IEnumerator PlayRoutine(Color accentColor, float peakRotation)
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float pulse = Mathf.Sin(t * Mathf.PI);
            float wobble = Mathf.Sin(t * Mathf.PI * 2f) * (1f - t);

            transform.localScale = baseScale * Mathf.Lerp(1f, peakScale, pulse);
            transform.localRotation = baseRotation * Quaternion.Euler(0f, 0f, peakRotation * wobble);
            label.color = Color.Lerp(baseColor, accentColor, pulse);
            yield return null;
        }

        RestoreBaseState();
        routine = null;
    }

    private void OnDisable()
    {
        if (routine != null)
        {
            StopCoroutine(routine);
            routine = null;
        }
        RestoreBaseState();
    }

    private void CacheBaseState()
    {
        if (label == null)
        {
            label = GetComponent<TMP_Text>();
            baseScale = transform.localScale;
            baseRotation = transform.localRotation;
            baseColor = label != null ? label.color : Color.white;
        }
    }

    private void RestoreBaseState()
    {
        transform.localScale = baseScale;
        transform.localRotation = baseRotation;
        if (label != null)
        {
            label.color = baseColor;
        }
    }
}
