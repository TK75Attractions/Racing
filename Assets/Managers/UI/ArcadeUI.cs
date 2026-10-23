using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>The approved arcade sport palette and artwork. Colours belong to the display owner.</summary>
public static class ArcadeUI
{
    public readonly struct Palette
    {
        public readonly Color Primary, Soft, Ink, Paper, Secondary, Accent;
        public Palette(string primary, string soft, string ink, string paper, string secondary, string accent)
        { Primary=Hex(primary); Soft=Hex(soft); Ink=Hex(ink); Paper=Hex(paper); Secondary=Hex(secondary); Accent=Hex(accent); }
    }
    public static readonly Palette Blue=new Palette("4b9df3","acd3fb","193348","f1f6fb","67d8c5","f5d474");
    public static readonly Palette Pink=new Palette("f08bb9","f9c4dd","423147","fff2f7","f4d575","adcfe9");
    private static TMP_FontAsset digits;
    public static Color Hex(string value) { ColorUtility.TryParseHtmlString("#"+value,out Color c); return c; }
    public static Palette For(int player)=>player==1?Pink:Blue;
    public static Palette For(Transform t)=>For(t!=null?t.GetComponentInParent<ArcadeDisplayTheme>(true)?.PlayerIndex??0:0);
    public static void Bind(Transform t,int player)
    { var theme=t.GetComponent<ArcadeDisplayTheme>()??t.gameObject.AddComponent<ArcadeDisplayTheme>();theme.PlayerIndex=Mathf.Clamp(player,0,1); }
    public static TMP_FontAsset Digits
    {
        get
        {
            if(digits==null)
            {
                Font source=Resources.Load<Font>("UI/Arcade/BarlowCondensed-Bold");
                if(source!=null) { digits=TMP_FontAsset.CreateFontAsset(source); digits.name="Arcade Barlow Condensed"; }
            }
            return digits;
        }
    }
    public static TMP_Text Label(Transform parent,string name,string value,Vector2 min,Vector2 max,float size,Color tint,bool numeric=false,TextAlignmentOptions alignment=TextAlignmentOptions.Left)
    {
        TMP_Text label=RacingUITheme.Label(parent,name,value,min,max,size,tint,FontRole.Japanese,alignment);
        TMP_FontAsset font=numeric?Digits:RacingUIFontCatalog.GetHUD(true);
        if(font!=null){label.font=font;label.fontSharedMaterial=font.material;}
        label.fontStyle=FontStyles.Normal;label.enableVertexGradient=false;label.characterSpacing=0;
        return label;
    }
    public static RectTransform Panel(Transform parent,string name,Vector2 min,Vector2 max,Color tint)
    {
        RectTransform r=RacingUITheme.Rect(parent,name,min,max);
        var face=r.GetComponent<ArcadeSurfaceGraphic>()??r.gameObject.AddComponent<ArcadeSurfaceGraphic>();face.color=tint;face.raycastTarget=false;return r;
    }
    public static void Place(Transform parent,string name,float x0,float y0,float x1,float y1)
    { if(parent.Find(name) is RectTransform r){r.anchorMin=new Vector2(x0,y0);r.anchorMax=new Vector2(x1,y1);r.offsetMin=r.offsetMax=Vector2.zero;} }
    public static void Hide(Transform parent,params string[] names)
    {foreach(string name in names){var t=parent.Find(name);if(t!=null)t.gameObject.SetActive(false);}}
    public static RectTransform Logo(Transform parent,Vector2 min,Vector2 max)
    {
        var root=RacingUITheme.Rect(parent,"CircuitLogo",min,max);
        var image=root.GetComponent<RawImage>()??root.gameObject.AddComponent<RawImage>();
        image.texture=Resources.Load<Texture2D>("UI/Arcade/CircuitLogo");image.color=Color.white;image.raycastTarget=false;
        var fit=root.GetComponent<AspectRatioFitter>()??root.gameObject.AddComponent<AspectRatioFitter>();fit.aspectMode=AspectRatioFitter.AspectMode.FitInParent;fit.aspectRatio=1720f/465;
        var outline=root.GetComponent<Outline>()??root.gameObject.AddComponent<Outline>();outline.effectColor=Blue.Paper;outline.effectDistance=new Vector2(2.5f,-2.5f);outline.useGraphicAlpha=true;
        Shadow shadow=null;foreach(var effect in root.GetComponents<Shadow>())if(effect.GetType()==typeof(Shadow))shadow=effect;
        if(shadow==null)shadow=root.gameObject.AddComponent<Shadow>();shadow.effectColor=new Color(0.04f,.10f,.14f,.75f);shadow.effectDistance=new Vector2(4,-5);
        return root;
    }
}
