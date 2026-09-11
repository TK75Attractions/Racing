using System;
using TMPro;
using UnityEngine;

[Serializable]
public class UIPosition
{
    [SerializeField] private GameObject root;
    [SerializeField] private TMP_Text positionText;
    [SerializeField] private int currentPosition = 1;
    private UIValuePulse valuePulse;

    public void Init(Transform parent)
    {
        if (parent != null)
        {
            root = parent.gameObject;
        }

        Transform rootTransform = root != null ? root.transform : parent;
        if (positionText == null && rootTransform != null)
        {
            Transform textTransform = rootTransform.Find("Txt");
            positionText = textTransform != null
                ? textTransform.GetComponentInChildren<TMP_Text>(true)
                : rootTransform.GetComponentInChildren<TMP_Text>(true);
        }

        if (positionText != null)
        {
            valuePulse = positionText.GetComponent<UIValuePulse>();
            if (valuePulse == null)
            {
                valuePulse = positionText.gameObject.AddComponent<UIValuePulse>();
            }
        }

        UpdateText();
    }

    public void SetPosition(int position)
    {
        position = Mathf.Max(1, position);

        if (position != currentPosition)
        {
            PositionTextAnimation(currentPosition, position);
            currentPosition = position;
        }

        UpdateText();
    }

    private void PositionTextAnimation(int oldPosition, int newPosition)
    {
        if (valuePulse == null)
        {
            return;
        }

        // 順位アップはシアン、順位ダウンは赤で一瞬だけ知らせる。
        Color accent = newPosition < oldPosition
            ? new Color(0.15f, 1f, 0.85f, 1f)
            : new Color(1f, 0.32f, 0.22f, 1f);
        valuePulse.Play(accent, newPosition < oldPosition ? -8f : 8f);
    }

    public void SetActive(bool isActive)
    {
        if (root != null)
        {
            root.SetActive(isActive);
        }
    }

    private void UpdateText()
    {
        if (positionText != null)
        {
            positionText.text = currentPosition.ToString();
        }
    }
}
