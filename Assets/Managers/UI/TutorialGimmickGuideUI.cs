using TMPro;
using UnityEngine;

/// <summary>Photographs of real gameplay, followed by an animated pickup walkthrough.</summary>
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
        var panel=ArcadeUI.Panel(parent,"GimmickGuide",new Vector2(.10f,.15f),new Vector2(.90f,.765f),p.Ink);root=panel.gameObject;
        heading=ArcadeUI.Label(panel,"Heading","",new Vector2(.035f,.87f),new Vector2(.82f,.97f),40,p.Paper);
        counter=ArcadeUI.Label(panel,"Page","",new Vector2(.83f,.875f),new Vector2(.965f,.96f),27,p.Accent,true,TextAlignmentOptions.Right);
        for(int i=0;i<pages.Length;i++){var page=RacingUITheme.Rect(panel,"Page"+i,new Vector2(.025f,.18f),new Vector2(.975f,.855f));pages[i]=page.gameObject;page.gameObject.SetActive(false);}
        Card(pages[0].transform,0,"金色のジャンプ台","まっすぐ進んでジャンプ！","ペダルをふんで進もう。\n空中で大きく曲げないでね。","Jump",p);
        Card(pages[0].transform,1,"チューブの加速パネル","ふむだけでスピードアップ！","回る矢印が目印だよ。\nふまなくても走れるよ。","Acceleration",p);
        Card(pages[1].transform,0,"ドリフトで加速","曲がってためて、戻そう！","カーブでためたパワーは\n少し逆に切ると加速に！","Drift",p);
        Card(pages[1].transform,1,"前の車を追いかけよう","後ろについていると加速！","少しの間、追いかけてみよう。\nぶつからないようにね。","Slipstream",p);
        pages[2].AddComponent<TutorialItemPickupFlow>().Configure(player);
        Item(pages[3].transform,0,"Shield","シールド","守って、当たるとスピン！",p);
        Item(pages[3].transform,1,"Rocket","ロケット","自動で走って超加速！",p);
        Item(pages[3].transform,2,"Oil","オイル","後ろに油。ふむとすべるよ。",p);
        Item(pages[3].transform,3,"Confuse","コンフューズ","前の車のハンドルが左右逆！",p);
        var crystal=ArcadeUI.Panel(pages[3].transform,"Crystal",new Vector2(0,.005f),new Vector2(1,.135f),p.Paper);
        TutorialPhotoUI.Create(crystal,"Photo","Crystal",new Vector2(0,0),new Vector2(.14f,1));
        ArcadeUI.Label(crystal,"Caption","青いクリスタル：パワーをためて、カーブで戻すと加速！",new Vector2(.17f,.08f),new Vector2(.98f,.92f),25,p.Ink);
        ArcadeUI.Label(panel,"Safety","",new Vector2(.04f,.108f),new Vector2(.96f,.175f),24,p.Paper);
        next=ArcadeUI.Label(panel,"Next","",new Vector2(.04f,.025f),new Vector2(.96f,.092f),23,p.Soft,alignment:TextAlignmentOptions.Center);
        var track=RacingUITheme.Rect(panel,"ReadProgress",new Vector2(.04f,.009f),new Vector2(.96f,.016f));progress=track.gameObject.AddComponent<UnityEngine.UI.Image>();progress.color=p.Accent;progress.raycastTarget=false;root.SetActive(false);
    }
    private static void Card(Transform parent,int index,string title,string action,string detail,string photo,ArcadeUI.Palette p)
    {
        float x=index==0?0f:.515f;
        var card=ArcadeUI.Panel(parent,"Card"+index,new Vector2(x,.01f),new Vector2(x+.485f,.99f),p.Paper);
        ArcadeUI.Label(card,"Title",title,new Vector2(.045f,.87f),new Vector2(.96f,.98f),31,p.Ink);
        TutorialPhotoUI.Create(card,"Photo",photo,new Vector2(.025f,.37f),new Vector2(.975f,.86f));
        ArcadeUI.Label(card,"Action",action,new Vector2(.045f,.245f),new Vector2(.96f,.37f),31,p.Ink);
        var label=ArcadeUI.Label(card,"Detail",detail,new Vector2(.045f,.035f),new Vector2(.96f,.235f),26,p.Ink);label.textWrappingMode=TextWrappingModes.Normal;
    }
    private static void Item(Transform parent,int index,string photo,string title,string description,ArcadeUI.Palette p)
    {
        float x=index%2==0?0:.515f,y=index<2?.575f:.145f;
        var card=ArcadeUI.Panel(parent,"Item"+index,new Vector2(x,y),new Vector2(x+.485f,y+.405f),p.Paper);
        TutorialPhotoUI.Create(card,"Photo",photo,new Vector2(.015f,.04f),new Vector2(.43f,.96f));
        ArcadeUI.Label(card,"Name",title,new Vector2(.46f,.55f),new Vector2(.98f,.94f),29,p.Ink);
        var detail=ArcadeUI.Label(card,"Effect",description,new Vector2(.46f,.08f),new Vector2(.98f,.54f),25,p.Ink);detail.textWrappingMode=TextWrappingModes.Normal;
    }
    public void Update(TutorialGimmickBriefing guide,bool connected)
    {
        root.SetActive(guide!=null);if(guide==null)return;
        bool complete=guide.IsComplete;int page=Mathf.Min(guide.Page,pages.Length-1);
        if(shownPage!=page||shownComplete!=complete)
        {
            for(int i=0;i<pages.Length;i++)pages[i].SetActive(i==page);
            heading.text=complete?"準備OK！アイテムを見て待とう":page==0?"ジャンプと加速パネル":page==1?"慣れたらチャレンジ！":page==2?"光る箱は、ふれるだけで自動発動！":"アイテムの効果を覚えよう";
            if(complete)counter.text="OK";else counter.SetText("{0} / {1}",page+1,pages.Length);
            root.transform.Find("Safety").GetComponent<TMP_Text>().text=page>=2?"すべったらペダルをゆるめよう。赤い画面はハンドルを戻してね。":"むずかしければ、道にそって走るだけでもOK！";
            shownPage=page;shownComplete=complete;shownSeconds=-1;
        }
        int seconds=Mathf.CeilToInt(guide.SecondsRemaining);bool ready=guide.CanAdvance;
        if(shownSeconds!=seconds||shownReady!=ready||shownConnected!=connected)
        {
            if(complete)next.text="もう一人の準備がそろったらレースへ";
            else next.SetText(connected&&ready?"ペダルを離して、ふむと次へ  /  自動で次へ {0}秒":"読むだけでOK  /  自動で次へ {0}秒",seconds);
            shownSeconds=seconds;shownReady=ready;shownConnected=connected;
        }
        progress.gameObject.SetActive(!complete);progress.rectTransform.anchorMax=new Vector2(.04f+.92f*Mathf.Clamp01(guide.Elapsed/TutorialGimmickBriefing.PageSeconds),.016f);
    }
}
