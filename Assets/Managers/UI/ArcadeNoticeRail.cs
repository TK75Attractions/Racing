using TMPro;
using UnityEngine;

/// <summary>One quiet line beneath the clock. Confirmed item effects outrank proximity and jump hints.</summary>
public sealed class ArcadeNoticeRail : MonoBehaviour
{
    private readonly string[] notices=new string[4];
    private RectTransform panel;
    private TMP_Text label;
    private string shown;
    public static ArcadeNoticeRail Get(Transform canvas)
    {
        var rail=canvas.GetComponent<ArcadeNoticeRail>()??canvas.gameObject.AddComponent<ArcadeNoticeRail>();
        if(rail.panel==null)
        {
            var p=ArcadeUI.For(canvas);
            rail.panel=ArcadeUI.Panel(canvas,"QuietRaceNotice",new Vector2(.92f,.683f),new Vector2(.92f,.738f),p.Ink);
            rail.panel.pivot=new Vector2(1,.5f);rail.panel.sizeDelta=new Vector2(250,0);
            RacingUITheme.Rule(rail.panel,"Accent",new Vector2(0,.25f),new Vector2(.007f,.75f),p.Primary);
            rail.label=ArcadeUI.Label(rail.panel,"Text","",new Vector2(.045f,.1f),new Vector2(.96f,.90f),22,p.Paper,false,TextAlignmentOptions.Right);
            rail.panel.gameObject.SetActive(false);
        }
        return rail;
    }
    // 0: jump, 1: front rival, 2: rear rival, 3: confirmed effect.
    public void Set(int source,string text)
    {
        if(panel==null||label==null||source<0||source>=notices.Length)return;notices[source]=text;
        string next="";for(int i=notices.Length-1;i>=0;i--)if(!string.IsNullOrEmpty(notices[i])){next=notices[i];break;}
        if(next==shown)return;shown=next;label.text=next;panel.sizeDelta=new Vector2(Mathf.Clamp(label.GetPreferredValues(next).x+44,130,480),0);panel.gameObject.SetActive(next.Length>0);
    }
    public void Clear()
    {
        System.Array.Clear(notices,0,notices.Length);shown="";
        if(label!=null)label.text="";if(panel!=null)panel.gameObject.SetActive(false);
    }
    private void LateUpdate(){if(Gmanager.Control!=null&&Gmanager.Control.state!=Gmanager.State.Game&&Text.Length>0)Clear();}
    public string Text=>shown??"";
}
