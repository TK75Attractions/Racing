using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Two real Play sessions, with the user's maintenance preference restored on every exit.</summary>
public static class MaintenanceModeValidation
{
    private const string Key="Racing.Maintenance.Validation";
    private static double started;
    private static bool restarting;
    public static void RunBatch()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Stop Play Mode first.");
        RacingHUDFontBuilder.Build();
        SessionState.SetBool(Key+".HadPreference",PlayerPrefs.HasKey(RaceMaintenanceController.PreferenceKey));
        SessionState.SetInt(Key+".Preference",PlayerPrefs.GetInt(RaceMaintenanceController.PreferenceKey));
        PlayerPrefs.SetInt(RaceMaintenanceController.PreferenceKey,0);
        SessionState.SetString(Key+".Stage","initial");SessionState.SetBool(Key,true);
        Prepare();Resume();EditorApplication.isPlaying=true;
    }
    private static void Prepare()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        var input=UnityEngine.Object.FindFirstObjectByType<InputManager>();input.isDebugMode=true;input.isP1SerialP2KeyboardDebugMode=false;
    }
    [InitializeOnLoadMethod] private static void Resume()
    {
        if(!SessionState.GetBool(Key,false))return;
        started=EditorApplication.timeSinceStartup;
        SceneManager.sceneLoaded-=OnLoaded;SceneManager.sceneLoaded+=OnLoaded;
        EditorApplication.update-=Tick;EditorApplication.update+=Tick;
    }
    private static void OnLoaded(Scene scene,LoadSceneMode mode)
    {
        foreach(var bridge in UnityEngine.Object.FindObjectsByType<RaceDashboardBridge>(FindObjectsSortMode.None))bridge.enabled=false;
        foreach(var publisher in UnityEngine.Object.FindObjectsByType<RaceRemoteFirebasePublisher>(FindObjectsSortMode.None))publisher.enabled=false;
    }
    private static void Tick()
    {
        if(EditorApplication.timeSinceStartup-started>120){Finish(false,"Maintenance validation timeout.");return;}
        if(!EditorApplication.isPlaying)
        {
            if(SessionState.GetString(Key+".Stage","")=="restart" && !EditorApplication.isPlayingOrWillChangePlaymode && !restarting)
            {restarting=true;Prepare();EditorApplication.isPlaying=true;}
            return;
        }
        if(Gmanager.Control==null || UnityEngine.Object.FindFirstObjectByType<MaintenancePlaytestProbe>()!=null)return;
        var probe=new GameObject("Maintenance validation probe").AddComponent<MaintenancePlaytestProbe>();
        probe.VerifyRestart=SessionState.GetString(Key+".Stage","")=="restart";
        probe.Completed=result=>
        {
            if(result=="restart"){SessionState.SetString(Key+".Stage","restart");EditorApplication.isPlaying=false;}
            else Finish(result=="passed",result);
        };
    }
    private static void Finish(bool success,string reason)
    {
        SessionState.SetBool(Key,false);EditorApplication.update-=Tick;SceneManager.sceneLoaded-=OnLoaded;
        if(SessionState.GetBool(Key+".HadPreference",false))PlayerPrefs.SetInt(RaceMaintenanceController.PreferenceKey,SessionState.GetInt(Key+".Preference",0));
        else PlayerPrefs.DeleteKey(RaceMaintenanceController.PreferenceKey);
        PlayerPrefs.Save();Directory.CreateDirectory("Artifacts/Maintenance");
        File.WriteAllText("Artifacts/Maintenance/playmode-result.json",JsonUtility.ToJson(new Result{success=success,reason=reason,unity=Application.unityVersion},true));
        if(success)Debug.Log("MAINTENANCE_PLAYMODE_PASS: seven real phases, all three canvases, physics/timers/audio, disable/re-enable and a second Play session restoring persisted maintenance.");
        else Debug.LogError("MAINTENANCE_VALIDATION_FAILED: "+reason);
        if(Application.isBatchMode)EditorApplication.Exit(success?0:1);else EditorApplication.isPlaying=false;
    }
    [Serializable] private sealed class Result{public bool success;public string reason,unity;}
}
