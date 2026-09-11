using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>レース結果のデータ反映と、結果専用ビューの構築を担当します。</summary>
public class ResultUIManager
{
    private RaceResultRecord currentResult;
    private RaceSessionResult currentSessionResult;
    private readonly TMP_Text[] rankLabels = new TMP_Text[2];
    private readonly TMP_Text[] playerLabels = new TMP_Text[2];
    private readonly TMP_Text[] timeLabels = new TMP_Text[2];
    private TMP_Text winnerLabel;
    private TMP_FontAsset inheritedFont;

    public RaceResultRecord CurrentResult => currentResult;
    public RaceSessionResult CurrentSessionResult => currentSessionResult;

    public void Init(Transform parent)
    {
        if (parent == null)
        {
            Debug.LogWarning("ResultUIManager requires a Result root.");
            return;
        }

        Transform legacyPanel = parent.Find("Panel");
        TMP_Text template = legacyPanel != null
            ? legacyPanel.GetComponentInChildren<TMP_Text>(true)
            : parent.GetComponentInChildren<TMP_Text>(true);
        inheritedFont = template != null ? template.font : null;

        BuildPresentation(parent);
        if (legacyPanel != null)
        {
            legacyPanel.gameObject.SetActive(false);
        }
    }

    public void ShowResults()
    {
        ShowResults((RaceResultRecord)null);
    }

    public void ShowResults(RaceResultRecord resultRecord)
    {
        currentResult = resultRecord;
        currentSessionResult = null;
        ApplyRow(0, currentResult, 1);
        ApplyRow(1, null, 2);
        ApplyWinner(currentResult);
    }

    public void ShowResults(RaceSessionResult sessionResult)
    {
        currentSessionResult = sessionResult;
        currentResult = GetResultAtPosition(sessionResult, 1);
        ApplyRow(0, currentResult, 1);
        ApplyRow(1, GetResultAtPosition(sessionResult, 2), 2);
        ApplyWinner(currentResult);
    }

    public void HideResults()
    {
        currentResult = null;
        currentSessionResult = null;
    }

    private void BuildPresentation(Transform parent)
    {
        GameObject presentation = GetOrCreateRectObject(parent, "ResultPresentation", typeof(Image));
        Anchor(presentation.GetComponent<RectTransform>(), Vector2.zero, Vector2.one);
        Image backdrop = presentation.GetComponent<Image>();
        backdrop.color = new Color(0.008f, 0.018f, 0.032f, 0.68f);
        backdrop.raycastTarget = true;

        GameObject topAccent = GetOrCreateRectObject(presentation.transform, "TopAccent", typeof(Image));
        Anchor(topAccent.GetComponent<RectTransform>(), new Vector2(0.12f, 0.935f), new Vector2(0.88f, 0.942f));
        topAccent.GetComponent<Image>().color = new Color(0.1f, 0.82f, 1f, 0.95f);

        GameObject card = GetOrCreateRectObject(presentation.transform, "ResultCard", typeof(Image), typeof(CanvasGroup));
        Anchor(card.GetComponent<RectTransform>(), new Vector2(0.16f, 0.1f), new Vector2(0.84f, 0.92f));
        card.GetComponent<Image>().color = new Color(0.01f, 0.025f, 0.045f, 0.48f);

        CreateLabel(card.transform, "Eyebrow", "FINAL CLASSIFICATION", new Vector2(0.08f, 0.89f), new Vector2(0.92f, 0.96f), 24f,
            new Color(0.18f, 0.86f, 1f, 1f), FontStyles.Bold, TextAlignmentOptions.Center);
        CreateLabel(card.transform, "Title", "RESULT / 結果", new Vector2(0.08f, 0.72f), new Vector2(0.92f, 0.9f), 82f,
            Color.white, FontStyles.Bold, TextAlignmentOptions.Center);
        winnerLabel = CreateLabel(card.transform, "Winner", string.Empty, new Vector2(0.1f, 0.64f), new Vector2(0.9f, 0.73f), 31f,
            new Color(1f, 0.82f, 0.12f, 1f), FontStyles.Bold, TextAlignmentOptions.Center);

        BuildResultRow(card.transform, 0, new Vector2(0.07f, 0.41f), new Vector2(0.93f, 0.61f),
            new Color(0.025f, 0.045f, 0.065f, 0.94f), new Color(1f, 0.91f, 0.05f, 1f));
        BuildResultRow(card.transform, 1, new Vector2(0.07f, 0.19f), new Vector2(0.93f, 0.39f),
            new Color(0.025f, 0.045f, 0.065f, 0.88f), new Color(0.05f, 0.075f, 0.1f, 1f));

        GameObject promptPanel = GetOrCreateRectObject(card.transform, "PromptPanel", typeof(Image));
        Anchor(promptPanel.GetComponent<RectTransform>(), new Vector2(0.32f, 0.045f), new Vector2(0.68f, 0.14f));
        promptPanel.GetComponent<Image>().color = new Color(0.02f, 0.08f, 0.12f, 0.94f);
        CreateLabel(promptPanel.transform, "ReturnPrompt", "次へ  /  HOLD PEDAL", new Vector2(0.05f, 0.08f), new Vector2(0.95f, 0.92f), 25f,
            new Color(0.82f, 0.95f, 1f, 1f), FontStyles.Bold, TextAlignmentOptions.Center);
    }

    private void BuildResultRow(Transform parent, int index, Vector2 anchorMin, Vector2 anchorMax, Color backgroundColor, Color rankColor)
    {
        GameObject row = GetOrCreateRectObject(parent, $"ResultRow{index + 1}", typeof(Image));
        Anchor(row.GetComponent<RectTransform>(), anchorMin, anchorMax);
        row.GetComponent<Image>().color = backgroundColor;

        GameObject rankPlate = GetOrCreateRectObject(row.transform, "RankPlate", typeof(Image));
        Anchor(rankPlate.GetComponent<RectTransform>(), Vector2.zero, new Vector2(0.2f, 1f));
        rankPlate.GetComponent<Image>().color = rankColor;
        rankLabels[index] = CreateLabel(rankPlate.transform, "Rank", index == 0 ? "1" : "2",
            new Vector2(0.03f, 0.08f), new Vector2(0.97f, 0.92f), 72f,
            index == 0 ? new Color(0.015f, 0.02f, 0.025f, 1f) : Color.white,
            FontStyles.Bold, TextAlignmentOptions.Center);
        playerLabels[index] = CreateLabel(row.transform, "Player", "---",
            new Vector2(0.24f, 0.08f), new Vector2(0.59f, 0.92f), 43f, Color.white, FontStyles.Bold, TextAlignmentOptions.Left);
        timeLabels[index] = CreateLabel(row.transform, "Time", "--:--.---",
            new Vector2(0.59f, 0.08f), new Vector2(0.95f, 0.92f), 48f, Color.white, FontStyles.Bold, TextAlignmentOptions.Right);
    }

    private void ApplyRow(int index, RaceResultRecord result, int position)
    {
        if (index < 0 || index >= playerLabels.Length || playerLabels[index] == null)
        {
            return;
        }

        rankLabels[index].text = position.ToString();
        playerLabels[index].text = result != null
            ? (result.playerNumber > 0 ? $"PLAYER {result.playerNumber}" : result.carName)
            : "NO FINISH";
        timeLabels[index].text = result == null ? "---" : result.didFinish ? FormatTime(result.totalRaceTime) : "DNF";
    }

    private void ApplyWinner(RaceResultRecord winner)
    {
        if (winnerLabel == null)
        {
            return;
        }

        winnerLabel.text = winner == null
            ? "RACE COMPLETE"
            : winner.playerNumber > 0 ? $"PLAYER {winner.playerNumber} WINS" : $"{winner.carName} WINS";
    }

    private TMP_Text CreateLabel(Transform parent, string objectName, string text, Vector2 anchorMin, Vector2 anchorMax,
        float fontSize, Color color, FontStyles style, TextAlignmentOptions alignment)
    {
        GameObject labelObject = GetOrCreateRectObject(parent, objectName, typeof(TextMeshProUGUI));
        Anchor(labelObject.GetComponent<RectTransform>(), anchorMin, anchorMax);
        TMP_Text label = labelObject.GetComponent<TMP_Text>();
        if (inheritedFont != null)
        {
            label.font = inheritedFont;
        }
        label.text = text;
        label.color = color;
        label.fontStyle = style;
        label.alignment = alignment;
        label.enableAutoSizing = true;
        label.fontSizeMin = 15f;
        label.fontSizeMax = fontSize;
        label.raycastTarget = false;
        return label;
    }

    private static GameObject GetOrCreateRectObject(Transform parent, string name, params System.Type[] extraComponents)
    {
        Transform existing = parent.Find(name);
        if (existing != null)
        {
            return existing.gameObject;
        }

        System.Type[] components = new System.Type[extraComponents.Length + 2];
        components[0] = typeof(RectTransform);
        components[1] = typeof(CanvasRenderer);
        for (int index = 0; index < extraComponents.Length; index++)
        {
            components[index + 2] = extraComponents[index];
        }

        GameObject created = new GameObject(name, components);
        created.layer = parent.gameObject.layer;
        created.transform.SetParent(parent, false);
        return created;
    }

    private static void Anchor(RectTransform rectTransform, Vector2 anchorMin, Vector2 anchorMax)
    {
        rectTransform.anchorMin = anchorMin;
        rectTransform.anchorMax = anchorMax;
        rectTransform.offsetMin = Vector2.zero;
        rectTransform.offsetMax = Vector2.zero;
        rectTransform.localScale = Vector3.one;
    }

    private static string FormatTime(float seconds)
    {
        int totalMilliseconds = Mathf.FloorToInt(Mathf.Max(0f, seconds) * 1000f);
        int minutes = totalMilliseconds / 60000;
        int secondsPart = totalMilliseconds / 1000 % 60;
        int milliseconds = totalMilliseconds % 1000;
        return $"{minutes:00}:{secondsPart:00}.{milliseconds:000}";
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
}
