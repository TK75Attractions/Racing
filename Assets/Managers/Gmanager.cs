using System;
using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>人間2人の入力・表示と、CPUを含むレース参加車両・進行を統括します。</summary>
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
        public bool resultConfirmArmed;
        public bool returnedToTitle;
        public PlayerDriveInputGate inputGate;
        public InterruptionMenuUI interruptionMenu;
        public bool withdrewFromRace;
        public bool isSpectating;
        public CarItemEffects itemEffects;
        public PlayerItemHUD itemHud;
        public CarSelectionUI carSelection;
        public State preparationState = State.Title;
        public bool titleInputArmed;
        public float titleReleaseTimer;
    }

    public static Gmanager Control = null;
    public RaceFeatureController Features { get; private set; }
    private RaceRuleSet activeRules;
    private int standardGoalLaps;
    private float RaceMaximumSeconds => activeRules?.MaximumSeconds ?? maximumRaceSeconds;
    private float RaceGraceSeconds => activeRules?.GraceSeconds ?? secondPlaceTimeoutSeconds;
    private float RaceMaximumGraceSeconds => activeRules?.MaximumGraceSeconds ?? maximumHumanFinishGraceSeconds;

    public void ApplyFeatureRules()
    {
        if (Features == null || lapManager == null) return;
        activeRules = new RaceRuleSet(Features.Settings.ActivePreset, standardGoalLaps, maximumRaceSeconds,
            secondPlaceTimeoutSeconds, maximumHumanFinishGraceSeconds);
        lapManager.SetGoalLaps(activeRules.GoalLaps);
        foreach (ScreenTransitionController transition in screenTransitions) transition?.RefreshLapTotal();
        foreach (OnPlayUIManager ui in onPlayUIManagers) ui?.Lap.SetTotalLaps(activeRules.GoalLaps);
    }
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
    [SerializeField, Min(0f)] private float maximumHumanFinishGraceSeconds = 180f;

    [Header("Preparation Time Limits")]
    [SerializeField, Min(1f)] private float carSelectionTimeLimitSeconds = CarSelectionUI.DefaultTimeLimitSeconds;
    [SerializeField, Min(1f)] private float tutorialTimeLimitSeconds = DrivingTutorialProgress.DefaultTimeLimitSeconds;

    [Header("CPU opponents (rehearsal)")]
    [SerializeField, Range(0, 2)] private int npcCount = 2;
    [SerializeField, Min(60f)] private float maximumRaceSeconds = 360f;

    [Header("Displays")]
    [Tooltip("P1 uses the main game window. On macOS, P2 opens a movable window on this preferred screen (or the main screen if unavailable). In the Editor, this is P2's Game view display number.")]
    [SerializeField, Range(2, 8)] private int playerTwoDisplay = 2;
    public int PlayerTwoDisplayIndex => playerTwoDisplay - 1;

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
    private bool finishingPreparation;
    private float countdownTimeRemaining;
    private float goMessageTimeRemaining;
    private RaceParticipantSession raceSession;
    private readonly List<RaceParticipant> participants = new List<RaceParticipant>(4);
    private readonly List<RaceParticipant> orderedParticipants = new List<RaceParticipant>(4);
    public IReadOnlyList<RaceParticipant> Participants => participants;
    public RaceParticipant GetParticipant(int index) => index >= 0 && index < participants.Count ? participants[index] : null;
    public RaceParticipant FindParticipant(Rigidbody body) => participants.Find(entry => entry.Body == body);
    private RaceResultRecord latestResult;
    private RaceSessionResult latestSessionResult;
    private RaceLightningDirector raceLightning;
    private DrivingTutorialController drivingTutorial;
    private RaceSoundEffects soundEffects;
    private int lastCountdownSound = -1;

    public RaceResultRecord LatestResult => latestResult;
    public RaceSessionResult LatestSessionResult => latestSessionResult;
    public float SecondPlaceTimeRemaining => raceSession?.SecondPlaceTimeRemaining ?? 0f;
    public bool WaitingForSecondPlace => raceSession != null && raceSession.WaitingForSecondPlace;
    public float CountdownTimeRemaining => countdownTimeRemaining;
    public bool IsCourseIntroPlaying => courseIntro != null && courseIntro.IsPlaying;
    private CourseIntroDirector courseIntro;
    public bool IsPreparing => state == State.Title || state == State.CarSelection || state == State.Tutorial;
    public State GetPlayerPreparationState(int index) => index >= 0 && index < PlayerCount ? players[index]?.preparationState ?? State.Title : state;
    public void GetPlayerDashboardProgress(int index, out string playerState, out string carName,
        out string step, out float remaining, out float progress)
    {
        PlayerRuntime player = index >= 0 && index < PlayerCount ? players[index] : null;
        State phase = IsPreparing ? GetPlayerPreparationState(index) : state;
        playerState = phase.ToString(); carName = player?.carSelection?.DashboardVehicleName ?? "";
        step = ""; remaining = progress = 0f;
        if (!IsPreparing && player != null)
        {
            if (state == State.Game) step = player.withdrewFromRace ? "Withdrawn" : player.isSpectating ? "Spectating" :
                player.result != null ? (player.result.didFinish ? "Finished" : "DNF") :
                player.interruptionMenu?.IsOpen == true ? "Menu" : "Driving";
            if (state == State.Result && player.returnedToTitle) { playerState = State.Title.ToString(); step = "Returned"; }
        }
        if (phase == State.CarSelection && player?.carSelection != null)
        {
            step = player.carSelection.DashboardSelectionStep;
            remaining = player.carSelection.TimeRemaining; progress = player.carSelection.ConfirmationProgress;
        }
        if (phase == State.Tutorial)
        {
            DrivingTutorialProgress lesson = drivingTutorial?.GetPlayerProgress(index);
            step = lesson == null ? "Waiting" : lesson.HasTimedOut ? "TimedOut" : lesson.CurrentStage.ToString();
            remaining = lesson?.TimeRemaining ?? 0f; progress = lesson?.HoldProgress ?? 0f;
        }
    }
    public bool IsDrivingEnabled => state == State.Game || ((state == State.CarSelection || state == State.Tutorial) &&
        !finishingPreparation && drivingTutorial != null && drivingTutorial.HasAnyStarted);
    public bool CanUseDashboardControls => !IsScreenTransitioning();
    public bool CanDriveVehicle(Rigidbody body) => IsDrivingEnabled ||
        (body != null && body.GetComponent<FinishRunController>()?.IsActive == true);
    public bool IsEngineAudioActive(Rigidbody body)
    {
        if (body != null && body.GetComponent<FinishRunController>()?.IsActive == true) return true;
        if (state != State.Countdown && state != State.Game && state != State.Tutorial && state != State.CarSelection)
            return false;
        RaceParticipant participant = FindParticipant(body);
        return participant == null || participant.Result == null;
    }
    public int GoalLap => lapManager != null ? lapManager.GoalLap : 0;
    public int GetPlayerLap(int index)
    {
        if (index < 0 || index >= players.Length) return 0;
        return lapManager?.GetCarData(players[index]?.rigidbody)?.lapCount ?? 0;
    }
    public float GetPlayerSpeed(int index)
    {
        if (index < 0 || index >= players.Length || players[index]?.rigidbody == null) return 0f;
        return players[index].rigidbody.linearVelocity.magnitude * speedUnitMultiplier;
    }
    public Camera GetPlayerCaptureCamera(int index)
    {
        if (index < 0 || index >= displayRigs.Length) return null;
        return displayRigs[index]?.BackImageCamera ?? displayRigs[index]?.MainCamera;
    }

    public byte[] CapturePlayerFrame(int index) => index >= 0 && index < displayRigs.Length
        ? displayRigs[index]?.CaptureJpeg(640, 360) : null;

    public Camera GetPlayerViewCamera(int index) => index >= 0 && index < displayRigs.Length ? displayRigs[index]?.MainCamera : null;

    public Transform GetPlayerCarTransform(int index) => index >= 0 && index < PlayerCount ? players[index]?.car?.transform : null;
    public int GetPlayerRacePosition(int index) => index >= 0 && index < PlayerCount ? GetRacePosition(index) : 0;
    public int GetParticipantLap(RaceParticipant entry) => entry == null ? 0 :
        entry.Result?.completedLaps ?? lapManager?.GetCarData(entry.Body)?.lapCount ?? 0;
    public float GetParticipantSpeed(RaceParticipant entry) => entry?.Body != null ?
        entry.Body.linearVelocity.magnitude * speedUnitMultiplier : 0f;
    public float GetPlayerProgressGap(int index)
    {
        if (index < 0 || index >= PlayerCount || lapManager == null || players[index]?.rigidbody == null || players[1-index]?.rigidbody == null) return 0f;
        return lapManager.GetRaceProgressDistance(players[index].rigidbody) - lapManager.GetRaceProgressDistance(players[1-index].rigidbody);
    }

    public RaceCourse course;
    public Transform test;
    public float time = 0f;

    public enum State { Title, Countdown, Game, Goal, Result, Tutorial, CarSelection }
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
        standardGoalLaps = lapManager != null ? lapManager.GoalLap : LapManager.DefaultGoalLaps;
        Features = GetComponent<RaceFeatureController>() ?? gameObject.AddComponent<RaceFeatureController>();
        Features.Configure(this);
        ApplyFeatureRules();
        EnsureSpeedScenery();
        var campusAtmosphere = GetComponent<CampusAtmosphereController>();
        if (campusAtmosphere == null) campusAtmosphere = gameObject.AddComponent<CampusAtmosphereController>();
        campusAtmosphere.Configure(this, course);
        if (lapManager != null)
        {
            lapManager.ResetRace();
            lapManager.CarFinished += HandleCarFinished;
        }

        displayRigs = TwoPlayerDisplayFactory.Create(transform.parent, cameraBlendSeconds, PlayerTwoDisplayIndex);
        InitializePlayerDisplays();
        if (GetComponent<RaceAudioOutput>() == null) gameObject.AddComponent<RaceAudioOutput>();
        soundEffects = GetComponent<RaceSoundEffects>() ?? gameObject.AddComponent<RaceSoundEffects>();
        if (GetComponent<RaceBackgroundMusic>() == null) gameObject.AddComponent<RaceBackgroundMusic>();
        InitializeVolumes();
        InitializeRaceLightning();
        ApplyStateImmediate(State.Title);
        SwitchCameraForState(State.Title);
        ResetTitleStartInputGate();
        gameObject.AddComponent<RaceDashboardBridge>();
        gameObject.AddComponent<RaceRemoteFirebasePublisher>();
    }

    public void Update()
    {
        float dt = Time.deltaTime;
        if (IManager != null)
        {
            IManager.UpdateInput(dt);
            UpdateInterruptionMenuInput();
            if (IsPreparing)
            {
                for (int i = 0; i < PlayerCount; i++)
                    if (players[i]?.preparationState == State.CarSelection && screenTransitions[i]?.IsTransitioning != true)
                        players[i].carSelection?.TickInput(IManager.GetInputState(i), IManager.IsPlayerConnected(i), Time.unscaledDeltaTime,
                            DriverInputNotice.Connection(IManager.GetPlayerInputSource(i)));
                UpdateTitleStartInput(dt);
                UpdateTitlePedalUI();
            }
            else if (!IsScreenTransitioning() && state == State.Result) UpdateResultReturnInput(dt);
        }

        if ((state == State.CarSelection || state == State.Tutorial) && !finishingPreparation)
        {
            drivingTutorial?.Tick(Time.unscaledDeltaTime);
            if (drivingTutorial != null && drivingTutorial.AllComplete && !IsScreenTransitioning())
            {
                finishingPreparation = true;
                TransitionTo(State.Countdown, FinishTutorialWhenScreenCovered, CompleteGameStart);
            }
        }
        else if (state == State.Countdown && HasSpawnedCars())
        {
            UpdateOnPlayUI();
            UpdateRaceCountdown(dt);
        }
        else if (state == State.Game && HasSpawnedCars())
        {
            time += dt;
            UpdateParticipantOrder();
            UpdateOnPlayUI();
            UpdateRaceLightning();
            UpdateSecondPlaceTimeout(dt);
            UpdateGoMessage(dt);
        }
        else if (state == State.Goal && !IsScreenTransitioning())
        {
            ShowResultsAfterGoal();
        }

#if UNITY_EDITOR
        if (Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame && course != null && test != null)
        {
            Debug.Log(course.IsPointInsideCourse(test.position));
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

    private void InitializeRaceLightning()
    {
        raceLightning = GetComponent<RaceLightningDirector>();
        if (raceLightning == null) raceLightning = gameObject.AddComponent<RaceLightningDirector>();
        RaceSpeedVisualController[] visuals = new RaceSpeedVisualController[displayRigs.Length];
        for (int index = 0; index < displayRigs.Length; index++) visuals[index] = displayRigs[index].RaceVisuals;
        raceLightning.Configure(VManager, visuals);
    }

    /// <summary>追い抜きとファイナルラップ突入を、稲妻の演出へ伝えます。</summary>
    private void UpdateRaceLightning()
    {
        if (raceLightning == null) return;
        PlayerRuntime playerOne = players[0];
        PlayerRuntime playerTwo = players[1];
        bool bothRacing = playerOne?.car != null && playerTwo?.car != null &&
            playerOne.result == null && playerTwo.result == null &&
            !playerOne.withdrewFromRace && !playerTwo.withdrewFromRace;
        int leaderIndex = -1;
        if (bothRacing && playerOne.displayedRacePosition != playerTwo.displayedRacePosition)
            leaderIndex = GetPlayerProgressGap(0) > 0 ? 0 : 1;
        raceLightning.UpdateOvertake(time, leaderIndex);

        if (lapManager == null) return;
        foreach (PlayerRuntime player in players)
        {
            if (player?.rigidbody == null || player.result != null || player.withdrewFromRace) continue;
            LapManager.CarTimeData lapData = lapManager.GetCarData(player.rigidbody);
            if (lapData != null) raceLightning.UpdateLap(player.playerIndex, lapData.lapCount, lapManager.GoalLap);
        }
    }

    private void LateUpdate()
    {
        bool gameplayVisualsActive = state == State.Game;
        foreach (PlayerRuntime player in players)
            player?.displayRig?.RaceVisuals?.SetGameplayActive(gameplayVisualsActive);

        if (VManager == null) return;
        if (state != State.Game)
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
        if (IManager == null || !IManager.IsAnyDebugMode || Features?.PanelVisible == true)
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
        RaceSessionResult preview = new RaceSessionResult(PlayerCount + Mathf.Clamp(npcCount,0,2));
        preview.SetPlayerResult(0, new RaceResultRecord
        {
            playerNumber = 1, finishPosition = 1, didFinish = true, totalRaceTime = 92.461f
        });
        preview.SetPlayerResult(1, new RaceResultRecord
        {
            playerNumber = 2, finishPosition = 2, didFinish = true, totalRaceTime = 94.012f
        });
        for(int cpu=0;cpu<Mathf.Clamp(npcCount,0,2);cpu++)
            preview.SetPlayerResult(PlayerCount+cpu,new RaceResultRecord { playerNumber=3+cpu,isCpu=true,
                finishPosition=3+cpu,didFinish=true,totalRaceTime=96+cpu*2,carName="SPORT CAR" });
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

    public void ConfirmTitleStart(int playerIndex)
    {
        if (!IsPreparing || playerIndex < 0 || playerIndex >= PlayerCount) return;
        PlayerRuntime player = players[playerIndex];
        if (player.preparationState != State.Title || screenTransitions[playerIndex]?.IsTransitioning == true) return;
        if (Features != null && !Features.Settings.RulesLocked)
        { Features.Settings.BeginPreparation(); ApplyFeatureRules(); }
        soundEffects?.Play(RaceSoundCue.Ready, playerIndex);
        TransitionPlayer(playerIndex, State.CarSelection, () =>
        {
            player.preparationState = State.CarSelection;
            if (state == State.Title) state = State.CarSelection;
            player.carSelection.Open(carSelectionTimeLimitSeconds);
        });
    }

    // Dashboard/editor start enters selection for every player still at the title.
    public void StartGame()
    {
        if (!IsPreparing) return;
        for (int i = 0; i < PlayerCount; i++) ConfirmTitleStart(i);
    }

    public bool AdvanceDashboardToTutorial()
    {
        if ((state != State.CarSelection && state != State.Tutorial) || !CanUseDashboardControls || finishingPreparation) return false;
        bool advanced = false;
        for (int i = 0; i < PlayerCount; i++)
        {
            if (players[i].preparationState == State.CarSelection)
            { players[i].carSelection.Confirm(); advanced = true; }
            else if (players[i].preparationState == State.Title)
            { int index = i; TransitionPlayer(index, State.Tutorial, () => BeginPlayerTutorial(index)); advanced = true; }
        }
        return advanced;
    }

    public bool AdvanceDashboardToCountdown()
    {
        if (state != State.Tutorial || !CanUseDashboardControls || finishingPreparation || drivingTutorial == null) return false;
        for (int i = 0; i < PlayerCount; i++)
            if (players[i].preparationState != State.Tutorial) return false;
        finishingPreparation = true;
        TransitionTo(State.Countdown, FinishTutorialWhenScreenCovered, CompleteGameStart);
        return true;
    }

    private void ConfirmCarSelection(int index)
    {
        PlayerRuntime player = players[index];
        if (!IsPreparing || player.preparationState != State.CarSelection || screenTransitions[index]?.IsTransitioning == true) return;
        TransitionPlayer(index, State.Tutorial, () => BeginPlayerTutorial(index));
    }

    private void CancelCarSelection(int index)
    {
        if (!IsPreparing || players[index].preparationState != State.CarSelection) return;
        TransitionPlayer(index, State.Title, () =>
        {
            PlayerRuntime player = players[index];
            player.preparationState = State.Title;
            player.titleInputArmed = false; player.titleReleaseTimer = player.readyHoldTimer = 0f;
            bool allTitle = true;
            foreach (PlayerRuntime participant in players) allTitle &= participant.preparationState == State.Title;
            if (allTitle)
            {
                state = State.Title; Features?.Settings.UnlockAtTitle(); ApplyFeatureRules();
            }
        });
    }

    private void TransitionPlayer(int index, State target, Action covered)
    {
        ScreenTransitionController transition = screenTransitions[index];
        if (transition == null)
        {
            covered(); screenTransitions[index]?.ApplyStateImmediate(target); return;
        }
        transition.TryTransitionTo(target, covered);
    }

    public void ShowResult() => ShowResult(null);

    /// <summary>既存コード向けの互換入口です。通常の完走は CarFinished から処理します。</summary>
    public void ShowResult(RaceResultRecord resultRecord)
    {
        if (state != State.Game || IsScreenTransitioning() || (resultRecord != null && (resultRecord.isCpu || resultRecord.playerNumber > PlayerCount))) return;

        if (resultRecord == null)
        {
            // Operator stop classifies the current standings; it does not award P1 a finish.
            if (raceSession == null) raceSession = new RaceParticipantSession(PlayerCount, 0, 0f, RaceMaximumSeconds);
            if (participants.Count == 0)
                for (int i = 0; i < PlayerCount; i++) raceSession.CloseParticipant(i, CreateFallbackResult(i, false, i + 1));
            CompleteRace(raceSession.Result, null);
            return;
        }

        RaceResultRecord first = resultRecord;
        first.playerNumber = first.playerNumber > 0 ? first.playerNumber : 1;
        first.didFinish = true;
        if (raceSession != null && participants.Count > 0)
        {
            int index = Mathf.Clamp(first.playerNumber-1,0,PlayerCount-1);
            RaceParticipant entry = GetParticipant(index);
            if (entry.Result == null && raceSession.RegisterFinish(index,first) != RaceFinishRegistration.Ignored)
            { entry.Result=first; players[index].result=first; }
            CompleteRace(raceSession.Result,null);
            return;
        }
        raceSession = new RaceParticipantSession(PlayerCount, 0, 0f, RaceMaximumSeconds);
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

        Action covered = () =>
        {
            ClearCurrentRaceObjects();
            Features?.Settings.BeginPreparation();
            ApplyFeatureRules();
            StartGameWhenScreenCovered();
        };
        if (primary == null)
        {
            covered();
            ApplyStateImmediate(State.Countdown);
            CompleteGameStart();
            return;
        }
        if (!primary.TryCloseResultAndTransition(State.Countdown, covered, CompleteGameStart))
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
            player.carSelection = CarSelectionUI.Create(rig.CanvasRoot.transform.Find("Title"), playerIndex);
            int selectionPlayer = playerIndex;
            player.carSelection.Confirmed += () => ConfirmCarSelection(selectionPlayer);
            player.carSelection.Cancelled += () => CancelCarSelection(selectionPlayer);

            OnPlayUIManager playUi = playerIndex == 0 && onPlayUIManager != null
                ? onPlayUIManager : new OnPlayUIManager();
            playUi.Init(rig.CanvasRoot.transform.Find("OnPlay"), course, playerIndex, lapManager != null ? lapManager.GoalLap : LapManager.DefaultGoalLaps);
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
            player.itemHud = PlayerItemHUD.Create(rig.CanvasRoot.transform);
            rig.CanvasRoot.AddComponent<RivalFeedbackHUD>().Configure(this, playerIndex, rig);
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

    private void BeginPlayerTutorial(int index)
    {
        players[index].preparationState = State.Tutorial;
        state = State.Tutorial;
        screenTransitions[index]?.ClearRaceStatus();
        if (drivingTutorial == null) drivingTutorial = gameObject.AddComponent<DrivingTutorialController>();
        drivingTutorial.BeginPlayer(carPrefab, IManager, displayRigs, index,
            (vehicle, playerIndex) => players[playerIndex].carSelection?.ApplyToCar(vehicle), tutorialTimeLimitSeconds);
        SwitchPlayerCameraForState(players[index], State.Tutorial);
    }

    private void FinishTutorialWhenScreenCovered()
    {
        drivingTutorial.End();
        StartGameWhenScreenCovered();
    }

    public void StartAutodriveTestRace()
    {
        if ((state != State.Title && state != State.Result) || IsScreenTransitioning()) return;
        Features.SetFeature(RaceFeature.PlayerTwoAutodrive, true);
        ClearCurrentRaceObjects(); Features.Settings.BeginPreparation(); ApplyFeatureRules();
        StartGameWhenScreenCovered(); CompleteGameStart();
        foreach (ScreenTransitionController transition in screenTransitions) transition?.ApplyStateImmediate(State.Countdown);
    }

    private void StartGameWhenScreenCovered()
    {
        if (Features != null && !Features.Settings.RulesLocked)
        { Features.Settings.BeginPreparation(); ApplyFeatureRules(); }
        Transform spawnPoint = GetStartPoint();
        Vector3 basePosition = spawnPoint != null ? spawnPoint.position : Vector3.zero;
        Quaternion spawnRotation = spawnPoint != null ? spawnPoint.rotation : Quaternion.identity;
        Vector3 gridRight = spawnPoint != null ? spawnPoint.right : Vector3.right;

        lapManager?.ResetRace();
        TireMarkRenderer.ClearAll();
        OilSlick.ClearAll();
        RaceItemPickup.ResetAll();
        raceLightning?.ResetRace();
        participants.Clear(); orderedParticipants.Clear();
        raceSession = new RaceParticipantSession(PlayerCount, Mathf.Clamp(npcCount, 0, 2), RaceGraceSeconds, RaceMaximumSeconds);
        raceSession.Start();
        countdownTimeRemaining = Mathf.Max(0f, raceCountdownSeconds);
        lastCountdownSound = -1;
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
            player.isSpectating = false;
            foreach (AudioSource source in player.car.GetComponentsInChildren<AudioSource>(true))
            {
                PlayerAudioCapture capture = source.GetComponent<PlayerAudioCapture>();
                if (capture == null) capture = source.gameObject.AddComponent<PlayerAudioCapture>();
                capture.ConfigureEngine(GetComponent<RaceAudioOutput>(), this, player.car.transform, playerIndex);
            }
            PlayerCarPaint paint = player.car.GetComponent<PlayerCarPaint>();
            if (paint == null) paint = player.car.AddComponent<PlayerCarPaint>();
            if (player.carSelection != null) player.carSelection.ApplyToCar(player.car);
            else paint.SetPlayerIndex(playerIndex);
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
            CarCollisionSparks collisionSparks = player.car.GetComponent<CarCollisionSparks>();
            if (collisionSparks == null) collisionSparks = player.car.AddComponent<CarCollisionSparks>();
            // 衝突の揺れは、ぶつかった車を操作しているプレイヤーのカメラだけに伝えます。
            RaceSpeedVisualController raceVisuals = player.displayRig.RaceVisuals;
            if (raceVisuals != null) collisionSparks.Impact += raceVisuals.AddImpact;
            if (player.car.GetComponent<TireEffectsVisual>() == null)
                player.car.AddComponent<TireEffectsVisual>();
            if (player.car.GetComponent<CarWallCollisionResponse>() == null)
                player.car.AddComponent<CarWallCollisionResponse>();
            player.chargeVisual = player.car.GetComponent<DriftChargeVisual>();
            if (player.chargeVisual == null)
                player.chargeVisual = player.car.AddComponent<DriftChargeVisual>();
            DriftLightningVisual driftLightning = player.car.GetComponent<DriftLightningVisual>();
            if (driftLightning == null) driftLightning = player.car.AddComponent<DriftLightningVisual>();
            driftLightning.Configure(playerIndex, VManager, player.displayRig.RaceVisuals);
            raceLightning?.SetCar(playerIndex, player.car.transform);
            if (player.car.GetComponent<CarLightController>() == null)
                player.car.AddComponent<CarLightController>();
            player.itemEffects = player.car.GetComponent<CarItemEffects>();
            if (player.itemEffects == null)
                player.itemEffects = player.car.AddComponent<CarItemEffects>();
            player.itemEffects.Configure(playerIndex);
            player.itemHud?.Bind(player.itemEffects);
            player.result = null;
            player.withdrewFromRace = false;
            player.displayedRacePosition = playerIndex + 1;
            AssignPlayerInput(player.car, playerIndex);
            player.car.AddComponent<DriftCoachingUI>().Configure(this, player.mover, playerIndex);
            lapManager?.RegisterCar(player.rigidbody, spawnPoint);
            RaceParticipant participant = new RaceParticipant { Index = playerIndex, Car = player.car, Body = player.rigidbody,
                Effects = player.itemEffects, Position = playerIndex + 1 };
            participants.Add(participant);
            player.car.AddComponent<RaceDrivingSoundEffects>().Configure(this, participant);
            player.car.AddComponent<SlipstreamController>().Configure(this, participant);
            player.car.AddComponent<CourseSteeringAssist>().Configure(this, participant);
            player.car.AddComponent<LandingRewardController>().Configure(this, participant);
            player.car.AddComponent<TubeChargeLineController>().Configure(this, participant);
            if (playerIndex == 1) player.car.AddComponent<PlayerAutodrive>().Configure(this, participant);

            player.cameraController.SetCar(player.car.transform);
            player.displayRig.RaceCamera.Follow = player.cameraController.CameraTarget;
            player.displayRig.RaceCamera.LookAt = player.cameraController.LookTarget;
            SnapRaceCameraToTarget(player);
        }

        SpawnCpuCars(spawnPoint, basePosition, spawnRotation, gridRight);
        UpdateParticipantOrder();
        lapManager?.PauseRace();
        car = players[0].car;
        BindMiniMapCars();
        SwitchCameraForState(State.Countdown);
        time = 0f;
        resultReturnInputDelayTimer = 0f;
        UpdateOnPlayUI();
        courseIntro = GetComponent<CourseIntroDirector>();
        if (courseIntro == null) courseIntro = gameObject.AddComponent<CourseIntroDirector>();
        courseIntro.Begin(this, displayRigs);
        Debug.Log("Two-player game start");
    }

    private void SpawnCpuCars(Transform start, Vector3 position, Quaternion rotation, Vector3 right)
    {
        for (int cpu = 0; cpu < Mathf.Clamp(npcCount, 0, 2); cpu++)
        {
            int index = PlayerCount + cpu;
            GameObject vehicle = Instantiate(carPrefab, position - rotation * Vector3.forward * 7f + right * startGridSpacing * (cpu == 0 ? -.5f : .5f), rotation);
            vehicle.name = $"CPU{cpu + 1}_Car";
            // CPUs borrow the vehicle physics/visuals, never a player display or input device.
            foreach (Camera camera in vehicle.GetComponentsInChildren<Camera>(true)) camera.enabled = false;
            foreach (AudioListener listener in vehicle.GetComponentsInChildren<AudioListener>(true)) listener.enabled = false;
            CarItemEffects effects = vehicle.GetComponent<CarItemEffects>() ?? vehicle.AddComponent<CarItemEffects>();
            effects.Configure(index);
            RaceParticipant entry = new RaceParticipant { Index = index, IsCpu = true, Car = vehicle,
                Body = vehicle.GetComponent<Rigidbody>(), Effects = effects, Position = index + 1 };
            participants.Add(entry);
            lapManager?.RegisterCar(entry.Body, start);
            entry.Driver = vehicle.AddComponent<NpcRaceDriver>(); entry.Driver.Configure(this, lapManager, entry);
            PlayerCarPaint paint = vehicle.GetComponent<PlayerCarPaint>() ?? vehicle.AddComponent<PlayerCarPaint>(); paint.SetColor(cpu == 0 ? new Color(1f,.65f,.025f) : new Color(.20f,.9f,.4f));
            foreach (AudioSource source in vehicle.GetComponentsInChildren<AudioSource>(true))
            {
                PlayerAudioCapture capture = source.GetComponent<PlayerAudioCapture>() ?? source.gameObject.AddComponent<PlayerAudioCapture>();
                capture.ConfigureEngine(GetComponent<RaceAudioOutput>(), this, vehicle.transform, index);
            }
            if (vehicle.GetComponent<CarWallCollisionResponse>() == null) vehicle.AddComponent<CarWallCollisionResponse>();
            Transform label = new GameObject("CPU identity").transform; label.SetParent(vehicle.transform, false); label.localPosition = Vector3.up * 2.5f;
            TMPro.TextMeshPro text = label.gameObject.AddComponent<TMPro.TextMeshPro>();
            text.font = RacingUIFontCatalog.GetHUD(false);
            text.text = entry.Label; text.fontSize = 5f; text.alignment = TMPro.TextAlignmentOptions.Center;
            text.color = new Color(1f, .8f, .25f); text.rectTransform.sizeDelta = new Vector2(4f, 1f);
        }
    }

    private void UpdateParticipantOrder()
    {
        orderedParticipants.Clear();
        foreach (RaceParticipant entry in participants)
        { entry.Progress = lapManager != null ? lapManager.GetRaceProgressDistance(entry.Body) : 0; orderedParticipants.Add(entry); }
        // Begin in previous rank order, then move only past cars with a clear progress gap.
        orderedParticipants.Sort((a, b) => a.Position.CompareTo(b.Position));
        for (int i = 1; i < orderedParticipants.Count; i++)
        {
            RaceParticipant moving = orderedParticipants[i]; int j = i - 1;
            while (j >= 0 && CompareParticipants(moving, orderedParticipants[j]) < 0)
            { orderedParticipants[j + 1] = orderedParticipants[j]; j--; }
            orderedParticipants[j + 1] = moving;
        }
        for (int i = 0; i < orderedParticipants.Count; i++)
        {
            RaceParticipant entry = orderedParticipants[i]; entry.Position = i + 1;
            if (!entry.IsCpu) players[entry.Index].displayedRacePosition = entry.Position;
        }
    }

    private int CompareParticipants(RaceParticipant a, RaceParticipant b)
    {
        bool aFinished = a.Result?.didFinish == true, bFinished = b.Result?.didFinish == true;
        if (aFinished != bFinished) return aFinished ? -1 : 1;
        if (aFinished) return a.Result.finishPosition.CompareTo(b.Result.finishPosition);
        if ((a.Result != null) != (b.Result != null)) return a.Result != null ? 1 : -1;
        if (Mathf.Abs(a.Progress - b.Progress) > racePositionTieDistance) return b.Progress.CompareTo(a.Progress);
        return 0;
    }

    public bool IsNpcRecoveryVisible(Vector3 position)
    {
        for (int i = 0; i < PlayerCount; i++)
        {
            Camera camera = GetPlayerViewCamera(i); if (camera == null) return true;
            Vector3 view = camera.WorldToViewportPoint(position);
            if (view.z > 0 && view.z < camera.farClipPlane && view.x > -.15f && view.x < 1.15f && view.y > -.15f && view.y < 1.15f) return true;
        }
        return false;
    }

    private void CompleteGameStart()
    {
        finishingPreparation = false;
        state = State.Countdown;
        if (IsCourseIntroPlaying) return;
        UpdateCountdownDisplay();
        if (countdownTimeRemaining <= 0f)
        {
            StartRaceAfterCountdown();
        }
    }

    private void UpdateRaceCountdown(float dt)
    {
        if (IsCourseIntroPlaying)
        {
            courseIntro.Advance(dt);
            if (!IsCourseIntroPlaying)
            {
                if (countdownTimeRemaining <= 0f) StartRaceAfterCountdown();
                else UpdateCountdownDisplay();
            }
            return;
        }
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
        if (displayedSeconds != lastCountdownSound)
        {
            lastCountdownSound = displayedSeconds;
            soundEffects?.PlayForAll(RaceSoundCue.Countdown);
        }
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
        soundEffects?.PlayForAll(RaceSoundCue.Go);
        lapManager?.ResumeRace();
        foreach (RaceParticipant entry in participants) entry.Driver?.SetDriving(true);
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
        RaceParticipant entry = FindParticipant(finishedRigidbody);
        if (entry == null || entry.Result != null) return;
        PlayerRuntime player = entry.IsCpu ? null : players[entry.Index];
        result.carName = player?.carSelection?.SelectedVehicleName ?? result.carName;
        if (!entry.IsCpu) result.achievements = CaptureAchievements(entry);
        RaceFinishRegistration registration = raceSession.RegisterFinish(entry.Index, result);
        if (registration == RaceFinishRegistration.Ignored) return;
        entry.Result = result; latestSessionResult = raceSession.Result;
        if (registration == RaceFinishRegistration.FirstPlace && lapManager != null && course != null)
        {
            RaceParticipant remaining = GetParticipant(raceSession.UnfinishedPlayerIndex);
            if (remaining?.IsRacing == true && lapManager.GoalLap > 0)
            {
                float progress = lapManager.GetRaceProgressDistance(remaining.Body);
                float pace = Mathf.Max(0f, progress) / Mathf.Max(1f, time);
                raceSession.ExtendHumanGrace(HumanFinishGrace.Estimate(
                    lapManager.GoalLap * course.TotalLength - progress, pace,
                    RaceGraceSeconds, RaceMaximumGraceSeconds));
            }
        }
        if (entry.IsCpu)
        {
            if (!StartFinishRun(entry)) FreezeCpu(entry);
            UpdateParticipantOrder(); return;
        }
        player.result = result;
        if (!StartFinishRun(entry)) FreezePlayer(player, disableCollisions: true);
        if (registration == RaceFinishRegistration.RaceComplete) { CompleteRace(latestSessionResult, player); return; }
        latestResult = latestSessionResult.GetResultAtPosition(1);
        PlayPlayerGoal(player, () => BeginSpectating(player.playerIndex));
        UpdateSecondPlaceDisplay();
    }

    private void UpdateSecondPlaceTimeout(float dt)
    {
        if (raceSession == null) return;
        if (RaceFeatureController.Enabled(RaceFeature.FinishApproachGrace) && raceSession.WaitingForSecondPlace &&
            raceSession.SecondPlaceTimeRemaining <= 1f && course != null && lapManager != null)
        {
            RaceParticipant remaining = GetParticipant(raceSession.UnfinishedPlayerIndex);
            if (remaining?.IsRacing == true && lapManager.GetCarData(remaining.Body)?.isOffCourse == false &&
                course.TryGetNearestRoadSample(remaining.Body.position, out var road))
            {
                float distance = lapManager.GoalLap * course.TotalLength - lapManager.GetRaceProgressDistance(remaining.Body);
                float forward = Vector3.Dot(remaining.Body.linearVelocity, road.forward) * CourseProgressSign;
                raceSession.TryExtendFinishApproach(distance, forward, RaceMaximumGraceSeconds);
            }
        }
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
        CompleteRace(raceSession.Result, null);
    }

    private RaceResultRecord CreateParticipantDnf(RaceParticipant entry)
    {
        if (!entry.IsCpu) return CreateFallbackResult(entry.Index, false, entry.Position);
        LapManager.CarTimeData data = lapManager?.GetCarData(entry.Body);
        RaceResultRecord result = data != null ? lapManager.CreateResultRecord(data, data.currentLapTime) : new RaceResultRecord();
        if (data != null) result.totalRaceTime += data.currentLapTime;
        result.carName = entry.Label; result.isCpu = true; result.playerNumber = entry.Index + 1; result.didFinish = false;
        return result;
    }

    private bool StartFinishRun(RaceParticipant entry)
    {
        if (!RaceFeatureController.Enabled(RaceFeature.FinishRun) || course == null || !course.HasValidPath) return false;
        var run = entry.Car.GetComponent<FinishRunController>() ?? entry.Car.AddComponent<FinishRunController>();
        run.Begin(this, entry);
        return true;
    }

    private static void FreezeCpu(RaceParticipant entry)
    {
        if (entry.Car.GetComponent<FinishRunController>()?.IsActive == true) return;
        entry.Driver?.SetDriving(false);
        foreach (Collider collider in entry.Car.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
        SetBehaviourEnabled<DebugMover>(entry.Car, false);
        SetBehaviourEnabled<CarStabilityController>(entry.Car, false);
        SetBehaviourEnabled<CarItemEffects>(entry.Car, false);
        SetBehaviourEnabled<CarSoundController>(entry.Car, false);
        foreach (AudioSource source in entry.Car.GetComponentsInChildren<AudioSource>(true)) source.Stop();
    }

    private void CompleteRace(RaceSessionResult sessionResult, PlayerRuntime finalFinisher)
    {
        if (state != State.Game) return;
        UpdateParticipantOrder();
        foreach (RaceParticipant entry in orderedParticipants)
        {
            if (entry.Result == null)
            {
                RaceResultRecord record = CreateParticipantDnf(entry);
                raceSession.CloseParticipant(entry.Index, record); entry.Result = record;
                if (!entry.IsCpu) players[entry.Index].result = record;
            }
            if (entry.IsCpu) FreezeCpu(entry);
        }
        for (int i = 0; i < orderedParticipants.Count; i++)
            if (orderedParticipants[i].Result?.didFinish == false) orderedParticipants[i].Result.finishPosition = i + 1;
        latestSessionResult = sessionResult;
        latestResult = sessionResult?.GetResultAtPosition(1);
        state = State.Goal;
        TireMarkRenderer.ClearAll();
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

        soundEffects?.Play(RaceSoundCue.Finish, player.playerIndex);
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
        // A late goal callback must not replace a result screen after timeout/withdrawal.
        if (state != State.Game) return;
        if (finishedPlayerIndex < 0 || finishedPlayerIndex >= players.Length) return;
        int watchedPlayerIndex = 1 - finishedPlayerIndex;
        PlayerRuntime finished = players[finishedPlayerIndex];
        PlayerRuntime watched = players[watchedPlayerIndex];
        if (watched?.result != null || watched?.withdrewFromRace == true) return;
        if (finished?.displayRig?.RaceCamera == null || watched?.displayRig?.RaceCamera == null) return;

        finished.displayRig.RaceCamera.Follow = watched.displayRig.RaceCamera.Follow;
        finished.displayRig.RaceCamera.LookAt = watched.displayRig.RaceCamera.LookAt;
        finished.isSpectating = true;
        screenTransitions[finishedPlayerIndex]?.ApplyStateImmediate(State.Game);
        screenTransitions[finishedPlayerIndex]?.ShowSpectator(watchedPlayerIndex);
    }

    private void ShowResultsAfterGoal()
    {
        if (state != State.Goal || IsScreenTransitioning())
        {
            return;
        }

        state = State.Result;
        soundEffects?.PlayForAll(RaceSoundCue.Results);
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
        Features?.Settings.ResetForNextGroup();
        ApplyFeatureRules();
        SwitchCameraForState(State.Title);
        ResetTitleStartInputGate();
        Debug.Log("Two-player game reset");
    }

    private void ClearCurrentRaceObjects()
    {
        courseIntro?.Stop();
        drivingTutorial?.End();
        finishingPreparation = false;
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
            player.isSpectating = false;
            player.interruptionMenu?.Hide();
            player.itemHud?.Bind(null);
            player.itemEffects = null;
        }

        foreach (RaceParticipant entry in participants)
        {
            if (!entry.IsCpu) continue;
            lapManager?.UnregisterCar(entry.Body);
            if (entry.Car != null) Destroy(entry.Car);
        }
        participants.Clear(); orderedParticipants.Clear();
        OilSlick.ClearAll();
        car = null;
        BindMiniMapCars();
        lapManager?.ResetRace();
        TireMarkRenderer.ClearAll();
        raceLightning?.ResetRace();
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

    public int GetAudioViewedPlayer(int displayPlayerIndex)
    {
        if (displayPlayerIndex < 0 || displayPlayerIndex >= players.Length) return -1;
        return state == State.Game && players[displayPlayerIndex]?.isSpectating == true
            ? 1 - displayPlayerIndex : displayPlayerIndex;
    }

    public bool IsPlayerOnFinalLap(int playerIndex)
    {
        if (playerIndex < 0 || playerIndex >= players.Length || lapManager == null || lapManager.GoalLap <= 0) return false;
        LapManager.CarTimeData data = lapManager.GetCarData(players[playerIndex]?.rigidbody);
        return data != null && data.lapCount >= lapManager.GoalLap - 1;
    }

    public Transform GetPlayerAudioPerspective(int displayPlayerIndex)
    {
        if (displayPlayerIndex < 0 || displayPlayerIndex >= players.Length) return null;
        return players[displayPlayerIndex]?.displayRig?.MainCamera?.transform;
    }

    private void AssignPlayerInput(GameObject playerCar, int playerIndex)
    {
        if (playerCar == null || IManager == null) return;
        IDriveInputSource source = IManager.GetPlayerInputSource(playerIndex);
        PlayerDriveInputGate gate = new PlayerDriveInputGate(source);
        players[playerIndex].inputGate = gate;
        playerCar.GetComponent<DebugMover>()?.SetInputSource(gate);
        playerCar.GetComponent<CarResetter>()?.SetInputSource(gate);
        playerCar.GetComponent<CarResetter>()?.SetCourse(course, lapManager);
    }

    private void UpdateInterruptionMenuInput()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null) return;
        if (!keyboard.escapeKey.wasPressedThisFrame || state != State.Game || IsScreenTransitioning()) return;

        bool anyMenuOpen = false;
        for (int index = 0; index < players.Length; index++)
        {
            if (players[index]?.interruptionMenu?.IsOpen == true)
            {
                anyMenuOpen = true;
                break;
            }
        }

        for (int index = 0; index < players.Length; index++)
        {
            if (anyMenuOpen)
            {
                soundEffects?.Play(RaceSoundCue.Cancel, index);
                CloseInterruptionMenu(index);
            }
            else OpenInterruptionMenu(index);
        }
    }

    public void OpenInterruptionMenu(int playerIndex)
    {
        if (state != State.Game || playerIndex < 0 || playerIndex >= players.Length) return;
        PlayerRuntime player = players[playerIndex];
        if (player?.car == null || player.result != null || player.withdrewFromRace) return;
        if (player.interruptionMenu?.IsOpen != true) soundEffects?.Play(RaceSoundCue.Press, playerIndex);
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
        if (player?.car == null || player.result != null || player.withdrewFromRace) return;
        RaceResultRecord result = CreateFallbackResult(playerIndex, false, 2);
        if (raceSession == null || !raceSession.RegisterWithdrawal(playerIndex, result)) return;
        player.withdrewFromRace = true;
        player.result = result;
        if (GetParticipant(playerIndex) != null) GetParticipant(playerIndex).Result = result;
        latestSessionResult = raceSession.Result;
        player.inputGate?.SetBlocked(true);
        player.interruptionMenu?.Hide();
        FreezePlayer(player, disableCollisions: true);
        if (raceSession.IsComplete) CompleteRace(latestSessionResult, null);
        else BeginSpectating(playerIndex);
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
        player?.car?.GetComponent<CarResetter>()?.ResetToStart();
        CloseInterruptionMenu(playerIndex);
    }

    private void UpdateOnPlayUI()
    {
        float progressGap = GetPlayerProgressGap(0);
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
            DebugMover mover = player.mover;
            int ownerPosition = GetRacePosition(playerIndex, playerIndex == 0 ? progressGap : -progressGap);
            int viewedPosition = viewedPlayerIndex == playerIndex ? ownerPosition
                : GetRacePosition(viewedPlayerIndex, viewedPlayerIndex == 0 ? progressGap : -progressGap);
            ui.UpdateUI(viewedPosition, lapValue, totalSeconds, lapSeconds, speedValue,
                mover != null ? mover.AccelerationPadBoostTimeRemaining : 0f,
                mover != null ? mover.AccelerationPadBoostDuration : 0f, ownerPosition, playerIndex == 0 ? progressGap : -progressGap);
            ui.UpdateParticipants(participants);
            ui.UpdateDriverNotice(player.inputGate, player.car.GetComponent<CarResetter>(),
                state == State.Game && player.result == null && !player.withdrewFromRace,
                course != null && course.IsJumpAhead(player.rigidbody.position,CourseProgressSign),
                player.car.GetComponent<DriftCoachingUI>()?.Notice ?? "", mover != null && mover.IsSlipstreamBoosting, player.car.GetComponent<LandingRewardController>()?.Notice ?? "", player.car.GetComponent<TubeChargeLineController>()?.Notice ?? "", player.car.GetComponent<PlayerAutodrive>()?.IsActive == true);
        }
    }

    /// <summary>両プレイヤーのミニマップに、現在の車を割り当て直します。</summary>
    private void BindMiniMapCars()
    {
        Transform playerOneCar = players[0]?.car != null ? players[0].car.transform : null;
        Transform playerTwoCar = players[1]?.car != null ? players[1].car.transform : null;
        for (int index = 0; index < onPlayUIManagers.Length; index++)
        {
            OnPlayUIManager ui = onPlayUIManagers[index];
            ui?.MiniMap.SetViewTransform(players[index]?.displayRig?.MainCamera != null
                ? players[index].displayRig.MainCamera.transform : null);
            ui?.SetMiniMapCars(playerOneCar, playerTwoCar);
            ui?.MiniMap.SetCpuCars(GetParticipant(2)?.Car?.transform, GetParticipant(3)?.Car?.transform);
        }
    }

    private int GetCurrentLapValue(LapManager.CarTimeData lapData)
    {
        if (lapData == null) return 1;
        int lapValue = lapData.lapCount + 1;
        if (lapManager != null && lapManager.GoalLap > 0) lapValue = Mathf.Min(lapValue, lapManager.GoalLap);
        return Mathf.Max(1, lapValue);
    }

    private int GetRacePosition(int playerIndex, float? knownProgressDelta = null)
    {
        RaceParticipant entry = GetParticipant(playerIndex);
        return entry != null ? entry.Position : playerIndex + 1;
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
        foreach (PlayerRuntime player in players) SwitchPlayerCameraForState(player, targetState);
    }

    private static void SwitchPlayerCameraForState(PlayerRuntime player, State targetState)
    {
        bool useTitleCamera = targetState == State.Title || targetState == State.CarSelection;
        if (player?.displayRig?.RaceCamera == null || player.titleCamera == null) return;
        player.titleCamera.Priority = useTitleCamera ? 20 : 10;
        player.displayRig.RaceCamera.Priority = useTitleCamera ? 10 : 20;
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
        if (player.car.GetComponent<FinishRunController>()?.IsActive == true) return;
        SetBehaviourEnabled<DebugMover>(player.car, false);
        SetBehaviourEnabled<CarStabilityController>(player.car, false);
        SetBehaviourEnabled<CarResetter>(player.car, false);
        SetBehaviourEnabled<CarSoundController>(player.car, false);
        SetBehaviourEnabled<CarItemEffects>(player.car, false);
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

    public void SelectResultOption(int playerIndex, int option)
    {
        if(option < 0 || option > 1 || state != State.Result || IsScreenTransitioning() || playerIndex < 0 || playerIndex >= PlayerCount || players[playerIndex].returnedToTitle) return;
        soundEffects?.Play(option == 0 ? RaceSoundCue.Confirm : RaceSoundCue.Cancel, playerIndex);
        resultUIManagers[playerIndex]?.PlayConfirm(option);
        if(option == 0) { RetryGame(); return; }
        ReturnPlayerToTitle(playerIndex);
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
                player.resultSelection = 1 - player.resultSelection;
                soundEffects?.Play(RaceSoundCue.Select, index);
                player.resultSteeringLatch = true;
                player.resultConfirmTimer = 0f;
            }
            else if (Mathf.Abs(input.steering) < resultSteeringThreshold * 0.45f)
            {
                player.resultSteeringLatch = false;
            }
            // Finishing with the pedal down must never confirm the default retry option.
            if (!player.resultConfirmArmed)
            {
                player.resultConfirmArmed = Mathf.Abs(input.pedal) <= 0.05f;
                player.resultConfirmTimer = 0f;
                resultUIManagers[index]?.SetMenuState(player.resultSelection, 0f);
                resultUIManagers[index]?.SetPedalConfirmationReady(player.resultConfirmArmed);
                continue;
            }
            player.resultConfirmTimer = input.pedal > resultReturnPedalThreshold ? player.resultConfirmTimer + dt : 0f;
            resultUIManagers[index]?.SetMenuState(player.resultSelection, Mathf.Clamp01(player.resultConfirmTimer / Mathf.Max(.01f,resultReturnHoldSeconds)));
            if (player.resultConfirmTimer < Mathf.Max(0.01f, resultReturnHoldSeconds)) continue;
            player.resultConfirmTimer = 0f;
            SelectResultOption(index, player.resultSelection);
            if (player.resultSelection == 0) return;
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
            player.resultConfirmArmed = false;
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
        foreach (PlayerRuntime player in players)
        {
            if (player == null) continue;
            player.preparationState = State.Title;
            player.isReady = false;
            player.readyHoldTimer = player.titleReleaseTimer = 0f;
            player.titleInputArmed = false;
        }
        SetTitlePrompt(titleReleasePromptText);
    }

    private void UpdateTitleStartInput(float dt)
    {
        for (int index = 0; index < PlayerCount; index++)
        {
            PlayerRuntime player = players[index];
            if (player.preparationState != State.Title || screenTransitions[index]?.IsTransitioning == true) continue;
            DriveInputState input = IManager.GetInputState(index);
            if (!IManager.IsPlayerConnected(index))
            { player.titleInputArmed = false; player.titleReleaseTimer = player.readyHoldTimer = 0f; continue; }
            if (!player.titleInputArmed)
            {
                player.titleReleaseTimer = Mathf.Abs(input.pedal) < .2f ? player.titleReleaseTimer + dt : 0f;
                if (player.titleReleaseTimer >= titlePedalReleaseSeconds) player.titleInputArmed = true;
                continue;
            }
            player.readyHoldTimer = input.pedal >= titleStartPedalThreshold ? player.readyHoldTimer + dt : 0f;
            if (input.readyPressed || player.readyHoldTimer >= Mathf.Max(.01f, titleStartHoldSeconds)) ConfirmTitleStart(index);
        }
    }

    private void SetTitlePrompt(string prompt)
    {
        foreach (ScreenTransitionController transition in screenTransitions) transition?.SetTitlePrompt(prompt);
    }

    private void UpdateTitlePedalUI()
    {
        float duration = Mathf.Max(.01f, titleStartHoldSeconds);
        float first = Mathf.Clamp01(players[0].readyHoldTimer / duration);
        float second = Mathf.Clamp01(players[1].readyHoldTimer / duration);
        for (int index = 0; index < PlayerCount; index++)
            if (players[index].preparationState == State.Title)
                screenTransitions[index]?.UpdateTitlePedals(first, second, false, false, players[index].titleInputArmed);
    }

    private void ClearRaceStatus()
    {
        foreach (ScreenTransitionController transition in screenTransitions) transition?.ClearRaceStatus();
    }

    public void RefreshResultHighlights()
    { foreach (ResultUIManager ui in resultUIManagers) ui?.RefreshHighlight(); }

    private static RaceAchievementSnapshot CaptureAchievements(RaceParticipant entry)
    {
        if (entry == null || entry.Car == null) return new RaceAchievementSnapshot();
        return new RaceAchievementSnapshot {
            driftReleases = entry.Car.GetComponent<DriftCoachingUI>()?.SuccessfulReleases ?? 0,
            landings = entry.Car.GetComponent<LandingRewardController>()?.SuccessfulLandings ?? 0,
            tubeLines = entry.Car.GetComponent<TubeChargeLineController>()?.SuccessfulLines ?? 0,
            slipstreamBoosts = entry.Car.GetComponent<SlipstreamController>()?.SuccessfulBoosts ?? 0,
            verifiedProgress = Mathf.Max(0f, entry.Progress),
            usedAutodrive = entry.Car.GetComponent<PlayerAutodrive>()?.WasUsed ?? false
        };
    }

    private RaceResultRecord CreateFallbackResult(int playerIndex, bool didFinish, int finishPosition)
    {
        PlayerRuntime player = players[playerIndex];
        LapManager.CarTimeData data = lapManager?.GetCarData(player?.rigidbody);
        float currentLapTime = data != null ? data.currentLapTime : time;
        float totalRaceTime = data != null ? data.totalRaceTime + data.currentLapTime : time;
        float bestLapTime = data != null && data.bestLapTime < float.MaxValue ? data.bestLapTime : 0f;
        var record = new RaceResultRecord
        {
            playerNumber = playerIndex + 1,
            finishPosition = finishPosition,
            didFinish = didFinish,
            carName = player?.carSelection?.SelectedVehicleName ?? (player?.car != null ? player.car.name : $"Player{playerIndex + 1}_Car"),
            completedLaps = data != null ? data.lapCount : 0,
            goalLap = lapManager != null ? lapManager.GoalLap : 0,
            totalRaceTime = totalRaceTime,
            finalLapTime = currentLapTime,
            bestLapTime = bestLapTime
        };
        record.achievements = CaptureAchievements(GetParticipant(playerIndex));
        return record;
    }

    /// <summary>
    /// アイテム抽選用に、指定車両の現在順位と、妨害対象になる相手車両の効果を返します。
    /// 相手がゴール済み・中断済みの場合、opponentEffects は null です。
    /// </summary>
    /// <summary>
    /// コース中心線の進行距離が増える向きにレースが進むなら 1、逆なら -1 です。
    /// ロケットの自動操縦が、周回判定と同じ向きへ走るために使います。
    /// </summary>
    public float CourseProgressSign => lapManager != null && lapManager.IsProgressReversed ? -1f : 1f;

    public bool TryGetItemContext(Rigidbody target, out int racePosition, out CarItemEffects opponentEffects)
    {
        racePosition = 1;
        opponentEffects = null;
        RaceParticipant participant = FindParticipant(target);
        if (participant == null || !participant.IsRacing) return false;
        racePosition = participant.Position;
        float nearest = float.MaxValue;
        foreach (RaceParticipant candidate in participants)
        {
            if (candidate == participant || !candidate.IsRacing || candidate.Effects == null || !candidate.Effects.isActiveAndEnabled) continue;
            float distance = Mathf.Abs(candidate.Progress - participant.Progress);
            // Prefer the closest car ahead; at the front, target the closest chasing car.
            float score = candidate.Progress >= participant.Progress ? distance : distance + (course != null ? course.TotalLength : 10000f);
            if (score < nearest) { nearest = score; opponentEffects = candidate.Effects; }
        }
        return true;
    }

    private bool HasSpawnedCars()
    {
        foreach (PlayerRuntime player in players) if (player?.car != null) return true;
        return false;
    }

    private void OnDestroy()
    {
        drivingTutorial?.End();
        if (Control == this) VManager?.ResetDriftBoosts();
        if (lapManager != null) lapManager.CarFinished -= HandleCarFinished;
        foreach (PlayerDisplayRig rig in displayRigs) rig?.Dispose();
        if (Control == this) Control = null;
    }
}
