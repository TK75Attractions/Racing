using UnityEngine;

/// <summary>Reusable, interruptible stagger. Configure once; playback allocates no coroutines or arrays.</summary>
[DisallowMultipleComponent]
public sealed class UIRevealSequence : MonoBehaviour
{
    private struct Part { public RectTransform rect; public CanvasGroup group; public Vector2 position; public Vector3 scale; public float alpha,delay; }
    private Part[] parts;
    private Vector2 offset;
    private float age, stagger, duration,maximumDelay;
    private double lastTick;
    public bool IsPlaying { get; private set; }
    public void Configure(Transform scope, string[] paths, Vector2 from, float spacing=.045f, float seconds=UIMotion.RevealSeconds,float[] delays=null)
    {
        Cancel(); parts=new Part[paths.Length]; offset=from; stagger=Mathf.Max(0,spacing); duration=Mathf.Max(.01f,seconds);maximumDelay=0;
        for(int i=0;i<paths.Length;i++)
        {
            var rect=scope.Find(paths[i]) as RectTransform; if(rect==null)continue;
            var group=rect.GetComponent<CanvasGroup>();if(group==null)group=rect.gameObject.AddComponent<CanvasGroup>();
            float delay=delays!=null&&i<delays.Length?Mathf.Max(0,delays[i]):i*stagger;maximumDelay=Mathf.Max(maximumDelay,delay);
            parts[i]=new Part{rect=rect,group=group,position=rect.anchoredPosition,scale=rect.localScale,alpha=group.alpha,delay=delay};
        }
        if(Application.isPlaying&&isActiveAndEnabled)Play();
    }
    public void Play()
    {
        if(parts==null)return; age=0;lastTick=UIMotion.Realtime; IsPlaying=true; Apply();
    }
    public void Tick(float dt)
    {
        if(!IsPlaying)return; age+=Mathf.Max(0,dt); Apply();
        if(age>=duration+maximumDelay)Cancel();
    }
    private void Apply()
    {
        for(int i=0;i<parts.Length;i++)
        {
            Part p=parts[i]; if(p.rect==null)continue;
            float t=Mathf.Clamp01((age-p.delay)/duration), eased=UIMotion.Out(t);
            p.rect.anchoredPosition=p.position+offset*(1f-eased);
            p.rect.localScale=p.scale*(1f-.025f*(1f-eased)+.012f*UIMotion.Spring(t));
            p.group.alpha=p.alpha*eased;
        }
    }
    public void SampleExit(float normalized)
    {
        IsPlaying=false;if(parts==null)return;
        for(int i=0;i<parts.Length;i++)
        {
            Part p=parts[i];if(p.rect==null)continue;
            float delay=maximumDelay>0?(maximumDelay-p.delay)/maximumDelay*.12f:0;
            float t=Mathf.Clamp01((normalized-delay)/.88f),eased=UIMotion.In(t);
            p.rect.anchoredPosition=p.position-offset*eased;p.rect.localScale=p.scale*(1-.025f*eased);p.group.alpha=p.alpha*(1-UIMotion.Out(t));
        }
    }
    public void Cancel()
    {
        IsPlaying=false;
        if(parts==null)return;
        for(int i=0;i<parts.Length;i++)
        {
            Part p=parts[i]; if(p.rect==null)continue;
            p.rect.anchoredPosition=p.position;p.rect.localScale=p.scale;p.group.alpha=p.alpha;
        }
    }
    private void OnEnable(){if(Application.isPlaying)Play();}
    private void OnDisable()=>Cancel();
    private void Update(){if(!IsPlaying)return;double now=UIMotion.Realtime;if(UIMotion.IsPaused){lastTick=now;return;}Tick((float)(now-lastTick));lastTick=now;}
}
