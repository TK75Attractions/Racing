using System;
using System.IO;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Exercises production practice, source handover, independent results and retry in Play mode.</summary>
[InitializeOnLoad]
public static class PlayerOperationFlowValidation
{
    private const string Key = "Racing.PlayerOperationFlowQA";
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static int stage, changedFrame;
    private static double started;
    private static float firstResultElapsed;
    private static bool rankingSeeded;
    private static readonly Input[] inputs = { new Input { PlayerIndex = 0 }, new Input { PlayerIndex = 1 } };
    private sealed class Input : IDriveInputSource
    {
        public int PlayerIndex { get; set; }
        public string DeviceId => "QA ESP32 source " + PlayerIndex;
        public bool IsConnected { get; set; } = true;
        public DriveInputState CurrentState { get; set; }
        public void UpdateInput(float dt) { }
        public void Dispose() { }
    }
    static PlayerOperationFlowValidation() { EditorApplication.update += Tick; }

    [MenuItem("Racing/Validation/Player Operation Flow")]
    public static void Run()
    {
        DailyRankingValidation.Run();
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        var input = UnityEngine.Object.FindFirstObjectByType<InputManager>();
        input.isDebugMode = true; input.isP1SerialP2KeyboardDebugMode = false;
        SessionState.SetBool(Key, true);
        EditorApplication.isPlaying = true;
    }
    private static void Tick()
    {
        if (!SessionState.GetBool(Key, false) || !EditorApplication.isPlaying || Gmanager.Control == null) return;
        try
        {
            if (started == 0) started = EditorApplication.timeSinceStartup;
            if (EditorApplication.timeSinceStartup - started > 150) throw new Exception("Operation flow timed out at stage " + stage);
            var race = Gmanager.Control;
            foreach (var publisher in UnityEngine.Object.FindObjectsByType<RaceRemoteFirebasePublisher>(FindObjectsSortMode.None)) publisher.enabled = false;
            if (race.IsMaintenanceActive) race.SetMaintenance(false);
            var ui = (ResultUIManager[])Get(race, "resultUIManagers");
            if (stage == 0 && Time.frameCount > 12)
            {
                var live = (LiveDriveInputSource[])Get(race.IManager, "liveInputs");
                for (int i = 0; i < 2; i++)
                {
                    inputs[i].CurrentState = new DriveInputState { pedal = .15f };
                    live[i].SwitchSource(inputs[i], false);
                    race.ConfirmTitleStart(i);
                }
                Next();
            }
            else if (stage == 1 && race.GetComponent<DrivingTutorialController>()?.HasStarted(0) == true &&
                race.GetComponent<DrivingTutorialController>()?.HasStarted(1) == true && !Transitioning(race, 0) && !Transitioning(race, 1))
            {
                var tutorial = race.GetComponent<DrivingTutorialController>();
                for (int i = 0; i < 2; i++)
                {
                    Require(tutorial.GetPlayerProgress(i).PedalReleased, "Resting analog offsets must release practice.");
                    var gate = tutorial.GetPlayerCar(i).GetComponent<DebugMover>().InputSource as PlayerDriveInputGate;
                    Require(!gate.IsBlocked, "The practice car and lesson must agree about release.");
                }
                Call(race, "ResetGameWhenScreenCovered");
                race.Features.SetValidationFeature(RaceFeature.CourseIntro, false);
                race.Features.SetValidationFeature(RaceFeature.FinishRun, false);
                race.StartAutodriveTestRace(true);
                Next();
            }
            else if (stage == 2 && race.state == Gmanager.State.Game)
            {
                Require(race.BothPlayersAutodrive, "Both-auto start uses both independent switches.");
                for (int i = 0; i < 2; i++)
                {
                    var entry = race.GetParticipant(i);
                    var auto = entry.Car.GetComponent<PlayerAutodrive>();
                    var gate = entry.Car.GetComponent<DebugMover>().InputSource as PlayerDriveInputGate;
                    Require(auto.IsActive, "Autodrive must be active before switching off.");
                    inputs[i].CurrentState = new DriveInputState { pedal = .7f, steering = 9f };
                    race.Features.SetFeature(i == 0 ? RaceFeature.PlayerOneAutodrive : RaceFeature.PlayerTwoAutodrive, false);
                    Require(!auto.IsActive && !race.BothPlayersAutodrive && gate.CurrentState.pedal == 0 && gate.NeedsPedalRelease,
                        "OFF must restore manual source while latching a held pedal.");
                    inputs[i].CurrentState = new DriveInputState { pedal = .15f };
                    _ = gate.CurrentState;
                    inputs[i].CurrentState = new DriveInputState { pedal = .7f, steering = 9f };
                    Require(gate.CurrentState.pedal == .7f && gate.CurrentState.steering == 9f,
                        "The original ESP32 source must drive again after release.");
                    inputs[i].IsConnected = false;
                    Require(gate.CurrentState.pedal == 0, "Disconnected manual source cannot drive.");
                    inputs[i].IsConnected = true;
                    Require(gate.CurrentState.pedal == 0, "Reconnect with held pedal remains latched.");
                    inputs[i].CurrentState = DriveInputState.Neutral; _ = gate.CurrentState;
                }
                FinishPlayer(race, 0);
                Next();
            }
            else if (stage == 3 && ui[0].IsVisible && !Transitioning(race, 0))
            {
                Require(race.state == Gmanager.State.Game && race.IsPlayerStillRacing(1) && !ui[1].IsVisible,
                    "P1 results must appear while P2 is still playing.");
                if (!rankingSeeded)
                {
                    SeedRanking(race, 0); rankingSeeded = true; changedFrame = Time.frameCount; return;
                }
                if (Time.frameCount - changedFrame < 3) return;
                Capture(race, 0, "p1-first-result.png");
                Call(race, "UpdateResultDisplay", Gmanager.ResultDisplaySeconds);
                Require(!ui[0].IsVisible && Root(0).Find("SpectatorOverlay").gameObject.activeSelf,
                    "Only an unfinished opponent permits spectator view after results.");
                Require(Root(0).Find("SpectatorOverlay/WaitingForOpponent/Message").GetComponent<TMP_Text>().text == "相手のゴールを待っています",
                    "Spectator view must include the central waiting message.");
                Next();
            }
            else if (stage == 4 && Time.frameCount - changedFrame > 2)
            {
                Capture(race, 0, "waiting-for-p2.png");
                FinishPlayer(race, 1);
                Require(!Root(0).Find("SpectatorOverlay").gameObject.activeSelf,
                    "The waiting message must disappear as soon as the opponent finishes.");
                Next();
            }
            else if (stage == 5 && race.state == Gmanager.State.Result && ui[1].IsVisible && !Transitioning(race, 1) && !Transitioning(race, 0))
            {
                Require(!ui[0].IsVisible && (Root(0).GetComponent<ArcadeEndingUI>().IsVisible || (bool)Get(Player(race, 0), "returnedToTitle")),
                    "P1 may finish its thank-you screen during P2's celebration, but must not replay its result.");
                Require(!Root(0).Find("SpectatorOverlay").gameObject.activeSelf, "Waiting ends when P2 finishes.");
                Require((float)Get(Player(race, 1), "resultDisplayElapsed") < 2f, "P2 gets its own fresh result timer.");
                race.SelectResultOption(1, 0);
                Require(Root(1).GetComponent<ArcadeEndingUI>().IsVisible, "Retry regression starts with an actual visible thank-you screen.");
                race.RetryGame(); Next();
            }
            else if (stage == 6 && race.state == Gmanager.State.Game)
            {
                for (int i = 0; i < 2; i++)
                {
                    var ending = Root(i).GetComponent<ArcadeEndingUI>();
                    Require(ending == null || !ending.IsVisible, "Retry must dismiss every thank-you overlay.");
                    ending?.TickTime(100f); ending?.TickInput(1f, 100f);
                    Require(race.state == Gmanager.State.Game, "A stale ending callback cannot reset the new race.");
                }
                FinishPlayer(race, 0); Next();
            }
            else if (stage == 7 && ui[0].IsVisible && !Transitioning(race, 0))
            {
                Call(race, "UpdateResultDisplay", 3f);
                firstResultElapsed = (float)Get(Player(race, 0), "resultDisplayElapsed");
                FinishPlayer(race, 1); Next();
            }
            else if (stage == 8 && race.state == Gmanager.State.Result && !Transitioning(race, 1))
            {
                Require(ui[0].IsVisible && ui[1].IsVisible, "Close finishes retain both independent result screens.");
                Require((float)Get(Player(race, 0), "resultDisplayElapsed") >= firstResultElapsed,
                    "The later finish must never restart the first result timer.");
                Require(!Root(0).Find("SpectatorOverlay").gameObject.activeSelf, "A completed opponent needs no spectator view.");
                race.RetryGame(); Next();
            }
            else if (stage == 9 && race.state == Gmanager.State.Game) { FinishPlayer(race, 1); Next(); }
            else if (stage == 10 && ui[1].IsVisible && !Transitioning(race, 1))
            {
                Require(race.IsPlayerStillRacing(0) && !ui[0].IsVisible, "P2-first results are symmetric.");
                race.SelectResultOption(1, 0);
                Require(Root(1).Find("SpectatorOverlay").gameObject.activeSelf, "P2 Next also waits for the unfinished opponent.");
                Call(race, "CompleteRaceAfterTimeout"); Next();
            }
            else if (stage == 11 && race.state == Gmanager.State.Result && !Transitioning(race, 0))
            {
                Require(ui[0].IsVisible && Root(1).GetComponent<ArcadeEndingUI>().IsVisible,
                    "Timeout gives the unfinished player a DNF result without replaying the other's result.");
                Debug.Log("PLAYER_OPERATION_FLOW_PASS: practice offsets; independent auto OFF, original source handover and reconnect; P1/P2 first finish; timed and Next-driven waiting; close finish clocks; DNF; retry overlay cancellation; rendered result and spectator views.");
                Finish(0);
            }
        }
        catch (Exception error) { Debug.LogException(error); Finish(1); }
    }
    private static void SeedRanking(Gmanager race, int player)
    {
        var book = new DailyRankingBook { day = "2026-10-31" };
        for (int i = 0; i < 10; i++) book.entries.Add(new DailyRankingEntry {
            raceId = i == 0 ? "current" : "previous" + i, boardKey = "QA", playNumber = i + 1,
            playerNumber = player + 1, milliseconds = 82000 + i * 1000 });
        race.LatestSessionResult.dailyRanking = DailyRankingView.Build(book, "QA", "current");
        var local = race.LatestSessionResult.GetPlayerResult(player);
        local.dailyRankingExcluded = false; local.achievements.usedAutodrive = false;
        ((ResultUIManager[])Get(race, "resultUIManagers"))[player].RefreshDailyRanking();
        var panel = Root(player).Find("Result/ResultPresentation/ResultCard/DailyRanking");
        Require(panel.Find("BestTime").GetComponent<TMP_Text>().text.Contains("1:22.000"), "Show today's matching-board best time.");
        Require(panel.Find("Celebration").GetComponent<DailyRankCelebration>().IsCelebrating, "Top fifth must have the enhanced animation.");
    }
    private static void FinishPlayer(Gmanager race, int index) => Call(race, "HandleCarFinished", race.GetParticipant(index).Body,
        new RaceResultRecord { playerNumber = index + 1, didFinish = true, goalLap = 2, completedLaps = 2, totalRaceTime = 82f + index });
    private static object Player(Gmanager race, int index) => ((Array)Get(race, "players")).GetValue(index);
    private static bool Transitioning(Gmanager race, int index) => ((ScreenTransitionController[])Get(race, "screenTransitions"))[index].IsTransitioning;
    private static Transform Root(int player) => ((PlayerDisplayRig[])Get(Gmanager.Control, "displayRigs"))[player].CanvasRoot.transform;
    private static object Get(object owner, string field) => owner.GetType().GetField(field, Private | BindingFlags.Public).GetValue(owner);
    private static void Call(object owner, string method, params object[] args) => owner.GetType().GetMethod(method, Private).Invoke(owner, args);
    private static void Next() { stage++; changedFrame = Time.frameCount; }
    private static void Capture(Gmanager race, int player, string name)
    {
        string folder = Path.GetFullPath("Artifacts/PlayerOperationFlow"); Directory.CreateDirectory(folder);
        typeof(DrivingTutorialValidation).GetMethod("Capture", BindingFlags.NonPublic | BindingFlags.Static)
            .Invoke(null, new object[] { race, player, Path.Combine(folder, name) });
    }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void Finish(int code) { SessionState.SetBool(Key, false); EditorApplication.isPlaying = false; if (Application.isBatchMode) EditorApplication.Exit(code); }
}
