using UnityEngine;

/// <summary>全プレイヤー共通の音量設定です。</summary>
public static class RaceAudioSettings
{
    private const string MasterKey = "Racing.Audio.Master";
    private const string BgmKey = "Racing.Audio.BGM";
    private const string EngineKey = "Racing.Audio.Engine";

    public static float Master => PlayerPrefs.GetFloat(MasterKey, 1f);
    public static float Bgm => PlayerPrefs.GetFloat(BgmKey, 1f);
    public static float Engine => PlayerPrefs.GetFloat(EngineKey, 0.35f);

    public static void ApplyMaster()
    {
        AudioListener.volume = Mathf.Clamp01(Master);
    }

    public static void SetMaster(float value)
    {
        PlayerPrefs.SetFloat(MasterKey, Mathf.Clamp01(value));
        ApplyMaster();
    }

    public static void SetBgm(float value)
    {
        PlayerPrefs.SetFloat(BgmKey, Mathf.Clamp01(value));
    }

    public static void SetEngine(float value)
    {
        PlayerPrefs.SetFloat(EngineKey, Mathf.Clamp01(value));
    }
}
