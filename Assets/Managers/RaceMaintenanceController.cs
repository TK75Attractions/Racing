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
    private readonly List<bool> screenVisible = new List<bool>();
    private readonly bool[] updateHolds = new bool[2];
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
        foreach(var canvas in outputCanvases) {previousDistance.Add(canvas.planeDistance);screenVisible.Add(false);}
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
            manager.GetComponent<RaceBackgroundMusic>()?.SetMaintenanceMusic(true);
        }
        else RestorePause();
        manager.SetMaintenanceInputBlocked(active);
        RefreshScreens();
        if (persist) { PlayerPrefs.SetInt(PreferenceKey, active ? 1 : 0); PlayerPrefs.Save(); }
    }

    private void RestorePause()
    {
        if (!ownsPause) return;
        if (manager != null) manager.GetComponent<RaceBackgroundMusic>()?.SetMaintenanceMusic(false);
        pausedSeconds += Time.realtimeSinceStartupAsDouble - pauseBegan;
        Time.timeScale = previousTimeScale; AudioListener.pause = previousAudioPause;
        RaceAudioSettings.MaintenanceMuted = false;
        ownsPause = false;
    }
    public void SetUpdateHold(int player,bool active)
    {
        if(player<0 || player>=updateHolds.Length)return;
        updateHolds[player]=active;RefreshScreens();
    }
    private void RefreshScreens()
    {
        if(!configured)return;
        for(int i=0;i<screens.Count;i++)
        {
            bool visible=IsActive || (i<2?updateHolds[i]:updateHolds[0]&&updateHolds[1]);
            var canvas=outputCanvases[i];
            if(visible!=screenVisible[i] && canvas!=null)
            {
                if(visible)
                {
                    previousDistance[i]=canvas.planeDistance;
                    if(canvas.worldCamera!=null)canvas.planeDistance=canvas.worldCamera.nearClipPlane+.01f;
                }
                else canvas.planeDistance=previousDistance[i];
            }
            screenVisible[i]=visible;
            if(screens[i]!=null)
            {
                screens[i].SetActive(visible);
                if(visible)screens[i].transform.SetAsLastSibling();
            }
        }
    }
    private void LateUpdate() => RefreshScreens();
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
        RestorePause();IsActive=false;updateHolds[0]=updateHolds[1]=false;RefreshScreens();
        foreach (var screen in screens) if (screen != null) Destroy(screen);
    }
}
