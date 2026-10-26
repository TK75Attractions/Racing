#if UNITY_EDITOR
using UnityEditor;

/// <summary>The modern UI entry point now validates the approved arcade implementation.</summary>
public static class ModernUIValidation
{
    [MenuItem("Racing/UI/Validate Modern UI")]
    public static void Run()=>ArcadeUIValidation.Run();
    [MenuItem("Racing/UI/Preview HUD")]
    public static void PreviewHUD()=>ArcadeUIValidation.Capture();
}
#endif
