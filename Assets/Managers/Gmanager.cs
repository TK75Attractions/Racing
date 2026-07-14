using Microsoft.Unity.VisualStudio.Editor;
using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Cinemachine;

// Game Manager
// シーン全体のゲーム進行（入力管理、車の生成、カメラ制御、状態遷移など）を行うシングルトン
public class Gmanager : MonoBehaviour
{
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
    public CarControl car = null;
    public GameObject carPrefab;
    [SerializeField] private CheckpointSensor startCheckpoint;
    [SerializeField] private int startCheckpointIndex = 0;
    [SerializeField] private LapManager lapManager;
    [SerializeField] private OnPlayUIManager onPlayUIManager;
    [SerializeField] private ResultUIManager resultUIManager;
    [SerializeField] private float resultReturnPedalThreshold = 0.8f;
    [SerializeField] private float resultReturnHoldSeconds = 1f;
    [SerializeField] private float resultReturnInputDelaySeconds = 3f;
    [SerializeField] private int playerPosition = 1;
    [SerializeField] private float speedUnitMultiplier = 3.6f;

    private float resultReturnHoldTimer = 0f;
    private float resultReturnInputDelayTimer = 0f;
    private bool waitForPedalReleaseBeforeTitleStart = false;
    private RaceResultRecord latestResult;
    private Rigidbody playerRigidbody;

    public RaceResultRecord LatestResult => latestResult;

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
    // Game:  ゲームプレイ中
    // Result: 結果画面
    public enum State
    {
        Title,
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
        ResolveLapManager();
        onPlayUIManager = new();
        onPlayUIManager.Init(transform.parent.Find("MainCanvas").Find("OnPlay").transform);

        resultUIManager = new();
        resultUIManager.Init(transform.parent.Find("MainCanvas").Find("Result").transform);

        SetOnPlayUIActive(false);
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
            else if (state == State.Result)
            {
                UpdateResultReturnInput(dt);
            }
        }

        // 車が存在する場合は車の更新処理を実行
        if (car != null && state == State.Game)
        {
            time += dt;
            car.UpdateCar(dt);
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
        if (state != State.Title)
        {
            return;
        }

        Transform spawnPoint = GetStartPoint();
        Vector3 spawnPosition = spawnPoint != null ? spawnPoint.position : Vector3.zero;
        Quaternion spawnRotation = spawnPoint != null ? spawnPoint.rotation : Quaternion.identity;

        car = Instantiate(carPrefab, spawnPosition, spawnRotation).GetComponent<CarControl>();
        car.Init(spawnPosition);
        car.transform.SetPositionAndRotation(spawnPosition, spawnRotation);
        playerRigidbody = car.GetComponent<Rigidbody>();
        RegisterPlayerCar(spawnPoint);
        VCamera.Follow = car.transform;
        time = 0f;
        resultReturnHoldTimer = 0f;
        resultReturnInputDelayTimer = 0f;
        waitForPedalReleaseBeforeTitleStart = false;
        state = State.Game;
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
        if (state != State.Game)
        {
            return;
        }

        latestResult = resultRecord;
        state = State.Result;
        resultReturnHoldTimer = 0f;
        resultReturnInputDelayTimer = 0f;
        SetOnPlayUIActive(false);
        if (resultUIManager != null)
        {
            resultUIManager.ShowResults(resultRecord);
        }
        else
        {
            Debug.LogWarning("ResultUIManager was not found.");
        }

        Debug.Log("Game End");
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
            Destroy(car.gameObject);
            car = null;
        }

        playerRigidbody = null;
        time = 0f;
        resultReturnHoldTimer = 0f;
        resultReturnInputDelayTimer = 0f;
        SetOnPlayUIActive(false);
        if (resultUIManager != null)
        {
            resultUIManager.HideResults();
        }

        waitForPedalReleaseBeforeTitleStart = true;
        state = State.Title;
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
            if (IManager.peddale < resultReturnPedalThreshold)
            {
                waitForPedalReleaseBeforeTitleStart = false;
            }

            return;
        }

        if (IManager.peddale >= 1)
        {
            StartGame();
        }
    }
}
