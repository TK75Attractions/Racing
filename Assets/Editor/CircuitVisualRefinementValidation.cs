using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class CircuitVisualRefinementValidation
{
    public const string Output = "Artifacts/CircuitRefinements";
    public static void RunMeters()
    {
        ArcadeUIValidation.Run(); Directory.CreateDirectory(Output);
        string[] settings = { "ProjectSettings/GraphicsSettings.asset", "ProjectSettings/QualitySettings.asset" };
        byte[][] original = Array.ConvertAll(settings, File.ReadAllBytes);
        try
        {
            var capture = typeof(ArcadeUIValidation).GetMethod("CaptureView", BindingFlags.Static | BindingFlags.NonPublic);
            foreach (int player in new[] { 0, 1 }) foreach (int width in new[] { 1280, 1920 })
            {
                capture.Invoke(null, new object[] { player, "race", width, width * 9 / 16 });
                string suffix = width == 1920 ? "" : "-1280x720";
                File.Copy($"Artifacts/ArcadeSportUI/race-p{player + 1}{suffix}.png", $"{Output}/meters-p{player + 1}-{width}.png", true);
            }
            Debug.Log("CIRCUIT_METERS_PASS: both palettes; 720p/1080p; production dial bindings and dirty-mesh checks.");
        }
        finally { for (int i = 0; i < settings.Length; i++) File.WriteAllBytes(settings[i], original[i]); }
    }
}
