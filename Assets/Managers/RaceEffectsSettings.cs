using UnityEngine;

/// <summary>Per-user accessibility controls for nonessential race presentation.</summary>
public static class RaceEffectsSettings
{
    private const string Prefix = "Race.Effects.";
    public static bool CameraShake => PlayerPrefs.GetInt(Prefix + "CameraShake", 1) != 0;
    public static bool SpeedLines => PlayerPrefs.GetInt(Prefix + "SpeedLines", 1) != 0;
    public static bool StrongFlashes => PlayerPrefs.GetInt(Prefix + "StrongFlashes", 1) != 0;
    public static bool FieldOfView => PlayerPrefs.GetInt(Prefix + "FieldOfView", 1) != 0;
    public static void SetCameraShake(bool value) => Set("CameraShake", value);
    public static void SetSpeedLines(bool value) => Set("SpeedLines", value);
    public static void SetStrongFlashes(bool value) => Set("StrongFlashes", value);
    public static void SetFieldOfView(bool value) => Set("FieldOfView", value);
    private static void Set(string key, bool value) { PlayerPrefs.SetInt(Prefix + key, value ? 1 : 0); PlayerPrefs.Save(); }
}
