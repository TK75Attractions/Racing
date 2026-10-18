using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>One animation owner for pointer, keyboard and pedal buttons. Settles to zero rebuilds.</summary>
[DisallowMultipleComponent]
public sealed class PedalButtonFeedback : MonoBehaviour
{
    private RectTransform rect;
    private RacingPanelGraphic surface;
    private Vector2 basePosition;
    private Vector3 baseScale;
    private Quaternion baseRotation;
    private struct Content { public RectTransform rect; public Vector2 position; public Graphic graphic; public Color tint; public bool chevron; }
    private readonly List<Content> content = new List<Content>(8);
    private RacingButtonGaugeGraphic gauge;
    private TMP_Text percentage;
    private RacingButtonFXGraphic fx;
    private bool selected, pointerFocused, pointerPressed, available=true, confirmed, retainStart, dirty=true;
    private float progress, pointerProgress, focus, press, shownProgress, highlightAge=10f, confirmAge=10f;
    private Color accent;
    private int displayedPercent=-1;
    public bool IsRetained => confirmed && retainStart;
    public bool HasGauge => gauge != null;

    public void Bind(RacingPanelGraphic value)
    {
        if(rect==null)
        {
            rect=GetComponent<RectTransform>();basePosition=rect.anchoredPosition;baseScale=rect.localScale;baseRotation=rect.localRotation;
        }
        surface=value;accent=value.Accent;
        RectTransform effect=RacingUITheme.Rect(transform,"InteractionFX",Vector2.zero,Vector2.one);
        fx=effect.GetComponent<RacingButtonFXGraphic>() ?? effect.gameObject.AddComponent<RacingButtonFXGraphic>();
        effect.SetSiblingIndex(1);CacheContent();dirty=true;
    }
    public void Configure(Color color,RacingPanelGraphic.SurfaceStyle style=RacingPanelGraphic.SurfaceStyle.Primary)
    {
        Image image=GetComponent<Image>();if(image!=null)image.enabled=false;
        Outline outline=GetComponent<Outline>();if(outline!=null)outline.enabled=false;
        RectTransform face=RacingUITheme.Rect(transform,"ButtonSurface",Vector2.zero,Vector2.one);face.SetAsFirstSibling();
        PedalButtonSurface graphic=face.GetComponent<PedalButtonSurface>() ?? face.gameObject.AddComponent<PedalButtonSurface>();
        graphic.Configure(style,color);Bind(graphic);selected=false;confirmed=false;progress=pointerProgress=0;dirty=true;
    }
    public void CacheContent()
    {
        if(content.Count>0)RestoreContent();content.Clear();
        foreach(string name in new[]{"Icon","Chevron","Label","Caption","Index","BackIcon","DirectionStatus"})
        {
            RectTransform child=transform.Find(name) as RectTransform;if(child==null)continue;
            Graphic graphic=child.GetComponent<Graphic>();
            content.Add(new Content{rect=child,position=child.anchoredPosition,graphic=graphic,tint=graphic!=null?graphic.color:Color.white,chevron=name=="Chevron"});
        }
    }
    public void EnableGauge(bool keepSelected)
    {
        retainStart=keepSelected;
        RectTransform bar=RacingUITheme.Rect(transform,"HoldGauge",new Vector2(.075f,.085f),new Vector2(.78f,.27f));
        gauge=bar.GetComponent<RacingButtonGaugeGraphic>() ?? bar.gameObject.AddComponent<RacingButtonGaugeGraphic>();
        percentage=NeonUI.Text(transform,"HoldPercent","0%",new Vector2(.79f,.08f),new Vector2(.955f,.29f),20f,TextAlignmentOptions.Right,true);
        SetAnchors("Label",new Vector2(.28f,.51f),new Vector2(.87f,.96f));
        SetAnchors("Caption",new Vector2(.285f,.29f),new Vector2(.87f,.51f));
        SetAnchors("Icon",new Vector2(.085f,.40f),new Vector2(.235f,.90f));
        SetAnchors("Chevron",new Vector2(.88f,.48f),new Vector2(.955f,.88f));
        displayedPercent=-1;CacheContent();dirty=true;ApplyVisual(0,0);
    }
    private void SetAnchors(string name,Vector2 min,Vector2 max)
    {
        RectTransform child=transform.Find(name) as RectTransform;if(child==null)return;
        child.anchorMin=min;child.anchorMax=max;child.offsetMin=child.offsetMax=Vector2.zero;
    }
    public void SetState(bool isSelected,float amount,Color color)
    {
        amount=Mathf.Clamp01(amount);
        if(selected==isSelected && progress==amount)return;
        if(isSelected && !selected)highlightAge=0;
        selected=isSelected;progress=amount;dirty=true;
    }
    public void SetPointerState(bool focused,bool pressed,bool interactable)
    {
        if(pointerFocused==focused && pointerPressed==pressed && available==interactable)return;
        if(focused && !pointerFocused)highlightAge=0;
        pointerFocused=focused;pointerPressed=pressed;available=interactable;dirty=true;
    }
    public void SetPointerCharge(float amount)
    {amount=Mathf.Clamp01(amount);if(pointerProgress==amount)return;pointerProgress=amount;dirty=true;}
    public void SetConfirmed(bool value)
    {
        if(confirmed==value)return;confirmed=value;dirty=true;if(value)PlayConfirm();
    }
    public void PlayConfirm(){confirmAge=0;dirty=true;}
    private void Update()=>Tick(Time.unscaledDeltaTime);
    public void Tick(float deltaTime)
    {
        if(rect==null || surface==null)return;
        bool retained=IsRetained && available;
        float targetFocus=available && (selected || pointerFocused || retained)?1f:0f;
        float targetPress=available && !retained && (pointerPressed || progress>0f || pointerProgress>0f || confirmAge<.10f)?1f:0f;
        float targetProgress=retained?1f:available?Mathf.Max(progress,pointerProgress):0f;
        bool moving=focus!=targetFocus || press!=targetPress || shownProgress!=targetProgress || highlightAge<.52f || confirmAge<.48f;
        if(!dirty && !moving)return;
        highlightAge+=deltaTime;confirmAge+=deltaTime;
        focus=Mathf.MoveTowards(focus,targetFocus,deltaTime*10f);
        press=Mathf.MoveTowards(press,targetPress,deltaTime*14f);
        // The gauge follows actual hold time without delaying the MAX confirmation.
        shownProgress=targetProgress;
        float flash=confirmAge<.48f?Mathf.Sin(Mathf.Clamp01(confirmAge/.48f)*Mathf.PI):0f;
        float spring=highlightAge<.52f?Mathf.Sin(highlightAge*24f)*Mathf.Exp(-highlightAge*9f)*focus:0f;
        ApplyVisual(flash,spring);
        dirty=false;
    }
    private void ApplyVisual(float flash,float spring)
    {
        bool retained=IsRetained && available;
        Color tint=retained?RacingUITheme.Gold:surface.Style==RacingPanelGraphic.SurfaceStyle.Danger?NeonUI.Red:surface.Style==RacingPanelGraphic.SurfaceStyle.Primary?NeonUI.Pink:accent;
        surface.SetButtonModifiers(!available,retained);
        surface.SetState(focus,press,flash,tint);
        float scale=1f+focus*.012f+spring*.023f+flash*.020f-press*.017f;
        Vector3 size=Vector3.Scale(baseScale,new Vector3(scale,scale,1));
        Vector2 position=basePosition+Vector2.down*(press*1.5f);
        if(rect.anchoredPosition!=position)rect.anchoredPosition=position;
        if(rect.localScale!=size)rect.localScale=size;
        if(rect.localRotation!=baseRotation)rect.localRotation=baseRotation;
        foreach(Content item in content)
        {
            Vector2 offset=Vector2.down*(press*2.5f)+(item.chevron?Vector2.right*(focus*2.5f):Vector2.zero);
            if(item.rect.anchoredPosition!=item.position+offset)item.rect.anchoredPosition=item.position+offset;
            if(item.graphic!=null)
            {
                Color color=available?item.tint:new Color(.55f,.59f,.67f,.72f);
                if(item.graphic.color!=color)item.graphic.color=color;
            }
        }
        if(gauge!=null)
        {
            gauge.SetVisual(shownProgress,tint,flash,!available);
            int percent=Mathf.RoundToInt(shownProgress*100f);
            if(displayedPercent!=percent){percentage.SetText("{0}%",percent);displayedPercent=percent;}
            Color c=available?(retained?RacingUITheme.Gold:Color.white):new Color(.55f,.59f,.67f,.72f);
            if(percentage.color!=c)percentage.color=c;
        }
        float sweep=available && highlightAge<.44f?Mathf.Clamp01(highlightAge/.44f):0;
        fx?.SetEffect(sweep,available?flash:0,tint);
    }
    private void RestoreContent()
    {
        foreach(Content item in content)
        {
            if(item.rect!=null)item.rect.anchoredPosition=item.position;
            if(item.graphic!=null)item.graphic.color=item.tint;
        }
    }
    private void OnDisable()
    {
        if(rect==null)return;
        rect.anchoredPosition=basePosition;rect.localScale=baseScale;rect.localRotation=baseRotation;RestoreContent();
        selected=pointerFocused=pointerPressed=confirmed=false;progress=pointerProgress=focus=press=shownProgress=0;
        highlightAge=confirmAge=10f;dirty=true;displayedPercent=-1;
        surface?.SetButtonModifiers(false,false);surface?.SetState(0,0,0,accent);
        gauge?.SetVisual(0,accent,0,false);fx?.SetEffect(0,0,accent);
    }
#if UNITY_EDITOR
    public void Preview(float focusValue,float pressValue,float charge,bool waiting,bool disabled,float burst=0)
    {
        focus=disabled?0:focusValue;press=disabled?0:pressValue;shownProgress=disabled?0:charge;available=!disabled;confirmed=waiting;retainStart=waiting||retainStart;
        highlightAge=10;confirmAge=10;ApplyVisual(burst,0);dirty=false;
    }
#endif
}
