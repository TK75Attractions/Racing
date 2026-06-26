using System;
using TMPro;
using UnityEngine;

[Serializable]
public class UILap
{
    [SerializeField] private GameObject root;
    [SerializeField] private TMP_Text lapText;
    [SerializeField] private int currentLap = 1;

    public int CurrentLap => currentLap;

    public void Initialize()
    {
        if (lapText == null && root != null)
        {
            lapText = root.GetComponentInChildren<TMP_Text>(true);
        }

        SetLap(currentLap);
    }

    public void SetLap(int lap)
    {
        currentLap = lap;

        if (lapText != null)
        {
            lapText.text = currentLap.ToString();
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
