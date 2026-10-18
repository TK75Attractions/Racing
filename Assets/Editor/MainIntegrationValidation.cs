using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Checks the combined gameplay, serial preparation, HUD and output changes.</summary>
public static class MainIntegrationValidation
{
    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        RacingHUDFontBuilder.Build();
        ModernUIValidation.Run();
        MultiDisplayValidation.Run();
        PlayerAudioValidation.Run();
        SteeringValidation.Run();
        DriftValidation.Run();
        SkillGameplayValidation.RunAll();
        Debug.Log("MAIN_INTEGRATION_VALIDATION_PASS");
    }
}
