using System.Collections.Generic;
using UnityEngine;

/// <summary>One persisted service switch for both players and the broadcast output. HTTP continues while physics is paused.</summary>
[DisallowMultipleComponent]
public sealed class RaceMaintenanceController : MonoBehaviour
{
    public const string PreferenceKey = "Racing.Maintenance.Enabled";
    private readonly List<GameObject> screens = new List<GameObject>();
    private readonly List<Canvas> outputCanvases = new List<Canvas>();
    private readonly List<float> previousDistance = new List<float>();
    private Gmanager manager;
    private float previousTimeScale;
    private double pauseBegan, pausedSeconds;
    private bool previousAudioPause, ownsPause, configured, resumeAfterDisable;
    public bool IsActive { get; private set; }
    public int ScreenCount => screens.Count;
    // A monotonic presentation clock that excludes service downtime. Ordinary game pauses
    // still animate on real time; only maintenance suspends UI motion and pending callbacks.
    public double PresentationTime => (ownsPause ? pauseBegan : Time.realtimeSinceStartupAsDouble) - pausedSeconds;

    public void Configure(Gmanager race, PlayerDisplayRig[] players, RaceBroadcastDirector broadcast)
    {
        if (configured) return;
        manager = race;
        foreach (var player in players)
        {
            if (player?.Canvas != null)
            {
                screens.Add(RaceMaintenanceScreen.Create(player.Canvas, player.PlayerIndex));
                outputCanvases.Add(player.Canvas);
            }
        }
        if (broadcast?.PresentationCanvas != null)
        {
            screens.Add(RaceMaintenanceScreen.Create(broadcast.PresentationCanvas, 0));
            outputCanvases.Add(broadcast.PresentationCanvas);
        }
        configured = true;
        SetActive(PlayerPrefs.GetInt(PreferenceKey, 0) == 1, persist: false);
    }

    public void SetActive(bool active, bool persist = true)
    {
        if (!configured || active == IsActive) return;
        IsActive = active;
        if (active)
        {
            pauseBegan = Time.realtimeSinceStartupAsDouble;
            previousTimeScale = Time.timeScale; previousAudioPause = AudioListener.pause; ownsPause = true;
            Time.timeScale = 0; AudioListener.pause = true; RaceAudioSettings.MaintenanceMuted = true;
            previousDistance.Clear();
            foreach (var canvas in outputCanvases)
            {
                previousDistance.Add(canvas.planeDistance);
                // Put the opaque service sheet just inside the UI camera's near plane,
                // ahead of retained world depth, without changing URP's read-only clearDepth.
                if (canvas.worldCamera != null) canvas.planeDistance = canvas.worldCamera.nearClipPlane + .01f;
            }
        }
        else RestorePause();
        manager.SetMaintenanceInputBlocked(active);
        foreach (var screen in screens) if (screen != null)
        {
            if (active) screen.transform.SetAsLastSibling();
            screen.SetActive(active);
        }
        if (persist) { PlayerPrefs.SetInt(PreferenceKey, active ? 1 : 0); PlayerPrefs.Save(); }
    }

    private void RestorePause()
    {
        if (!ownsPause) return;
        pausedSeconds += Time.realtimeSinceStartupAsDouble - pauseBegan;
        Time.timeScale = previousTimeScale; AudioListener.pause = previousAudioPause;
        RaceAudioSettings.MaintenanceMuted = false;
        for (int i=0;i<outputCanvases.Count;i++) if (outputCanvases[i] != null) outputCanvases[i].planeDistance = previousDistance[i];
        ownsPause = false;
    }
    private void LateUpdate()
    {
        // A screen transition may reorder its fade panel. Keep one service layer last,
        // inside the owner's canvas rather than introducing another camera-facing canvas.
        if (IsActive) foreach (var screen in screens) if (screen != null) screen.transform.SetAsLastSibling();
    }
    private void OnDisable()
    {
        resumeAfterDisable = IsActive;
        if (IsActive) SetActive(false, persist: false);
    }
    private void OnEnable()
    {
        if (configured && resumeAfterDisable) { resumeAfterDisable = false; SetActive(true, persist: false); }
    }
    private void OnDestroy()
    {
        RestorePause();
        foreach (var screen in screens) if (screen != null) Destroy(screen);
    }
}
