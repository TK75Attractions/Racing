using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Cinemachine;

// Game Manager
// シーン全体のゲーム進行（入力管理、車の生成、カメラ制御、状態遷移など）を行うシングルトン
public class Gmanager : MonoBehaviour
{
    public enum RaceDifficulty
    {
        Easy,
        Normal,
        Hard
    }

    [Serializable]
    public class RaceDifficultySetting
    {
        public RaceDifficulty difficulty = RaceDifficulty.Normal;
        public string displayName = "NORMAL";
        public int goalLapOverride = 0;
    }

    // --- 機能追加の方法 ---
    // - 新しいマネージャを追加する場合:
    //   1) フィールドをクラス上部に宣言する（例: public XxxManager xxxManager;）
    //   2) Awake() 内で GetComponent<...>() か Find で取得し、必要なら初期化メソッドを呼ぶ
    //      // 例: xxxManager = GetComponent<XxxManager>(); xxxManager.Init();
    // - ゲーム開始／終了に関わる初期化は StartGame() / (将来的に) GameEnd() にまとめる
    //   -> 車やエフェクト、UI の生成・初期化は StartGame() に書く
    // - 毎フレームの処理は Update() に追加するが、状態ごとの処理は state による分岐で管理する
    //   -> 例: if (state == State.Game) { /* ゲーム中の処理 */ }
    // - デバッグ用処理は #if UNITY_EDITOR や isDebugMode フラグで囲む
    // - 新しいイベントやコールバックは専用メソッドにまとめ、Awake() で購読、OnDestroy() で解約する
    // ---------------------------------------------

    // シングルトンインスタンス: シーン内で1つだけ存在する
    public static Gmanager Control = null;
    [SerializeField] public InputManager IManager = null;
    public CinemachineCamera VCamera;
    public GameObject car = null;
    public GameObject carPrefab;
    [SerializeField] private CheckpointSensor startCheckpoint;
    [SerializeField] private int startCheckpointIndex = 0;
    [SerializeField] private LapManager lapManager;
    [SerializeField] private OnPlayUIManager onPlayUIManager;
    [SerializeField] private ResultUIManager resultUIManager;
    [SerializeField] private TitleUIManager titleUIManager;
    [SerializeField] private DifficultyUIManager difficultyUIManager;
    [SerializeField] private float resultReturnPedalThreshold = 0.8f;
    [SerializeField] private float resultReturnHoldSeconds = 1f;
    [SerializeField] private float resultReturnInputDelaySeconds = 3f;
    [SerializeField] private float titleStartPedalThreshold = 0.8f;
    [SerializeField] private float difficultyConfirmPedalThreshold = 0.8f;
    [SerializeField] private float difficultyHandleThreshold = 0.35f;
    [SerializeField] private float difficultyHandleNeutralThreshold = 0.2f;
    [SerializeField] private float difficultySelectionRepeatDelay = 0.3f;
    [SerializeField] private int playerPosition = 1;
    [SerializeField] private float speedUnitMultiplier = 3.6f;
    [SerializeField] private int initialDifficultyIndex = 1;
    [SerializeField]
    private RaceDifficultySetting[] difficultySettings =
    {
        new RaceDifficultySetting { difficulty = RaceDifficulty.Easy, displayName = "EASY", goalLapOverride = 1 },
        new RaceDifficultySetting { difficulty = RaceDifficulty.Normal, displayName = "NORMAL", goalLapOverride = 3 },
        new RaceDifficultySetting { difficulty = RaceDifficulty.Hard, displayName = "HARD", goalLapOverride = 5 }
    };

    private RaceDirectionCameraController raceDirectionCamera;
    private float resultReturnHoldTimer = 0f;
    private float resultReturnInputDelayTimer = 0f;
    private bool waitForPedalReleaseBeforeTitleStart = false;
    private bool waitForPedalReleaseBeforeDifficultyConfirm = false;
    private bool difficultyHandleIsNeutral = true;
    private float difficultySelectionCooldownTimer = 0f;
    private int selectedDifficultyIndex = 1;
    private RaceResultRecord latestResult;
    private readonly List<RaceResultRecord> raceResults = new List<RaceResultRecord>();
    private Rigidbody playerRigidbody;

    public RaceResultRecord LatestResult => latestResult;
    public IReadOnlyList<RaceResultRecord> RaceResults => raceResults;
    public RaceDifficulty SelectedDifficulty => GetSelectedDifficultySetting().difficulty;

    // コースデータ
    // レースコース情報（コース判定や経路情報を保持）
    public RaceCourse course;

    // デバッグ用
    // デバッグ用途の位置参照（エディタ上で指定）
    public Transform test;

    // 時間を格納
    // 経過時間（秒）
    public float time = 0;

    // ゲームの状態を表す列挙型
    // Title: タイトル画面
    // Difficulty: 難易度選択画面
    // Game:  ゲームプレイ中
    // Result: 結果画面
    public enum State
    {
        Title,
        Difficulty,
        Game,
        Result
    }

    public State state = State.Title;

    // UI関連のスプライト配列
    public Sprite[] NumberSprites;

    // Unity: Awake
    // オブジェクト生成時の初期化処理を行う（シングルトン設定、各種マネージャ取得・初期化）
    public void Awake()
    {
        // シングルトンの初期化
        if (Control == null) Control = this;
        else
        {
            Destroy(this.gameObject);
            return;
        }

        // 各種マネージャ参照の取得と初期化
        IManager = GetComponent<InputManager>();
        IManager.Init();

        VCamera = transform.parent.Find("VCamera").GetComponent<CinemachineCamera>();
        raceDirectionCamera = GetComponent<RaceDirectionCameraController>();
        if (raceDirectionCamera == null)
        {
            raceDirectionCamera = gameObject.AddComponent<RaceDirectionCameraController>();
        }
        raceDirectionCamera.SetCamera(VCamera);
        ResolveLapManager();
        Transform mainCanvas = transform.parent != null ? transform.parent.Find("MainCanvas") : null;
        if (mainCanvas == null)
        {
            Debug.LogError("MainCanvas was not found. UI state transitions cannot be initialized.");
            return;
        }

        if (onPlayUIManager == null) onPlayUIManager = new();
        Transform onPlay = mainCanvas.Find("OnPlay");
        onPlayUIManager.Init(onPlay);

        if (resultUIManager == null) resultUIManager = new();
        Transform result = mainCanvas.Find("Result");
        resultUIManager.Init(result);

        if (titleUIManager == null) titleUIManager = new();
        titleUIManager.Init(mainCanvas.Find("Title"));

        selectedDifficultyIndex = Mathf.Clamp(initialDifficultyIndex, 0, GetDifficultyCount() - 1);
        if (difficultyUIManager == null) difficultyUIManager = new();
        difficultyUIManager.Init(GetOrCreateScreen(mainCanvas, "Difficulty"), GetDifficultyLabels(), selectedDifficultyIndex);

        SetOnPlayUIActive(false);
        SetDifficultyUIActive(false);
        SetTitleUIActive(true);
        resultUIManager?.HideResults();
    }


    // Unity: Update
    // 毎フレームの更新処理（入力更新、車の更新、デバッグ処理など）
    public void Update()
    {
        float dt = Time.deltaTime;

        // 入力を更新。タイトル画面でペダルが閾値を超えたらゲーム開始（暫定判定）
        if (IManager != null)
        {
            IManager.UpdateInput(dt);
            if (state == State.Title)
            {
                UpdateTitleStartInput();
            }
            else if (state == State.Difficulty)
            {
                UpdateDifficultyInput(dt);
            }
            else if (state == State.Result)
            {
                UpdateResultReturnInput(dt);
            }
        }

        // 車が存在する場合はレース時間とUIを更新する。
        // 車両の物理更新は DebugMover / TireForce の FixedUpdate が担当する。
        if (car != null && state == State.Game)
        {
            time += dt;
            UpdateOnPlayUI();
        }
        // デバッグ: スペースキー押下で test がコース内にあるかチェックしてログ出力（エディタ実行向け）
        if (Keyboard.current.spaceKey.wasPressedThisFrame)
        {
            if (course != null)
            {
                Debug.Log(course.IsPointInsideCourse(new Vector2(test.position.x, test.position.z)));
            }
        }
    }

    // ゲーム開始処理。
    // ゲーム開始処理: 車を生成して初期化し、カメラ追従を設定、ゲーム状態を Game に遷移させる
    public void StartGame()
    {
        if (state != State.Title && state != State.Difficulty)
        {
            return;
        }

        ApplySelectedDifficulty();
        Transform spawnPoint = GetStartPoint();
        Vector3 spawnPosition = spawnPoint != null ? spawnPoint.position : Vector3.zero;
        Quaternion spawnRotation = spawnPoint != null ? spawnPoint.rotation : Quaternion.identity;

        car = Instantiate(carPrefab, spawnPosition, spawnRotation);
        playerRigidbody = car.GetComponent<Rigidbody>();
        RegisterPlayerCar(spawnPoint);
        raceDirectionCamera.SetCar(car.transform);
        VCamera.Follow = raceDirectionCamera.CameraTarget;
        VCamera.LookAt = raceDirectionCamera.LookTarget;
        time = 0f;
        resultReturnHoldTimer = 0f;
        resultReturnInputDelayTimer = 0f;
        waitForPedalReleaseBeforeTitleStart = false;
        waitForPedalReleaseBeforeDifficultyConfirm = false;
        raceResults.Clear();
        latestResult = null;
        state = State.Game;
        SetTitleUIActive(false);
        SetDifficultyUIActive(false);
        SetOnPlayUIActive(true);
        UpdateOnPlayUI();
        Debug.Log("Game Start");
    }

    private Transform GetStartPoint()
    {
        if (startCheckpoint != null)
        {
            return startCheckpoint.transform;
        }

        CheckpointSensor[] checkpoints = FindObjectsOfType<CheckpointSensor>();
        CheckpointSensor selected = null;

        foreach (CheckpointSensor checkpoint in checkpoints)
        {
            if (checkpoint.CheckpointIndex != startCheckpointIndex)
            {
                continue;
            }

            if (selected == null || checkpoint.transform.GetSiblingIndex() < selected.transform.GetSiblingIndex())
            {
                selected = checkpoint;
            }
        }

        if (selected == null)
        {
            Debug.LogWarning($"Start checkpoint index {startCheckpointIndex} was not found. Spawning at world origin.");
            return null;
        }

        startCheckpoint = selected;
        return selected.transform;
    }

    public void ShowResult()
    {
        ShowResult(null);
    }

    public void ShowResult(RaceResultRecord resultRecord)
    {
        ShowResult(resultRecord, null);
    }

    // マルチプレイ側から全参加者の順位を渡せる拡張入口。
    public void ShowResult(RaceResultRecord resultRecord, IReadOnlyList<RaceResultRecord> standings)
    {
        if (state != State.Game)
        {
            return;
        }

        List<RaceResultRecord> suppliedStandings = CopyResults(standings);
        raceResults.Clear();
        for (int index = 0; index < suppliedStandings.Count; index++)
        {
            AddRaceResult(suppliedStandings[index]);
        }

        latestResult = AddRaceResult(resultRecord);

        state = State.Result;
        resultReturnHoldTimer = 0f;
        resultReturnInputDelayTimer = 0f;
        SetOnPlayUIActive(false);
        if (resultUIManager != null)
        {
            resultUIManager.ShowResults(latestResult, raceResults);
        }
        else
        {
            Debug.LogWarning("ResultUIManager was not found.");
        }

        Debug.Log("Game End");
    }

    // 通信で後から確定した順位表を結果画面へ反映するための入口。
    public void UpdateRaceResults(IReadOnlyList<RaceResultRecord> standings)
    {
        if (state != State.Result || standings == null)
        {
            return;
        }

        List<RaceResultRecord> suppliedStandings = CopyResults(standings);
        raceResults.Clear();
        for (int index = 0; index < suppliedStandings.Count; index++)
        {
            AddRaceResult(suppliedStandings[index]);
        }

        if (latestResult != null)
        {
            latestResult = AddRaceResult(latestResult);
        }

        resultUIManager?.ShowResults(latestResult, raceResults);
    }

    public void ShowResults()
    {
        ShowResult();
    }

    public void ResetGame()
    {
        if (state != State.Result)
        {
            return;
        }

        if (car != null)
        {
            Destroy(car);
            car = null;
        }

        if (raceDirectionCamera != null)
        {
            raceDirectionCamera.ClearCar();
        }

        if (VCamera != null)
        {
            VCamera.Follow = null;
            VCamera.LookAt = null;
        }

        playerRigidbody = null;
        time = 0f;
        resultReturnHoldTimer = 0f;
        resultReturnInputDelayTimer = 0f;
        SetOnPlayUIActive(false);
        SetDifficultyUIActive(false);
        if (resultUIManager != null)
        {
            resultUIManager.HideResults();
        }

        waitForPedalReleaseBeforeTitleStart = true;
        waitForPedalReleaseBeforeDifficultyConfirm = false;
        state = State.Title;
        SetTitleUIActive(true);
        Debug.Log("Game Reset");
    }

    private void ResolveLapManager()
    {
        if (lapManager != null)
        {
            return;
        }

        lapManager = FindFirstObjectByType<LapManager>(FindObjectsInactive.Include);
    }

    private void RegisterPlayerCar(Transform startTransform)
    {
        ResolveLapManager();
        if (lapManager == null || playerRigidbody == null)
        {
            return;
        }

        lapManager.RegisterCar(playerRigidbody, startTransform);
    }

    private void UpdateOnPlayUI()
    {
        if (onPlayUIManager == null || playerRigidbody == null)
        {
            return;
        }

        LapManager.CarTimeData lapData = lapManager != null ? lapManager.GetCarData(playerRigidbody) : null;
        int lapValue = GetCurrentLapValue(lapData);
        float lapSeconds = lapData != null ? lapData.currentLapTime : time;
        float totalSeconds = lapData != null ? lapData.totalRaceTime + lapData.currentLapTime : time;
        float speedValue = playerRigidbody.linearVelocity.magnitude * speedUnitMultiplier;

        onPlayUIManager.UpdateUI(playerPosition, lapValue, totalSeconds, lapSeconds, speedValue);
    }

    private int GetCurrentLapValue(LapManager.CarTimeData lapData)
    {
        if (lapData == null)
        {
            return 1;
        }

        int lapValue = lapData.lapCount + 1;
        if (lapManager != null && lapManager.GoalLap > 0)
        {
            lapValue = Mathf.Min(lapValue, lapManager.GoalLap);
        }

        return Mathf.Max(1, lapValue);
    }

    private void SetOnPlayUIActive(bool isActive)
    {
        if (onPlayUIManager == null)
        {
            return;
        }

        onPlayUIManager.SetActive(isActive);
    }

    private void SetTitleUIActive(bool isActive)
    {
        if (titleUIManager == null)
        {
            return;
        }

        titleUIManager.SetActive(isActive);
    }

    private void SetDifficultyUIActive(bool isActive)
    {
        if (difficultyUIManager == null)
        {
            return;
        }

        difficultyUIManager.SetActive(isActive);
    }

    private void UpdateResultReturnInput(float dt)
    {
        if (resultReturnInputDelayTimer < resultReturnInputDelaySeconds)
        {
            resultReturnInputDelayTimer += dt;
            resultReturnHoldTimer = 0f;
            return;
        }

        if (IManager.peddale <= resultReturnPedalThreshold)
        {
            resultReturnHoldTimer = 0f;
            return;
        }

        resultReturnHoldTimer += dt;
        if (resultReturnHoldTimer >= Mathf.Max(0.01f, resultReturnHoldSeconds))
        {
            ResetGame();
        }
    }

    private void UpdateTitleStartInput()
    {
        if (waitForPedalReleaseBeforeTitleStart)
        {
            if (IManager.peddale < titleStartPedalThreshold)
            {
                waitForPedalReleaseBeforeTitleStart = false;
            }

            return;
        }

        if (IManager.peddale >= titleStartPedalThreshold)
        {
            ShowDifficultySelect();
        }
    }

    private void ShowDifficultySelect()
    {
        if (state != State.Title)
        {
            return;
        }

        state = State.Difficulty;
        difficultySelectionCooldownTimer = 0f;
        difficultyHandleIsNeutral = Mathf.Abs(IManager.handle) < difficultyHandleNeutralThreshold;
        waitForPedalReleaseBeforeDifficultyConfirm = true;
        SetTitleUIActive(false);
        SetDifficultyUIActive(true);
        difficultyUIManager?.SetSelectedIndex(selectedDifficultyIndex);
    }

    private void UpdateDifficultyInput(float dt)
    {
        difficultySelectionCooldownTimer = Mathf.Max(0f, difficultySelectionCooldownTimer - dt);
        UpdateDifficultySelection();

        if (waitForPedalReleaseBeforeDifficultyConfirm)
        {
            if (IManager.peddale < difficultyConfirmPedalThreshold)
            {
                waitForPedalReleaseBeforeDifficultyConfirm = false;
            }

            return;
        }

        if (IManager.peddale >= difficultyConfirmPedalThreshold)
        {
            StartGame();
        }
    }

    private void UpdateDifficultySelection()
    {
        float handle = IManager.handle;
        float absHandle = Mathf.Abs(handle);
        if (absHandle < difficultyHandleNeutralThreshold)
        {
            difficultyHandleIsNeutral = true;
            return;
        }

        if (absHandle < difficultyHandleThreshold || (!difficultyHandleIsNeutral && difficultySelectionCooldownTimer > 0f))
        {
            return;
        }

        int direction = handle > 0f ? 1 : -1;
        selectedDifficultyIndex = WrapIndex(selectedDifficultyIndex + direction, GetDifficultyCount());
        difficultyUIManager?.SetSelectedIndex(selectedDifficultyIndex);
        difficultyHandleIsNeutral = false;
        difficultySelectionCooldownTimer = Mathf.Max(0.01f, difficultySelectionRepeatDelay);
    }

    private void ApplySelectedDifficulty()
    {
        RaceDifficultySetting setting = GetSelectedDifficultySetting();
        if (lapManager != null && setting.goalLapOverride > 0)
        {
            lapManager.SetGoalLap(setting.goalLapOverride);
        }
    }

    private RaceResultRecord AddRaceResult(RaceResultRecord resultRecord)
    {
        if (resultRecord == null)
        {
            resultRecord = CreateFallbackResultRecord();
        }

        RaceResultRecord existingResult = FindExistingRaceResult(resultRecord);
        if (existingResult != null)
        {
            if (existingResult.finishPosition <= 0 && resultRecord.finishPosition > 0)
            {
                existingResult.finishPosition = resultRecord.finishPosition;
            }

            if (string.IsNullOrEmpty(existingResult.difficultyName) && !string.IsNullOrEmpty(resultRecord.difficultyName))
            {
                existingResult.difficultyName = resultRecord.difficultyName;
            }

            return existingResult;
        }

        RaceDifficultySetting setting = GetSelectedDifficultySetting();
        if (string.IsNullOrEmpty(resultRecord.difficultyName))
        {
            resultRecord.difficultyName = GetDifficultyDisplayName(setting);
        }

        if (resultRecord.finishPosition <= 0)
        {
            resultRecord.finishPosition = raceResults.Count + 1;
        }

        raceResults.Add(resultRecord);
        return resultRecord;
    }

    private RaceResultRecord FindExistingRaceResult(RaceResultRecord candidate)
    {
        for (int index = 0; index < raceResults.Count; index++)
        {
            if (AreSameRaceResult(raceResults[index], candidate))
            {
                return raceResults[index];
            }
        }

        return null;
    }

    private static bool AreSameRaceResult(RaceResultRecord left, RaceResultRecord right)
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

    private RaceResultRecord CreateFallbackResultRecord()
    {
        return new RaceResultRecord
        {
            carName = car != null ? car.name : "Player",
            completedLaps = lapManager != null ? lapManager.GoalLap : 0,
            goalLap = lapManager != null ? lapManager.GoalLap : 0,
            totalRaceTime = time,
            finalLapTime = time,
            bestLapTime = time
        };
    }

    private RaceDifficultySetting GetSelectedDifficultySetting()
    {
        if (difficultySettings == null || difficultySettings.Length == 0)
        {
            difficultySettings = new[]
            {
                new RaceDifficultySetting { difficulty = RaceDifficulty.Normal, displayName = "NORMAL", goalLapOverride = 0 }
            };
        }

        selectedDifficultyIndex = Mathf.Clamp(selectedDifficultyIndex, 0, difficultySettings.Length - 1);
        if (difficultySettings[selectedDifficultyIndex] == null)
        {
            difficultySettings[selectedDifficultyIndex] = new RaceDifficultySetting
            {
                difficulty = (RaceDifficulty)selectedDifficultyIndex,
                displayName = ((RaceDifficulty)selectedDifficultyIndex).ToString().ToUpperInvariant(),
                goalLapOverride = 0
            };
        }

        return difficultySettings[selectedDifficultyIndex];
    }

    private int GetDifficultyCount()
    {
        return Mathf.Max(1, difficultySettings == null ? 0 : difficultySettings.Length);
    }

    private string[] GetDifficultyLabels()
    {
        GetSelectedDifficultySetting();
        int count = GetDifficultyCount();
        string[] labels = new string[count];
        for (int index = 0; index < count; index++)
        {
            if (difficultySettings[index] == null)
            {
                difficultySettings[index] = new RaceDifficultySetting
                {
                    difficulty = (RaceDifficulty)index,
                    displayName = ((RaceDifficulty)index).ToString().ToUpperInvariant(),
                    goalLapOverride = 0
                };
            }

            labels[index] = GetDifficultyDisplayName(difficultySettings[index]);
        }

        return labels;
    }

    private static string GetDifficultyDisplayName(RaceDifficultySetting setting)
    {
        if (setting != null && !string.IsNullOrEmpty(setting.displayName))
        {
            return setting.displayName;
        }

        return setting != null ? setting.difficulty.ToString().ToUpperInvariant() : "NORMAL";
    }

    private static List<RaceResultRecord> CopyResults(IReadOnlyList<RaceResultRecord> results)
    {
        List<RaceResultRecord> copy = new List<RaceResultRecord>();
        if (results == null)
        {
            return copy;
        }

        for (int index = 0; index < results.Count; index++)
        {
            if (results[index] != null)
            {
                copy.Add(results[index]);
            }
        }

        return copy;
    }

    private static int WrapIndex(int value, int count)
    {
        if (count <= 0)
        {
            return 0;
        }

        return (value % count + count) % count;
    }

    private static Transform GetOrCreateScreen(Transform canvas, string screenName)
    {
        Transform screen = canvas.Find(screenName);
        if (screen != null)
        {
            return screen;
        }

        GameObject screenObject = new GameObject(screenName, typeof(RectTransform), typeof(CanvasRenderer));
        RectTransform rect = screenObject.GetComponent<RectTransform>();
        rect.SetParent(canvas, false);
        rect.anchorMin = new Vector2(0f, 0f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        rect.pivot = new Vector2(0.5f, 0.5f);
        return screenObject.transform;
    }
}
