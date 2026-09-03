using System;
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
    public GameObject car = null;
    public GameObject carPrefab;
    [SerializeField] private CheckpointSensor startCheckpoint;
    [SerializeField] private int startCheckpointIndex = 0;
    [SerializeField] private LapManager lapManager;
    [SerializeField] private OnPlayUIManager onPlayUIManager;
    [SerializeField] private ResultUIManager resultUIManager;
    [SerializeField] private ScreenTransitionController screenTransitionController;
    [SerializeField] private string titleText = "RACING";
    [SerializeField] private string titlePromptText = "PRESS THE PEDAL TO START";
    [SerializeField] private string titleReleasePromptText = "RELEASE THE PEDAL";
    [SerializeField] private float titleStartPedalThreshold = 0.8f;
    [SerializeField] private float titlePedalReleaseSeconds = 0.25f;
    [SerializeField] private float titleStartHoldSeconds = 0.25f;
    [SerializeField] private float cameraBlendSeconds = 0.65f;
    [SerializeField] private float resultReturnPedalThreshold = 0.8f;
    [SerializeField] private float resultReturnHoldSeconds = 1f;
    [SerializeField] private float resultReturnInputDelaySeconds = 3f;
    [SerializeField] private int playerPosition = 1;
    [SerializeField] private float speedUnitMultiplier = 3.6f;

    private RaceDirectionCameraController raceDirectionCamera;
    private float resultReturnHoldTimer = 0f;
    private float resultReturnInputDelayTimer = 0f;
    private float titlePedalReleaseTimer;
    private float titleStartHoldTimer;
    private bool titleStartArmed;
    private RaceResultRecord latestResult;
    private Rigidbody playerRigidbody;
    private CinemachineCamera titleCamera;
    private Vector3 titleCameraPosition;
    private Quaternion titleCameraRotation;
    private bool hasTitleCameraPose;

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
        CaptureTitleCameraPose();
        InitializeCameraTransition();
        raceDirectionCamera = GetComponent<RaceDirectionCameraController>();
        if (raceDirectionCamera == null)
        {
            raceDirectionCamera = gameObject.AddComponent<RaceDirectionCameraController>();
        }
        raceDirectionCamera.SetCamera(VCamera);
        ResolveLapManager();
        lapManager?.ResetRace();
        Transform mainCanvas = transform.parent.Find("MainCanvas");
        if (screenTransitionController == null)
        {
            screenTransitionController = mainCanvas.GetComponent<ScreenTransitionController>();
        }

        if (screenTransitionController == null)
        {
            screenTransitionController = mainCanvas.gameObject.AddComponent<ScreenTransitionController>();
        }

        screenTransitionController.Initialize(
            mainCanvas.Find("Title"),
            mainCanvas.Find("OnPlay"),
            mainCanvas.Find("Result"),
            titleText,
            titlePromptText);
        if (onPlayUIManager == null) onPlayUIManager = new();
        onPlayUIManager.Init(mainCanvas.Find("OnPlay"));

        if (resultUIManager == null) resultUIManager = new();
        resultUIManager.Init(mainCanvas.Find("Result"));

        screenTransitionController.ApplyStateImmediate(State.Title);
        SwitchCameraForState(State.Title);
        ResetTitleStartInputGate();
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
            bool canHandleStateInput = !IsScreenTransitioning();
            if (canHandleStateInput && state == State.Title)
            {
                UpdateTitleStartInput(dt);
            }
            else if (canHandleStateInput && state == State.Result)
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
        if (state != State.Title || IsScreenTransitioning())
        {
            return;
        }

        titlePedalReleaseTimer = 0f;
        titleStartHoldTimer = 0f;
        TransitionTo(State.Game, StartGameWhenScreenCovered, CompleteGameStart);
    }

    private void StartGameWhenScreenCovered()
    {
        Transform spawnPoint = GetStartPoint();
        Vector3 spawnPosition = spawnPoint != null ? spawnPoint.position : Vector3.zero;
        Quaternion spawnRotation = spawnPoint != null ? spawnPoint.rotation : Quaternion.identity;

        lapManager?.ResetRace();
        car = Instantiate(carPrefab, spawnPosition, spawnRotation);
        playerRigidbody = car.GetComponent<Rigidbody>();
        RegisterPlayerCar(spawnPoint);
        lapManager?.PauseRace();
        raceDirectionCamera.SetCar(car.transform);
        VCamera.Follow = raceDirectionCamera.CameraTarget;
        VCamera.LookAt = raceDirectionCamera.LookTarget;
        SnapRaceCameraToTarget();
        SwitchCameraForState(State.Game);
        time = 0f;
        resultReturnHoldTimer = 0f;
        resultReturnInputDelayTimer = 0f;
        UpdateOnPlayUI();
        Debug.Log("Game Start");
    }

    private void CompleteGameStart()
    {
        state = State.Game;
        lapManager?.ResumeRace();
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
        if (state != State.Game || IsScreenTransitioning())
        {
            return;
        }

        if (resultRecord == null)
        {
            resultRecord = new RaceResultRecord
            {
                carName = car != null ? car.name : string.Empty,
                totalRaceTime = time,
                finalLapTime = time,
                bestLapTime = time
            };
        }

        latestResult = resultRecord;
        state = State.Result;
        resultReturnHoldTimer = 0f;
        resultReturnInputDelayTimer = 0f;
        lapManager?.PauseRace();
        FreezePlayerForResult();
        TransitionTo(State.Result, () => ShowResultWhenScreenCovered(resultRecord));
    }

    private void ShowResultWhenScreenCovered(RaceResultRecord resultRecord)
    {
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
        if (state != State.Result || IsScreenTransitioning())
        {
            return;
        }

        state = State.Title;
        TransitionTo(State.Title, ResetGameWhenScreenCovered);
    }

    private void ResetGameWhenScreenCovered()
    {
        Rigidbody carRigidbodyToRemove = playerRigidbody;
        lapManager?.UnregisterCar(carRigidbodyToRemove);

        if (car != null)
        {
            Destroy(car);
            car = null;
        }

        if (raceDirectionCamera != null)
        {
            raceDirectionCamera.ClearCar();
        }

        RestoreTitleCameraPose();

        playerRigidbody = null;
        lapManager?.ResetRace();
        latestResult = null;
        time = 0f;
        resultReturnHoldTimer = 0f;
        resultReturnInputDelayTimer = 0f;
        if (resultUIManager != null)
        {
            resultUIManager.HideResults();
        }

        ResetTitleStartInputGate();
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

    private bool IsScreenTransitioning()
    {
        return screenTransitionController != null && screenTransitionController.IsTransitioning;
    }

    private void TransitionTo(
        State targetState,
        Action onScreenCovered,
        Action onCompleted = null)
    {
        if (screenTransitionController == null)
        {
            onScreenCovered?.Invoke();
            onCompleted?.Invoke();
            return;
        }

        if (!screenTransitionController.TryTransitionTo(targetState, onScreenCovered, onCompleted))
        {
            Debug.LogWarning($"Screen transition to {targetState} was ignored because another transition is active.");
        }
    }

    private void CaptureTitleCameraPose()
    {
        if (VCamera == null)
        {
            return;
        }

        titleCameraPosition = VCamera.transform.position;
        titleCameraRotation = VCamera.transform.rotation;
        hasTitleCameraPose = true;
    }

    private void InitializeCameraTransition()
    {
        if (VCamera == null || !hasTitleCameraPose)
        {
            return;
        }

        titleCamera = Instantiate(VCamera, VCamera.transform.parent);
        titleCamera.name = "TitleCamera";
        titleCamera.Follow = null;
        titleCamera.LookAt = null;
        titleCamera.ForceCameraPosition(titleCameraPosition, titleCameraRotation);

        CinemachineBrain brain = FindFirstObjectByType<CinemachineBrain>();
        if (brain != null)
        {
            brain.DefaultBlend = new CinemachineBlendDefinition(
                CinemachineBlendDefinition.Styles.EaseInOut,
                Mathf.Max(0f, cameraBlendSeconds));
        }
    }

    private void SwitchCameraForState(State targetState)
    {
        if (VCamera == null || titleCamera == null)
        {
            return;
        }

        bool useTitleCamera = targetState == State.Title;
        titleCamera.Priority = useTitleCamera ? 20 : 10;
        VCamera.Priority = useTitleCamera ? 10 : 20;
    }

    private void SnapRaceCameraToTarget()
    {
        if (VCamera == null || raceDirectionCamera == null ||
            raceDirectionCamera.CameraTarget == null || raceDirectionCamera.LookTarget == null)
        {
            return;
        }

        Vector3 cameraPosition = raceDirectionCamera.CameraTarget.position;
        Vector3 lookDirection = raceDirectionCamera.LookTarget.position - cameraPosition;
        Quaternion cameraRotation = lookDirection.sqrMagnitude > Mathf.Epsilon
            ? Quaternion.LookRotation(lookDirection.normalized, Vector3.up)
            : raceDirectionCamera.CameraTarget.rotation;

        VCamera.ForceCameraPosition(cameraPosition, cameraRotation);
    }

    private void RestoreTitleCameraPose()
    {
        if (VCamera == null)
        {
            return;
        }

        VCamera.Follow = null;
        VCamera.LookAt = null;

        if (titleCamera != null && hasTitleCameraPose)
        {
            titleCamera.ForceCameraPosition(titleCameraPosition, titleCameraRotation);
            SwitchCameraForState(State.Title);
        }
        else if (hasTitleCameraPose)
        {
            VCamera.ForceCameraPosition(titleCameraPosition, titleCameraRotation);
        }
    }

    private void FreezePlayerForResult()
    {
        if (car == null)
        {
            return;
        }

        SetBehaviourEnabled<DebugMover>(false);
        SetBehaviourEnabled<CarStabilityController>(false);
        SetBehaviourEnabled<CarResetter>(false);
        SetBehaviourEnabled<CarSoundController>(false);

        foreach (AudioSource audioSource in car.GetComponentsInChildren<AudioSource>(true))
        {
            audioSource.Stop();
        }

        if (playerRigidbody != null)
        {
            playerRigidbody.linearVelocity = Vector3.zero;
            playerRigidbody.angularVelocity = Vector3.zero;
            playerRigidbody.isKinematic = true;
        }
    }

    private void SetBehaviourEnabled<T>(bool isEnabled) where T : Behaviour
    {
        foreach (T behaviour in car.GetComponentsInChildren<T>(true))
        {
            behaviour.enabled = isEnabled;
        }
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

    private void ResetTitleStartInputGate()
    {
        titlePedalReleaseTimer = 0f;
        titleStartHoldTimer = 0f;
        titleStartArmed = false;
        screenTransitionController?.SetTitlePrompt(titleReleasePromptText);
    }

    private void UpdateTitleStartInput(float dt)
    {
        if (!titleStartArmed)
        {
            if (IManager.peddale < titleStartPedalThreshold)
            {
                titlePedalReleaseTimer += dt;
                if (titlePedalReleaseTimer >= Mathf.Max(0f, titlePedalReleaseSeconds))
                {
                    titleStartArmed = true;
                    titleStartHoldTimer = 0f;
                    screenTransitionController?.SetTitlePrompt(titlePromptText);
                }
            }
            else
            {
                titlePedalReleaseTimer = 0f;
            }

            return;
        }

        if (IManager.peddale < titleStartPedalThreshold)
        {
            titleStartHoldTimer = 0f;
            return;
        }

        titleStartHoldTimer += dt;
        if (titleStartHoldTimer >= Mathf.Max(0.01f, titleStartHoldSeconds))
        {
            titleStartHoldTimer = 0f;
            StartGame();
        }
    }
}
