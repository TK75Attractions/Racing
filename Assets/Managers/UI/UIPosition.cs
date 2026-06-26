using System;
using TMPro;
using UnityEngine;

[Serializable]
public class UIPosition
{
    [SerializeField] private GameObject root;
    [SerializeField] private TMP_Text positionText;
    [SerializeField] private int currentPosition = 1;

    public int CurrentPosition => currentPosition;

    public void Initialize()
    {
        if (positionText == null && root != null)
        {
            positionText = root.GetComponentInChildren<TMP_Text>(true);
        }

        SetPosition(currentPosition);
    }

    public void SetPosition(int position)
    {
        currentPosition = position;

        if (positionText != null)
        {
            positionText.text = currentPosition.ToString();
        }
    }

    public void SetActive(bool isActive)
    {
        if (root != null)
        {
            root.SetActive(isActive);
        }
    }
}
