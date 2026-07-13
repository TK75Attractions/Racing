using System;
using TMPro;
using UnityEngine;

[Serializable]
public class UILap
{
    [SerializeField] private GameObject root;
    [SerializeField] private TMP_Text lapText;
    [SerializeField] private int currentLap = 1;

    public void Init(Transform parent)
    {
        if (parent != null)
        {
            root = parent.gameObject;
        }

        root = parent.gameObject;
        lapText = parent.Find("Txt").GetComponent<TMP_Text>();

        UpdateText();
    }

    public void SetLap(int lap)
    {
        lap = Mathf.Max(1, lap);

        if (lap != currentLap)
        {
            LapTextAnimation(currentLap, lap);
            currentLap = lap;
        }

        UpdateText();
    }

    private void LapTextAnimation(int oldLap, int newLap)
    {

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
        if (lapText != null)
        {
            lapText.text = currentLap.ToString();
        }
    }
}
