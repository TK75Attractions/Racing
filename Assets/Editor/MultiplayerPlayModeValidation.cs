using System;
using System.Reflection;
using TMPro;
using Unity.Cinemachine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// CLIから実行する、SampleSceneの2人対戦スモークテストです。
/// 実機シリアルの代わりにキーボード入力源を使用します。
/// </summary>
public static class MultiplayerPlayModeValidation
{
    private const string SessionKey = "Racing.MultiplayerValidation.Active";
    private const double TimeoutSeconds = 30d;

    private static int stage;
    private static int frameCount;
    private static double testStartTime;
    private static double stageStartTime;
    private static Gmanager manager;
    private static MethodInfo finishMethod;

    public static void RunBatch()
    {
        if (!Application.isBatchMode)
        {
            throw new InvalidOperationException("RunBatch must be started with Unity -batchmode.");
        }

        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        InputManager input = UnityEngine.Object.FindFirstObjectByType<InputManager>();
        if (input == null)
        {
            Fail("InputManager was not found before entering Play mode.");
            return;
        }

        input.isDebugMode = true;
        SessionState.SetBool(SessionKey, true);
        EditorApplication.isPlaying = true;
    }

    [InitializeOnLoadMethod]
    private static void ResumeAfterDomainReload()
    {
        if (!SessionState.GetBool(SessionKey, false))
        {
            return;
        }

        if (!EditorApplication.isPlayingOrWillChangePlaymode)
        {
            SessionState.SetBool(SessionKey, false);
            return;
        }

        EditorApplication.delayCall += Attach;
    }

    private static void Attach()
    {
        if (!SessionState.GetBool(SessionKey, false))
        {
            return;
        }

        stage = 0;
        frameCount = 0;
        testStartTime = EditorApplication.timeSinceStartup;
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
    }

    private static void Tick()
    {
        try
        {
            frameCount++;
            if (EditorApplication.timeSinceStartup - testStartTime > TimeoutSeconds)
            {
                Fail("Validation timed out.");
                return;
            }

            switch (stage)
            {
                case 0 when frameCount > 5:
                    ValidateDisplayAndTitle();
                    manager.StartGame();
                    stage = 1;
                    break;

                case 1 when manager.state == Gmanager.State.Countdown:
                    ValidateSpawnAndCountdown();
                    stage = 2;
                    break;

                case 2 when manager.state == Gmanager.State.Game:
                    ValidateFirstFinish();
                    stage = 3;
                    break;

                case 3 when GameObject.Find("GameManagers/MainCanvas/SpectatorOverlay") != null:
                    ValidateSpectatorAndFinishSecondPlayer();
                    stage = 4;
                    stageStartTime = EditorApplication.timeSinceStartup;
                    break;

                case 4 when EditorApplication.timeSinceStartup - stageStartTime > 0.25d:
                    ValidateSecondPlayerGoal();
                    stage = 5;
                    break;

                case 5 when manager.state == Gmanager.State.Result:
                    ValidateSharedResult();
                    Debug.Log("MULTIPLAYER_PLAYMODE_VALIDATION_PASS");
                    Finish(0);
                    break;
            }
        }
        catch (Exception exception)
        {
            Fail(exception.ToString());
        }
    }

    private static void ValidateDisplayAndTitle()
    {
        manager = UnityEngine.Object.FindFirstObjectByType<Gmanager>();
        Require(manager != null, "Gmanager is missing.");

        GameObject p2CameraRoot = GameObject.Find("GameManagers/CManager_P2");
        Camera p2MainCamera = p2CameraRoot != null
            ? Array.Find(p2CameraRoot.GetComponentsInChildren<Camera>(true), candidate => candidate.name == "MainCamera")
            : null;
        Canvas p2Canvas = FindComponentIncludingInactive<Canvas>("GameManagers/MainCanvas_P2");
        CinemachineCamera p2VirtualCamera = FindComponentIncludingInactive<CinemachineCamera>("GameManagers/VCamera_P2");
        Require(p2MainCamera != null, "P2 main camera is missing.");
        Require(p2Canvas != null && p2Canvas.worldCamera != null, "P2 UI camera is missing.");
        if (Display.displays.Length >= 2)
        {
            Require(p2MainCamera.targetDisplay == 1, "P2 main camera is not assigned to Display 1.");
            Require(p2Canvas.worldCamera.targetDisplay == 1, "P2 UI camera is not assigned to Display 1.");
        }
        Require(p2VirtualCamera != null && p2VirtualCamera.OutputChannel == OutputChannels.Channel01,
            "P2 Cinemachine output channel is incorrect.");

        TMP_Text p1Title = FindComponent<TMP_Text>("GameManagers/MainCanvas/Title/StartPrompt");
        TMP_Text p2Title = FindComponent<TMP_Text>("GameManagers/MainCanvas_P2/Title/StartPrompt");
        Require(p1Title != null && p2Title != null && p1Title.text.Contains("P1") && p2Title.text.Contains("P2"),
            "Title prompts are not personalized for each display.");
        Require(FindComponentIncludingInactive<PedalButtonSurface>("GameManagers/MainCanvas/Title/Player1Pedal/ButtonSurface") != null &&
                FindComponentIncludingInactive<PedalButtonSurface>("GameManagers/MainCanvas_P2/Title/Player2Pedal/ButtonSurface") != null &&
                FindComponentIncludingInactive<PedalButtonSurface>("GameManagers/MainCanvas/Title/Player2Pedal/ButtonSurface") == null &&
                FindComponentIncludingInactive<PedalButtonSurface>("GameManagers/MainCanvas_P2/Title/Player1Pedal/ButtonSurface") == null,
            "Each display must contain only its own pedal gauge.");
    }

    private static void ValidateSpawnAndCountdown()
    {
        GameObject p1Car = GameObject.Find("Player1_Car");
        GameObject p2Car = GameObject.Find("Player2_Car");
        Require(p1Car != null && p2Car != null, "Two cars were not spawned.");

        DebugMover p1Mover = p1Car.GetComponent<DebugMover>();
        DebugMover p2Mover = p2Car.GetComponent<DebugMover>();
        Require(p1Mover?.InputSource?.PlayerIndex == 0, "P1 input source was not assigned.");
        Require(p2Mover?.InputSource?.PlayerIndex == 1, "P2 input source was not assigned.");

        CarResetter p1Resetter = p1Car.GetComponent<CarResetter>();
        Require(p1Resetter != null, "P1 resetter is missing.");
        p1Resetter.ResetCar();
        Require(p1Mover.IsInputSuppressed, "Driving input was not suppressed after resetting the car.");

        Require(!manager.IsDrivingEnabled, "Driving was enabled during countdown.");
        Require(manager.CountdownTimeRemaining > 0f && manager.CountdownTimeRemaining <= 3f,
            "Three-second countdown did not start.");
        Require(GetRaceStatus(0) == GetRaceStatus(1) && !string.IsNullOrWhiteSpace(GetRaceStatus(0)),
            "Countdown status differs between displays.");
    }

    private static void ValidateFirstFinish()
    {
        GameObject p1Car = GameObject.Find("Player1_Car");
        finishMethod = typeof(Gmanager).GetMethod("HandleCarFinished", BindingFlags.NonPublic | BindingFlags.Instance);
        Require(finishMethod != null, "Finish handler is missing.");

        finishMethod.Invoke(manager, new object[]
        {
            p1Car.GetComponent<Rigidbody>(),
            new RaceResultRecord { totalRaceTime = 10f, completedLaps = 3, goalLap = 3 }
        });

        Require(manager.state == Gmanager.State.Game, "Race ended when only first place finished.");
        Require(manager.WaitingForSecondPlace, "Second-place wait did not start.");
        Require(Mathf.Abs(manager.SecondPlaceTimeRemaining - 40f) < 0.2f, "Second-place timeout is not 40 seconds.");
        foreach (Collider collider in p1Car.GetComponentsInChildren<Collider>(true))
        {
            Require(!collider.enabled, "First-place car still blocks the course.");
        }

        GoalCelebrationUI p1Goal = FindComponentIncludingInactive<GoalCelebrationUI>("GameManagers/MainCanvas/Goal");
        GoalCelebrationUI p2Goal = FindComponentIncludingInactive<GoalCelebrationUI>("GameManagers/MainCanvas_P2/Goal");
        Require(p1Goal != null && p1Goal.gameObject.activeInHierarchy &&
                p2Goal != null && !p2Goal.gameObject.activeInHierarchy,
            "Only the player who finished should see the goal screen.");
        Require(!IsFinishWarningVisible(0) && IsFinishWarningVisible(1) && GetRaceStatus(1).Contains("40.0"),
            "Only the unfinished player should see the second-place timer.");
    }

    private static void ValidateSpectatorAndFinishSecondPlayer()
    {
        TMP_Text spectatorLabel = FindComponent<TMP_Text>(
            "GameManagers/MainCanvas/SpectatorOverlay/PlayerPlate/PlayerLabel");
        CinemachineCamera p1Camera = FindComponentIncludingInactive<CinemachineCamera>("GameManagers/VCamera");
        CinemachineCamera p2Camera = FindComponentIncludingInactive<CinemachineCamera>("GameManagers/VCamera_P2");
        Require(spectatorLabel != null && spectatorLabel.text.Contains("P2"),
            "The finished player's display does not identify the watched player.");
        Require(p1Camera != null && p2Camera != null && p1Camera.Follow == p2Camera.Follow,
            "The finished player's camera is not following the unfinished player.");

        GameObject p2Car = GameObject.Find("Player2_Car");
        finishMethod.Invoke(manager, new object[]
        {
            p2Car.GetComponent<Rigidbody>(),
            new RaceResultRecord { totalRaceTime = 12f, completedLaps = 3, goalLap = 3 }
        });
    }

    private static void ValidateSharedResult()
    {
        TMP_Text p1Winner = FindComponent<TMP_Text>("GameManagers/MainCanvas/Result/ResultPresentation/ResultCard/Winner");
        TMP_Text p2Winner = FindComponent<TMP_Text>("GameManagers/MainCanvas_P2/Result/ResultPresentation/ResultCard/Winner");
        TMP_Text p1First = FindComponent<TMP_Text>("GameManagers/MainCanvas/Result/ResultPresentation/ResultCard/ResultRow1/Player");
        TMP_Text p1Second = FindComponent<TMP_Text>("GameManagers/MainCanvas/Result/ResultPresentation/ResultCard/ResultRow2/Player");
        Require(manager.state == Gmanager.State.Result, "Result state was not reached.");
        Require(p1Winner != null && p2Winner != null && p1Winner.text == p2Winner.text,
            "Winner text differs between displays.");
        Require(p1First != null && p1Second != null && p1First.text.Contains("1") && p1Second.text.Contains("2"),
            "Result does not contain both players.");
    }

    private static void ValidateSecondPlayerGoal()
    {
        GoalCelebrationUI p1Goal = FindComponentIncludingInactive<GoalCelebrationUI>("GameManagers/MainCanvas/Goal");
        GoalCelebrationUI p2Goal = FindComponentIncludingInactive<GoalCelebrationUI>("GameManagers/MainCanvas_P2/Goal");
        TMP_Text p2GoalText = FindComponentIncludingInactive<TMP_Text>(
            "GameManagers/MainCanvas_P2/Goal/Hero/GoalText");
        Transform p2Confetti = GameObject.Find("GameManagers")?.transform.Find("MainCanvas_P2/Goal/Confetti");
        Require(manager.state == Gmanager.State.Goal, "Goal celebration state was not reached.");
        Require(p1Goal != null && !p1Goal.gameObject.activeInHierarchy &&
                p2Goal != null && p2Goal.gameObject.activeInHierarchy && p2GoalText != null && p2GoalText.text == "GOAL!",
            "Only the second finisher should see the second goal screen.");
        Require(p2Confetti != null && p2Confetti.childCount > 0,
            "Goal confetti was not created for the second finisher.");
    }

    private static string GetRaceStatus(int playerIndex)
    {
        string canvasName = playerIndex == 0 ? "MainCanvas" : "MainCanvas_P2";
        TMP_Text countdown = FindComponent<TMP_Text>($"GameManagers/{canvasName}/OnPlay/CountdownStatus/RaceStatus");
        TMP_Text warning = FindComponent<TMP_Text>($"GameManagers/{canvasName}/OnPlay/FinishWarningStatus/FinishWarningText");
        return warning != null && warning.gameObject.activeInHierarchy
            ? warning.text
            : countdown != null ? countdown.text : string.Empty;
    }

    private static bool IsFinishWarningVisible(int playerIndex)
    {
        string canvasName = playerIndex == 0 ? "MainCanvas" : "MainCanvas_P2";
        GameObject warning = GameObject.Find($"GameManagers/{canvasName}/OnPlay/FinishWarningStatus");
        return warning != null && warning.activeInHierarchy;
    }

    private static T FindComponent<T>(string path) where T : Component
    {
        return GameObject.Find(path)?.GetComponent<T>();
    }

    private static T FindComponentIncludingInactive<T>(string path) where T : Component
    {
        int separator = path.IndexOf('/');
        string rootName = separator >= 0 ? path.Substring(0, separator) : path;
        GameObject root = GameObject.Find(rootName);
        if (root == null) return null;
        Transform target = separator >= 0 ? root.transform.Find(path.Substring(separator + 1)) : root.transform;
        return target != null ? target.GetComponent<T>() : null;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void Fail(string message)
    {
        Debug.LogError($"MULTIPLAYER_PLAYMODE_VALIDATION_FAIL: {message}");
        Finish(1);
    }

    private static void Finish(int exitCode)
    {
        SessionState.SetBool(SessionKey, false);
        EditorApplication.update -= Tick;
        EditorApplication.Exit(exitCode);
    }
}
