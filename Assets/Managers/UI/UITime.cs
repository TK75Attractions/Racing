using System;
using TMPro;
using UnityEngine;

[Serializable]
public class UITime
{
    [SerializeField] private GameObject root;
    [SerializeField] private TMP_Text totalTimeText;
    [SerializeField] private TMP_Text lapTimeText;
    [SerializeField] private float totalTime;
    [SerializeField] private float lapTime;

    public float TotalTime => totalTime;
    public float LapTime => lapTime;

    public void Init(Transform parent)
    {
        if (parent != null)
        {
            root = parent.gameObject;
        }

        TMP_Text[] texts = root != null ? root.GetComponentsInChildren<TMP_Text>(true) : null;

        if (totalTimeText == null && texts != null && texts.Length > 0)
        {
            totalTimeText = texts[0];
        }

        if (lapTimeText == null && texts != null && texts.Length > 1)
        {
            lapTimeText = texts[1];
        }

        SetTotalTime(totalTime);
        SetLapTime(lapTime);
    }

    public void SetTotalTime(float seconds)
    {
        totalTime = Mathf.Max(0f, seconds);
        SetTimeText(totalTimeText, totalTime);
    }

    public void SetLapTime(float seconds)
    {
        lapTime = Mathf.Max(0f, seconds);
        SetTimeText(lapTimeText, lapTime);
    }

    public void SetActive(bool isActive)
    {
        if (root != null)
        {
            root.SetActive(isActive);
        }
    }

    private static void SetTimeText(TMP_Text text, float seconds)
    {
        if (text != null)
        {
            text.text = FormatTime(seconds);
        }
    }

    private static string FormatTime(float seconds)
    {
        int totalMilliseconds = Mathf.FloorToInt(Mathf.Max(0f, seconds) * 1000f);
        int minutes = totalMilliseconds / 60000;
        int secondsPart = totalMilliseconds / 1000 % 60;
        int milliseconds = totalMilliseconds % 1000;

        return $"{minutes:00}:{secondsPart:00}.{milliseconds:000}";
    }
}
