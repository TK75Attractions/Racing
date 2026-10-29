using UnityEngine;

/// <summary>全プレイヤー共通の音量設定です。</summary>
public static class RaceAudioSettings
{
    public static volatile bool MaintenanceMuted;
    private const string MasterKey = "Racing.Audio.Master";
    private const string BgmKey = "Racing.Audio.BGM";
    private const string EngineKey = "Racing.Audio.Engine";
    private const string EffectsKey = "Racing.Audio.Effects";
    private const string PresetKey = "Racing.Audio.Preset";
    private const string DiagnosticsKey = "Racing.Audio.Diagnostics";
    private static float duckUntil;

    public enum VenuePreset { Standard, Quiet, NoisyVenue, Custom }

    public static float Master => PlayerPrefs.GetFloat(MasterKey, 1f);
    public static float Bgm => PlayerPrefs.GetFloat(BgmKey, 0.8f);
    public static float Engine => PlayerPrefs.GetFloat(EngineKey, 0.45f);
    public static float Effects => PlayerPrefs.GetFloat(EffectsKey, 1f);
    public static VenuePreset Preset => (VenuePreset)Mathf.Clamp(PlayerPrefs.GetInt(PresetKey, 0), 0, 3);
    public static bool DiagnosticsEnabled => PlayerPrefs.GetInt(DiagnosticsKey, 0) != 0;
    public static float NotificationDuck => Time.unscaledTime < duckUntil ? 0.78f : 1f;

    public static void NotifyCue()
    {
        duckUntil = Mathf.Max(duckUntil, Time.unscaledTime + 0.55f);
    }

    public static void SetDiagnostics(bool enabled) => PlayerPrefs.SetInt(DiagnosticsKey, enabled ? 1 : 0);

    public static void ApplyVenuePreset(VenuePreset preset)
    {
        PlayerPrefs.SetInt(PresetKey, (int)preset);
        switch (preset)
        {
            case VenuePreset.Quiet: SetLevels(.82f, .78f, .30f, .85f); break;
            case VenuePreset.NoisyVenue: SetLevels(1f, .48f, .34f, 1f); break;
            default: SetLevels(1f, .8f, .42f, 1f); break;
        }
        ApplyMaster();
    }

    private static void SetLevels(float master, float bgm, float engine, float effects)
    {
        PlayerPrefs.SetFloat(MasterKey, master);
        PlayerPrefs.SetFloat(BgmKey, bgm);
        PlayerPrefs.SetFloat(EngineKey, engine);
        PlayerPrefs.SetFloat(EffectsKey, effects);
    }

    public static void ApplyMaster()
    {
        AudioListener.volume = Mathf.Clamp01(Master);
    }

    public static void SetMaster(float value)
    {
        PlayerPrefs.SetFloat(MasterKey, Mathf.Clamp01(value));
        MarkCustom();
        ApplyMaster();
    }

    public static void SetBgm(float value)
    {
        PlayerPrefs.SetFloat(BgmKey, Mathf.Clamp01(value)); MarkCustom();
    }

    public static void SetEngine(float value)
    {
        PlayerPrefs.SetFloat(EngineKey, Mathf.Clamp01(value)); MarkCustom();
    }
    public static void SetEffects(float value) { PlayerPrefs.SetFloat(EffectsKey, Mathf.Clamp01(value)); MarkCustom(); }
    private static void MarkCustom() => PlayerPrefs.SetInt(PresetKey,(int)VenuePreset.Custom);
}
