using UnityEngine;

/// <summary>画面ごとに通常走行/Final Lap/メニューのBGMを独立して切り替えます。</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(RaceAudioOutput))]
public sealed class RaceBackgroundMusic : MonoBehaviour
{
    private const string MenuClipPath = "Audio/BGM/TachibanaMoroe";
    private const string RaceClipPath = "Audio/BGM/racegame_v3";
    private const string FinalLapClipPath = "Audio/BGM/racegame_v3_final lap";
    [SerializeField, Range(0f, 1f)] private float volume = 0.9f;
    private Gmanager gameManager;
    private readonly AudioSource[] sources = new AudioSource[2];
    private AudioClip menuClip, raceClip, finalLapClip;

    private void Awake()
    {
        gameManager = GetComponent<Gmanager>();
        menuClip = Resources.Load<AudioClip>(MenuClipPath);
        raceClip = Resources.Load<AudioClip>(RaceClipPath);
        finalLapClip = Resources.Load<AudioClip>(FinalLapClipPath);
        for (int player = 0; player < sources.Length; player++)
        {
            GameObject sourceObject = new GameObject($"BackgroundMusic_P{player + 1}");
            sourceObject.transform.SetParent(transform, false);
            AudioSource source = sourceObject.AddComponent<AudioSource>();
            sources[player] = source;
            source.playOnAwake = false;
            source.loop = true;
            source.spatialBlend = 0f;
            source.pitch = 1f;
            sourceObject.AddComponent<PlayerAudioCapture>().ConfigureMusic(GetComponent<RaceAudioOutput>(), player);
        }
        RaceAudioSettings.ApplyMaster();
        if (menuClip == null || raceClip == null || finalLapClip == null)
            Debug.LogError($"RaceBackgroundMusic: BGMの読み込み失敗。Menu={menuClip != null}, Race={raceClip != null}, FinalLap={finalLapClip != null}", this);
    }

    private void Update()
    {
        if (gameManager == null) return;
        for (int player = 0; player < sources.Length; player++)
        {
            AudioSource source = sources[player];
            source.volume = volume * Mathf.Clamp01(RaceAudioSettings.Bgm);
            int viewedPlayer = gameManager.GetAudioViewedPlayer(player);
            bool menu = gameManager.state == Gmanager.State.Title || gameManager.state == Gmanager.State.Result;
            AudioClip desired = menu ? menuClip : gameManager.IsPlayerOnFinalLap(viewedPlayer) ? finalLapClip : raceClip;
            if (source.clip == desired && source.isPlaying) continue;
            source.Stop();
            source.clip = desired;
            if (desired != null && source.isActiveAndEnabled) source.Play();
        }
    }
}
