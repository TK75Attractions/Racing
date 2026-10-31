using System.Collections.Generic;
using UnityEngine;

/// <summary>画面ごとのBGMと、ゲーム停止中も流すメンテナンス用BGMを管理します。</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(RaceAudioOutput))]
public sealed class RaceBackgroundMusic : MonoBehaviour
{
    private const string MenuClipPath = "Audio/BGM/Race_Opening_v3";
    private const string TutorialClipPath = "Audio/BGM/TachibanaMoroe";
    private const string FinishClipPath = "Audio/BGM/finishv4";
    private const string ResultClipPath = "Audio/BGM/result_v3";
    private const string RaceClipPath = "Audio/BGM/racegame_v3";
    private const string FinalLapClipPath = "Audio/BGM/racegame_v3_final lap";
    [SerializeField, Range(0f, 1f)] private float volume = 0.9f;
    private Gmanager gameManager;
    private readonly AudioSource[] sources = new AudioSource[2];
    private readonly RaceSoundEffectsMixer[] mixers = new RaceSoundEffectsMixer[2];
    private readonly AudioClip[] requestedClips = new AudioClip[2];
    private readonly float[] transitionGains = { 1f, 1f };
    private readonly bool[] switching = new bool[2];
    private readonly AudioSource[] maintenanceSources = new AudioSource[2];
    private readonly RaceSoundEffectsMixer[] maintenanceMixers = new RaceSoundEffectsMixer[2];
    private AudioClip menuClip, tutorialClip, finishClip, resultClip, raceClip, finalLapClip;
    private AudioClip silentClip;
    // DSP time stops with AudioListener.pause, so maintenance preserves the transport.
    private readonly Dictionary<AudioClip, double> clipEpochs = new Dictionary<AudioClip, double>();
    private readonly List<AudioClip> expiredClips = new List<AudioClip>();
    private readonly double[] playbackStarts = new double[2];
    private readonly FinishMusicSequence[] finishSequences = { new FinishMusicSequence(), new FinishMusicSequence() };

    private void Awake()
    {
        gameManager = GetComponent<Gmanager>();
        if (GetComponent<RaceAudioOperatorPanel>()==null) gameObject.AddComponent<RaceAudioOperatorPanel>();
        menuClip = Resources.Load<AudioClip>(MenuClipPath);
        tutorialClip = Resources.Load<AudioClip>(TutorialClipPath);
        finishClip = Resources.Load<AudioClip>(FinishClipPath);
        resultClip = Resources.Load<AudioClip>(ResultClipPath);
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
        if (menuClip == null || tutorialClip == null || finishClip == null || resultClip == null || raceClip == null || finalLapClip == null)
            Debug.LogError($"RaceBackgroundMusic: BGMの読み込み失敗。Menu={menuClip != null}, Tutorial={tutorialClip != null}, Finish={finishClip != null}, Result={resultClip != null}, Race={raceClip != null}, FinalLap={finalLapClip != null}", this);
    }

    private void Update()
    {
        Tick(Time.unscaledDeltaTime);
    }

    private void Tick(float dt)
    {
        if (gameManager == null) return;
        if (gameManager.IsMaintenanceActive)
        {
            SetMaintenanceMusic(true);
            return; // Keep the paused gameplay tracks and their fades at the exact same position.
        }
        SetMaintenanceMusic(false);
        // Retire a transport after both outputs have left it. A new race starts at zero.
        expiredClips.Clear();
        foreach (var transport in clipEpochs)
            if (sources[0].clip != transport.Key && sources[1].clip != transport.Key)
                expiredClips.Add(transport.Key);
        foreach (AudioClip clip in expiredClips) clipEpochs.Remove(clip);
        AudioSettings.GetDSPBufferSize(out int bufferFrames, out _);
        double startAt = AudioSettings.dspTime + System.Math.Max(.1, 2.0 * bufferFrames / AudioSettings.outputSampleRate);
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
                    finishSequences[player].Reset();
                    desired = menuClip;
                    break;
                case Gmanager.State.Tutorial:
                    finishSequences[player].Reset();
                    desired = menuClip;
                    break;
                case Gmanager.State.Countdown:
                    finishSequences[player].Reset();
                    desired = gameManager.IsPlayerOnFinalLap(viewedPlayer) ? finalLapClip : raceClip;
                    break;
                default:
                    bool finished=gameManager.DidPlayerFinish(player);
                    var track=finishSequences[player].Select(finished,phase!=Gmanager.State.Result && gameManager.IsPlayerStillRacing(1-player),
                        phase==Gmanager.State.Goal || phase==Gmanager.State.Result,AudioSettings.dspTime);
                    if(track==FinishMusicSequence.Track.Finish) desired=finishClip;
                    else if(track==FinishMusicSequence.Track.Results) desired=resultClip;
                    else
                    {
                        int racer=finished ? 1-player : viewedPlayer;
                        desired=gameManager.IsPlayerOnFinalLap(racer) ? finalLapClip : raceClip;
                    }
                    break;
            }
            desired = desired ?? silentClip;
            if (source.clip == null)
            {
                requestedClips[player] = desired;
                StartOnTransport(player, desired, startAt);
            }
            else if (requestedClips[player] != desired)
            {
                requestedClips[player] = desired;
                if (source.clip == finishClip && finishSequences[player].HasStarted && AudioSettings.dspTime >= finishSequences[player].EndsAt)
                {
                    // A completed one-shot has no tail to fade out. Join the opponent/result transport now.
                    StartOnTransport(player,desired,startAt);
                    switching[player]=false;
                }
                else switching[player] = true;
            }

            if (switching[player])
            {
                transitionGains[player] = Mathf.MoveTowards(transitionGains[player], 0f, dt / .35f);
                if (transitionGains[player] <= 0f)
                {
                    StartOnTransport(player, requestedClips[player], startAt);
                    switching[player] = false;
                }
            }
            else if (AudioSettings.dspTime >= playbackStarts[player])
                transitionGains[player] = Mathf.MoveTowards(transitionGains[player], 1f, dt / .55f);
            mixers[player].SetMusicGain(volume * RaceAudioSettings.Bgm * RaceAudioSettings.NotificationDuck * transitionGains[player]);
        }
    }

    private void StartOnTransport(int player, AudioClip clip, double startAt)
    {
        AudioSource source = sources[player];
        source.Stop();
        source.clip = clip;
        source.loop=clip!=finishClip;
        if (clip==finishClip)
        {
            source.timeSamples=0;
            finishSequences[player].Begin(startAt,(double)clip.samples/clip.frequency);
            playbackStarts[player]=startAt;
            if(source.isActiveAndEnabled) source.PlayScheduled(startAt);
            return;
        }
        if (!clipEpochs.TryGetValue(clip, out double epoch))
        {
            epoch = startAt;
            clipEpochs.Add(clip, epoch);
        }
        // Both new sources use one scheduled DSP boundary. A late joiner seeks
        // to that same transport, including across loop boundaries.
        double elapsed = System.Math.Max(0, startAt - epoch);
        source.timeSamples = (int)(System.Math.Floor(elapsed * clip.frequency) % clip.samples);
        playbackStarts[player] = startAt;
        if (source.isActiveAndEnabled) source.PlayScheduled(startAt);
    }

    public void SetMaintenanceMusic(bool active)
    {
        for (int player = 0; player < maintenanceSources.Length; player++)
        {
            AudioSource source = maintenanceSources[player];
            if (!active)
            {
                if (source != null && source.isPlaying) source.Stop();
                continue;
            }
            if (tutorialClip == null) continue;
            if (source == null)
            {
                var musicObject = new GameObject($"MaintenanceMusic_P{player + 1}");
                musicObject.transform.SetParent(transform, false);
                source = musicObject.AddComponent<AudioSource>();
                maintenanceSources[player] = source;
                source.playOnAwake = false;
                source.loop = true;
                source.spatialBlend = 0f;
                source.volume = 1f;
                source.clip = tutorialClip; // 橘諸兄
                // This source alone continues while AudioListener.pause stops the game audio.
                source.ignoreListenerPause = true;
                var mixer = musicObject.AddComponent<RaceSoundEffectsMixer>();
                maintenanceMixers[player] = mixer;
                musicObject.AddComponent<PlayerAudioCapture>().ConfigureMusic(GetComponent<RaceAudioOutput>(), player, maintenance: true);
            }
            maintenanceMixers[player].SetMusicGain(volume * RaceAudioSettings.Bgm);
            if (source.isActiveAndEnabled && !source.isPlaying) source.Play();
        }
    }

    private void OnDisable() => SetMaintenanceMusic(false);

    private void OnDestroy()
    {
        if (silentClip == null) return;
        if (Application.isPlaying) Destroy(silentClip); else DestroyImmediate(silentClip);
    }
}
