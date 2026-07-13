using System;
using TMPro;
using UnityEngine;

[Serializable]
public class UIPosition
{
    [SerializeField] private GameObject root;
    [SerializeField] private TMP_Text positionText;
    [SerializeField] private int currentPosition = 1;

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
        // Implement animation logic here if needed
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
