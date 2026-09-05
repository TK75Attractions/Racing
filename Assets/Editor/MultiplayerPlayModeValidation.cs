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
                    ValidateFinishFlow();
                    stage = 3;
                    stageStartTime = EditorApplication.timeSinceStartup;
                    break;

                case 3 when EditorApplication.timeSinceStartup - stageStartTime > 1.5d:
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

        Camera p2MainCamera = FindComponent<Camera>("GameManagers/CManager_P2/MainCamera");
        Canvas p2Canvas = FindComponent<Canvas>("GameManagers/MainCanvas_P2");
        CinemachineCamera p2VirtualCamera = FindComponent<CinemachineCamera>("GameManagers/VCamera_P2");
        Require(p2MainCamera != null && p2MainCamera.targetDisplay == 1, "P2 main camera is not assigned to Display 1.");
        Require(p2Canvas != null && p2Canvas.worldCamera != null && p2Canvas.worldCamera.targetDisplay == 1,
            "P2 UI camera is not assigned to Display 1.");
        Require(p2VirtualCamera != null && p2VirtualCamera.OutputChannel == OutputChannels.Channel01,
            "P2 Cinemachine output channel is incorrect.");

        TMP_Text p1Title = FindComponent<TMP_Text>("GameManagers/MainCanvas/Title/StartPrompt");
        TMP_Text p2Title = FindComponent<TMP_Text>("GameManagers/MainCanvas_P2/Title/StartPrompt");
        Require(p1Title != null && p2Title != null && p1Title.text == p2Title.text,
            "Title prompts differ between displays.");
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

    private static void ValidateFinishFlow()
    {
        GameObject p1Car = GameObject.Find("Player1_Car");
        GameObject p2Car = GameObject.Find("Player2_Car");
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

        Require(GetRaceStatus(0) == GetRaceStatus(1) && GetRaceStatus(0).Contains("40.0"),
            "Second-place timer differs between displays.");

        finishMethod.Invoke(manager, new object[]
        {
            p2Car.GetComponent<Rigidbody>(),
            new RaceResultRecord { totalRaceTime = 12f, completedLaps = 3, goalLap = 3 }
        });
    }

    private static void ValidateSharedResult()
    {
        TMP_Text p1Result = FindComponent<TMP_Text>("GameManagers/MainCanvas/Result/Panel/Time/Txt");
        TMP_Text p2Result = FindComponent<TMP_Text>("GameManagers/MainCanvas_P2/Result/Panel/Time/Txt");
        Require(manager.state == Gmanager.State.Result, "Result state was not reached.");
        Require(p1Result != null && p2Result != null && p1Result.text == p2Result.text,
            "Result text differs between displays.");
        Require(p1Result.text.Contains("P1") && p1Result.text.Contains("P2"),
            "Result does not contain both players.");
    }

    private static string GetRaceStatus(int playerIndex)
    {
        string canvasName = playerIndex == 0 ? "MainCanvas" : "MainCanvas_P2";
        TMP_Text status = FindComponent<TMP_Text>($"GameManagers/{canvasName}/OnPlay/RaceStatus");
        return status != null ? status.text : string.Empty;
    }

    private static T FindComponent<T>(string path) where T : Component
    {
        return GameObject.Find(path)?.GetComponent<T>();
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
