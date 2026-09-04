using TMPro;
using UnityEngine;

public class ResultUIManager
{
    private GameObject root;
    private RaceResultRecord currentResult;
    private RaceSessionResult currentSessionResult;
    private TMP_Text timeTxt = null;

    public RaceResultRecord CurrentResult => currentResult;
    public RaceSessionResult CurrentSessionResult => currentSessionResult;

    public void Init(Transform parent)
    {
        root = parent.Find("Panel").gameObject;
        timeTxt = root.transform.Find("Time").Find("Txt").GetComponent<TMP_Text>();
    }

    public void ShowResults()
    {
        ShowResults((RaceResultRecord)null);
    }

    public void ShowResults(RaceResultRecord resultRecord)
    {
        currentResult = resultRecord;
        currentSessionResult = null;
        if (currentResult != null && timeTxt != null)
        {
            timeTxt.text = FormatTime(currentResult.totalRaceTime);
        }
    }

    public void ShowResults(RaceSessionResult sessionResult)
    {
        currentSessionResult = sessionResult;
        currentResult = GetFirstPlaceResult(sessionResult);

        if (timeTxt == null)
        {
            return;
        }

        RaceResultRecord first = GetResultAtPosition(sessionResult, 1);
        RaceResultRecord second = GetResultAtPosition(sessionResult, 2);
        timeTxt.text = $"{FormatResultLine(first, 1)}\n{FormatResultLine(second, 2)}";
    }

    public void HideResults()
    {
        currentResult = null;
        currentSessionResult = null;
    }

    private static string FormatTime(float seconds)
    {
        int totalMilliseconds = Mathf.FloorToInt(Mathf.Max(0f, seconds) * 1000f);
        int minutes = totalMilliseconds / 60000;
        int secondsPart = totalMilliseconds / 1000 % 60;
        int milliseconds = totalMilliseconds % 1000;

        return $"{minutes:00}:{secondsPart:00}.{milliseconds:000}";
    }

    private static RaceResultRecord GetFirstPlaceResult(RaceSessionResult sessionResult)
    {
        return GetResultAtPosition(sessionResult, 1);
    }

    private static RaceResultRecord GetResultAtPosition(RaceSessionResult sessionResult, int position)
    {
        if (sessionResult?.playerResults == null)
        {
            return null;
        }

        foreach (RaceResultRecord result in sessionResult.playerResults)
        {
            if (result != null && result.finishPosition == position)
            {
                return result;
            }
        }

        return null;
    }

    private static string FormatResultLine(RaceResultRecord result, int position)
    {
        string ordinal = position == 1 ? "1ST" : "2ND";
        if (result == null)
        {
            return $"{ordinal}  ---";
        }

        string playerLabel = result.playerNumber > 0 ? $"P{result.playerNumber}" : result.carName;
        string value = result.didFinish ? FormatTime(result.totalRaceTime) : "DNF";
        return $"{ordinal}  {playerLabel}  {value}";
    }
}
