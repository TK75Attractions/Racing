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
    private Quaternion baseRotation,startRotation;
    private Color baseColor,accent,startColor;
    private float age,duration,rotation,direction;
    private UINumberImpactGraphic impact;
    private double lastTick;
    public bool IsPlaying { get; private set; }
    private void Awake()=>Cache();
    private void Cache()
    {
        if(label!=null)return;
        label=GetComponent<TMP_Text>();rect=transform as RectTransform;
        basePosition=rect!=null?rect.anchoredPosition:Vector2.zero;baseScale=transform.localScale;baseRotation=transform.localRotation;
        baseColor=label!=null?label.color:Color.white;
        if(label!=null)
        {
            var accents=RacingUITheme.Rect(transform,"NumberImpact",Vector2.zero,Vector2.one);
            impact=accents.GetComponent<UINumberImpactGraphic>();if(impact==null)impact=accents.gameObject.AddComponent<UINumberImpactGraphic>();
            impact.SetImpulse(1,0,baseColor);
        }
    }
    public void SetRestColor(Color color){Cache();baseColor=color;if(!IsPlaying&&label!=null)label.color=color;}
    public void Play(Color color,float angle)=>StartPulse(color,angle,0,.44f);
    public void PlayRank(Color color,int change)=>StartPulse(color,-4f*Mathf.Sign(change),Mathf.Sign(change),.48f);
    private void StartPulse(Color color,float angle,float travel,float seconds)
    {
        Cache();if(label==null||rect==null||!isActiveAndEnabled)return;
        startPosition=rect.anchoredPosition;
        if(!IsPlaying)startPosition-=Vector2.up*travel*24f;
        startScale=transform.localScale;startRotation=transform.localRotation;startColor=label.color;
        accent=color;rotation=angle;direction=travel;duration=seconds;age=0;lastTick=UIMotion.Realtime;IsPlaying=true;Apply(0);
    }
    public void Tick(float dt)
    {
        if(!IsPlaying)return;age+=Mathf.Max(0,dt);float t=Mathf.Clamp01(age/duration);Apply(t);
        if(t>=1)ResetPose();
    }
    private void Apply(float t)
    {
        float eased=UIMotion.Out(t);
        // A short compression stores energy; a tall punch, small recoil and
        // directional landing release it. The number stays factual throughout.
        float anticipation=t<.14f?Mathf.Sin(t/.14f*Mathf.PI):0;
        float release=Mathf.Clamp01((t-.14f)/.86f);
        float recoil=Mathf.Sin(release*Mathf.PI*3)*Mathf.Exp(-release*4)*(1-release);
        rect.anchoredPosition=Vector2.LerpUnclamped(startPosition,basePosition,eased)+Vector2.up*(direction*6f*recoil);
        Vector3 shape=new Vector3(1+.055f*anticipation+.20f*recoil,1-.11f*anticipation+.40f*recoil,1);
        Vector3 size=Vector3.Scale(baseScale,shape)+Vector3.Lerp(startScale-baseScale,Vector3.zero,eased);
        transform.localScale=Vector3.Max(baseScale*.86f,Vector3.Min(baseScale*1.20f,size));
        transform.localRotation=Quaternion.Slerp(startRotation,baseRotation,eased)*Quaternion.Euler(0,0,rotation*recoil);
        label.color=Color.Lerp(Color.Lerp(startColor,baseColor,eased),accent,Mathf.Clamp01(recoil*2.4f));
        impact?.SetImpulse(t,direction,accent);
    }
    public void ResetPose()
    {
        IsPlaying=false;if(label==null)return;
        rect.anchoredPosition=basePosition;transform.localScale=baseScale;transform.localRotation=baseRotation;label.color=baseColor;
        impact?.SetImpulse(1,0,baseColor);
    }
    private void Update(){if(!IsPlaying)return;double now=UIMotion.Realtime;if(UIMotion.IsPaused){lastTick=now;return;}Tick((float)(now-lastTick));lastTick=now;}
    private void OnDisable()=>ResetPose();
}
