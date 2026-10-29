using System;
using UnityEngine;

/// <summary>Queues each cue once per destination, sharing the existing independent audio outputs.</summary>
[DisallowMultipleComponent]
public sealed class RaceSoundEffects : MonoBehaviour
{
    public static RaceSoundEffects Instance { get; private set; }
    [SerializeField,Range(0f,1f)] private float volume=.8f;
    private readonly RaceSoundEffectsMixer[] mixers=new RaceSoundEffectsMixer[2];
    private readonly float[,] nextAllowed=new float[2,RaceSoundBank.CueCount];
    private readonly float[,] nextVehicleAllowed=new float[2,4];
    private readonly SkidSource[] skidSources=new SkidSource[8];
    public event Action<RaceSoundCue,int> CueQueued;
    private struct SkidSource { public int id; public Vector3 position; public float strength,lastUpdated; public int surface; }

    private void Awake()
    {
        Instance=this;
        foreach (RaceSoundCue cue in Enum.GetValues(typeof(RaceSoundCue))) RaceSoundBank.Get(cue);
        foreach (RaceVehicleAudioCue cue in Enum.GetValues(typeof(RaceVehicleAudioCue))) RaceVehicleSoundBank.Get(cue);
    }
    public void RegisterMixer(int player,RaceSoundEffectsMixer mixer)
    {
        if (player<0 || player>=mixers.Length) return;
        mixers[player]=mixer;
        mixer.SetEffectsGain(volume*RaceAudioSettings.Effects);
    }
    private void Update()
    {
        foreach (RaceSoundEffectsMixer mixer in mixers) if (mixer!=null) mixer.SetEffectsGain(volume*RaceAudioSettings.Effects);
        for (int player=0;player<mixers.Length;player++)
        {
            float left=0f,right=0f; int surface=0;
            for (int i=0;i<skidSources.Length;i++)
            {
                SkidSource source=skidSources[i];
                if (source.id==0 || Time.unscaledTime-source.lastUpdated>.15f || source.strength<=0f) continue;
                GetWorldGains(player,source.position,out float sourceLeft,out float sourceRight);
                float level=source.strength*Mathf.Max(sourceLeft,sourceRight);
                if (level>Mathf.Max(left,right)) surface=source.surface;
                left=Mathf.Max(left,source.strength*sourceLeft); right=Mathf.Max(right,source.strength*sourceRight);
            }
            mixers[player]?.SetTireSkid(left,right,surface);
        }
    }
    public bool Play(RaceSoundCue cue,int player)
    {
        if (player<0 || player>=mixers.Length || mixers[player]==null || (uint)(int)cue>=(uint)RaceSoundBank.CueCount) return false;
        float now=Time.unscaledTime;
        if (now<nextAllowed[player,(int)cue]) return false;
        if (!mixers[player].Enqueue(RaceSoundBank.Get(cue))) return false;
        nextAllowed[player,(int)cue]=now+Cooldown(cue);
        RaceAudioSettings.NotifyCue();
        CueQueued?.Invoke(cue,player);
        return true;
    }
    private static float Cooldown(RaceSoundCue cue)
    {
        switch (cue)
        {
            case RaceSoundCue.Impact: return .3f;
            case RaceSoundCue.TireSkid: return .65f;
            case RaceSoundCue.DriftCharge: return .25f;
            case RaceSoundCue.Boost: return .35f;
            case RaceSoundCue.Charge: return .12f;
            case RaceSoundCue.Select: return .07f;
            default: return .035f;
        }
    }
    public void PlayForAll(RaceSoundCue cue) { for (int player=0;player<mixers.Length;player++) Play(cue,player); }
    public static void UpdateTireSkid(int sourceId,Vector3 position,float strength,int surface)
    {
        if (Instance==null) return;
        int slot=-1;
        for (int i=0;i<Instance.skidSources.Length;i++)
        {
            if (Instance.skidSources[i].id==sourceId) { slot=i; break; }
            if (slot<0 && (Instance.skidSources[i].id==0 || Time.unscaledTime-Instance.skidSources[i].lastUpdated>.15f)) slot=i;
        }
        if (slot<0) return;
        Instance.skidSources[slot]=new SkidSource { id=sourceId,position=position,strength=Mathf.Clamp01(strength),surface=Mathf.Clamp(surface,0,2),lastUpdated=Time.unscaledTime };
    }
    public static void PlayVehicleCue(RaceVehicleAudioCue cue,Vector3 position,float strength=1f)
    {
        if (Instance==null) return;
        int cueIndex=(int)cue;
        for (int player=0;player<Instance.mixers.Length;player++)
        {
            RaceSoundEffectsMixer mixer=Instance.mixers[player];
            if (mixer==null || Time.unscaledTime<Instance.nextVehicleAllowed[player,cueIndex]) continue;
            Instance.GetWorldGains(player,position,out float left,out float right);
            if (!mixer.Enqueue(RaceVehicleSoundBank.Get(cue),left*Mathf.Clamp01(strength),right*Mathf.Clamp01(strength))) continue;
            Instance.nextVehicleAllowed[player,cueIndex]=Time.unscaledTime+(cue==RaceVehicleAudioCue.Scrape?.20f:.12f);
            if (cue!=RaceVehicleAudioCue.Scrape) RaceAudioSettings.NotifyCue();
        }
    }
    private void GetWorldGains(int player,Vector3 position,out float left,out float right)
    {
        left=right=0f;
        Transform camera=Gmanager.Control!=null?Gmanager.Control.GetPlayerAudioPerspective(player):null;
        if (camera==null) return;
        Vector3 relative=camera.InverseTransformPoint(position);
        float distance=relative.magnitude;
        float attenuation=Mathf.Clamp01(1f-distance/70f);
        float pan=distance>.01f?Mathf.Clamp(relative.x/distance,-1f,1f):0f;
        left=attenuation*Mathf.Sqrt((1f-pan)*.5f);
        right=attenuation*Mathf.Sqrt((1f+pan)*.5f);
    }
    public static void PlayForPlayer(RaceSoundCue cue,int player) => Instance?.Play(cue,player);
    public static void PlayForButton(RaceSoundCue cue,Transform button)
    {
        Canvas canvas=button.GetComponentInParent<Canvas>();
        if (canvas!=null)
        {
            Canvas root=canvas.rootCanvas;
            int display=root.renderMode==RenderMode.ScreenSpaceCamera && root.worldCamera!=null
                ? root.worldCamera.targetDisplay : root.targetDisplay;
            int player=Gmanager.Control!=null && display==Gmanager.Control.PlayerTwoDisplayIndex ? 1 : display;
            PlayForPlayer(cue,player);
        }
    }
    private void OnDestroy() { if (Instance==this) Instance=null; }
}
