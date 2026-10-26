using UnityEngine;

/// <summary>画面ごとにメニュー・練習・周回・ゴール後のBGMを独立して切り替えます。</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(RaceAudioOutput))]
public sealed class RaceBackgroundMusic : MonoBehaviour
{
    private const string MenuClipPath = "Audio/BGM/Race_Opening_v2";
    private const string TutorialClipPath = "Audio/BGM/TachibanaMoroe";
    private const string FinishClipPath = "Audio/BGM/finish_v2";
    private const string RaceClipPath = "Audio/BGM/racegame_v3";
    private const string FinalLapClipPath = "Audio/BGM/racegame_v3_final lap";
    [SerializeField, Range(0f, 1f)] private float volume = 0.9f;
    private Gmanager gameManager;
    private readonly AudioSource[] sources = new AudioSource[2];
    private readonly RaceSoundEffectsMixer[] mixers = new RaceSoundEffectsMixer[2];
    private readonly AudioClip[] requestedClips = new AudioClip[2];
    private readonly float[] transitionGains = { 1f, 1f };
    private readonly bool[] switching = new bool[2];
    private AudioClip menuClip, tutorialClip, finishClip, raceClip, finalLapClip;
    private AudioClip silentClip;

    private void Awake()
    {
        gameManager = GetComponent<Gmanager>();
        if (GetComponent<RaceAudioOperatorPanel>()==null) gameObject.AddComponent<RaceAudioOperatorPanel>();
        menuClip = Resources.Load<AudioClip>(MenuClipPath);
        tutorialClip = Resources.Load<AudioClip>(TutorialClipPath);
        finishClip = Resources.Load<AudioClip>(FinishClipPath);
        raceClip = Resources.Load<AudioClip>(RaceClipPath);
        finalLapClip = Resources.Load<AudioClip>(FinalLapClipPath);
        silentClip = AudioClip.Create("Silent effects clock",512,1,AudioSettings.outputSampleRate,false);
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
            // Music gain is applied inside the filter, so BGM mute cannot silence UI cues.
            source.volume = 1f;
            mixers[player] = sourceObject.AddComponent<RaceSoundEffectsMixer>();
            mixers[player].SetMusicGain(volume * RaceAudioSettings.Bgm * RaceAudioSettings.NotificationDuck);
            GetComponent<RaceSoundEffects>()?.RegisterMixer(player,mixers[player]);
            sourceObject.AddComponent<PlayerAudioCapture>().ConfigureMusic(GetComponent<RaceAudioOutput>(), player);
        }
        RaceAudioSettings.ApplyMaster();
        if (menuClip == null || tutorialClip == null || finishClip == null || raceClip == null || finalLapClip == null)
            Debug.LogError($"RaceBackgroundMusic: BGMの読み込み失敗。Menu={menuClip != null}, Tutorial={tutorialClip != null}, Finish={finishClip != null}, Race={raceClip != null}, FinalLap={finalLapClip != null}", this);
    }

    private void Update()
    {
        Tick(Time.unscaledDeltaTime);
    }

    private void Tick(float dt)
    {
        if (gameManager == null) return;
        for (int player = 0; player < sources.Length; player++)
        {
            AudioSource source = sources[player];
            int viewedPlayer = gameManager.GetAudioViewedPlayer(player);
            Gmanager.State phase = gameManager.GetPlayerAudioState(player);
            AudioClip desired;
            switch (phase)
            {
                case Gmanager.State.Title:
                case Gmanager.State.CarSelection:
                    desired = menuClip;
                    break;
                case Gmanager.State.Tutorial:
                    desired = tutorialClip;
                    break;
                case Gmanager.State.Goal:
                case Gmanager.State.Result:
                    desired = finishClip;
                    break;
                default:
                    desired = gameManager.IsPlayerOnFinalLap(viewedPlayer) ? finalLapClip : raceClip;
                    break;
            }
            desired = desired ?? silentClip;
            if (source.clip == null)
            {
                source.clip = desired;
                requestedClips[player] = desired;
                if (desired != null && source.isActiveAndEnabled) source.Play();
            }
            else if (requestedClips[player] != desired)
            {
                requestedClips[player] = desired;
                switching[player] = true;
            }

            if (switching[player])
            {
                transitionGains[player] = Mathf.MoveTowards(transitionGains[player], 0f, dt / .35f);
                if (transitionGains[player] <= 0f)
                {
                    source.clip = requestedClips[player];
                    source.time = 0f;
                    if (source.isActiveAndEnabled) source.Play();
                    switching[player] = false;
                }
            }
            else
                transitionGains[player] = Mathf.MoveTowards(transitionGains[player], 1f, dt / .55f);
            mixers[player].SetMusicGain(volume * RaceAudioSettings.Bgm * RaceAudioSettings.NotificationDuck * transitionGains[player]);
        }
    }
    private void OnDestroy()
    {
        if (silentClip == null) return;
        if (Application.isPlaying) Destroy(silentClip); else DestroyImmediate(silentClip);
    }
}
