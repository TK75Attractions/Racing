using UnityEngine;

/// <summary>ゲーム進行に合わせて、メニュー用とレース用のBGMを切り替えます。</summary>
[DisallowMultipleComponent]
public sealed class RaceBackgroundMusic : MonoBehaviour
{
    private const string MenuClipPath = "Audio/BGM/TachibanaMoroe";
    private const string RaceClipPath = "Audio/BGM/Maze";

    [SerializeField, Range(0f, 1f)] private float volume = 0.55f;

    private Gmanager gameManager;
    private AudioSource audioSource;
    private AudioClip menuClip;
    private AudioClip raceClip;
    private Gmanager.State? playingState;

    private void Awake()
    {
        gameManager = GetComponent<Gmanager>();
        menuClip = Resources.Load<AudioClip>(MenuClipPath);
        raceClip = Resources.Load<AudioClip>(RaceClipPath);

        GameObject sourceObject = new GameObject("BackgroundMusic");
        sourceObject.transform.SetParent(transform, false);
        audioSource = sourceObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;
        audioSource.loop = true;
        audioSource.spatialBlend = 0f;
        audioSource.volume = volume;

        if (menuClip == null || raceClip == null)
        {
            Debug.LogError(
                $"RaceBackgroundMusic: BGMの読み込みに失敗しました。" +
                $" Menu={menuClip != null}, Race={raceClip != null}", this);
        }

        Refresh(force: true);
    }

    private void Update()
    {
        Refresh(force: false);
    }

    private void Refresh(bool force)
    {
        if (gameManager == null || audioSource == null) return;
        Gmanager.State state = gameManager.state;
        if (!force && playingState == state) return;

        AudioClip desiredClip = IsMenuState(state) ? menuClip : raceClip;
        playingState = state;
        if (desiredClip == null || (audioSource.clip == desiredClip && audioSource.isPlaying)) return;

        audioSource.Stop();
        audioSource.clip = desiredClip;
        audioSource.Play();
    }

    private static bool IsMenuState(Gmanager.State state)
    {
        return state == Gmanager.State.Title || state == Gmanager.State.Result;
    }
}
