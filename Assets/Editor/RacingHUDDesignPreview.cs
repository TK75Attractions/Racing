#if UNITY_EDITOR
using UnityEditor;

/// <summary>Existing capture commands now export only the approved arcade sport design.</summary>
public static class RacingHUDDesignPreview
{
    [MenuItem("Racing/UI/Capture HUD Design")]
    public static void Capture()=>ArcadeUIValidation.Capture();
    public static void CaptureHudIndicators()=>Capture();
    public static void CaptureSteeringComfort()=>Capture();
    public static void RunBatch()=>ArcadeUIValidation.RunAndCapture();
}
#endif
