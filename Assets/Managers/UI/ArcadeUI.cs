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
    private static TMP_FontAsset digits,speedDigits;
    private static Material logoMaterial;
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
                if(source!=null) { digits=TMP_FontAsset.CreateFontAsset(source); digits.name="Arcade Barlow Condensed"; digits.fallbackFontAssetTable=new System.Collections.Generic.List<TMP_FontAsset>{RacingUIFontCatalog.GetHUD(true)}; }
            }
            return digits;
        }
    }
    public static TMP_FontAsset SpeedDigits
    {
        get
        {
            if(speedDigits==null)
            {
                var source=Resources.Load<Font>("UI/Arcade/BarlowCondensed-ExtraBold");
                if(source!=null){speedDigits=TMP_FontAsset.CreateFontAsset(source,120,12,UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDFAA,1024,1024);speedDigits.name="Arcade ExtraBold speed digits";}
            }
            return speedDigits??Digits;
        }
    }
    public static void HUDShadow(Transform target)
    {
        var face=target.GetComponent<ArcadeSurfaceGraphic>();if(face==null)return;
        var p=For(target);face.ShadowOffset=new Vector2(7,-7);face.ShadowColor=Color.Lerp(p.Ink,Color.black,.38f);face.SetVerticesDirty();
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
        var artwork=RacingUITheme.Rect(root,"Artwork",Vector2.zero,Vector2.one);
        var image=artwork.GetComponent<RawImage>()??artwork.gameObject.AddComponent<RawImage>();
        image.texture=Resources.Load<Texture2D>("UI/Arcade/CircuitLogo");image.color=Color.white;image.raycastTarget=false;
        var fit=artwork.GetComponent<AspectRatioFitter>()??artwork.gameObject.AddComponent<AspectRatioFitter>();fit.aspectMode=AspectRatioFitter.AspectMode.FitInParent;fit.aspectRatio=1720f/465;
        if(logoMaterial==null){var shader=Resources.Load<Shader>("UI/Arcade/ArcadeLogo");if(shader!=null)logoMaterial=new Material(shader){name="Arcade logo legibility"};}
        image.material=logoMaterial;
        return root;
    }
}
