using TMPro;
using UnityEngine;

/// <summary>One peripheral notice beneath the clock. Input and safety instructions preempt rival feedback.</summary>
public sealed class ArcadeNoticeRail : MonoBehaviour
{
    public const int DrivingGuidance=0, SafetyInstruction=4, InputInstruction=5;
    private readonly string[] notices=new string[6];
    private RectTransform panel;
    private TMP_Text label;
    private UIRevealSequence motion;
    private string shown;
    private int shownSource=-1;
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
            rail.label.textWrappingMode=TextWrappingModes.Normal;
            rail.label.fontSizeMin=20;
            rail.motion=rail.panel.gameObject.AddComponent<UIRevealSequence>();rail.motion.Configure(rail.panel,new[]{"Text"},new Vector2(8,0),0,.16f);
            rail.panel.gameObject.SetActive(false);
        }
        return rail;
    }
    // 0: driving guidance, 1: front rival, 2: rear rival, 3: confirmed effect, 4: safety, 5: input/recovery.
    public void Set(int source,string text)
    {
        if(panel==null||label==null||source<0||source>=notices.Length)return;notices[source]=text;
        string next="";int nextSource=-1;
        for(int i=notices.Length-1;i>=0;i--)if(!string.IsNullOrEmpty(notices[i])){next=notices[i];nextSource=i;break;}
        if(next==shown&&nextSource==shownSource)return;shown=next;shownSource=nextSource;label.text=next;
        var p=ArcadeUI.For(transform);bool critical=nextSource==InputInstruction;
        panel.GetComponent<ArcadeSurfaceGraphic>().color=critical?p.Accent:p.Ink;label.color=critical?p.Ink:p.Paper;
        panel.sizeDelta=new Vector2(Mathf.Clamp(label.GetPreferredValues(next).x+44,130,480),0);panel.gameObject.SetActive(next.Length>0);
        if(Application.isPlaying&&next.Length>0)motion.Play();
    }
    public void Clear()
    {
        System.Array.Clear(notices,0,notices.Length);shown="";shownSource=-1;
        if(label!=null)label.text="";if(panel!=null)panel.gameObject.SetActive(false);
    }
    private void LateUpdate(){if(Gmanager.Control!=null&&Gmanager.Control.state!=Gmanager.State.Game&&Text.Length>0)Clear();}
    public string Text=>shown??"";
}
