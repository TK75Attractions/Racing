using System;
using UnityEditor;
using UnityEngine;

/// <summary>Regression checks for the October branch reconciliation.</summary>
public static class BranchIntegrationValidation
{
    [InitializeOnLoadMethod]
    private static void ProtectIsolatedPlaytests()
    {
        if (Environment.GetEnvironmentVariable("RACING_BRANCH_INTEGRATION_VALIDATION") != "1") return;
        EditorApplication.playModeStateChanged += state =>
        {
            if (!Application.isBatchMode || state != PlayModeStateChange.EnteredPlayMode) return;
            foreach (var publisher in UnityEngine.Object.FindObjectsByType<RaceRemoteFirebasePublisher>(FindObjectsSortMode.None))
                publisher.enabled = false;
        };
    }

    public static void RunPhysics()
    {
        SteeringValidation.Run();
        HighSpeedCorneringValidation.Run();
        AbruptSteeringValidation.Run();
        Debug.Log("BRANCH_INTEGRATION_PHYSICS_PASS");
    }
}
