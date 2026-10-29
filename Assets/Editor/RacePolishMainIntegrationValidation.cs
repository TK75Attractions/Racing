using System;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>Checks the integration of the new racing helpers with main's night UI and notices.</summary>
public static class RacePolishMainIntegrationValidation
{
    public static void Run()
    {
        Directory.CreateDirectory("Logs/MainIntegration");
        TubeChargeLineValidation.Run();
        SteeringAndItemFairnessValidation.Run();
        TutorialGimmickCapture.Run();
        NightArcadeIntegrationValidation.RunWithMain();
        Debug.Log("RACE_POLISH_MAIN_INTEGRATION_PASS: acceleration pads and one-shot notices, physical steering warning, fair items, photographic tutorial, main night scenery, tube and driving physics.");
    }
}
