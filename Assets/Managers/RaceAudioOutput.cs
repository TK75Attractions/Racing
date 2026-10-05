using System;
using System.Runtime.InteropServices;
using UnityEngine;

/// <summary>2台のUSB/HDMI出力機器を、独立したステレオ出力として開きます。</summary>
[DisallowMultipleComponent]
public sealed class RaceAudioOutput : MonoBehaviour
{
#if UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX
    private const string Library = "libRaceAudio";
#else
    private const string Library = "RaceAudio";
#endif
    private const string PreferencePrefix = "Racing.Audio.Device.P";
    public static RaceAudioOutput Instance { get; private set; }
    public string[] DeviceNames { get; private set; } = Array.Empty<string>();
    private string[] deviceKeys = Array.Empty<string>();
    private readonly int[] selected = { -1, -1 };
    private readonly string[] status = { "未接続", "未接続" };
    private bool available;
    private bool pluginLoaded;
    private volatile bool accepting;
    private int sampleRate;
    private float nextDeviceCheck;

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] private static extern int ra_refresh_devices();
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr ra_device_name(int index);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr ra_device_key(int index);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] private static extern int ra_open(int player, int device, int sampleRate);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] private static extern int ra_is_started(int player);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] private static extern void ra_close(int player);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] private static extern void ra_shutdown();
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] private static extern void ra_push(
        int player, int stream, [In] float[] samples, int frames, int channels, float leftGain, float rightGain);

    private void Awake()
    {
        Instance = this;
        sampleRate = AudioSettings.outputSampleRate;
        AudioSettings.OnAudioConfigurationChanged += OnAudioConfigurationChanged;
        try
        {
            available = ra_refresh_devices() >= 0;
            pluginLoaded = true;
            if (!available)
            {
                status[0] = status[1] = "音声機器を初期化できません";
                Debug.LogError("RaceAudioOutput: 音声機器を初期化できません。USB/HDMI機器の接続とOSの音声設定を確認してください。", this);
                return;
            }
            RefreshDevices();
            accepting = available;
        }
        catch (Exception error) when (error is DllNotFoundException || error is EntryPointNotFoundException || error is BadImageFormatException)
        {
            status[0] = status[1] = "音声プラグインがありません";
            Debug.LogError("RaceAudioOutput: ネイティブ音声プラグインを読み込めません。Native~/RaceAudio のビルド手順を確認してください。 " + error.Message, this);
        }
    }

    public string GetDeviceLabel(int player) => player >= 0 && player < 2 ? status[player] : "未接続";

    public void RefreshDevices()
    {
        if (!pluginLoaded) return;
        int count = ra_refresh_devices();
        if (count < 0) return;
        available = true;
        accepting = false;
        DeviceNames = new string[count];
        deviceKeys = new string[count];
        for (int i = 0; i < count; i++)
        {
            DeviceNames[i] = Marshal.PtrToStringUTF8(ra_device_name(i)) ?? string.Empty;
            deviceKeys[i] = Marshal.PtrToStringUTF8(ra_device_key(i)) ?? string.Empty;
        }
        for (int player = 0; player < 2; player++)
        {
            string saved = PlayerPrefs.GetString(PreferencePrefix + player, string.Empty);
            int device = string.IsNullOrEmpty(saved) ? (player == 0 && count > 0 ? 0 : -1) : Array.IndexOf(deviceKeys, saved);
            selected[player] = -1;
            if (device < 0 || (player == 1 && device == selected[0]))
            {
                ra_close(player);
                status[player] = saved == "__disabled__" ? "出力なし" : string.IsNullOrEmpty(saved) ? "出力機器を選択してください" : "選択した機器が未接続です";
                continue;
            }
            OpenDevice(player, device, persist: false);
        }
        accepting = true;
    }

    public void SelectNextDevice(int player)
    {
        if (!available || player < 0 || player > 1) return;
        int current = selected[player] < 0 ? DeviceNames.Length : selected[player];
        for (int offset = 1; offset <= DeviceNames.Length + 1; offset++)
        {
            int device = (current + offset) % (DeviceNames.Length + 1);
            if (device == DeviceNames.Length)
            {
                ra_close(player);
                selected[player] = -1;
                status[player] = "出力なし";
                PlayerPrefs.SetString(PreferencePrefix + player, "__disabled__");
                PlayerPrefs.Save();
                return;
            }
            if (device == selected[1 - player]) continue;
            OpenDevice(player, device, persist: true);
            return;
        }
        status[player] = "別の出力機器を接続してください";
    }

    private void OpenDevice(int player, int device, bool persist)
    {
        int result = ra_open(player, device, sampleRate);
        selected[player] = result == 0 ? device : -1;
        status[player] = result == 0 ? DeviceNames[device] : $"接続失敗 ({result})";
        if (result != 0) Debug.LogWarning($"P{player + 1}の音声出力 {DeviceNames[device]} を開けません: {result}", this);
        if (persist && result == 0)
        {
            PlayerPrefs.SetString(PreferencePrefix + player, deviceKeys[device]);
            PlayerPrefs.Save();
        }
    }

    // Called on Unity's audio thread. Never access Unity objects or PlayerPrefs here.
    public void Submit(int player, int stream, float[] samples, int channels, float leftGain, float rightGain)
    {
        if (accepting && channels > 0)
            ra_push(player, stream, samples, samples.Length / channels, channels, leftGain, rightGain);
    }

    private void Update()
    {
        if (!available || Time.unscaledTime < nextDeviceCheck) return;
        nextDeviceCheck = Time.unscaledTime + 1f;
        for (int player = 0; player < 2; player++)
            if (selected[player] >= 0 && ra_is_started(player) == 0)
            {
                ra_close(player);
                selected[player] = -1;
                status[player] = "出力機器が切断されました";
            }
    }

    private void OnAudioConfigurationChanged(bool deviceWasChanged)
    {
        accepting = false;
        sampleRate = AudioSettings.outputSampleRate;
        RefreshDevices();
        accepting = available;
    }

    private void OnDestroy()
    {
        accepting = false;
        AudioSettings.OnAudioConfigurationChanged -= OnAudioConfigurationChanged;
        if (available) ra_shutdown();
        if (Instance == this) Instance = null;
    }
}
