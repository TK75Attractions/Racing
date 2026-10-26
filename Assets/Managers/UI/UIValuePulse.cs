using TMPro;
using UnityEngine;

/// <summary>Directional HUD impulse. Repeated events retarget from the current pose without accumulating offsets.</summary>
[DisallowMultipleComponent]
public sealed class UIValuePulse : MonoBehaviour
{
    private TMP_Text label;
    private RectTransform rect;
    private Vector2 basePosition,startPosition;
    private Vector3 baseScale,startScale;
    private Quaternion baseRotation;
    private Color baseColor,accent,startColor;
    private float age,duration,rotation,direction,peakScale;
    private double lastTick;
    public bool IsPlaying { get; private set; }
    private void Awake()=>Cache();
    private void Cache()
    {
        if(label!=null)return;
        label=GetComponent<TMP_Text>();rect=transform as RectTransform;
        basePosition=rect!=null?rect.anchoredPosition:Vector2.zero;baseScale=transform.localScale;baseRotation=transform.localRotation;
        baseColor=label!=null?label.color:Color.white;
    }
    public void SetRestColor(Color color){Cache();baseColor=color;if(!IsPlaying&&label!=null)label.color=color;}
    public void Play(Color color,float angle)=>StartPulse(color,angle,0,1.16f,.34f);
    public void PlayRank(Color color,int change)=>StartPulse(color,0,Mathf.Sign(change),1.15f,.38f);
    private void StartPulse(Color color,float angle,float travel,float scale,float seconds)
    {
        Cache();if(label==null||rect==null||!isActiveAndEnabled)return;
        startPosition=rect.anchoredPosition;
        if(!IsPlaying)startPosition-=Vector2.up*travel*24f;
        startScale=transform.localScale;startColor=label.color;
        accent=color;rotation=angle;direction=travel;peakScale=scale;duration=seconds;age=0;lastTick=Time.realtimeSinceStartupAsDouble;IsPlaying=true;Apply(0);
    }
    public void Tick(float dt)
    {
        if(!IsPlaying)return;age+=Mathf.Max(0,dt);float t=Mathf.Clamp01(age/duration);Apply(t);
        if(t>=1)ResetPose();
    }
    private void Apply(float t)
    {
        float eased=UIMotion.Out(t),hit=Mathf.Sin(Mathf.PI*t),spring=UIMotion.Spring(t);
        rect.anchoredPosition=Vector2.LerpUnclamped(startPosition,basePosition,eased)+Vector2.up*(direction*4f*spring);
        transform.localScale=Vector3.Min(baseScale*1.18f,Vector3.Lerp(startScale,baseScale,eased)+baseScale*((peakScale-1)*hit));
        transform.localRotation=baseRotation*Quaternion.Euler(0,0,rotation*spring*.4f);
        label.color=Color.Lerp(Color.Lerp(startColor,baseColor,eased),accent,hit);
    }
    public void ResetPose()
    {
        IsPlaying=false;if(label==null)return;
        rect.anchoredPosition=basePosition;transform.localScale=baseScale;transform.localRotation=baseRotation;label.color=baseColor;
    }
    private void Update(){if(!IsPlaying)return;double now=Time.realtimeSinceStartupAsDouble;Tick((float)(now-lastTick));lastTick=now;}
    private void OnDisable()=>ResetPose();
}
