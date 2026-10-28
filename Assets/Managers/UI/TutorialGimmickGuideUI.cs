using TMPro;
using UnityEngine;

/// <summary>Three calm, illustrated pages shown only after the practice car has stopped.</summary>
public sealed class TutorialGimmickGuideUI
{
    private readonly GameObject root;
    private readonly GameObject[] pages = new GameObject[TutorialGimmickBriefing.PageCount];
    private readonly TMP_Text heading, counter, next;
    private readonly UnityEngine.UI.Image progress;
    private int shownPage = -1, shownSeconds = -1;
    private bool shownReady, shownConnected;

    public TutorialGimmickGuideUI(Transform parent, int player)
    {
        var p = ArcadeUI.For(player);
        var panel = ArcadeUI.Panel(parent, "GimmickGuide", new Vector2(.12f,.17f), new Vector2(.88f,.765f), p.Ink);
        root = panel.gameObject;
        heading = ArcadeUI.Label(panel,"Heading","",new Vector2(.035f,.87f),new Vector2(.82f,.97f),40,p.Paper);
        counter = ArcadeUI.Label(panel,"Page","",new Vector2(.83f,.875f),new Vector2(.965f,.96f),27,p.Accent,true,TextAlignmentOptions.Right);
        for (int i=0; i<pages.Length; i++)
        {
            var page=RacingUITheme.Rect(panel,"Page"+i,new Vector2(.025f,.18f),new Vector2(.975f,.855f));pages[i]=page.gameObject;
            page.gameObject.SetActive(false);
        }
        Card(pages[0].transform,0,"金色のジャンプ台","まっすぐ進んでジャンプ！","ペダルをふんで進もう。\n空中で大きく曲げないでね。",TutorialGimmickGraphic.Kind.Jump,p);
        Card(pages[0].transform,1,"チューブの光るライン","走り切るとチャージ！","加速のパワーがたまるよ。\nふつうに走り抜けてもOK。",TutorialGimmickGraphic.Kind.Tube,p);
        Card(pages[1].transform,0,"ドリフトで加速","曲がってためて、戻そう！","カーブでためたパワーは\n少し逆に切ると加速に！",TutorialGimmickGraphic.Kind.Drift,p);
        Card(pages[1].transform,1,"前の車を追いかけよう","後ろについていると加速！","少しの間、追いかけてみよう。\nぶつからないようにね。",TutorialGimmickGraphic.Kind.Slipstream,p);
        var items=ItemGuideUI.Create(pages[2].transform,player);
        var itemRect=(RectTransform)items.transform;itemRect.anchorMin=Vector2.zero;itemRect.anchorMax=Vector2.one;itemRect.offsetMin=itemRect.offsetMax=Vector2.zero;
        // The common page title already explains automatic use; use this space for the five rows.
        ArcadeUI.Hide(items.transform,"Heading","Hint");
        foreach (string name in new[]{"Shield","Rocket","Oil","Confuse","Charge"})
        {
            var row=items.transform.Find(name) as RectTransform;
            row.anchorMin=new Vector2(.035f,row.anchorMin.y*1.12f+.045f);row.anchorMax=new Vector2(.965f,row.anchorMax.y*1.12f+.045f);
            Place(row.Find("Icon"),new Vector2(.01f,.15f),new Vector2(.09f,.85f));
            Place(row.Find("Name"),new Vector2(.12f,.08f),new Vector2(.38f,.92f));
            Place(row.Find("Effect"),new Vector2(.40f,.08f),new Vector2(.98f,.92f));
            foreach(var label in row.GetComponentsInChildren<TMP_Text>()) { label.fontSize=30;label.fontSizeMax=30;label.fontSizeMin=24; }
        }
        ArcadeUI.Label(panel,"Safety","むずかしければ、道にそって走るだけでもOK！",new Vector2(.04f,.108f),new Vector2(.96f,.175f),24,p.Paper);
        next=ArcadeUI.Label(panel,"Next","",new Vector2(.04f,.025f),new Vector2(.96f,.092f),23,p.Soft,alignment:TextAlignmentOptions.Center);
        var track=RacingUITheme.Rect(panel,"ReadProgress",new Vector2(.04f,.009f),new Vector2(.96f,.016f));
        progress=track.gameObject.AddComponent<UnityEngine.UI.Image>();progress.color=p.Accent;progress.raycastTarget=false;
        root.SetActive(false);
    }
    private static void Place(Transform target,Vector2 min,Vector2 max)
    { var r=(RectTransform)target;r.anchorMin=min;r.anchorMax=max;r.offsetMin=r.offsetMax=Vector2.zero; }
    private static void Card(Transform parent,int index,string title,string action,string detail,TutorialGimmickGraphic.Kind kind,ArcadeUI.Palette p)
    {
        float x=index==0?0f:.515f;
        var card=ArcadeUI.Panel(parent,"Card"+index,new Vector2(x,.01f),new Vector2(x+.485f,.99f),p.Paper);
        ArcadeUI.Label(card,"Title",title,new Vector2(.045f,.84f),new Vector2(.96f,.98f),31,p.Ink);
        var art=RacingUITheme.Rect(card,"Diagram",new Vector2(.025f,.37f),new Vector2(.975f,.84f)).gameObject.AddComponent<TutorialGimmickGraphic>();
        art.Illustration=kind;art.color=p.Primary;art.raycastTarget=false;
        ArcadeUI.Label(card,"Action",action,new Vector2(.045f,.245f),new Vector2(.96f,.38f),31,p.Ink);
        var description=ArcadeUI.Label(card,"Detail",detail,new Vector2(.045f,.035f),new Vector2(.96f,.235f),26,p.Ink);
        description.textWrappingMode=TextWrappingModes.Normal;
    }
    public void Update(TutorialGimmickBriefing guide,bool connected)
    {
        bool active=guide!=null&&!guide.IsComplete;root.SetActive(active);if(!active)return;
        int page=guide.Page;
        if(shownPage!=page)
        {
            for(int i=0;i<pages.Length;i++)pages[i].SetActive(i==page);
            heading.text=page==0?"コースのしかけを知ろう":page==1?"慣れたらチャレンジ！":"光る箱は、ふれるだけで自動発動！";
            counter.SetText("{0} / 3",page+1);
            var safety=root.transform.Find("Safety").GetComponent<TMP_Text>();
            safety.text=page==2?"赤い画面はハンドルの回しすぎ。すべったらペダルをゆるめよう。":"むずかしければ、道にそって走るだけでもOK！";
            shownPage=page;
        }
        int seconds=Mathf.CeilToInt(guide.SecondsRemaining);bool ready=guide.CanAdvance;
        if(shownSeconds!=seconds||shownReady!=ready||shownConnected!=connected)
        {
            next.SetText(connected&&ready?"ペダルを離して、ふむと次へ  /  自動で次へ {0}秒":"読むだけでOK  /  自動で次へ {0}秒",seconds);
            shownSeconds=seconds;shownReady=ready;shownConnected=connected;
        }
        progress.rectTransform.anchorMax=new Vector2(.04f+.92f*Mathf.Clamp01(guide.Elapsed/TutorialGimmickBriefing.PageSeconds),.016f);
    }
}
