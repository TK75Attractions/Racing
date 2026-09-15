using System;
using TMPro;
using UnityEngine;

[Serializable]
public class UILap
{
    [SerializeField] private GameObject root;
    [SerializeField] private TMP_Text lapText;
    [SerializeField] private int currentLap = 1;
    private UIValuePulse valuePulse;

    public void Init(Transform parent)
    {
        if (parent != null)
        {
            root = parent.gameObject;
        }

        Transform rootTransform = root != null ? root.transform : parent;
        if (lapText == null && rootTransform != null)
        {
            Transform textTransform = rootTransform.Find("Txt");
            lapText = textTransform != null
                ? textTransform.GetComponentInChildren<TMP_Text>(true)
                : rootTransform.GetComponentInChildren<TMP_Text>(true);
        }

        if (lapText != null)
        {
            valuePulse = lapText.GetComponent<UIValuePulse>();
            if (valuePulse == null)
            {
                valuePulse = lapText.gameObject.AddComponent<UIValuePulse>();
            }
        }

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
        valuePulse?.Play(new Color(1f, 0.84f, 0.12f, 1f), -12f);
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
