using TMPro;
using UnityEngine;

[System.Serializable]
public class RaceResultRecord
{
    public string carName;
    public int completedLaps;
    public int goalLap;
    public float totalRaceTime;
    public float finalLapTime;
    public float bestLapTime;
}

public class ResultUIManager
{
    private GameObject root;
    private RaceResultRecord currentResult;
    private TMP_Text timeTxt = null;

    public RaceResultRecord CurrentResult => currentResult;

    public void Init(Transform parent)
    {
        root = parent.Find("Panel").gameObject;
        timeTxt = root.transform.Find("Time").Find("Txt").GetComponent<TMP_Text>();
    }

    public void ShowResults()
    {
        ShowResults(null);
    }

    public void ShowResults(RaceResultRecord resultRecord)
    {
        currentResult = resultRecord;
        if (currentResult != null && timeTxt != null)
        {
            timeTxt.text = FormatTime(currentResult.totalRaceTime);
        }
    }

    public void HideResults()
    {
        currentResult = null;
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
