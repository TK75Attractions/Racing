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
        public RaceDirectionCameraController cameraController;
        public PlayerDisplayRig displayRig;
        public CinemachineCamera titleCamera;
        public Vector3 titleCameraPosition;
        public Quaternion titleCameraRotation;
        public RaceResultRecord result;
        public bool isReady;
        public float readyHoldTimer;
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

    [Header("Camera")]
    [SerializeField] private float cameraBlendSeconds = 0.65f;
    [Header("HUD")]
    [SerializeField] private int playerPosition = 1;
    [SerializeField] private float speedUnitMultiplier = 3.6f;

    private readonly PlayerRuntime[] players = new PlayerRuntime[PlayerCount];
    private readonly OnPlayUIManager[] onPlayUIManagers = new OnPlayUIManager[PlayerCount];
    private readonly ResultUIManager[] resultUIManagers = new ResultUIManager[PlayerCount];
    private readonly ScreenTransitionController[] screenTransitions = new ScreenTransitionController[PlayerCount];
    private PlayerDisplayRig[] displayRigs = new PlayerDisplayRig[0];
    private float resultReturnHoldTimer;
    private float resultReturnInputDelayTimer;
    private float titlePedalReleaseTimer;
    private bool titleStartArmed;
    private float countdownTimeRemaining;
    private float goMessageTimeRemaining;
    private TwoPlayerRaceSession raceSession;
    private RaceResultRecord latestResult;
    private RaceSessionResult latestSessionResult;

    public RaceResultRecord LatestResult => latestResult;
    public RaceSessionResult LatestSessionResult => latestSessionResult;
    public float SecondPlaceTimeRemaining => raceSession?.SecondPlaceTimeRemaining ?? 0f;
    public bool WaitingForSecondPlace => raceSession != null && raceSession.WaitingForSecondPlace;
    public float CountdownTimeRemaining => countdownTimeRemaining;
    public bool IsDrivingEnabled => state == State.Game;

    public RaceCourse course;
    public Transform test;
    public float time = 0f;

    public enum State { Title, Countdown, Game, Result }
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
        if (lapManager != null)
        {
            lapManager.ResetRace();
            lapManager.CarFinished += HandleCarFinished;
        }

        displayRigs = TwoPlayerDisplayFactory.Create(transform.parent, cameraBlendSeconds);
        InitializePlayerDisplays();
        InitializeVolumes();
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

    private void LateUpdate()
    {
        if (VManager == null) return;
        if (!IsDrivingEnabled)
        {
            VManager.ResetDriftBoosts();
            return;
        }

        for (int index = 0; index < players.Length; index++)
        {
            DebugMover mover = players[index]?.mover;
            VManager.SetDriftBoost(index, mover != null ? mover.DriftBoostVisualIntensity : 0f);
        }
        VManager.TickDriftBoost(Time.deltaTime);
    }

    private void OnDisable()
    {
        if (Control == this) VManager?.ResetDriftBoosts();
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
        CompleteRace(raceSession.Result);
    }

    public void ShowResults() => ShowResult();

    public void ResetGame()
    {
        if (state != State.Result || IsScreenTransitioning()) return;
        state = State.Title;
        TransitionTo(State.Title, ResetGameWhenScreenCovered);
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
                titlePromptText);
            screenTransitions[playerIndex] = transition;

            OnPlayUIManager playUi = playerIndex == 0 && onPlayUIManager != null
                ? onPlayUIManager : new OnPlayUIManager();
            playUi.Init(rig.CanvasRoot.transform.Find("OnPlay"));
            onPlayUIManagers[playerIndex] = playUi;

            ResultUIManager resultsUi = playerIndex == 0 && resultUIManager != null
                ? resultUIManager : new ResultUIManager();
            resultsUi.Init(rig.CanvasRoot.transform.Find("Result"));
            resultUIManagers[playerIndex] = resultsUi;
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
            player.result = null;
            AssignPlayerInput(player.car, playerIndex);
            lapManager?.RegisterCar(player.rigidbody, spawnPoint);

            player.cameraController.SetCar(player.car.transform);
            player.displayRig.RaceCamera.Follow = player.cameraController.CameraTarget;
            player.displayRig.RaceCamera.LookAt = player.cameraController.LookTarget;
            SnapRaceCameraToTarget(player);
        }

        lapManager?.PauseRace();
        car = players[0].car;
        SwitchCameraForState(State.Countdown);
        time = 0f;
        resultReturnHoldTimer = 0f;
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
        SetRaceStatus(displayedSeconds.ToString());
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
        SetRaceStatus("GO!");
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
            SetRaceStatus(string.Empty);
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
            UpdateSecondPlaceDisplay();
            Debug.Log($"P{player.playerIndex + 1} finished first. Waiting {SecondPlaceTimeRemaining:F0} seconds for second place.");
            if (SecondPlaceTimeRemaining <= 0f && raceSession.Tick(0f)) CompleteRaceAfterTimeout();
            return;
        }

        CompleteRace(latestSessionResult);
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
        SetRaceStatus($"{playerLabel}  {raceSession.SecondPlaceTimeRemaining:0.0}s TO FINISH");
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
        CompleteRace(latestSessionResult);
    }

    private void CompleteRace(RaceSessionResult sessionResult)
    {
        if (state != State.Game || IsScreenTransitioning()) return;
        latestSessionResult = sessionResult;
        latestResult = sessionResult?.GetResultAtPosition(1);
        state = State.Result;
        SetRaceStatus(string.Empty);
        resultReturnHoldTimer = 0f;
        resultReturnInputDelayTimer = 0f;
        lapManager?.PauseRace();
        foreach (PlayerRuntime player in players) FreezePlayer(player, disableCollisions: false);
        TransitionTo(State.Result, ShowResultWhenScreenCovered);
    }

    private void ShowResultWhenScreenCovered()
    {
        foreach (ResultUIManager manager in resultUIManagers) manager?.ShowResults(latestSessionResult);
        Debug.Log("Two-player game end");
    }

    private void ResetGameWhenScreenCovered()
    {
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
        }

        car = null;
        lapManager?.ResetRace();
        latestResult = null;
        latestSessionResult = null;
        raceSession = null;
        countdownTimeRemaining = 0f;
        goMessageTimeRemaining = 0f;
        time = 0f;
        resultReturnHoldTimer = 0f;
        resultReturnInputDelayTimer = 0f;
        foreach (ResultUIManager manager in resultUIManagers) manager?.HideResults();
        SwitchCameraForState(State.Title);
        ResetTitleStartInputGate();
        Debug.Log("Two-player game reset");
    }

    private void ResolveLapManager()
    {
        if (lapManager == null) lapManager = FindFirstObjectByType<LapManager>(FindObjectsInactive.Include);
    }

    private void AssignPlayerInput(GameObject playerCar, int playerIndex)
    {
        if (playerCar == null || IManager == null) return;
        IDriveInputSource inputSource = IManager.GetPlayerInputSource(playerIndex);
        playerCar.GetComponent<DebugMover>()?.SetInputSource(inputSource);
        playerCar.GetComponent<CarResetter>()?.SetInputSource(inputSource);
    }

    private void UpdateOnPlayUI()
    {
        for (int playerIndex = 0; playerIndex < PlayerCount; playerIndex++)
        {
            PlayerRuntime player = players[playerIndex];
            OnPlayUIManager ui = onPlayUIManagers[playerIndex];
            if (player?.rigidbody == null || ui == null) continue;
            LapManager.CarTimeData lapData = lapManager?.GetCarData(player.rigidbody);
            int lapValue = GetCurrentLapValue(lapData);
            float lapSeconds = lapData != null ? lapData.currentLapTime : time;
            float totalSeconds = lapData != null ? lapData.totalRaceTime + lapData.currentLapTime : time;
            float speedValue = player.rigidbody.linearVelocity.magnitude * speedUnitMultiplier;
            ui.UpdateUI(GetRacePosition(playerIndex), lapValue, totalSeconds, lapSeconds, speedValue);
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
        if (currentData.lapCount != otherData.lapCount) return currentData.lapCount > otherData.lapCount ? 1 : 2;
        if (currentData.lastCheckpointIndex != otherData.lastCheckpointIndex)
            return currentData.lastCheckpointIndex > otherData.lastCheckpointIndex ? 1 : 2;
        return playerIndex + 1;
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
            resultReturnHoldTimer = 0f;
            return;
        }
        bool returnRequested = false;
        for (int playerIndex = 0; playerIndex < PlayerCount; playerIndex++)
            if (IManager.GetInputState(playerIndex).pedal > resultReturnPedalThreshold) returnRequested = true;
        if (!returnRequested)
        {
            resultReturnHoldTimer = 0f;
            return;
        }
        resultReturnHoldTimer += dt;
        if (resultReturnHoldTimer >= Mathf.Max(0.01f, resultReturnHoldSeconds)) ResetGame();
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
                bool readyInput = input.readyPressed || input.pedal >= titleStartPedalThreshold;
                player.readyHoldTimer = readyInput ? player.readyHoldTimer + dt : 0f;
                player.isReady = player.readyHoldTimer >= Mathf.Max(0.01f, titleStartHoldSeconds);
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
        string p1 = players[0].isReady ? "P1 READY" : "P1 PRESS PEDAL";
        string p2 = players[1].isReady ? "P2 READY" : "P2 PRESS PEDAL";
        return $"{p1}     {p2}";
    }

    private void SetTitlePrompt(string prompt)
    {
        foreach (ScreenTransitionController transition in screenTransitions) transition?.SetTitlePrompt(prompt);
    }

    private void SetRaceStatus(string status)
    {
        foreach (ScreenTransitionController transition in screenTransitions) transition?.SetRaceStatus(status);
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
