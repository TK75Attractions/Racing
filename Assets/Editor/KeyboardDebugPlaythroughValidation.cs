#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

/// <summary>
/// Input Mode: Debug（キーボード P1/P2）で、実際のキー入力だけを使って一連のプレイを通します。
/// タイトル → 車選択 → チュートリアル → カウントダウン → レース → リザルト → リトライ →
/// リザルト → タイトル → 2 周目の車選択まで進め、途中のエラーも失敗として扱います。
/// </summary>
[InitializeOnLoad]
public static class KeyboardDebugPlaythroughValidation
{
    private const string Prefix = "DEBUG_PLAYTHROUGH";
    private const string Active = "Racing.KeyboardDebugPlaythrough.Active";
    private const string Began = Active + ".began";
    private const double TimeoutSeconds = 600;
    private static readonly string[] PrefKeys = { "Racing.Car.P0", "Racing.Paint.P0", "Racing.Car.P1", "Racing.Paint.P1" };

    // 既知の環境由来メッセージ（実機の音声プラグイン未ビルドなど）は、進行の失敗として扱いません。
    private static readonly string[] IgnoredErrors = { };

    private enum Step
    {
        Boot, Title, CarSelection, Tutorial, Countdown, Race, RaceEscape, RaceGoal,
        ResultRetry, RetryRace, ResultTitle, ReturnedTitle, SoloTitle, SoloSelection, SoloTutorial, SoloResult, Done
    }

    private static Step step;
    private static double stepBegan;
    private static Keyboard keyboard;
    private static readonly HashSet<Key> held = new HashSet<Key>();
    private static readonly List<string> errors = new List<string>();
    private static readonly PlayerDriver[] drivers = { new PlayerDriver(0), new PlayerDriver(1) };
    private static int round;

    static KeyboardDebugPlaythroughValidation()
    {
        EditorApplication.update += Tick;
        if (SessionState.GetBool(Active, false)) Application.logMessageReceived += CaptureLog;
    }

    [MenuItem("Racing/Debug/Validate Keyboard Debug Playthrough")]
    public static void Run()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play Mode before starting this validation.");
        for (int i = 0; i < PrefKeys.Length; i++)
        {
            SessionState.SetBool(Active + i + ".exists", PlayerPrefs.HasKey(PrefKeys[i]));
            SessionState.SetString(Active + i + ".value", i % 2 == 0 ? PlayerPrefs.GetString(PrefKeys[i]) : PlayerPrefs.GetInt(PrefKeys[i]).ToString());
        }
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        InputManager input = UnityEngine.Object.FindFirstObjectByType<InputManager>();
        if (input == null) throw new InvalidOperationException("SampleScene has no InputManager.");
        input.isDebugMode = true;
        input.isP1SerialP2KeyboardDebugMode = false;
        SessionState.SetBool(Active, true);
        SessionState.SetFloat(Began, (float)EditorApplication.timeSinceStartup);
        Application.logMessageReceived -= CaptureLog;
        Application.logMessageReceived += CaptureLog;
        EditorApplication.isPlaying = true;
    }

    private static void CaptureLog(string message, string stackTrace, LogType type)
    {
        if (!SessionState.GetBool(Active, false) || !EditorApplication.isPlaying) return;
        if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
        if (message.StartsWith(Prefix, StringComparison.Ordinal)) return;
        foreach (string ignored in IgnoredErrors) if (message.Contains(ignored)) return;
        string firstFrame = stackTrace?.Split('\n')[0] ?? "";
        errors.Add($"[{step}] {type}: {message} @ {firstFrame}");
    }

    private static void Tick()
    {
        if (!SessionState.GetBool(Active, false)) return;
        try
        {
            if (EditorApplication.timeSinceStartup - SessionState.GetFloat(Began, 0f) > TimeoutSeconds)
                throw new InvalidOperationException($"Timed out at step {step}.");
            if (!EditorApplication.isPlaying || Gmanager.Control == null) return;
            if (keyboard == null) Boot();
            Advance(Gmanager.Control);
            Apply();
        }
        catch (Exception exception)
        {
            Debug.LogError($"{Prefix}_FAIL: {exception.Message} (step {step}, state {Gmanager.Control?.state})");
            Finish(1);
        }
    }

    private static void Boot()
    {
        // バッチ実行には Game ビューのフォーカスが無いため、キーボード入力を常にゲームへ渡します。
        InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        keyboard = InputSystem.AddDevice<Keyboard>("DebugPlaythroughKeyboard");
        keyboard.MakeCurrent();
        errors.Clear();
        round = 0;
        To(Step.Boot);
        Log("keyboard ready; Input Mode = " + Gmanager.Control.IManager.InputModeLabel);
    }

    private static void Advance(Gmanager manager)
    {
        double age = Age;
        foreach (PlayerDriver driver in drivers) driver.Bind(manager);
        switch (step)
        {
            case Step.Boot:
                Require(manager.IManager.isDebugMode && manager.IManager.IsAnyDebugMode, "InputManager is in keyboard debug mode");
                Require(Keyboard.current == keyboard, "Keyboard.current is the injected keyboard");
                if (manager.state == Gmanager.State.Title && !manager.IsTransitioning()) To(Step.Title);
                break;

            case Step.Title:
                // P1 は Enter、P2 はペダル（↑）の長押しでタイトルを抜けます。
                drivers[0].TitleWithReadyKey(age);
                drivers[1].TitleWithPedalHold(age);
                if (drivers[0].PreparationIs(Gmanager.State.CarSelection) && drivers[1].PreparationIs(Gmanager.State.CarSelection))
                { Log("both players reached car selection"); To(Step.CarSelection); }
                else if (age > 15) throw new InvalidOperationException($"Title start failed: P1 {manager.GetPlayerPreparationState(0)}, P2 {manager.GetPlayerPreparationState(1)}");
                break;

            case Step.CarSelection:
                drivers[0].CarSelection(age);
                drivers[1].CarSelection(age);
                if (drivers[0].PreparationIs(Gmanager.State.Tutorial) && drivers[1].PreparationIs(Gmanager.State.Tutorial))
                {
                    Require(drivers[0].ChangedVehicle && drivers[1].ChangedVehicle, "Steering keys change the selected vehicle");
                    Log("both players confirmed their cars by keyboard");
                    To(Step.Tutorial);
                }
                else if (age > 25) throw new InvalidOperationException($"Car selection failed: P1 {drivers[0].SelectionStatus()}, P2 {drivers[1].SelectionStatus()}");
                break;

            case Step.Tutorial:
                drivers[0].Tutorial();
                drivers[1].Tutorial();
                if (manager.state == Gmanager.State.Countdown || manager.state == Gmanager.State.Game)
                {
                    Require(drivers[0].LessonCompleted && drivers[1].LessonCompleted, "Both tutorials were completed by keyboard, not by timeout");
                    Log("both tutorials completed by keyboard");
                    To(Step.Countdown);
                }
                else if (age > 75) throw new InvalidOperationException($"Tutorial failed: P1 {drivers[0].LessonStatus()}, P2 {drivers[1].LessonStatus()}");
                break;

            case Step.Countdown:
                if (manager.state == Gmanager.State.Game && !manager.IsTransitioning())
                {
                    foreach (PlayerDriver driver in drivers) driver.MarkRaceStart();
                    To(Step.Race);
                }
                else if (age > 15) throw new InvalidOperationException("Countdown did not reach the race.");
                break;

            case Step.Race:
                // 2 秒アクセル、1 秒アクセル＋左、と走らせてから、Space（P1）/ Right Ctrl（P2）でリセットします。
                foreach (PlayerDriver driver in drivers) driver.Race(age);
                if (age > 4.6)
                {
                    foreach (PlayerDriver driver in drivers) driver.VerifyRaceDriving();
                    Log($"race driving ok: P1 moved {drivers[0].RaceDistance:F1} m / turned {drivers[0].RaceTurn:F0} deg, P2 moved {drivers[1].RaceDistance:F1} m / turned {drivers[1].RaceTurn:F0} deg");
                    To(Step.RaceEscape);
                }
                break;

            case Step.RaceEscape:
                // Esc で中断メニューを開き、もう一度 Esc で閉じます。
                if (age < .15) Hold(Key.Escape);
                else if (age > .5 && age < .65)
                {
                    Require(drivers[0].MenuOpen && drivers[1].MenuOpen, "Escape opens the interruption menu");
                    Hold(Key.Escape);
                }
                else if (age > 1.0)
                {
                    Require(!drivers[0].MenuOpen && !drivers[1].MenuOpen, "Escape closes the interruption menu");
                    To(Step.RaceGoal);
                }
                break;

            case Step.RaceGoal:
                // デバッグ用の GOAL ボタンと同じ処理で、両プレイヤーをゴールさせます。
                if (age < .1) { manager.DebugAdvanceLapForValidation(0); manager.DebugAdvanceLapForValidation(1); }
                if (manager.state == Gmanager.State.Result && !manager.IsTransitioning())
                { Log("race finished via debug GOAL"); To(round == 0 ? Step.ResultRetry : round == 1 ? Step.ResultTitle : Step.SoloResult); }
                else if (age > 30) throw new InvalidOperationException($"Goal did not reach the result (state {manager.state}).");
                break;

            case Step.ResultRetry:
                // P1 がペダル長押しで「もう一度」を選びます（両者リトライ）。
                drivers[0].ResultConfirm(age, toggle: false);
                if (manager.state == Gmanager.State.Countdown || manager.state == Gmanager.State.Game)
                { Log("retry from result by keyboard"); round = 1; To(Step.RetryRace); }
                else if (age > 15) throw new InvalidOperationException($"Result retry failed (state {manager.state}).");
                break;

            case Step.RetryRace:
                if (manager.state == Gmanager.State.Game && !manager.IsTransitioning() && age > 1)
                {
                    foreach (PlayerDriver driver in drivers) driver.MarkRaceStart();
                    To(Step.RaceGoal);
                }
                else if (age > 15) throw new InvalidOperationException("Retry did not start the race.");
                break;

            case Step.ResultTitle:
                // 両プレイヤーがハンドルで「タイトルへ」を選び、ペダル長押しで戻ります。
                drivers[0].ResultConfirm(age, toggle: true);
                drivers[1].ResultConfirm(age, toggle: true);
                if (manager.state == Gmanager.State.Title && !manager.IsTransitioning())
                { Log("both players returned to the title"); To(Step.ReturnedTitle); }
                else if (age > 20) throw new InvalidOperationException($"Return to title failed (state {manager.state}).");
                break;

            case Step.ReturnedTitle:
                foreach (PlayerDriver driver in drivers) driver.ResetForNextRound();
                round = 2;
                Log("solo round: only P1 keys, P2 is advanced with the debug panel");
                To(Step.SoloTitle);
                break;

            // 1 人デバッグ: P1 だけをキーボードで操作し、P2 はデバッグパネルの ALL PLAYERS ボタンで進めます。
            case Step.SoloTitle:
                drivers[0].TitleWithReadyKey(age);
                if (drivers[0].PreparationIs(Gmanager.State.CarSelection) && manager.GetPlayerPreparationState(1) == Gmanager.State.Title)
                    manager.PressDebugAllPlayers("START");
                if (drivers[0].PreparationIs(Gmanager.State.CarSelection) && drivers[1].PreparationIs(Gmanager.State.CarSelection))
                { Log("solo: P2 started from the debug panel"); To(Step.SoloSelection); }
                else if (age > 15) throw new InvalidOperationException($"Solo title start failed: P1 {manager.GetPlayerPreparationState(0)}, P2 {manager.GetPlayerPreparationState(1)}");
                break;

            case Step.SoloSelection:
                drivers[0].CarSelection(age);
                if (drivers[0].PreparationIs(Gmanager.State.Tutorial) && drivers[1].PreparationIs(Gmanager.State.CarSelection))
                    manager.PressDebugAllPlayers("CONFIRM CAR");
                if (drivers[0].PreparationIs(Gmanager.State.Tutorial) && drivers[1].PreparationIs(Gmanager.State.Tutorial))
                { Log("solo: P2 car confirmed from the debug panel"); To(Step.SoloTutorial); }
                else if (age > 25) throw new InvalidOperationException($"Solo car selection failed: P1 {drivers[0].SelectionStatus()}, P2 {drivers[1].SelectionStatus()}");
                break;

            case Step.SoloTutorial:
                drivers[0].Tutorial();
                if (drivers[0].LessonCompleted && manager.state == Gmanager.State.Tutorial) manager.PressDebugAllPlayers("SKIP TUTORIAL");
                if (manager.state == Gmanager.State.Countdown || manager.state == Gmanager.State.Game)
                {
                    Require(drivers[0].LessonCompleted, "P1 completed the tutorial by keyboard");
                    Log("solo: tutorial skipped for the idle P2 from the debug panel");
                    To(Step.Countdown);
                }
                else if (age > 75) throw new InvalidOperationException($"Solo tutorial failed: P1 {drivers[0].LessonStatus()}, P2 {drivers[1].LessonStatus()}");
                break;

            case Step.SoloResult:
                drivers[0].ResultConfirm(age, toggle: true);
                if (manager.IsPlayerReturnedToTitle(0) && !manager.IsPlayerReturnedToTitle(1) && !manager.IsTransitioning())
                    manager.PressDebugAllPlayers("RETURN TO TITLE");
                if (manager.state == Gmanager.State.Title && !manager.IsTransitioning())
                { Log("solo: returned to the title without P2 input"); To(Step.Done); }
                else if (age > 20) throw new InvalidOperationException($"Solo return to title failed (state {manager.state}).");
                break;

            case Step.Done:
                if (age < 1) break;
                if (errors.Count > 0)
                    throw new InvalidOperationException($"{errors.Count} error(s) were logged during play:\n  " + string.Join("\n  ", errors));
                Debug.Log($"{Prefix}_PASS: title, car selection, tutorial, race driving, reset, menu, goal, retry, return to title and the solo debug-panel round all work with keyboard debug input");
                Finish(0);
                break;
        }
    }

    private static double Age => Time.realtimeSinceStartupAsDouble - stepBegan;

    private static void To(Step next)
    {
        step = next;
        stepBegan = Time.realtimeSinceStartupAsDouble;
        foreach (PlayerDriver driver in drivers) driver.BeginStep();
    }

    private static void Hold(Key key) => held.Add(key);

    private static void Apply()
    {
        InputSystem.QueueStateEvent(keyboard, new KeyboardState(new List<Key>(held).ToArray()));
        held.Clear();
    }

    private static void Log(string message) => Debug.Log($"{Prefix}: {message}");

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Finish(int code)
    {
        SessionState.SetBool(Active, false);
        Application.logMessageReceived -= CaptureLog;
        if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
        keyboard = null;
        for (int i = 0; i < PrefKeys.Length; i++)
        {
            if (!SessionState.GetBool(Active + i + ".exists", false)) PlayerPrefs.DeleteKey(PrefKeys[i]);
            else if (i % 2 == 0) PlayerPrefs.SetString(PrefKeys[i], SessionState.GetString(Active + i + ".value", ""));
            else PlayerPrefs.SetInt(PrefKeys[i], int.Parse(SessionState.GetString(Active + i + ".value", "0")));
        }
        PlayerPrefs.Save();
        EditorApplication.isPlaying = false;
        if (Application.isBatchMode) EditorApplication.Exit(code);
    }

    /// <summary>1 人分のキー配置で、画面ごとの操作を行います。</summary>
    private sealed class PlayerDriver
    {
        private readonly int index;
        private readonly Key pedal, brake, left, right, ready, reset;
        private Gmanager manager;
        private double phaseBegan;
        private int phase;
        private int startVehicle = -1;
        private Vector3 raceStart;
        private float raceStartYaw;
        private float maxSpeed;

        public bool ChangedVehicle { get; private set; }
        public bool LessonCompleted { get; private set; }
        public float RaceDistance { get; private set; }
        public float RaceTurn { get; private set; }

        public PlayerDriver(int playerIndex)
        {
            index = playerIndex;
            bool first = playerIndex == 0;
            pedal = first ? Key.W : Key.UpArrow;
            brake = first ? Key.S : Key.DownArrow;
            left = first ? Key.A : Key.LeftArrow;
            right = first ? Key.D : Key.RightArrow;
            ready = first ? Key.Enter : Key.RightShift;
            reset = first ? Key.Space : Key.RightCtrl;
        }

        public void Bind(Gmanager race) => manager = race;
        public void BeginStep() { phase = 0; phaseBegan = Time.realtimeSinceStartupAsDouble; }
        private double PhaseAge => Time.realtimeSinceStartupAsDouble - phaseBegan;
        private void Next() { phase++; phaseBegan = Time.realtimeSinceStartupAsDouble; }
        public bool PreparationIs(Gmanager.State expected) =>
            manager.GetPlayerPreparationState(index) == expected && !manager.IsPlayerTransitioning(index);

        private CarSelectionUI Selection => manager.GetPlayerCarSelection(index);
        private DrivingTutorialController TutorialController => manager.GetComponent<DrivingTutorialController>();

        public void TitleWithReadyKey(double age)
        {
            if (!PreparationIs(Gmanager.State.Title)) return;
            // 離した状態を見せてから、Enter / Right Shift を数フレーム押します。
            if (age > .6 && age < .8) Hold(ready);
        }

        public void TitleWithPedalHold(double age)
        {
            if (!PreparationIs(Gmanager.State.Title)) return;
            if (age > .6) Hold(pedal);
        }

        public void CarSelection(double age)
        {
            if (manager.GetPlayerPreparationState(index) != Gmanager.State.CarSelection) return;
            CarSelectionUI selection = Selection;
            if (selection == null || !selection.IsOpen || manager.IsPlayerTransitioning(index)) { phaseBegan = Time.realtimeSinceStartupAsDouble; return; }
            if (startVehicle < 0) startVehicle = selection.DraftVehicle;
            switch (phase)
            {
                case 0: if (PhaseAge > .5) Next(); break;                       // ペダルを離す
                case 1: Hold(right); if (PhaseAge > .2) Next(); break;          // 車種を切り替える
                case 2:
                    if (PhaseAge > .3)
                    {
                        ChangedVehicle = selection.DraftVehicle != startVehicle;
                        Next();
                    }
                    break;
                default: Hold(pedal); break;                                    // 長押しで決定
            }
        }

        public string SelectionStatus()
        {
            CarSelectionUI selection = Selection;
            return selection == null ? $"{manager.GetPlayerPreparationState(index)} (no screen)" :
                $"{manager.GetPlayerPreparationState(index)} open={selection.IsOpen} step={selection.DashboardSelectionStep} progress={selection.ConfirmationProgress:F2} locked={selection.IsSelectionLocked}";
        }

        public void Tutorial()
        {
            if (!PreparationIs(Gmanager.State.Tutorial)) return;
            DrivingTutorialController tutorial = TutorialController;
            DrivingTutorialProgress lesson = tutorial != null ? tutorial.GetPlayerProgress(index) : null;
            if (lesson == null) return;
            if (lesson.IsComplete) { LessonCompleted = true; return; }
            if (lesson.HasTimedOut) return;
            Rigidbody body = tutorial.GetPlayerCar(index)?.GetComponent<Rigidbody>();
            float speed = body != null ? Vector3.ProjectOnPlane(body.linearVelocity, Vector3.up).magnitude : 0f;
            switch (lesson.CurrentStage)
            {
                case DrivingTutorialProgress.Stage.Accelerate:
                    // 決定に使ったペダルを一度離してから踏みます。
                    if (phase == 0) { if (PhaseAge > .4) Next(); }
                    else Hold(pedal);
                    break;
                case DrivingTutorialProgress.Stage.Steer:
                    Hold(right);
                    if (speed < 7f) Hold(pedal);
                    break;
                case DrivingTutorialProgress.Stage.Stop:
                    if (speed > 2.5f) Hold(brake);
                    break;
            }
        }

        public string LessonStatus()
        {
            DrivingTutorialProgress lesson = TutorialController?.GetPlayerProgress(index);
            Rigidbody body = TutorialController?.GetPlayerCar(index)?.GetComponent<Rigidbody>();
            return lesson == null ? $"{manager.GetPlayerPreparationState(index)} (no lesson)" :
                $"stage={lesson.CurrentStage} timedOut={lesson.HasTimedOut} remaining={lesson.TimeRemaining:F1} speed={(body != null ? body.linearVelocity.magnitude : 0f):F1}";
        }

        public void MarkRaceStart()
        {
            Transform car = manager.GetPlayerCarTransform(index);
            Require(car != null, $"P{index + 1} race car exists");
            raceStart = car.position;
            raceStartYaw = car.eulerAngles.y;
            maxSpeed = 0f;
            RaceDistance = RaceTurn = 0f;
        }

        public void Race(double age)
        {
            Transform car = manager.GetPlayerCarTransform(index);
            if (car == null) return;
            if (age < 3.0) Hold(pedal);
            if (age > 2.0 && age < 3.0) Hold(left);
            maxSpeed = Mathf.Max(maxSpeed, manager.GetPlayerSpeed(index));
            if (age < 3.05)
            {
                RaceDistance = Vector3.Distance(raceStart, car.position);
                RaceTurn = Mathf.Abs(Mathf.DeltaAngle(raceStartYaw, car.eulerAngles.y));
            }
            if (age > 3.6 && age < 3.75) Hold(reset);
        }

        public void VerifyRaceDriving()
        {
            Require(RaceDistance > 8f, $"P{index + 1} accelerates with the keyboard (moved {RaceDistance:F1} m, max {maxSpeed:F0} km/h)");
            Require(RaceTurn > 3f, $"P{index + 1} steers with the keyboard (turned {RaceTurn:F1} deg)");
        }

        public bool MenuOpen => manager.IsInterruptionMenuOpen(index);

        public void ResultConfirm(double age, bool toggle)
        {
            if (manager.state != Gmanager.State.Result || manager.IsPlayerReturnedToTitle(index)) return;
            // 入力受付の待ち時間の後、離して確定を有効にし、必要ならハンドルで項目を切り替えてから長押しします。
            if (age < 3.6) return;
            if (toggle && age > 3.8 && age < 4.0) Hold(right);
            if (age > (toggle ? 4.4 : 3.9)) Hold(pedal);
        }

        public void ResetForNextRound()
        {
            startVehicle = -1;
            ChangedVehicle = LessonCompleted = false;
        }
    }
}

/// <summary>検証から Gmanager の内部状態を読むための補助です（本番コードには手を入れません）。</summary>
internal static class GmanagerPlaythroughProbe
{
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private static readonly FieldInfo PlayersField = typeof(Gmanager).GetField("players", Instance);
    private static readonly FieldInfo TransitionsField = typeof(Gmanager).GetField("screenTransitions", Instance);
    private static readonly MethodInfo IsScreenTransitioningMethod = typeof(Gmanager).GetMethod("IsScreenTransitioning", Instance);
    private static readonly MethodInfo AdvanceDebugLapMethod = typeof(Gmanager).GetMethod("AdvanceDebugLap", Instance);
    private static readonly MethodInfo AllPlayersLabelMethod = typeof(Gmanager).GetMethod("GetDebugAllPlayersStepLabel", Instance);
    private static readonly MethodInfo AllPlayersMethod = typeof(Gmanager).GetMethod("AdvanceDebugAllPlayers", Instance);

    private static object Player(Gmanager manager, int index) => ((Array)PlayersField.GetValue(manager)).GetValue(index);
    private static T PlayerField<T>(Gmanager manager, int index, string name)
    {
        object player = Player(manager, index);
        return player == null ? default : (T)player.GetType().GetField(name, Instance).GetValue(player);
    }

    public static bool IsTransitioning(this Gmanager manager) => (bool)IsScreenTransitioningMethod.Invoke(manager, null);
    public static bool IsPlayerTransitioning(this Gmanager manager, int index) =>
        ((ScreenTransitionController[])TransitionsField.GetValue(manager))[index]?.IsTransitioning == true;
    public static CarSelectionUI GetPlayerCarSelection(this Gmanager manager, int index) => PlayerField<CarSelectionUI>(manager, index, "carSelection");
    public static bool IsInterruptionMenuOpen(this Gmanager manager, int index) => PlayerField<InterruptionMenuUI>(manager, index, "interruptionMenu")?.IsOpen == true;
    public static bool IsPlayerReturnedToTitle(this Gmanager manager, int index) => PlayerField<bool>(manager, index, "returnedToTitle");
    // Gmanager.OnGUI の GOAL ボタンと同じ処理です。
    public static void DebugAdvanceLapForValidation(this Gmanager manager, int index) => AdvanceDebugLapMethod.Invoke(manager, new object[] { index, true });
    // デバッグパネルの ALL PLAYERS ボタンを、表示中のラベルを確かめてから押します。
    public static void PressDebugAllPlayers(this Gmanager manager, string expected)
    {
        string label = (string)AllPlayersLabelMethod.Invoke(manager, null);
        if (label == null) return;
        if (!label.EndsWith(expected, StringComparison.Ordinal))
            throw new InvalidOperationException($"Debug panel shows '{label}', expected '{expected}'.");
        AllPlayersMethod.Invoke(manager, null);
    }
}
#endif
