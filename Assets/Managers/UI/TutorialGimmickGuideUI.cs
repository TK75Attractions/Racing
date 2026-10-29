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
        heading=TutorialText.Label(panel,"Heading","",new Vector2(.035f,.87f),new Vector2(.82f,.97f),40,p.Paper);
        counter=TutorialText.Label(panel,"Page","",new Vector2(.83f,.875f),new Vector2(.965f,.96f),27,p.Accent,true,TextAlignmentOptions.Right);
        for(int i=0;i<pages.Length;i++){var page=RacingUITheme.Rect(panel,"Page"+i,new Vector2(.025f,.18f),new Vector2(.975f,.855f));pages[i]=page.gameObject;page.gameObject.SetActive(false);}
        Card(pages[0].transform,0,"{金色|きんいろ}のジャンプ{台|だい}","まっすぐジャンプ！","ペダルをふもう。","Jump",p);
        Card(pages[0].transform,1,"{加速|かそく}パネル","ふむと{加速|かそく}！","すすむ{矢印|やじるし}がめじるし。","Acceleration",p);
        Card(pages[1].transform,0,"ドリフトで{加速|かそく}","{曲|ま}げて、もどそう！","カーブでもどすと{加速|かそく}！","Drift",p);
        Card(pages[1].transform,1,"{前|まえ}の{車|くるま}をおおう","うしろにつくと{加速|かそく}！","ぶつからずについていこう。","Slipstream",p);
        pages[2].AddComponent<TutorialItemPickupFlow>().Configure(player);
        Item(pages[3].transform,0,"Shield","シールド","まもるバリア！",p);
        Item(pages[3].transform,1,"Rocket","ロケット","じどうで{加速|かそく}！",p);
        Item(pages[3].transform,2,"Oil","オイル","うしろにオイル！",p);
        Item(pages[3].transform,3,"Confuse","コンフューズ","{前|まえ}の{車|くるま}のハンドルが{逆|ぎゃく}！",p);
        var crystal=ArcadeUI.Panel(pages[3].transform,"Crystal",new Vector2(0,.005f),new Vector2(1,.135f),p.Paper);
        TutorialPhotoUI.Create(crystal,"Photo","Crystal",new Vector2(0,0),new Vector2(.14f,1));
        TutorialText.Label(crystal,"Caption","{青|あお}いクリスタルでパワーをためよう！",new Vector2(.17f,.08f),new Vector2(.98f,.92f),25,p.Ink);
        TutorialText.Label(panel,"Safety","",new Vector2(.04f,.108f),new Vector2(.96f,.175f),24,p.Paper);
        next=TutorialText.Label(panel,"Next","",new Vector2(.04f,.025f),new Vector2(.96f,.092f),23,p.Soft,alignment:TextAlignmentOptions.Center);
        var track=RacingUITheme.Rect(panel,"ReadProgress",new Vector2(.04f,.009f),new Vector2(.96f,.016f));progress=track.gameObject.AddComponent<UnityEngine.UI.Image>();progress.color=p.Accent;progress.raycastTarget=false;root.SetActive(false);
    }
    private static void Card(Transform parent,int index,string title,string action,string detail,string photo,ArcadeUI.Palette p)
    {
        float x=index==0?0f:.515f;
        var card=ArcadeUI.Panel(parent,"Card"+index,new Vector2(x,.01f),new Vector2(x+.485f,.99f),p.Paper);
        TutorialText.Label(card,"Title",title,new Vector2(.045f,.80f),new Vector2(.96f,.91f),31,p.Ink);
        TutorialPhotoUI.Create(card,"Photo",photo,new Vector2(.025f,.37f),new Vector2(.975f,.79f));
        TutorialText.Label(card,"Action",action,new Vector2(.045f,.245f),new Vector2(.96f,.37f),31,p.Ink);
        var label=TutorialText.Label(card,"Detail",detail,new Vector2(.045f,.035f),new Vector2(.96f,.235f),26,p.Ink);label.textWrappingMode=TextWrappingModes.Normal;
    }
    private static void Item(Transform parent,int index,string photo,string title,string description,ArcadeUI.Palette p)
    {
        float x=index%2==0?0:.515f,y=index<2?.575f:.145f;
        var card=ArcadeUI.Panel(parent,"Item"+index,new Vector2(x,y),new Vector2(x+.485f,y+.405f),p.Paper);
        TutorialPhotoUI.Create(card,"Photo",photo,new Vector2(.015f,.04f),new Vector2(.43f,.96f));
        TutorialText.Label(card,"Name",title,new Vector2(.46f,.55f),new Vector2(.98f,.94f),29,p.Ink);
        var detail=TutorialText.Label(card,"Effect",description,new Vector2(.46f,.08f),new Vector2(.98f,.54f),25,p.Ink);detail.textWrappingMode=TextWrappingModes.Normal;
    }
    public void Update(TutorialGimmickBriefing guide,bool connected)
    {
        root.SetActive(guide!=null);if(guide==null)return;
        bool complete=guide.IsComplete;int page=Mathf.Min(guide.Page,pages.Length-1);
        if(shownPage!=page||shownComplete!=complete)
        {
            for(int i=0;i<pages.Length;i++)pages[i].SetActive(i==page);
            TutorialText.Set(heading,complete?"{準備|じゅんび}OK！":page==0?"ジャンプと{加速|かそく}":page==1?"なれたらチャレンジ！":page==2?"{光|ひか}る{箱|はこ}にふれよう！":"アイテムの{効果|こうか}");
            if(complete)counter.text="OK";else counter.SetText("{0} / {1}",page+1,pages.Length);
            TutorialText.Set(root.transform.Find("Safety").GetComponent<TMP_Text>(),page>=2?"すべったら、ペダルをゆるめよう。":"{道|みち}にそって{走|はし}ろう！");
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
