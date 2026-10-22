using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class BroadcastValidation
{
    [MenuItem("Racing/Validation/Broadcast Camera Sample")]
    public static void RunLiveCameraSample()
    {
        Environment.SetEnvironmentVariable("RACING_BROADCAST_LIVE_ONLY","1");RunPlaythrough();
    }
    [MenuItem("Racing/Validation/Broadcast and Race Highlights")]
    public static void RunPlaythrough()
    {
        Environment.SetEnvironmentVariable("RACING_BROADCAST_CHECK","1");
        Environment.SetEnvironmentVariable("RACING_BALANCE_SKILL_SUITE","1");
        Environment.SetEnvironmentVariable("RACING_BALANCE_RUN","human-fast-control-7102");
        Environment.SetEnvironmentVariable("RACING_BALANCE_TIME_SCALE","4");
        bool liveOnly=Environment.GetEnvironmentVariable("RACING_BROADCAST_LIVE_ONLY")=="1";
        Environment.SetEnvironmentVariable("RACING_BALANCE_WINDOW_SECONDS",liveOnly?"20":"0");
        Environment.SetEnvironmentVariable("RACING_BALANCE_OUTPUT",System.IO.Path.GetFullPath(liveOnly?"Logs/BroadcastCameraSample":"Logs/BroadcastPlaythrough"));
        Environment.SetEnvironmentVariable("RACING_BROADCAST_HISTORY",System.IO.Path.GetFullPath("Logs/BroadcastPlaythrough/History"));
        RacingHUDFontBuilder.Build();
        Resume();BalancePlaytestValidation.RunBatch();
    }
    [InitializeOnLoadMethod]
    private static void Resume()
    {
        if(Environment.GetEnvironmentVariable("RACING_BROADCAST_CHECK")!="1")return;
        SceneManager.sceneLoaded-=OnSceneLoaded;SceneManager.sceneLoaded+=OnSceneLoaded;
    }
    private static void OnSceneLoaded(Scene scene,LoadSceneMode mode)
    {
        foreach(var bridge in UnityEngine.Object.FindObjectsByType<RaceDashboardBridge>(FindObjectsSortMode.None))bridge.enabled=false;
        foreach(var publisher in UnityEngine.Object.FindObjectsByType<RaceRemoteFirebasePublisher>(FindObjectsSortMode.None))publisher.enabled=false;
        foreach(var audio in UnityEngine.Object.FindObjectsByType<RaceAudioOutput>(FindObjectsSortMode.None))audio.enabled=false;
    }
}
