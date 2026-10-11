using System;
using UnityEngine;

/// <summary>Queues each cue once per destination, sharing the existing independent audio outputs.</summary>
[DisallowMultipleComponent]
public sealed class RaceSoundEffects : MonoBehaviour
{
    public static RaceSoundEffects Instance { get; private set; }
    [SerializeField,Range(0f,1f)] private float volume=.8f;
    private readonly RaceSoundEffectsMixer[] mixers=new RaceSoundEffectsMixer[2];
    private readonly float[,] nextAllowed=new float[2,9];
    public event Action<RaceSoundCue,int> CueQueued;

    private void Awake()
    {
        Instance=this;
        foreach (RaceSoundCue cue in Enum.GetValues(typeof(RaceSoundCue))) RaceSoundBank.Get(cue);
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
    }
    public bool Play(RaceSoundCue cue,int player)
    {
        if (player<0 || player>=mixers.Length || mixers[player]==null) return false;
        float now=Time.unscaledTime;
        if (now<nextAllowed[player,(int)cue]) return false;
        if (!mixers[player].Enqueue(RaceSoundBank.Get(cue))) return false;
        nextAllowed[player,(int)cue]=now+(cue==RaceSoundCue.Select?.07f:.035f);
        CueQueued?.Invoke(cue,player);
        return true;
    }
    public void PlayForAll(RaceSoundCue cue) { for (int player=0;player<mixers.Length;player++) Play(cue,player); }
    public static void PlayForPlayer(RaceSoundCue cue,int player) => Instance?.Play(cue,player);
    public static void PlayForButton(RaceSoundCue cue,Transform button)
    {
        Canvas canvas=button.GetComponentInParent<Canvas>();
        if (canvas!=null) PlayForPlayer(cue,canvas.rootCanvas.targetDisplay);
    }
    private void OnDestroy() { if (Instance==this) Instance=null; }
}
