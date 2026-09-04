using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[System.Serializable]
public class RaceResultRecord
{
    // 通信側で安定した参加者識別子を設定できる。未設定時は名前とタイムで重複を判定する。
    public string participantId;
    public string carName;
    public int completedLaps;
    public int goalLap;
    public float totalRaceTime;
    public float finalLapTime;
    public float bestLapTime;
    public string difficultyName;
    public int finishPosition;
}

public class ResultUIManager
{
    private GameObject root;
    private RaceResultRecord currentResult;
    private TMP_Text timeTxt = null;
    private TMP_Text leaderboardTxt = null;
    private readonly List<RaceResultRecord> sortedResults = new List<RaceResultRecord>();

    public RaceResultRecord CurrentResult => currentResult;

    public void Init(Transform parent)
    {
        if (parent == null)
        {
            Debug.LogWarning("ResultUIManager requires a parent Transform.");
            return;
        }

        Transform panel = parent.Find("Panel");
        root = (panel != null ? panel : parent).gameObject;

        Transform time = root.transform.Find("Time");
        Transform timeText = time != null ? time.Find("Txt") : null;
        timeTxt = timeText != null ? timeText.GetComponentInChildren<TMP_Text>(true) : null;

        Image leaderboardPanel = GetOrCreateImage(root.transform, "LeaderboardPanel");
        leaderboardPanel.color = new Color(0.015f, 0.04f, 0.07f, 0.88f);
        SetRect(leaderboardPanel.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(470f, -30f), new Vector2(760f, 480f));

        leaderboardTxt = GetOrCreateText(root.transform, "Leaderboard", 34f, TextAlignmentOptions.TopLeft);
        leaderboardTxt.color = Color.white;
        leaderboardTxt.textWrappingMode = TextWrappingModes.NoWrap;
        leaderboardTxt.margin = new Vector4(32f, 28f, 32f, 24f);
        SetRect(leaderboardTxt.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(470f, -30f), new Vector2(760f, 480f));
        leaderboardTxt.transform.SetAsLastSibling();

        root.SetActive(false);
    }

    public void ShowResults()
    {
        ShowResults(null, null);
    }

    public void ShowResults(RaceResultRecord resultRecord)
    {
        ShowResults(resultRecord, null);
    }

    public void ShowResults(RaceResultRecord resultRecord, IReadOnlyList<RaceResultRecord> standings)
    {
        currentResult = resultRecord != null ? resultRecord : FindFirstResult(standings);
        if (root == null)
        {
            Debug.LogWarning("ResultUIManager has not been initialized.");
            return;
        }

        root.SetActive(true);
        if (timeTxt != null)
        {
            timeTxt.text = currentResult != null ? FormatTime(currentResult.totalRaceTime) : FormatTime(0f);
        }

        UpdateLeaderboard(standings, currentResult);
    }

    public void HideResults()
    {
        if (root != null)
        {
            root.SetActive(false);
        }
    }

    private void UpdateLeaderboard(IReadOnlyList<RaceResultRecord> standings, RaceResultRecord fallbackResult)
    {
        if (leaderboardTxt == null)
        {
            return;
        }

        sortedResults.Clear();
        if (standings != null)
        {
            for (int index = 0; index < standings.Count; index++)
            {
                RaceResultRecord result = standings[index];
                if (result != null && !ContainsResult(result))
                {
                    sortedResults.Add(result);
                }
            }
        }

        if (fallbackResult != null && !ContainsResult(fallbackResult))
        {
            sortedResults.Add(fallbackResult);
        }

        sortedResults.Sort(CompareResults);
        StringBuilder builder = new StringBuilder();
        builder.AppendLine("RANKING");
        builder.AppendLine();

        for (int index = 0; index < sortedResults.Count; index++)
        {
            RaceResultRecord result = sortedResults[index];
            int rank = result.finishPosition > 0 ? result.finishPosition : index + 1;
            string name = string.IsNullOrEmpty(result.carName) ? "PLAYER" : result.carName;
            builder.Append(rank.ToString("00"));
            builder.Append("  ");
            builder.Append(name);
            builder.Append("  ");
            builder.Append(FormatTime(result.totalRaceTime));

            if (!string.IsNullOrEmpty(result.difficultyName))
            {
                builder.Append("  ");
                builder.Append(result.difficultyName);
            }

            builder.AppendLine();
        }

        if (sortedResults.Count == 0)
        {
            builder.Append("NO RESULTS");
        }

        leaderboardTxt.text = builder.ToString();
    }

    private bool ContainsResult(RaceResultRecord candidate)
    {
        for (int index = 0; index < sortedResults.Count; index++)
        {
            if (AreSameResult(sortedResults[index], candidate))
            {
                return true;
            }
        }

        return false;
    }

    private static bool AreSameResult(RaceResultRecord left, RaceResultRecord right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left == null || right == null)
        {
            return false;
        }

        if (!string.IsNullOrEmpty(left.participantId) && !string.IsNullOrEmpty(right.participantId))
        {
            return left.participantId == right.participantId;
        }

        return !string.IsNullOrEmpty(left.carName)
            && left.carName == right.carName
            && Mathf.Approximately(left.totalRaceTime, right.totalRaceTime);
    }

    private static int CompareResults(RaceResultRecord left, RaceResultRecord right)
    {
        int leftPosition = left.finishPosition > 0 ? left.finishPosition : int.MaxValue;
        int rightPosition = right.finishPosition > 0 ? right.finishPosition : int.MaxValue;
        int positionCompare = leftPosition.CompareTo(rightPosition);
        if (positionCompare != 0)
        {
            return positionCompare;
        }

        return left.totalRaceTime.CompareTo(right.totalRaceTime);
    }

    private static RaceResultRecord FindFirstResult(IReadOnlyList<RaceResultRecord> standings)
    {
        if (standings == null)
        {
            return null;
        }

        for (int index = 0; index < standings.Count; index++)
        {
            if (standings[index] != null)
            {
                return standings[index];
            }
        }

        return null;
    }

    private static Image GetOrCreateImage(Transform parent, string objectName)
    {
        Transform existing = parent.Find(objectName);
        Image image = existing != null ? existing.GetComponent<Image>() : null;
        if (image == null)
        {
            GameObject imageObject = new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer));
            imageObject.transform.SetParent(parent, false);
            image = imageObject.AddComponent<Image>();
        }

        if (image.sprite == null)
        {
            image.sprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0f, 0f, 1f, 1f), new Vector2(0.5f, 0.5f), 1f);
            image.type = Image.Type.Simple;
        }

        image.raycastTarget = false;
        return image;
    }

    private static TMP_Text GetOrCreateText(Transform parent, string objectName, float fontSize, TextAlignmentOptions alignment)
    {
        Transform existing = parent.Find(objectName);
        TMP_Text text = existing != null ? existing.GetComponent<TMP_Text>() : null;
        if (text == null)
        {
            GameObject textObject = new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer));
            textObject.transform.SetParent(parent, false);
            text = textObject.AddComponent<TextMeshProUGUI>();
        }

        if (text.font == null)
        {
            text.font = TMP_Settings.defaultFontAsset;
        }

        text.fontSize = fontSize;
        text.alignment = alignment;
        text.raycastTarget = false;
        return text;
    }

    private static void SetRect(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax, Vector2 position, Vector2 size)
    {
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        rect.localScale = Vector3.one;
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
