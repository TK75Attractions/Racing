using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using TMPro;
using UnityEngine;

/// <summary>Place readings above annotated kanji, e.g. {道|みち}.</summary>
public sealed class TutorialText : MonoBehaviour
{
    private sealed class Reading { public int first, length; public TMP_Text label; }
    private readonly List<Reading> readings = new List<Reading>();
    private TMP_Text target;
    private string shown;
    private static readonly Regex Annotation = new Regex(@"\{([^{}|]+)\|([^{}|]+)\}");
    public static TMP_Text Label(Transform parent,string name,string text,Vector2 min,Vector2 max,float size,Color color,bool bold=false,TextAlignmentOptions alignment=TextAlignmentOptions.MidlineLeft)
    {
        var label=ArcadeUI.Label(parent,name,"",min,max,size,color,bold,alignment);
        Set(label,text);return label;
    }
    public static void Set(TMP_Text label,string text)
    {
        var owner=label.GetComponent<TutorialText>()??label.gameObject.AddComponent<TutorialText>();
        owner.Apply(label,text);
    }
    private void Apply(TMP_Text label,string text)
    {
        if(shown==text)return;shown=text;
        if(target==null){target=label;target.OnPreRenderText+=Layout;}
        foreach(var reading in readings)ItemVisualFactory.DestroyObject(reading.label.gameObject);readings.Clear();
        var plain=new StringBuilder();int previous=0;
        foreach(Match match in Annotation.Matches(text))
        {
            plain.Append(text,previous,match.Index-previous);
            var node=new GameObject("ふりがな"+readings.Count,typeof(RectTransform),typeof(TextMeshProUGUI));
            var rect=node.GetComponent<RectTransform>();rect.SetParent(transform,false);rect.anchorMin=rect.anchorMax=new Vector2(.5f,.5f);rect.pivot=new Vector2(.5f,0f);
            var ruby=node.GetComponent<TextMeshProUGUI>();
            ruby.font=label.font;ruby.fontSize=label.fontSize*.4f;ruby.enableAutoSizing=false;ruby.color=label.color;ruby.raycastTarget=false;ruby.alignment=TextAlignmentOptions.Center;
            ruby.textWrappingMode=TextWrappingModes.NoWrap;ruby.overflowMode=TextOverflowModes.Overflow;ruby.text=match.Groups[2].Value;
            readings.Add(new Reading{first=plain.Length,length=match.Groups[1].Length,label=ruby});
            plain.Append(match.Groups[1].Value);previous=match.Index+match.Length;
        }
        plain.Append(text,previous,text.Length-previous);label.text=plain.ToString();label.lineSpacing=18f;
    }
    private void Layout(TMP_TextInfo info)
    {
        foreach(var reading in readings)
        {
            if(reading.first+reading.length>info.characterCount)continue;
            var first=info.characterInfo[reading.first];var last=info.characterInfo[reading.first+reading.length-1];float size=Mathf.Min(target.fontSize,first.pointSize)*.4f;
            reading.label.fontSize=size;
            // Leave room for SDF glyph overhang and pixel rounding at scaled output sizes.
            reading.label.rectTransform.sizeDelta=new Vector2(Mathf.Max(last.topRight.x-first.bottomLeft.x,size*reading.label.text.Length)+2f,size*1.6f+2f);
            reading.label.rectTransform.anchoredPosition=new Vector2((first.bottomLeft.x+last.topRight.x)*.5f,Mathf.Max(first.topLeft.y,last.topLeft.y)+size*.12f);
        }
    }
    private void LateUpdate(){if(target!=null&&readings.Count>0)Layout(target.textInfo);}
    private void OnDestroy(){if(target!=null)target.OnPreRenderText-=Layout;}
}
