using System;
using TMPro;
using UnityEngine;

[Serializable]
public class UITime
{
    [SerializeField] private GameObject root;
    [SerializeField] private TMP_Text totalTimeText;
    [SerializeField] private TMP_Text totalTimeTextMillis;
    [SerializeField] private TMP_Text lapTimeText;
    [SerializeField] private TMP_Text lapTimeTextMillis;
    [SerializeField] private float totalTime;
    [SerializeField] private float lapTime;

    private int totalWhole = -1, totalFraction = -1, lapWhole = -1, lapFraction = -1;

    public float TotalTime => totalTime;
    public float LapTime => lapTime;

    public void Init(Transform parent)
    {
        if (parent != null)
        {
            root = parent.gameObject;
        }

        if (parent != null && parent.Find("Total") != null)
        {
            totalTimeText = parent.Find("Total").GetComponent<TMP_Text>();
            totalTimeTextMillis = parent.Find("TotalFraction").GetComponent<TMP_Text>();
            lapTimeText = parent.Find("Lap").GetComponent<TMP_Text>();
            lapTimeTextMillis = parent.Find("LapFraction").GetComponent<TMP_Text>();
        }
        totalWhole = totalFraction = lapWhole = lapFraction = -1;
        SetTotalTime(totalTime);
        SetLapTime(lapTime);
    }

    public void SetTotalTime(float seconds)
    {
        totalTime = Mathf.Max(0f, seconds);
        SetTimeText(totalTimeText, totalTimeTextMillis, totalTime, ref totalWhole, ref totalFraction);
    }

    public void SetLapTime(float seconds)
    {
        lapTime = Mathf.Max(0f, seconds);
        SetTimeText(lapTimeText, lapTimeTextMillis, lapTime, ref lapWhole, ref lapFraction);
    }

    public void SetActive(bool isActive)
    {
        if (root != null)
        {
            root.SetActive(isActive);
        }
    }

    private static void SetTimeText(TMP_Text text, TMP_Text fractionText, float seconds, ref int previousWhole, ref int previousFraction)
    {
        if (text == null || fractionText == null) return;
        int milliseconds = Mathf.FloorToInt(seconds * 1000f);
        int whole = milliseconds / 1000, fraction = milliseconds / 10 % 100;
        if (whole != previousWhole)
        {
            text.SetText("{0:00}:{1:00}", whole / 60, whole % 60);
            previousWhole = whole;
        }
        if (fraction != previousFraction)
        {
            fractionText.SetText(".{0:00}", fraction);
            previousFraction = fraction;
        }
    }
}
