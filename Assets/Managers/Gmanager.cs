using System;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>2人分の入力、車両、表示、レース進行を統括します。</summary>
public class Gmanager : MonoBehaviour
{
    private const int PlayerCount = 2;

    private sealed class PlayerRuntime
    {
        public int playerIndex;
        public GameObject car;
        public Rigidbody rigidbody;
        public DebugMover mover;
        public DriftChargeVisual chargeVisual;
        public RaceDirectionCameraController cameraController;
        public PlayerDisplayRig displayRig;
        public CinemachineCamera titleCamera;
        public Vector3 titleCameraPosition;
        public Quaternion titleCameraRotation;
        public RaceResultRecord result;
        public int displayedRacePosition;
        public bool isReady;
        public float readyHoldTimer;
        public int resultSelection;
        public float resultConfirmTimer;
        public bool resultSteeringLatch;
        public bool returnedToTitle;
        public PlayerDriveInputGate inputGate;
        public InterruptionMenuUI interruptionMenu;
        public bool withdrewFromRace;
    }

    public static Gmanager Control = null;
    [SerializeField] public InputManager IManager = null;
    [SerializeField] public VManager VManager = null;
    public CinemachineCamera VCamera;
    public GameObject car = null;
    public GameObject carPrefab;

    [Header("Race Setup")]
    [SerializeField] private CheckpointSensor startCheckpoint;
    [SerializeField] private int startCheckpointIndex = 0;
    [SerializeField, Min(0f)] private float startGridSpacing = 4f;
    [SerializeField, Min(0f)] private float raceCountdownSeconds = 3f;
    [SerializeField, Min(0f)] private float goMessageSeconds = 0.75f;
    [SerializeField] private LapManager lapManager;
    [SerializeField, Min(0f)] private float secondPlaceTimeoutSeconds = 40f;

    [Header("UI")]
    [SerializeField] private OnPlayUIManager onPlayUIManager;
    [SerializeField] private ResultUIManager resultUIManager;
    [SerializeField] private ScreenTransitionController screenTransitionController;
    [SerializeField] private string titleText = "RACING";
    [SerializeField] private string titlePromptText = "BOTH PLAYERS: PRESS THE PEDAL";
    [SerializeField] private string titleReleasePromptText = "BOTH PLAYERS: RELEASE THE PEDAL";
    [SerializeField] private float titleStartPedalThreshold = 0.8f;
    [SerializeField] private float titlePedalReleaseSeconds = 0.25f;
    [SerializeField] private float titleStartHoldSeconds = 0.25f;
    [SerializeField] private float resultReturnPedalThreshold = 0.8f;
    [SerializeField] private float resultReturnHoldSeconds = 1f;
    [SerializeField] private float resultReturnInputDelaySeconds = 3f;
    [SerializeField, Range(0.1f, 1f)] private float resultSteeringThreshold = 0.45f;
    [SerializeField, Min(1.2f)] private float goalCelebrationSeconds = 5.5f;
    [SerializeField, Range(0, PlayerCount - 1)] private int defaultEscapePlayerIndex = 0;

    [Header("Camera")]
    [SerializeField] private float cameraBlendSeconds = 0.65f;
    [Header("HUD")]
    [SerializeField] private int playerPosition = 1;
    [SerializeField, Min(0f)] private float racePositionTieDistance = 0.25f;
    [SerializeField] private float speedUnitMultiplier = 3.6f;

    private readonly PlayerRuntime[] players = new PlayerRuntime[PlayerCount];
    private readonly OnPlayUIManager[] onPlayUIManagers = new OnPlayUIManager[PlayerCount];
    private readonly ResultUIManager[] resultUIManagers = new ResultUIManager[PlayerCount];
    private readonly ScreenTransitionController[] screenTransitions = new ScreenTransitionController[PlayerCount];
    private PlayerDisplayRig[] displayRigs = new PlayerDisplayRig[0];
    private float resultReturnInputDelayTimer;
    private float titlePedalReleaseTimer;
    private bool titleStartArmed;
    private float countdownTimeRemaining;
    private float goMessageTimeRemaining;
    private TwoPlayerRaceSession raceSession;
    private RaceResultRecord latestResult;
    private RaceSessionResult latestSessionResult;
    private int lastKeyboardPlayerIndex;

    public RaceResultRecord LatestResult => latestResult;
    public RaceSessionResult LatestSessionResult => latestSessionResult;
    public float SecondPlaceTimeRemaining => raceSession?.SecondPlaceTimeRemaining ?? 0f;
    public bool WaitingForSecondPlace => raceSession != null && raceSession.WaitingForSecondPlace;
    public float CountdownTimeRemaining => countdownTimeRemaining;
    public bool IsDrivingEnabled => state == State.Game;

    public RaceCourse course;
    public Transform test;
    public float time = 0f;

    public enum State { Title, Countdown, Game, Goal, Result }
    public State state = State.Title;
    public Sprite[] NumberSprites;

    public void Awake()
    {
        if (Control == null) Control = this;
        else
        {
            Destroy(gameObject);
            return;
        }

        IManager = GetComponent<InputManager>();
        IManager?.Init();
        ResolveLapManager();
        EnsureSpeedScenery();
        if (lapManager != null)
        {
            lapManager.ResetRace();
            lapManager.CarFinished += HandleCarFinished;
        }

        displayRigs = TwoPlayerDisplayFactory.Create(transform.parent, cameraBlendSeconds);
        InitializePlayerDisplays();
        InitializeVolumes();
        lastKeyboardPlayerIndex = Mathf.Clamp(defaultEscapePlayerIndex, 0, PlayerCount - 1);
        ApplyStateImmediate(State.Title);
        SwitchCameraForState(State.Title);
        ResetTitleStartInputGate();
    }

    public void Update()
    {
        float dt = Time.deltaTime;
        if (IManager != null)
        {
            IManager.UpdateInput(dt);
            UpdateInterruptionMenuInput();
            if (state == State.Title)
            {
                UpdateTitlePedalUI();
            }
            bool canHandleStateInput = !IsScreenTransitioning();
            if (canHandleStateInput && state == State.Title) UpdateTitleStartInput(dt);
            else if (canHandleStateInput && state == State.Result) UpdateResultReturnInput(dt);
        }

        if (state == State.Countdown && HasSpawnedCars())
        {
            UpdateOnPlayUI();
            UpdateRaceCountdown(dt);
        }
        else if (state == State.Game && HasSpawnedCars())
        {
            time += dt;
            UpdateOnPlayUI();
            UpdateSecondPlaceTimeout(dt);
            UpdateGoMessage(dt);
        }

#if UNITY_EDITOR
        if (Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame && course != null && test != null)
        {
            Debug.Log(course.IsPointInsideCourse(new Vector2(test.position.x, test.position.z)));
        }
#endif
    }

    private void InitializeVolumes()
    {
        if (VManager == null)
        {
            Transform volumeObject = transform.parent != null ? transform.parent.Find("VManager") : null;
            if (volumeObject != null)
            {
                VManager = volumeObject.GetComponent<VManager>();
                if (VManager == null) VManager = volumeObject.gameObject.AddComponent<VManager>();
            }
        }
        if (VManager == null)
        {
            Debug.LogWarning("Gmanager: VManager is not assigned; drift boost visuals are disabled.", this);
            return;
        }

        VManager.Init();
        Camera[] cameras = new Camera[displayRigs.Length];
        for (int index = 0; index < displayRigs.Length; index++) cameras[index] = displayRigs[index].MainCamera;
        VManager.ConfigurePlayerCameras(cameras);
    }

    private void EnsureSpeedScenery()
    {
        if (course == null) return;
        if (course.GetComponent<RaceSpeedSceneryController>() == null)
            course.gameObject.AddComponent<RaceSpeedSceneryController>();
    }

    private void LateUpdate()
    {
        bool gameplayVisualsActive = IsDrivingEnabled;
        foreach (PlayerRuntime player in players)
            player?.displayRig?.RaceVisuals?.SetGameplayActive(gameplayVisualsActive);

        if (VManager == null) return;
        if (!IsDrivingEnabled)
        {
            VManager.ResetDriftBoosts();
            return;
        }

        for (int index = 0; index < players.Length; index++)
        {
            DebugMover mover = players[index]?.mover;
            VManager.SetDriftBoost(index, mover != null ? mover.BoostVisualIntensity : 0f);
            // 画面端の色は、車体の火花と同じ段階色を使って揃えます。
            DriftChargeVisual chargeVisual = players[index]?.chargeVisual;
            VManager.SetDriftCharge(
                index,
                mover != null ? mover.NormalizedDriftCharge : 0f,
                chargeVisual != null ? chargeVisual.CurrentTierColor : Color.white,
                mover != null && mover.IsDriftChargeFull);
        }
        VManager.TickDriftBoost(Time.deltaTime);
    }

    private void OnDisable()
    {
        if (Control == this) VManager?.ResetDriftBoosts();
    }

    private void OnGUI()
    {
        if (IManager == null || !IManager.IsAnyDebugMode)
        {
            return;
        }

        GUILayout.BeginArea(new Rect(16f, 16f, 300f, 184f), GUI.skin.box);
        GUILayout.Label("DEBUG RACE CONTROLS");
        GUILayout.Label($"State: {state}");
        bool previousGuiEnabled = GUI.enabled;

        for (int playerIndex = 0; playerIndex < PlayerCount; playerIndex++)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label($"PLAYER {playerIndex + 1}", GUILayout.Width(90f));

            bool canAdvance = state == State.Game && players[playerIndex]?.rigidbody != null;
            GUI.enabled = canAdvance;
            if (GUILayout.Button("+1 LAP"))
            {
                AdvanceDebugLap(playerIndex, finish: false);
            }
            if (GUILayout.Button("GOAL"))
            {
                AdvanceDebugLap(playerIndex, finish: true);
            }
            GUI.enabled = previousGuiEnabled;
            GUILayout.EndHorizontal();
        }

        GUI.enabled = state == State.Title;
        if (GUILayout.Button("PREVIEW RESULT")) DebugPreviewResult();
        GUI.enabled = previousGuiEnabled;

        GUILayout.EndArea();
    }

    public void DebugPreviewResult()
    {
        ResetResultPlayerInputs();
        RaceSessionResult preview = new RaceSessionResult();
        preview.SetPlayerResult(0, new RaceResultRecord
        {
            playerNumber = 1, finishPosition = 1, didFinish = true, totalRaceTime = 92.461f
        });
        preview.SetPlayerResult(1, new RaceResultRecord
        {
            playerNumber = 2, finishPosition = 2, didFinish = true, totalRaceTime = 94.012f
        });
        latestSessionResult = preview;
        latestResult = preview.GetResultAtPosition(1);
        state = State.Result;
        resultReturnInputDelayTimer = 0f;
        foreach (ResultUIManager manager in resultUIManagers) manager?.ShowResults(preview);
        foreach (ScreenTransitionController transition in screenTransitions) transition?.TryShowResultAnimated();
    }

    private void AdvanceDebugLap(int playerIndex, bool finish)
    {
        if (state != State.Game || lapManager == null || playerIndex < 0 || playerIndex >= players.Length)
        {
            return;
        }

        Rigidbody playerRigidbody = players[playerIndex]?.rigidbody;
        if (playerRigidbody == null)
        {
            return;
        }

        lapManager.DebugAdvanceLap(playerRigidbody, finish);
        UpdateOnPlayUI();
    }

    public void StartGame()
    {
        if (state != State.Title || IsScreenTransitioning()) return;
        titlePedalReleaseTimer = 0f;
        TransitionTo(State.Countdown, StartGameWhenScreenCovered, CompleteGameStart);
    }

    public void ShowResult() => ShowResult(null);

    /// <summary>既存コード向けの互換入口です。通常の完走は CarFinished から処理します。</summary>
    public void ShowResult(RaceResultRecord resultRecord)
    {
        if (state != State.Game || IsScreenTransitioning()) return;

        RaceResultRecord first = resultRecord ?? CreateFallbackResult(0, true, 1);
        first.playerNumber = first.playerNumber > 0 ? first.playerNumber : 1;
        first.finishPosition = 1;
        first.didFinish = true;
        raceSession = new TwoPlayerRaceSession(0f);
        raceSession.Start();
        raceSession.RegisterFinish(0, first);
        raceSession.Tick(0f);
        raceSession.RegisterDnf(1, CreateFallbackResult(1, false, 2));
        CompleteRace(raceSession.Result, null);
    }

    public void ShowResults() => ShowResult();

    public void ResetGame()
    {
        if (state != State.Result || IsScreenTransitioning()) return;
        ScreenTransitionController primary = screenTransitions[0];
        for (int index = 1; index < screenTransitions.Length; index++)
        {
            screenTransitions[index]?.TryCloseResultAndTransition(State.Title, null);
        }

        if (primary == null)
        {
            ResetGameWhenScreenCovered();
            ApplyStateImmediate(State.Title);
            return;
        }

        if (!primary.TryCloseResultAndTransition(State.Title, ResetGameWhenScreenCovered))
        {
            Debug.LogWarning("Result close animation was ignored because another transition is active.");
        }
    }

    public void RetryGame()
    {
        if (state != State.Result || IsScreenTransitioning()) return;
        ScreenTransitionController primary = screenTransitions[0];
        for (int index = 1; index < screenTransitions.Length; index++)
            screenTransitions[index]?.TryCloseResultAndTransition(State.Countdown, null);

        if (primary == null)
        {
            RetryGameWhenScreenCovered();
            ApplyStateImmediate(State.Countdown);
            CompleteGameStart();
            return;
        }

        if (!primary.TryCloseResultAndTransition(State.Countdown, RetryGameWhenScreenCovered, CompleteGameStart))
            Debug.LogWarning("Result close animation was ignored because another transition is active.");
    }

    private void InitializePlayerDisplays()
    {
        if (displayRigs.Length != PlayerCount)
        {
            Debug.LogError("Two player display initialization failed.");
            return;
        }

        for (int playerIndex = 0; playerIndex < PlayerCount; playerIndex++)
        {
            PlayerDisplayRig rig = displayRigs[playerIndex];
            PlayerRuntime player = new PlayerRuntime
            {
                playerIndex = playerIndex,
                displayRig = rig,
                titleCameraPosition = rig.RaceCamera.transform.position,
                titleCameraRotation = rig.RaceCamera.transform.rotation
            };
            player.cameraController = CreateCameraController(playerIndex);
            player.cameraController.SetCamera(rig.RaceCamera);
            player.titleCamera = CreateTitleCamera(player);
            players[playerIndex] = player;

            ScreenTransitionController transition = rig.Transition;
            if (transition == null) transition = rig.CanvasRoot.AddComponent<ScreenTransitionController>();
            transition.Initialize(
                rig.CanvasRoot.transform.Find("Title"),
                rig.CanvasRoot.transform.Find("OnPlay"),
                rig.CanvasRoot.transform.Find("Result"),
                titleText,
                titlePromptText,
                playerIndex);
            screenTransitions[playerIndex] = transition;

            OnPlayUIManager playUi = playerIndex == 0 && onPlayUIManager != null
                ? onPlayUIManager : new OnPlayUIManager();
            playUi.Init(rig.CanvasRoot.transform.Find("OnPlay"), course, playerIndex, lapManager != null ? lapManager.GoalLap : 3);
            onPlayUIManagers[playerIndex] = playUi;

            ResultUIManager resultsUi = playerIndex == 0 && resultUIManager != null
                ? resultUIManager : new ResultUIManager();
            resultsUi.Init(rig.CanvasRoot.transform.Find("Result"), playerIndex + 1);
            resultUIManagers[playerIndex] = resultsUi;

            int capturedPlayerIndex = playerIndex;
            player.interruptionMenu = InterruptionMenuUI.Create(
                rig.CanvasRoot.transform,
                () => InterruptPlayer(capturedPlayerIndex),
                () => TogglePlayerDirection(capturedPlayerIndex),
                () => RestartPlayerFromStart(capturedPlayerIndex));
        }

        VCamera = displayRigs[0].RaceCamera;
        screenTransitionController = screenTransitions[0];
        onPlayUIManager = onPlayUIManagers[0];
        resultUIManager = resultUIManagers[0];
    }

    private RaceDirectionCameraController CreateCameraController(int playerIndex)
    {
        if (playerIndex == 0)
        {
            RaceDirectionCameraController existing = GetComponent<RaceDirectionCameraController>();
            return existing != null ? existing : gameObject.AddComponent<RaceDirectionCameraController>();
        }

        GameObject controllerObject = new GameObject($"Player{playerIndex + 1}CameraController");
        controllerObject.transform.SetParent(transform, false);
        return controllerObject.AddComponent<RaceDirectionCameraController>();
    }

    private CinemachineCamera CreateTitleCamera(PlayerRuntime player)
    {
        CinemachineCamera titleCamera = Instantiate(
            player.displayRig.RaceCamera,
            player.displayRig.RaceCamera.transform.parent);
        titleCamera.name = $"TitleCamera_P{player.playerIndex + 1}";
        titleCamera.Follow = null;
        titleCamera.LookAt = null;
        titleCamera.OutputChannel = (OutputChannels)(1 << player.playerIndex);
        titleCamera.ForceCameraPosition(player.titleCameraPosition, player.titleCameraRotation);
        return titleCamera;
    }

    private void StartGameWhenScreenCovered()
    {
        Transform spawnPoint = GetStartPoint();
        Vector3 basePosition = spawnPoint != null ? spawnPoint.position : Vector3.zero;
        Quaternion spawnRotation = spawnPoint != null ? spawnPoint.rotation : Quaternion.identity;
        Vector3 gridRight = spawnPoint != null ? spawnPoint.right : Vector3.right;

        lapManager?.ResetRace();
        raceSession = new TwoPlayerRaceSession(secondPlaceTimeoutSeconds);
        raceSession.Start();
        countdownTimeRemaining = Mathf.Max(0f, raceCountdownSeconds);
        goMessageTimeRemaining = 0f;
        latestResult = null;
        latestSessionResult = raceSession.Result;

        for (int playerIndex = 0; playerIndex < PlayerCount; playerIndex++)
        {
            PlayerRuntime player = players[playerIndex];
            float side = playerIndex == 0 ? -0.5f : 0.5f;
            Vector3 spawnPosition = basePosition + gridRight * startGridSpacing * side;
            player.car = Instantiate(carPrefab, spawnPosition, spawnRotation);
            player.car.name = $"Player{playerIndex + 1}_Car";
            player.rigidbody = player.car.GetComponent<Rigidbody>();
            player.mover = player.car.GetComponent<DebugMover>();
            player.displayRig.RaceVisuals?.Configure(
                playerIndex,
                player.rigidbody,
                player.mover,
                player.displayRig.MainCamera,
                player.displayRig.RaceCamera,
                player.displayRig.VisualEffectPivot,
                VManager);
            if (player.car.GetComponent<CarCollisionSparks>() == null)
                player.car.AddComponent<CarCollisionSparks>();
            if (player.car.GetComponent<CarWallCollisionResponse>() == null)
                player.car.AddComponent<CarWallCollisionResponse>();
            player.chargeVisual = player.car.GetComponent<DriftChargeVisual>();
            if (player.chargeVisual == null)
                player.chargeVisual = player.car.AddComponent<DriftChargeVisual>();
            if (player.car.GetComponent<CarLightController>() == null)
                player.car.AddComponent<CarLightController>();
            player.result = null;
            player.withdrewFromRace = false;
            player.displayedRacePosition = playerIndex + 1;
            AssignPlayerInput(player.car, playerIndex);
            lapManager?.RegisterCar(player.rigidbody, spawnPoint);

            player.cameraController.SetCar(player.car.transform);
            player.displayRig.RaceCamera.Follow = player.cameraController.CameraTarget;
            player.displayRig.RaceCamera.LookAt = player.cameraController.LookTarget;
            SnapRaceCameraToTarget(player);
        }

        lapManager?.PauseRace();
        car = players[0].car;
        BindMiniMapCars();
        SwitchCameraForState(State.Countdown);
        time = 0f;
        resultReturnInputDelayTimer = 0f;
        UpdateOnPlayUI();
        Debug.Log("Two-player game start");
    }

    private void CompleteGameStart()
    {
        state = State.Countdown;
        UpdateCountdownDisplay();
        if (countdownTimeRemaining <= 0f)
        {
            StartRaceAfterCountdown();
        }
    }

    private void UpdateRaceCountdown(float dt)
    {
        countdownTimeRemaining = Mathf.Max(0f, countdownTimeRemaining - Mathf.Max(0f, dt));
        if (countdownTimeRemaining <= 0f)
        {
            StartRaceAfterCountdown();
            return;
        }

        UpdateCountdownDisplay();
    }

    private void UpdateCountdownDisplay()
    {
        int displayedSeconds = Mathf.Max(1, Mathf.CeilToInt(countdownTimeRemaining));
        foreach (ScreenTransitionController transition in screenTransitions) transition?.ShowCountdown(displayedSeconds);
    }

    private void StartRaceAfterCountdown()
    {
        if (state != State.Countdown)
        {
            return;
        }

        countdownTimeRemaining = 0f;
        goMessageTimeRemaining = Mathf.Max(0f, goMessageSeconds);
        state = State.Game;
        lapManager?.ResumeRace();
        foreach (ScreenTransitionController transition in screenTransitions) transition?.ShowGo();
    }

    private void UpdateGoMessage(float dt)
    {
        if (goMessageTimeRemaining <= 0f || WaitingForSecondPlace)
        {
            return;
        }

        goMessageTimeRemaining = Mathf.Max(0f, goMessageTimeRemaining - Mathf.Max(0f, dt));
        if (goMessageTimeRemaining <= 0f)
        {
            ClearRaceStatus();
        }
    }

    private Transform GetStartPoint()
    {
        if (startCheckpoint != null) return startCheckpoint.transform;
        CheckpointSensor[] checkpoints = FindObjectsOfType<CheckpointSensor>();
        CheckpointSensor selected = null;
        foreach (CheckpointSensor checkpoint in checkpoints)
        {
            if (checkpoint.CheckpointIndex != startCheckpointIndex) continue;
            if (selected == null || checkpoint.transform.GetSiblingIndex() < selected.transform.GetSiblingIndex())
                selected = checkpoint;
        }

        if (selected == null)
        {
            Debug.LogWarning($"Start checkpoint index {startCheckpointIndex} was not found. Spawning at world origin.");
            return null;
        }

        startCheckpoint = selected;
        return selected.transform;
    }

    private void HandleCarFinished(Rigidbody finishedRigidbody, RaceResultRecord result)
    {
        if (state != State.Game || finishedRigidbody == null || result == null) return;
        PlayerRuntime player = FindPlayer(finishedRigidbody);
        if (player == null || player.result != null) return;

        RaceFinishRegistration registration = raceSession.RegisterFinish(player.playerIndex, result);
        if (registration == RaceFinishRegistration.Ignored) return;

        player.result = result;
        latestSessionResult = raceSession.Result;
        FreezePlayer(player, disableCollisions: true);

        if (registration == RaceFinishRegistration.FirstPlace)
        {
            latestResult = result;
            PlayPlayerGoal(player, () => BeginSpectating(player.playerIndex));
            UpdateSecondPlaceDisplay();
            Debug.Log($"P{player.playerIndex + 1} finished first. Waiting {SecondPlaceTimeRemaining:F0} seconds for second place.");
            if (SecondPlaceTimeRemaining <= 0f && raceSession.Tick(0f)) CompleteRaceAfterTimeout();
            return;
        }

        CompleteRace(latestSessionResult, player);
    }

    private void UpdateSecondPlaceTimeout(float dt)
    {
        if (raceSession == null || !raceSession.WaitingForSecondPlace) return;
        if (raceSession.Tick(dt)) CompleteRaceAfterTimeout();
        else UpdateSecondPlaceDisplay();
    }

    private void UpdateSecondPlaceDisplay()
    {
        if (raceSession == null || !raceSession.WaitingForSecondPlace)
        {
            return;
        }

        int unfinishedPlayerIndex = raceSession.UnfinishedPlayerIndex;
        string playerLabel = unfinishedPlayerIndex >= 0
            ? $"P{unfinishedPlayerIndex + 1}"
            : "SECOND PLACE";
        for (int playerIndex = 0; playerIndex < screenTransitions.Length; playerIndex++)
        {
            if (playerIndex == unfinishedPlayerIndex)
            {
                screenTransitions[playerIndex]?.ShowFinishWarning(playerLabel, raceSession.SecondPlaceTimeRemaining);
            }
        }
    }

    private void CompleteRaceAfterTimeout()
    {
        int unfinishedIndex = raceSession?.UnfinishedPlayerIndex ?? -1;
        PlayerRuntime unfinished = unfinishedIndex >= 0 ? players[unfinishedIndex] : null;
        if (unfinished != null)
        {
            RaceResultRecord dnfResult = CreateFallbackResult(unfinished.playerIndex, false, 2);
            unfinished.result = dnfResult;
            raceSession.RegisterDnf(unfinished.playerIndex, dnfResult);
            latestSessionResult = raceSession.Result;
            FreezePlayer(unfinished, disableCollisions: false);
        }
        CompleteRace(latestSessionResult, null);
    }

    private void CompleteRace(RaceSessionResult sessionResult, PlayerRuntime finalFinisher)
    {
        if (state != State.Game) return;
        latestSessionResult = sessionResult;
        latestResult = sessionResult?.GetResultAtPosition(1);
        state = State.Goal;
        ClearRaceStatus();
        resultReturnInputDelayTimer = 0f;
        lapManager?.PauseRace();
        foreach (PlayerRuntime player in players)
        {
            player?.interruptionMenu?.Hide();
            FreezePlayer(player, disableCollisions: false);
        }
        if (finalFinisher != null)
        {
            PlayPlayerGoal(finalFinisher, ShowResultsAfterGoal);
        }
        else
        {
            ShowResultsAfterGoal();
        }
    }

    private void PlayPlayerGoal(PlayerRuntime player, Action onCompleted)
    {
        if (player == null)
        {
            onCompleted?.Invoke();
            return;
        }

        string playerLabel = $"PLAYER {player.playerIndex + 1} FINISHED";
        string finishTime = player.result != null
            ? FormatRaceTime(player.result.totalRaceTime)
            : "--:--.---";
        ScreenTransitionController transition = screenTransitions[player.playerIndex];
        if (transition == null || !transition.TryPlayGoal(playerLabel, finishTime, goalCelebrationSeconds, onCompleted))
        {
            onCompleted?.Invoke();
        }
        Debug.Log($"P{player.playerIndex + 1} goal celebration started");
    }

    private void BeginSpectating(int finishedPlayerIndex)
    {
        if (finishedPlayerIndex < 0 || finishedPlayerIndex >= players.Length) return;
        int watchedPlayerIndex = 1 - finishedPlayerIndex;
        PlayerRuntime finished = players[finishedPlayerIndex];
        PlayerRuntime watched = players[watchedPlayerIndex];
        if (finished?.displayRig?.RaceCamera == null || watched?.displayRig?.RaceCamera == null) return;

        finished.displayRig.RaceCamera.Follow = watched.displayRig.RaceCamera.Follow;
        finished.displayRig.RaceCamera.LookAt = watched.displayRig.RaceCamera.LookAt;
        screenTransitions[finishedPlayerIndex]?.ApplyStateImmediate(State.Game);
        screenTransitions[finishedPlayerIndex]?.ShowSpectator(watchedPlayerIndex);
    }

    private void ShowResultsAfterGoal()
    {
        if (state != State.Goal)
        {
            return;
        }

        state = State.Result;
        ResetResultPlayerInputs();
        resultReturnInputDelayTimer = 0f;
        foreach (ResultUIManager manager in resultUIManagers) manager?.ShowResults(latestSessionResult);
        foreach (ScreenTransitionController transition in screenTransitions)
        {
            transition?.TryShowResultAnimated();
        }
        Debug.Log("Two-player game end");
    }

    private void ResetGameWhenScreenCovered()
    {
        state = State.Title;
        ClearCurrentRaceObjects();
        SwitchCameraForState(State.Title);
        ResetTitleStartInputGate();
        Debug.Log("Two-player game reset");
    }

    private void RetryGameWhenScreenCovered()
    {
        ClearCurrentRaceObjects();
        StartGameWhenScreenCovered();
        Debug.Log("Two-player race retry");
    }

    private void ClearCurrentRaceObjects()
    {
        ResetResultPlayerInputs();
        foreach (PlayerRuntime player in players)
        {
            if (player == null) continue;
            lapManager?.UnregisterCar(player.rigidbody);
            if (player.car != null) Destroy(player.car);
            player.cameraController?.ClearCar();
            player.displayRig.RaceCamera.Follow = null;
            player.displayRig.RaceCamera.LookAt = null;
            player.displayRig.RaceCamera.ForceCameraPosition(player.titleCameraPosition, player.titleCameraRotation);
            player.titleCamera.ForceCameraPosition(player.titleCameraPosition, player.titleCameraRotation);
            player.car = null;
            player.rigidbody = null;
            player.mover = null;
            player.result = null;
            player.inputGate?.Reset();
            player.inputGate = null;
            player.withdrewFromRace = false;
            player.interruptionMenu?.Hide();
        }

        car = null;
        BindMiniMapCars();
        lapManager?.ResetRace();
        latestResult = null;
        latestSessionResult = null;
        raceSession = null;
        countdownTimeRemaining = 0f;
        goMessageTimeRemaining = 0f;
        time = 0f;
        resultReturnInputDelayTimer = 0f;
        foreach (ResultUIManager manager in resultUIManagers) manager?.HideResults();
    }

    private static string FormatRaceTime(float seconds)
    {
        int totalMilliseconds = Mathf.FloorToInt(Mathf.Max(0f, seconds) * 1000f);
        int minutes = totalMilliseconds / 60000;
        int secondsPart = totalMilliseconds / 1000 % 60;
        int milliseconds = totalMilliseconds % 1000;
        return $"{minutes:00}:{secondsPart:00}.{milliseconds:000}";
    }

    private void ResolveLapManager()
    {
        if (lapManager == null) lapManager = FindFirstObjectByType<LapManager>(FindObjectsInactive.Include);
    }

    private void AssignPlayerInput(GameObject playerCar, int playerIndex)
    {
        if (playerCar == null || IManager == null) return;
        IDriveInputSource source = IManager.GetPlayerInputSource(playerIndex);
        PlayerDriveInputGate gate = new PlayerDriveInputGate(source);
        players[playerIndex].inputGate = gate;
        playerCar.GetComponent<DebugMover>()?.SetInputSource(gate);
        playerCar.GetComponent<CarResetter>()?.SetInputSource(gate);
    }

    private void UpdateInterruptionMenuInput()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null) return;

        bool playerOneActive = keyboard.wKey.isPressed || keyboard.aKey.isPressed ||
            keyboard.sKey.isPressed || keyboard.dKey.isPressed;
        bool playerTwoActive = keyboard.upArrowKey.isPressed || keyboard.leftArrowKey.isPressed ||
            keyboard.downArrowKey.isPressed || keyboard.rightArrowKey.isPressed;
        if (playerOneActive && !playerTwoActive) lastKeyboardPlayerIndex = 0;
        else if (playerTwoActive && !playerOneActive) lastKeyboardPlayerIndex = 1;

        if (!keyboard.escapeKey.wasPressedThisFrame || state != State.Game || IsScreenTransitioning()) return;

        for (int index = 0; index < players.Length; index++)
        {
            PlayerRuntime openPlayer = players[index];
            if (openPlayer?.interruptionMenu?.IsOpen != true) continue;
            CloseInterruptionMenu(index);
            return;
        }

        OpenInterruptionMenu(lastKeyboardPlayerIndex);
    }

    public void OpenInterruptionMenu(int playerIndex)
    {
        if (state != State.Game || playerIndex < 0 || playerIndex >= players.Length) return;
        PlayerRuntime player = players[playerIndex];
        if (player?.car == null || player.result != null || player.withdrewFromRace) return;
        player.inputGate?.SetBlocked(true);
        player.interruptionMenu?.Show(player.inputGate?.IsReverse == true);
    }

    private void CloseInterruptionMenu(int playerIndex)
    {
        PlayerRuntime player = players[playerIndex];
        player?.interruptionMenu?.Hide();
        if (player != null && !player.withdrewFromRace) player.inputGate?.SetBlocked(false);
    }

    private void InterruptPlayer(int playerIndex)
    {
        if (state != State.Game || playerIndex < 0 || playerIndex >= players.Length) return;
        PlayerRuntime player = players[playerIndex];
        if (player?.car == null) return;
        player.withdrewFromRace = true;
        player.inputGate?.SetBlocked(true);
        player.interruptionMenu?.Hide();
        FreezePlayer(player, disableCollisions: false);
        Debug.Log($"P{playerIndex + 1} interrupted the race.");
    }

    private void TogglePlayerDirection(int playerIndex)
    {
        if (playerIndex < 0 || playerIndex >= players.Length) return;
        PlayerRuntime player = players[playerIndex];
        if (player?.inputGate == null) return;
        bool isReverse = player.inputGate.ToggleDirection();
        player.interruptionMenu?.SetDirectionStatus(isReverse);
        CloseInterruptionMenu(playerIndex);
    }

    private void RestartPlayerFromStart(int playerIndex)
    {
        if (playerIndex < 0 || playerIndex >= players.Length) return;
        PlayerRuntime player = players[playerIndex];
        player?.car?.GetComponent<CarResetter>()?.ResetCar();
        CloseInterruptionMenu(playerIndex);
    }

    private void UpdateOnPlayUI()
    {
        for (int playerIndex = 0; playerIndex < PlayerCount; playerIndex++)
        {
            PlayerRuntime displayOwner = players[playerIndex];
            OnPlayUIManager ui = onPlayUIManagers[playerIndex];
            if (displayOwner == null || ui == null) continue;

            int viewedPlayerIndex = displayOwner.result != null && WaitingForSecondPlace
                ? 1 - playerIndex
                : playerIndex;
            PlayerRuntime player = players[viewedPlayerIndex];
            if (player?.rigidbody == null) continue;

            LapManager.CarTimeData lapData = lapManager?.GetCarData(player.rigidbody);
            int lapValue = GetCurrentLapValue(lapData);
            float lapSeconds = lapData != null ? lapData.currentLapTime : time;
            float totalSeconds = lapData != null ? lapData.totalRaceTime + lapData.currentLapTime : time;
            float speedValue = player.rigidbody.linearVelocity.magnitude * speedUnitMultiplier;
            ui.UpdateUI(GetRacePosition(viewedPlayerIndex), lapValue, totalSeconds, lapSeconds, speedValue);
        }
    }

    /// <summary>両プレイヤーのミニマップに、現在の車を割り当て直します。</summary>
    private void BindMiniMapCars()
    {
        Transform playerOneCar = players[0]?.car != null ? players[0].car.transform : null;
        Transform playerTwoCar = players[1]?.car != null ? players[1].car.transform : null;
        foreach (OnPlayUIManager ui in onPlayUIManagers)
        {
            ui?.SetMiniMapCars(playerOneCar, playerTwoCar);
        }
    }

    private int GetCurrentLapValue(LapManager.CarTimeData lapData)
    {
        if (lapData == null) return 1;
        int lapValue = lapData.lapCount + 1;
        if (lapManager != null && lapManager.GoalLap > 0) lapValue = Mathf.Min(lapValue, lapManager.GoalLap);
        return Mathf.Max(1, lapValue);
    }

    private int GetRacePosition(int playerIndex)
    {
        PlayerRuntime current = players[playerIndex];
        PlayerRuntime other = players[1 - playerIndex];
        if (current?.result != null) return current.result.finishPosition;
        if (other?.result != null) return 2;
        LapManager.CarTimeData currentData = lapManager?.GetCarData(current?.rigidbody);
        LapManager.CarTimeData otherData = lapManager?.GetCarData(other?.rigidbody);
        if (currentData == null || otherData == null) return playerIndex == 0 ? playerPosition : 2;

        float currentProgress = lapManager.GetRaceProgressDistance(current.rigidbody);
        float otherProgress = lapManager.GetRaceProgressDistance(other.rigidbody);
        float progressDelta = currentProgress - otherProgress;
        int position;

        if (Mathf.Abs(progressDelta) <= Mathf.Max(0f, racePositionTieDistance))
        {
            // 車体がほぼ並んでいる間は直前の順位を維持し、HUDのちらつきを抑えます。
            position = current.displayedRacePosition > 0
                ? current.displayedRacePosition
                : playerIndex + 1;
        }
        else
        {
            position = progressDelta > 0f ? 1 : 2;
        }

        current.displayedRacePosition = position;
        return position;
    }

    private void TransitionTo(State targetState, Action onScreenCovered, Action onCompleted = null)
    {
        ScreenTransitionController primary = screenTransitions[0];
        for (int index = 1; index < screenTransitions.Length; index++)
            screenTransitions[index]?.TryTransitionTo(targetState, null);
        if (primary == null)
        {
            onScreenCovered?.Invoke();
            onCompleted?.Invoke();
            return;
        }
        if (!primary.TryTransitionTo(targetState, onScreenCovered, onCompleted))
            Debug.LogWarning($"Screen transition to {targetState} was ignored because another transition is active.");
    }

    private void ApplyStateImmediate(State targetState)
    {
        foreach (ScreenTransitionController transition in screenTransitions) transition?.ApplyStateImmediate(targetState);
    }

    private bool IsScreenTransitioning()
    {
        foreach (ScreenTransitionController transition in screenTransitions)
            if (transition != null && transition.IsTransitioning) return true;
        return false;
    }

    private void SwitchCameraForState(State targetState)
    {
        bool useTitleCamera = targetState == State.Title;
        foreach (PlayerRuntime player in players)
        {
            if (player?.displayRig?.RaceCamera == null || player.titleCamera == null) continue;
            player.titleCamera.Priority = useTitleCamera ? 20 : 10;
            player.displayRig.RaceCamera.Priority = useTitleCamera ? 10 : 20;
        }
    }

    private static void SnapRaceCameraToTarget(PlayerRuntime player)
    {
        if (player?.displayRig?.RaceCamera == null || player.cameraController?.CameraTarget == null ||
            player.cameraController.LookTarget == null) return;
        Vector3 cameraPosition = player.cameraController.CameraTarget.position;
        Vector3 lookDirection = player.cameraController.LookTarget.position - cameraPosition;
        Quaternion cameraRotation = lookDirection.sqrMagnitude > Mathf.Epsilon
            ? Quaternion.LookRotation(lookDirection.normalized, Vector3.up)
            : player.cameraController.CameraTarget.rotation;
        player.displayRig.RaceCamera.ForceCameraPosition(cameraPosition, cameraRotation);
    }

    private static void FreezePlayer(PlayerRuntime player, bool disableCollisions)
    {
        if (player?.car == null) return;
        SetBehaviourEnabled<DebugMover>(player.car, false);
        SetBehaviourEnabled<CarStabilityController>(player.car, false);
        SetBehaviourEnabled<CarResetter>(player.car, false);
        SetBehaviourEnabled<CarSoundController>(player.car, false);
        foreach (AudioSource audioSource in player.car.GetComponentsInChildren<AudioSource>(true)) audioSource.Stop();
        if (disableCollisions)
            foreach (Collider collider in player.car.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
        if (player.rigidbody != null)
        {
            if (!player.rigidbody.isKinematic)
            {
                player.rigidbody.linearVelocity = Vector3.zero;
                player.rigidbody.angularVelocity = Vector3.zero;
            }
            player.rigidbody.isKinematic = true;
        }
    }

    private static void SetBehaviourEnabled<T>(GameObject target, bool isEnabled) where T : Behaviour
    {
        foreach (T behaviour in target.GetComponentsInChildren<T>(true)) behaviour.enabled = isEnabled;
    }

    private void UpdateResultReturnInput(float dt)
    {
        if (resultReturnInputDelayTimer < resultReturnInputDelaySeconds)
        {
            resultReturnInputDelayTimer += dt;
            return;
        }
        for (int index = 0; index < PlayerCount; index++)
        {
            PlayerRuntime player = players[index];
            if (player == null || player.returnedToTitle || screenTransitions[index]?.IsTransitioning == true) continue;
            DriveInputState input = IManager.GetInputState(index);
            if (!player.resultSteeringLatch && Mathf.Abs(input.steering) >= resultSteeringThreshold)
            {
                player.resultSelection = input.steering > 0f ? 1 : 0;
                player.resultSteeringLatch = true;
                player.resultConfirmTimer = 0f;
            }
            else if (Mathf.Abs(input.steering) < resultSteeringThreshold * 0.45f)
            {
                player.resultSteeringLatch = false;
            }
            resultUIManagers[index]?.SetMenuState(player.resultSelection, input.pedal);
            player.resultConfirmTimer = input.pedal > resultReturnPedalThreshold ? player.resultConfirmTimer + dt : 0f;
            if (player.resultConfirmTimer < Mathf.Max(0.01f, resultReturnHoldSeconds)) continue;
            player.resultConfirmTimer = 0f;
            resultUIManagers[index]?.PlayConfirm(player.resultSelection);
            if (player.resultSelection == 0)
            {
                RetryGame();
                return;
            }
            ReturnPlayerToTitle(index);
        }
    }

    private void ResetResultPlayerInputs()
    {
        foreach (PlayerRuntime player in players)
        {
            if (player == null) continue;
            player.resultSelection = 0;
            player.resultConfirmTimer = 0f;
            player.resultSteeringLatch = false;
            player.returnedToTitle = false;
        }
    }

    private void ReturnPlayerToTitle(int index)
    {
        PlayerRuntime player = players[index];
        Action covered = () =>
        {
            player.returnedToTitle = true;
            resultUIManagers[index]?.HideResults();
            player.titleCamera.Priority = 20;
            player.displayRig.RaceCamera.Priority = 10;
            screenTransitions[index]?.UpdateTitlePedals(0f, 0f, false, false, false);
            screenTransitions[index]?.SetTitlePrompt("WAITING FOR THE OTHER PLAYER TO RETURN");
        };
        Action completed = () =>
        {
            foreach (PlayerRuntime participant in players)
                if (participant == null || !participant.returnedToTitle) return;
            // 両者が戻るまでは結果データともう一方の画面を保持します。
            ResetGameWhenScreenCovered();
        };
        ScreenTransitionController transition = screenTransitions[index];
        if (transition == null)
        {
            covered();
            completed();
        }
        else transition.TryCloseResultAndTransition(State.Title, covered, completed);
    }

    private void ResetTitleStartInputGate()
    {
        titlePedalReleaseTimer = 0f;
        titleStartArmed = false;
        foreach (PlayerRuntime player in players)
        {
            if (player == null) continue;
            player.isReady = false;
            player.readyHoldTimer = 0f;
        }
        SetTitlePrompt(titleReleasePromptText);
    }

    private void UpdateTitleStartInput(float dt)
    {
        if (!titleStartArmed)
        {
            bool allReleased = true;
            for (int playerIndex = 0; playerIndex < PlayerCount; playerIndex++)
                if (IManager.GetInputState(playerIndex).pedal >= titleStartPedalThreshold) allReleased = false;
            titlePedalReleaseTimer = allReleased ? titlePedalReleaseTimer + dt : 0f;
            if (titlePedalReleaseTimer >= Mathf.Max(0f, titlePedalReleaseSeconds))
            {
                titleStartArmed = true;
                SetTitlePrompt(titlePromptText);
            }
            return;
        }

        bool allReady = true;
        for (int playerIndex = 0; playerIndex < PlayerCount; playerIndex++)
        {
            PlayerRuntime player = players[playerIndex];
            if (!player.isReady)
            {
                DriveInputState input = IManager.GetInputState(playerIndex);
                if (input.readyPressed)
                {
                    // Ready is a one-frame press event; it cannot satisfy a hold timer.
                    player.isReady = true;
                }
                else
                {
                    player.readyHoldTimer = input.pedal >= titleStartPedalThreshold
                        ? player.readyHoldTimer + dt : 0f;
                    player.isReady = player.readyHoldTimer >= Mathf.Max(0.01f, titleStartHoldSeconds);
                }
            }
            allReady &= player.isReady;
        }
        if (!allReady)
        {
            SetTitlePrompt(GetReadyPrompt());
            return;
        }
        StartGame();
    }

    private string GetReadyPrompt()
    {
        string p1 = players[0].isReady ? "P1  準備完了" : "P1  ペダルを踏んで準備";
        string p2 = players[1].isReady ? "P2  準備完了" : "P2  ペダルを踏んで準備";
        return $"{p1}     {p2}";
    }

    private void SetTitlePrompt(string prompt)
    {
        foreach (ScreenTransitionController transition in screenTransitions) transition?.SetTitlePrompt(prompt);
    }

    private void UpdateTitlePedalUI()
    {
        float playerOne = IManager.GetInputState(0).pedal;
        float playerTwo = IManager.GetInputState(1).pedal;
        bool playerOneReady = players[0] != null && players[0].isReady;
        bool playerTwoReady = players[1] != null && players[1].isReady;
        foreach (ScreenTransitionController transition in screenTransitions)
        {
            transition?.UpdateTitlePedals(playerOne, playerTwo, playerOneReady, playerTwoReady, titleStartArmed);
        }
    }

    private void ClearRaceStatus()
    {
        foreach (ScreenTransitionController transition in screenTransitions) transition?.ClearRaceStatus();
    }

    private RaceResultRecord CreateFallbackResult(int playerIndex, bool didFinish, int finishPosition)
    {
        PlayerRuntime player = players[playerIndex];
        LapManager.CarTimeData data = lapManager?.GetCarData(player?.rigidbody);
        float currentLapTime = data != null ? data.currentLapTime : time;
        float totalRaceTime = data != null ? data.totalRaceTime + data.currentLapTime : time;
        float bestLapTime = data != null && data.bestLapTime < float.MaxValue ? data.bestLapTime : 0f;
        return new RaceResultRecord
        {
            playerNumber = playerIndex + 1,
            finishPosition = finishPosition,
            didFinish = didFinish,
            carName = player?.car != null ? player.car.name : $"Player{playerIndex + 1}_Car",
            completedLaps = data != null ? data.lapCount : 0,
            goalLap = lapManager != null ? lapManager.GoalLap : 0,
            totalRaceTime = totalRaceTime,
            finalLapTime = currentLapTime,
            bestLapTime = bestLapTime
        };
    }

    private PlayerRuntime FindPlayer(Rigidbody target)
    {
        foreach (PlayerRuntime player in players)
            if (player != null && player.rigidbody == target) return player;
        return null;
    }

    private bool HasSpawnedCars()
    {
        foreach (PlayerRuntime player in players) if (player?.car != null) return true;
        return false;
    }

    private void OnDestroy()
    {
        if (Control == this) VManager?.ResetDriftBoosts();
        if (lapManager != null) lapManager.CarFinished -= HandleCarFinished;
        foreach (PlayerDisplayRig rig in displayRigs) rig?.Dispose();
        if (Control == this) Control = null;
    }
}
