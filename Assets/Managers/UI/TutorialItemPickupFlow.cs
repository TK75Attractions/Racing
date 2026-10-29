using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>The real box, real pickup and actual effect photos, highlighted in order.</summary>
public sealed class TutorialItemPickupFlow : MonoBehaviour
{
    private readonly Image[] borders=new Image[3];
    private readonly RectTransform[] arrows=new RectTransform[2];
    private readonly Texture2D[] effects=new Texture2D[4];
    private readonly string[] titles={"シールドで守る！","ロケットで超加速！","オイルですべる！","前の車のハンドル反転！"};
    private RawImage result,contact;
    private Texture2D pickupBefore,pickupAfter;
    private int shownItem=-1;
    private TMP_Text resultLabel;
    private ArcadeUI.Palette palette;
    private float elapsed;
    private bool configured;
    public int HighlightedStep {get;private set;}
    public void Configure(int player)
    {
        palette=ArcadeUI.For(player);
        string[] names={"MysteryBox","Pickup","Shield"};
        string[] labels={"1. 箱にふれる","2. ふれると消える","3. 自動で発動！"};
        for(int i=0;i<3;i++)
        {
            float x=i*.345f;
            var frame=RacingUITheme.Rect(transform,"FlowStep"+i,new Vector2(x,.14f),new Vector2(x+.31f,.98f));
            borders[i]=frame.gameObject.AddComponent<Image>();borders[i].raycastTarget=false;
            var picture=TutorialPhotoUI.Create(frame,"Photo",names[i],new Vector2(.025f,.25f),new Vector2(.975f,.975f));
            var label=ArcadeUI.Label(frame,"Caption",labels[i],new Vector2(.04f,.02f),new Vector2(.96f,.23f),28,palette.Ink,alignment:TextAlignmentOptions.Center);
            if(i==1)contact=picture;
            if(i==2){result=picture;resultLabel=label;}
            if(i<2)
            {
                float a=x+.312f;
                var icon=NeonUI.Icon(transform,"NextArrow"+i,RacingIconGraphic.Icon.DoubleChevron,new Vector2(a,.45f),new Vector2(a+.033f,.64f));
                icon.color=palette.Accent;arrows[i]=icon.rectTransform;
            }
        }
        string[] keys={"Shield","Rocket","Oil","Confuse"};for(int i=0;i<keys.Length;i++)effects[i]=Resources.Load<Texture2D>("UI/TutorialPhotos/"+keys[i]);
        ArcadeUI.Label(transform,"Automatic","ボタンはいらないよ。何が出るかはお楽しみ！",new Vector2(.02f,.01f),new Vector2(.98f,.12f),28,palette.Paper,alignment:TextAlignmentOptions.Center);
        pickupBefore=Resources.Load<Texture2D>("UI/TutorialPhotos/Pickup");pickupAfter=Resources.Load<Texture2D>("UI/TutorialPhotos/PickupAfter");
        configured=true;SetPhase(0f);
    }
    private void OnEnable(){elapsed=0f;if(configured)SetPhase(0f);}
    private void Update(){if(!configured||UIMotion.IsPaused)return;elapsed+=UIMotion.DeltaTime;SetPhase(elapsed);}
    public void SetPhase(float seconds)
    {
        if(!configured)return;
        float cycle=Mathf.Repeat(seconds,2.5f)/2.5f;HighlightedStep=Mathf.Min(2,(int)(cycle*3));
        int item=(int)(seconds/2.5f)%4;if(shownItem!=item){result.texture=effects[item];resultLabel.text="3. "+titles[item];shownItem=item;}
        contact.texture=cycle<.46f?pickupBefore:pickupAfter;
        for(int i=0;i<3;i++)borders[i].color=i==HighlightedStep?palette.Accent:palette.Paper;
        for(int i=0;i<2;i++)arrows[i].anchoredPosition=new Vector2(Mathf.Sin(seconds*3f)*3f,0f);
    }
}
