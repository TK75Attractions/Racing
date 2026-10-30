using TMPro;
using UnityEngine;

/// <summary>Four photographic course hints, visible together in one overview.</summary>
public sealed class TutorialGimmickGuideUI
{
    private readonly GameObject root;
    private readonly GameObject[] pages=new GameObject[TutorialGimmickBriefing.PageCount];
    private readonly TMP_Text heading,counter,next;
    private readonly UnityEngine.UI.Image progress;
    private int shownPage=-1,shownSeconds=-1;
    private bool shownReady,shownConnected,shownComplete;
    public TutorialGimmickGuideUI(Transform parent,int player)
    {
        var p=ArcadeUI.For(player);
        var panel=ArcadeUI.Panel(parent,"GimmickGuide",new Vector2(.07f,.11f),new Vector2(.93f,.86f),p.Ink);root=panel.gameObject;
        heading=TutorialText.Label(panel,"Heading","",new Vector2(.035f,.91f),new Vector2(.75f,.98f),40,p.Paper);
        counter=TutorialText.Label(panel,"Page","",new Vector2(.76f,.91f),new Vector2(.965f,.98f),27,p.Accent,true,TextAlignmentOptions.Right);
        for(int i=0;i<pages.Length;i++){var page=RacingUITheme.Rect(panel,"Page"+i,new Vector2(.025f,.14f),new Vector2(.975f,.89f));pages[i]=page.gameObject;page.gameObject.SetActive(false);}
        Card(pages[0].transform,0,"{金色|きんいろ}のジャンプ{台|だい}","まっすぐジャンプ！","ペダルをふもう。","Jump",p);
        Card(pages[0].transform,1,"{加速|かそく}パネル","ふむと{加速|かそく}！","すすむ{矢印|やじるし}がめじるし。","Acceleration",p);
        Card(pages[0].transform,2,"{光|ひか}る{箱|はこ}","ふれるとつかう！","ボタンはいらないよ。","MysteryBox",p);
        Card(pages[0].transform,3,"すべったら","ペダルをゆるめよう","{道|みち}にそってもどろう。","Oil",p);
        next=TutorialText.Label(panel,"Next","",new Vector2(.04f,.025f),new Vector2(.96f,.092f),23,p.Soft,alignment:TextAlignmentOptions.Center);
        var track=RacingUITheme.Rect(panel,"ReadProgress",new Vector2(.04f,.009f),new Vector2(.96f,.016f));progress=track.gameObject.AddComponent<UnityEngine.UI.Image>();progress.color=p.Accent;progress.raycastTarget=false;root.SetActive(false);
    }
    private static void Card(Transform parent,int index,string title,string action,string detail,string photo,ArcadeUI.Palette p)
    {
        float x=(index%2)*.51f,y=index<2?.52f:0f;
        var card=ArcadeUI.Panel(parent,"Card"+index,new Vector2(x,y),new Vector2(x+.49f,y+.48f),p.Paper);
        TutorialPhotoUI.Create(card,"Photo",photo,new Vector2(.025f,.10f),new Vector2(.43f,.90f));
        TutorialText.Label(card,"Title",title,new Vector2(.46f,.70f),new Vector2(.98f,.89f),30,p.Ink);
        var actionLabel=TutorialText.Label(card,"Action",action,new Vector2(.46f,.38f),new Vector2(.98f,.67f),32,p.Ink);actionLabel.textWrappingMode=TextWrappingModes.Normal;
        var label=TutorialText.Label(card,"Detail",detail,new Vector2(.46f,.08f),new Vector2(.98f,.34f),26,p.Ink);label.textWrappingMode=TextWrappingModes.Normal;
    }
    public void Update(TutorialGimmickBriefing guide,bool connected)
    {
        root.SetActive(guide!=null);if(guide==null)return;
        bool complete=guide.IsComplete;int page=Mathf.Min(guide.Page,pages.Length-1);
        if(shownPage!=page||shownComplete!=complete)
        {
            for(int i=0;i<pages.Length;i++)pages[i].SetActive(i==page);
            TutorialText.Set(heading,complete?"{準備|じゅんび}OK！":"コースのしかけ");
            counter.text=complete?"OK":"4つのしかけ";
            shownPage=page;shownComplete=complete;shownSeconds=-1;
        }
        int seconds=Mathf.CeilToInt(guide.SecondsRemaining);bool ready=guide.CanAdvance;
        if(shownSeconds!=seconds||shownReady!=ready||shownConnected!=connected)
        {
            if(complete)next.text="もうひとりをまとう。";
            else next.SetText(connected&&ready?"ふむとつぎへ / {0}びょうでつぎへ":"みていよう / {0}びょうでつぎへ",seconds);
            shownSeconds=seconds;shownReady=ready;shownConnected=connected;
        }
        progress.gameObject.SetActive(!complete);progress.rectTransform.anchorMax=new Vector2(.04f+.92f*Mathf.Clamp01(guide.Elapsed/TutorialGimmickBriefing.PageSeconds),.016f);
    }
}
