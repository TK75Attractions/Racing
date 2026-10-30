using UnityEngine;

/// <summary>Regression gate for the visitor audit; Play Mode journeys/races run separately.</summary>
public static class NoviceAuditValidation
{
    private const string IsolationKey="Racing.NoviceAudit.Isolated";
    private static void Isolate(){UnityEditor.SessionState.SetBool(IsolationKey,true);ResumeIsolation();}
    [UnityEditor.InitializeOnLoadMethod]private static void ResumeIsolation()
    {
        if(!UnityEditor.SessionState.GetBool(IsolationKey,false))return;
        UnityEditor.EditorApplication.playModeStateChanged-=OnPlayState;UnityEditor.EditorApplication.playModeStateChanged+=OnPlayState;
    }
    private static void OnPlayState(UnityEditor.PlayModeStateChange state)
    {
        if(state==UnityEditor.PlayModeStateChange.EnteredPlayMode)
        {
            foreach(var publisher in Object.FindObjectsByType<RaceRemoteFirebasePublisher>(FindObjectsSortMode.None))publisher.enabled=false;
            foreach(var bridge in Object.FindObjectsByType<RaceDashboardBridge>(FindObjectsSortMode.None))bridge.enabled=false;
        }
        else if(state==UnityEditor.PlayModeStateChange.ExitingPlayMode)
        {UnityEditor.SessionState.SetBool(IsolationKey,false);UnityEditor.EditorApplication.playModeStateChanged-=OnPlayState;}
    }
    public static void RunTutorial(){Isolate();DrivingTutorialValidation.Run();}
    public static void RunVisitorFlow(){Isolate();ShortVisitorFlowValidation.Run();}
    public static void RunStability()
    {
        NoviceInputValidation.ValidateDashboard();CarStabilityValidation.RunBatch();
    }
    public static void RunRaces()
    {
        System.IO.Directory.CreateDirectory("Artifacts/OperationsFixes");
        typeof(OperationsFixValidation).GetMethod("ValidateWallCamera",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic).Invoke(null,null);
        Debug.Log("NOVICE_CAMERA_OCCLUSION_PASS: actual camera retraction and smooth return on ten banks.");
        SteeringComfortValidation.RunCourseBatch();
    }
    public static void RunChecks()
    {
        RacingHUDFontBuilder.Build();
        NoviceRecoveryValidation.Run();
        AbruptSteeringValidation.Run();
        TubeWallRecoveryValidation.RunFullBatch();
        LapPracticeTubeValidation.Run();
        SteeringAndItemFairnessValidation.Run();
        Debug.Log("NOVICE_AUDIT_CHECKS_PASS: recovery, 85 m/s abrupt steering, tube/floor containment, practice, items, CPU clearance, lap timing and UI.");
    }
}
