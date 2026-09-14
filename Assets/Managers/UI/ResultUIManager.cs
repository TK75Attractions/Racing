using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>5台分の順位表と、ハンドル・ペダル操作メニューを構築します。</summary>
public class ResultUIManager
{
    private const int RowCount = 5;
    private static readonly string[] DummyNames = { "ゲスト A", "ゲスト B", "ゲスト C" };
    private static readonly float[] DummyOffsets = { 1.783f, 3.214f, 5.066f };

    private RaceResultRecord currentResult;
    private RaceSessionResult currentSessionResult;
    private readonly TMP_Text[] rankLabels = new TMP_Text[RowCount];
    private readonly TMP_Text[] playerLabels = new TMP_Text[RowCount];
    private readonly TMP_Text[] timeLabels = new TMP_Text[RowCount];
    private readonly TMP_Text[] gapLabels = new TMP_Text[RowCount];
    private TMP_Text winnerLabel;
    private TMP_FontAsset inheritedFont;
    private ResultMenuAnimator menuAnimator;
    private int localPlayerNumber = 1;
    private readonly Image[] rowBackgrounds = new Image[RowCount];
    private readonly Image[] localAccents = new Image[RowCount];
    private readonly TMP_Text[] localBadges = new TMP_Text[RowCount];

    public RaceResultRecord CurrentResult => currentResult;
    public RaceSessionResult CurrentSessionResult => currentSessionResult;

    public void Init(Transform parent, int playerNumber = 1)
    {
        localPlayerNumber = playerNumber;
        if (parent == null) { Debug.LogWarning("ResultUIManager requires a Result root."); return; }
        Transform legacyPanel = parent.Find("Panel");
        TMP_Text template = legacyPanel != null ? legacyPanel.GetComponentInChildren<TMP_Text>(true) : parent.GetComponentInChildren<TMP_Text>(true);
        inheritedFont = template != null ? template.font : null;
        BuildPresentation(parent);
        if (legacyPanel != null) legacyPanel.gameObject.SetActive(false);
    }

    public void ShowResults() => ShowResults((RaceResultRecord)null);

    public void ShowResults(RaceResultRecord resultRecord)
    {
        currentResult = resultRecord;
        currentSessionResult = null;
        float winnerTime = FinishedTime(currentResult);
        ApplyActualRow(0, currentResult, winnerTime);
        ApplyActualRow(1, null, winnerTime);
        ApplyDummyRows(GetLastFinishedTime(), winnerTime);
        ApplyWinner(currentResult);
        SetMenuState(0, 0f);
    }

    public void ShowResults(RaceSessionResult sessionResult)
    {
        currentSessionResult = sessionResult;
        currentResult = sessionResult?.GetResultAtPosition(1);
        float winnerTime = FinishedTime(currentResult);
        ApplyActualRow(0, currentResult, winnerTime);
        ApplyActualRow(1, sessionResult?.GetResultAtPosition(2), winnerTime);
        ApplyDummyRows(GetLastFinishedTime(), winnerTime);
        ApplyWinner(currentResult);
        SetMenuState(0, 0f);
    }

    public void SetMenuState(int selectedIndex, float pedalAmount) => menuAnimator?.SetState(selectedIndex, pedalAmount);
    public void PlayConfirm(int selectedIndex) => menuAnimator?.PlayConfirm(selectedIndex);

    public void HideResults()
    {
        currentResult = null;
        currentSessionResult = null;
        SetMenuState(0, 0f);
    }

    private void BuildPresentation(Transform parent)
    {
        GameObject presentation = GetOrCreate(parent, "ResultPresentation", typeof(Image));
        Anchor(presentation.GetComponent<RectTransform>(), Vector2.zero, Vector2.one);
        presentation.GetComponent<Image>().color = Color.black;
        InstantiateScreenBackground(presentation.transform, "UI/ResultScreenBackground", "ResultBackground");
        CreatePanel(presentation.transform, "ResultBackgroundTint", Vector2.zero, Vector2.one, new Color(0.002f, 0.008f, 0.016f, 0.65f));

        GameObject card = GetOrCreate(presentation.transform, "ResultCard", typeof(Image), typeof(CanvasGroup));
        Anchor(card.GetComponent<RectTransform>(), new Vector2(0.13f, 0.07f), new Vector2(0.87f, 0.95f));
        card.GetComponent<Image>().color = new Color(0.008f, 0.018f, 0.03f, 0.36f);
        CreatePanel(card.transform, "HeaderAccent", new Vector2(0.055f, 0.955f), new Vector2(0.095f, 0.958f), new Color(0.2f, 0.8f, 1f, 0.85f));
        TMP_Text classification = CreateLabel(card.transform, "Classification", "FINAL CLASSIFICATION", new Vector2(0.11f, 0.935f), new Vector2(0.46f, 0.975f), 14f,
            new Color(0.52f, 0.65f, 0.75f, 1f), FontStyles.Normal, TextAlignmentOptions.Left, FontRole.English);
        classification.characterSpacing = 3f;

        CreateLabel(card.transform, "Title", "RESULTS", new Vector2(0.1f, 0.815f), new Vector2(0.9f, 0.945f), 86f,
            Color.white, FontStyles.Bold | FontStyles.Italic, TextAlignmentOptions.Center, FontRole.English);
        TMP_Text subtitle = CreateLabel(card.transform, "Subtitle", "リザルト", new Vector2(0.1f, 0.77f), new Vector2(0.9f, 0.815f), 22f,
            new Color(0.72f, 0.79f, 0.85f, 1f), FontStyles.Normal, TextAlignmentOptions.Center, FontRole.Japanese);
        subtitle.characterSpacing = 8f;
        winnerLabel = CreateLabel(card.transform, "Winner", string.Empty, new Vector2(0.57f, 0.71f), new Vector2(0.945f, 0.752f), 16f,
            new Color(1f, 0.78f, 0.12f, 1f), FontStyles.Bold, TextAlignmentOptions.Right, FontRole.English);

        BuildTableHeader(card.transform);
        const float height = 0.082f;
        const float firstTop = 0.65f;
        for (int index = 0; index < RowCount; index++)
        {
            float top = firstTop - height * index;
            BuildResultRow(card.transform, index, new Vector2(0.055f, top - height + 0.004f), new Vector2(0.945f, top));
        }
        BuildMenu(card.transform);
        CreateLabel(card.transform, "MenuHint", "ハンドルで選択   /   ペダルで決定", new Vector2(0.18f, 0.012f), new Vector2(0.82f, 0.055f), 17f,
            new Color(0.52f, 0.62f, 0.7f, 1f), FontStyles.Normal, TextAlignmentOptions.Center, FontRole.Japanese);
    }

    private void BuildTableHeader(Transform parent)
    {
        GameObject header = CreatePanel(parent, "TableHeader", new Vector2(0.055f, 0.66f), new Vector2(0.945f, 0.706f), new Color(0.015f, 0.025f, 0.04f, 0.3f));
        CreatePanel(header.transform, "Divider", Vector2.zero, new Vector2(1f, 0.025f), new Color(0.35f, 0.48f, 0.58f, 0.45f));
        CreateLabel(header.transform, "Rank", "順位", new Vector2(0.01f, 0f), new Vector2(0.15f, 1f), 20f, HeaderColor, FontStyles.Bold, TextAlignmentOptions.Center, FontRole.Japanese);
        CreateLabel(header.transform, "Driver", "ドライバー", new Vector2(0.16f, 0f), new Vector2(0.49f, 1f), 20f, HeaderColor, FontStyles.Bold, TextAlignmentOptions.Left, FontRole.Japanese);
        CreateLabel(header.transform, "Time", "タイム", new Vector2(0.49f, 0f), new Vector2(0.73f, 1f), 20f, HeaderColor, FontStyles.Bold, TextAlignmentOptions.Center, FontRole.Japanese);
        CreateLabel(header.transform, "Gap", "トップとの差", new Vector2(0.73f, 0f), new Vector2(0.98f, 1f), 20f, HeaderColor, FontStyles.Bold, TextAlignmentOptions.Center, FontRole.Japanese);
    }

    private static Color HeaderColor => new Color(0.58f, 0.66f, 0.73f, 1f);

    private void BuildResultRow(Transform parent, int index, Vector2 anchorMin, Vector2 anchorMax)
    {
        Color rowColor = index == 0 ? new Color(0.12f, 0.09f, 0.025f, 0.58f)
            : index == 1 ? new Color(0.02f, 0.09f, 0.13f, 0.52f) : new Color(0.018f, 0.027f, 0.038f, index % 2 == 0 ? 0.42f : 0.26f);
        GameObject row = CreatePanel(parent, $"ResultRow{index + 1}", anchorMin, anchorMax, rowColor);
        rowBackgrounds[index] = row.GetComponent<Image>();
        localAccents[index] = CreatePanel(row.transform, "LocalAccent", Vector2.zero, new Vector2(0.006f, 1f), new Color(0.15f, 0.82f, 1f, 1f)).GetComponent<Image>();
        localAccents[index].gameObject.SetActive(false);
        CreatePanel(row.transform, "Divider", Vector2.zero, new Vector2(1f, 0.014f), new Color(0.35f, 0.45f, 0.53f, 0.22f));
        if (index < 2) CreatePanel(row.transform, "PlayerAccent", Vector2.zero, new Vector2(0.003f, 1f),
            index == 0 ? new Color(1f, 0.78f, 0.16f, 1f) : new Color(0.14f, 0.75f, 1f, 0.8f));
        Color textColor = index < 2 ? new Color(0.94f, 0.96f, 0.98f, 1f) : new Color(0.62f, 0.69f, 0.75f, 1f);

        rankLabels[index] = CreateLabel(row.transform, "Rank", (index + 1).ToString(), new Vector2(0.01f, 0.05f), new Vector2(0.15f, 0.95f), 34f,
            index == 0 ? new Color(1f, 0.8f, 0.18f, 1f) : textColor, FontStyles.Bold | FontStyles.Italic, TextAlignmentOptions.Center, FontRole.English);
        playerLabels[index] = CreateLabel(row.transform, "Player", "---", new Vector2(0.17f, 0.05f), new Vector2(0.49f, 0.95f), 27f,
            Color.white, FontStyles.Bold, TextAlignmentOptions.Left, FontRole.Japanese);
        localBadges[index] = CreateLabel(row.transform, "LocalBadge", "あなた", new Vector2(0.40f, 0.1f), new Vector2(0.48f, 0.9f), 27f,
            new Color(0.4f, 0.9f, 1f, 1f), FontStyles.Bold, TextAlignmentOptions.Right, FontRole.Japanese);
        localBadges[index].gameObject.SetActive(false);
        timeLabels[index] = CreateLabel(row.transform, "Time", "--:--.---", new Vector2(0.49f, 0.05f), new Vector2(0.73f, 0.95f), 28f,
            textColor, FontStyles.Bold | FontStyles.Italic, TextAlignmentOptions.Center, FontRole.English);
        gapLabels[index] = CreateLabel(row.transform, "Gap", "—", new Vector2(0.73f, 0.05f), new Vector2(0.98f, 0.95f), 24f,
            new Color(0.52f, 0.62f, 0.71f, 1f), FontStyles.Normal, TextAlignmentOptions.Center, FontRole.English);
    }

    private void BuildMenu(Transform parent)
    {
        GameObject menu = GetOrCreate(parent, "ResultMenu");
        Anchor(menu.GetComponent<RectTransform>(), new Vector2(0.20f, 0.07f), new Vector2(0.80f, 0.21f));
        RectTransform[] cards = new RectTransform[2];
        string[] labels = { "リトライ", "タイトルにもどる" };
        string[] captions = { "RETRY", "RETURN TO TITLE" };
        for (int index = 0; index < 2; index++)
        {
            float left = index == 0 ? 0.02f : 0.515f;
            float right = index == 0 ? 0.485f : 0.98f;
            GameObject option = CreatePanel(menu.transform, $"Option{index + 1}", new Vector2(left, 0.08f), new Vector2(right, 0.92f), new Color(0.015f, 0.045f, 0.075f, 0.96f));
            cards[index] = option.GetComponent<RectTransform>();
            AddOutline(option, new Color(0.4f, 0.48f, 0.56f, 0.7f), new Vector2(2f, -2f));
            TMP_Text caption = CreateLabel(option.transform, "Caption", captions[index], new Vector2(0.12f, 0.20f), new Vector2(0.88f, 0.43f), 14f,
                new Color(0.78f, 0.84f, 0.9f, 1f), FontStyles.Italic, TextAlignmentOptions.Center, FontRole.English);
            caption.characterSpacing = 2f;
            CreateLabel(option.transform, "Label", labels[index], new Vector2(0.12f, 0.43f), new Vector2(0.88f, 0.85f), 26f,
                Color.white, FontStyles.Bold, TextAlignmentOptions.Center, FontRole.Japanese);
        }
        menuAnimator = menu.GetComponent<ResultMenuAnimator>();
        if (menuAnimator == null) menuAnimator = menu.AddComponent<ResultMenuAnimator>();
        menuAnimator.Configure(cards);
    }

    private void ApplyActualRow(int index, RaceResultRecord result, float winnerTime)
    {
        rankLabels[index].text = (index + 1).ToString();
        playerLabels[index].text = result == null ? "NO FINISH" : result.playerNumber > 0 ? $"PLAYER {result.playerNumber}" : result.carName;
        timeLabels[index].text = result == null ? "---" : result.didFinish ? FormatTime(result.totalRaceTime) : "DNF";
        gapLabels[index].text = index == 0 || result == null || !result.didFinish ? "—" : $"+{Mathf.Max(0f, result.totalRaceTime - winnerTime):0.000}";
        bool isLocalPlayer = result != null && result.playerNumber == localPlayerNumber;
        localAccents[index].gameObject.SetActive(isLocalPlayer);
        localBadges[index].gameObject.SetActive(isLocalPlayer);
        playerLabels[index].rectTransform.anchorMax = new Vector2(isLocalPlayer ? 0.39f : 0.49f, 0.95f);
        playerLabels[index].color = Color.white;
        rankLabels[index].fontSizeMax = isLocalPlayer ? 42f : 34f;
        rankLabels[index].color = isLocalPlayer ? new Color(0.4f, 0.9f, 1f, 1f)
            : index == 0 ? new Color(1f, 0.8f, 0.18f, 1f) : Color.white;
        rowBackgrounds[index].color = isLocalPlayer ? new Color(0.015f, 0.22f, 0.34f, 0.9f)
            : index == 0 ? new Color(0.12f, 0.09f, 0.025f, 0.58f) : new Color(0.02f, 0.09f, 0.13f, 0.52f);
    }

    private void ApplyDummyRows(float baseTime, float winnerTime)
    {
        for (int index = 2; index < RowCount; index++)
        {
            float dummyTime = Mathf.Max(60f, baseTime) + DummyOffsets[index - 2];
            rankLabels[index].text = (index + 1).ToString();
            playerLabels[index].text = DummyNames[index - 2];
            timeLabels[index].text = FormatTime(dummyTime);
            gapLabels[index].text = $"+{Mathf.Max(0f, dummyTime - (winnerTime > 0f ? winnerTime : dummyTime)):0.000}";
        }
    }

    private float GetLastFinishedTime()
    {
        float last = FinishedTime(currentResult);
        if (currentSessionResult?.playerResults == null) return last;
        foreach (RaceResultRecord result in currentSessionResult.playerResults) last = Mathf.Max(last, FinishedTime(result));
        return last;
    }

    private static float FinishedTime(RaceResultRecord result) => result != null && result.didFinish ? result.totalRaceTime : 0f;

    private void ApplyWinner(RaceResultRecord winner)
    {
        winnerLabel.text = winner == null ? "RACE COMPLETE" : winner.playerNumber > 0 ? $"PLAYER {winner.playerNumber}  WINNER" : $"{winner.carName}  WINNER";
    }

    private TMP_Text CreateLabel(Transform parent, string name, string text, Vector2 min, Vector2 max, float size,
        Color color, FontStyles style, TextAlignmentOptions alignment, FontRole role)
    {
        GameObject obj = GetOrCreate(parent, name, typeof(TextMeshProUGUI));
        Anchor(obj.GetComponent<RectTransform>(), min, max);
        TMP_Text label = obj.GetComponent<TMP_Text>();
        TMP_FontAsset font = RacingUIFontCatalog.Get(role);
        if (font != null) label.font = font; else if (inheritedFont != null) label.font = inheritedFont;
        label.text = text;
        label.color = color;
        label.fontStyle = style;
        label.alignment = alignment;
        label.enableAutoSizing = true;
        label.fontSizeMin = 11f;
        label.fontSizeMax = size;
        label.raycastTarget = false;
        return label;
    }

    private static GameObject CreatePanel(Transform parent, string name, Vector2 min, Vector2 max, Color color)
    {
        GameObject panel = GetOrCreate(parent, name, typeof(Image));
        Anchor(panel.GetComponent<RectTransform>(), min, max);
        panel.GetComponent<Image>().color = color;
        panel.GetComponent<Image>().raycastTarget = false;
        return panel;
    }

    private static Outline AddOutline(GameObject target, Color color, Vector2 distance)
    {
        Outline outline = target.GetComponent<Outline>();
        if (outline == null) outline = target.AddComponent<Outline>();
        outline.effectColor = color;
        outline.effectDistance = distance;
        return outline;
    }

    private static GameObject GetOrCreate(Transform parent, string name, params System.Type[] extras)
    {
        Transform existing = parent.Find(name);
        if (existing != null) return existing.gameObject;
        System.Type[] types = new System.Type[extras.Length + 2];
        types[0] = typeof(RectTransform);
        types[1] = typeof(CanvasRenderer);
        for (int index = 0; index < extras.Length; index++) types[index + 2] = extras[index];
        GameObject created = new GameObject(name, types);
        created.layer = parent.gameObject.layer;
        created.transform.SetParent(parent, false);
        return created;
    }

    private static GameObject InstantiateScreenBackground(Transform parent, string resourcePath, string objectName)
    {
        Transform existing = parent.Find(objectName);
        if (existing != null) { existing.SetAsFirstSibling(); return existing.gameObject; }
        GameObject prefab = Resources.Load<GameObject>(resourcePath);
        if (prefab == null) { Debug.LogWarning($"Result background prefab was not found at Resources/{resourcePath}."); return null; }
        GameObject instance = Object.Instantiate(prefab, parent, false);
        instance.name = objectName;
        instance.layer = parent.gameObject.layer;
        if (instance.TryGetComponent(out RectTransform rect)) Anchor(rect, Vector2.zero, Vector2.one);
        instance.transform.SetAsFirstSibling();
        return instance;
    }

    private static void Anchor(RectTransform rect, Vector2 min, Vector2 max)
    {
        rect.anchorMin = min;
        rect.anchorMax = max;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        rect.localScale = Vector3.one;
    }

    private static string FormatTime(float seconds)
    {
        int totalMilliseconds = Mathf.FloorToInt(Mathf.Max(0f, seconds) * 1000f);
        return $"{totalMilliseconds / 60000:00}:{totalMilliseconds / 1000 % 60:00}.{totalMilliseconds % 1000:000}";
    }
}
