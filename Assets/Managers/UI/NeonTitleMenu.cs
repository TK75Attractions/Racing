using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Title controls and matching, usable selection/help/settings sheets.</summary>
public sealed class NeonTitleMenu : MonoBehaviour
{
    public enum Page { Cars, Settings, Help, Course }
    private Transform title;
    private RectTransform modal;
    private TMP_Text heading, body;
    private RacingMenuButton action;
    private int player;
    public bool IsOpen => modal != null && modal.gameObject.activeSelf;

    public void Build(Transform parent, int playerIndex)
    {
        title = parent; player = playerIndex;
        string[] labels = { "車の選択", "設定", "遊び方", "終了" };
        string[] english = { "SELECT CAR", "SETTINGS", "HOW TO PLAY", "EXIT" };
        RacingIconGraphic.Icon[] icons = { RacingIconGraphic.Icon.Car, RacingIconGraphic.Icon.Gear, RacingIconGraphic.Icon.Book, RacingIconGraphic.Icon.Power };
        for(int i=0;i<4;i++)
        {
            int option=i; float top=.627f-i*.114f;
            RacingMenuButton button=NeonUI.Button(title,"MenuOption"+i,labels[i],english[i],icons[i],
                new Vector2(.678f,top-.098f),new Vector2(.970f,top),false,
                () => { if(option==3) Exit();else Open((Page)option); });
            if(i==3)
            {
                RacingPanelGraphic surface=button.transform.Find("ModernSurface").GetComponent<RacingPanelGraphic>();
                surface.Configure(RacingPanelGraphic.SurfaceStyle.Danger, NeonUI.Red);
                button.Configure(surface);
                button.ConfigureHold(.65f,false);
            }
        }
        modal = RacingUITheme.Rect(title,"MenuSheet",Vector2.zero,Vector2.one);
        Image dim=modal.GetComponent<Image>() ?? modal.gameObject.AddComponent<Image>(); dim.color=new Color(.001f,.005f,.025f,.84f); dim.raycastTarget=true;
        RectTransform card=NeonUI.Panel(modal,"Card",new Vector2(.24f,.20f),new Vector2(.76f,.80f));
        heading=NeonUI.Text(card,"Title","",new Vector2(.07f,.79f),new Vector2(.93f,.94f),44f);
        body=NeonUI.Text(card,"Description","",new Vector2(.075f,.29f),new Vector2(.925f,.76f),27f);
        body.textWrappingMode=TextWrappingModes.Normal; body.overflowMode=TextOverflowModes.Overflow;
        action=NeonUI.Button(card,"Action","決定","CONFIRM",RacingIconGraphic.Icon.Flag,new Vector2(.07f,.075f),new Vector2(.59f,.23f),true,Close);
        NeonUI.Button(card,"Back","戻る","BACK",RacingIconGraphic.Icon.Back,new Vector2(.62f,.075f),new Vector2(.93f,.23f),false,Close);
        Close();
    }
    public void Open(Page page)
    {
        if(modal==null) return;
        modal.gameObject.SetActive(true); modal.SetAsLastSibling(); action.onClick.RemoveAllListeners();
        string caption="決定";
        switch(page)
        {
            case Page.Cars:
                heading.text="車の選択 / SELECT CAR";
                body.text="SPORT CAR\n\nプレイヤー " +(player+1)+" のレーシングカー\n選択中の車でレースに参加します。";
                caption="この車で決定"; action.onClick.AddListener(Close); break;
            case Page.Course:
                heading.text="コース選択 / COURSE";
                body.text="つくこまサーキット\nTsukukoma Circuit\n\n" + (Gmanager.Control != null ? Gmanager.Control.GoalLap : 3) + " 周のレース";
                caption="このコースで決定"; action.onClick.AddListener(Close); break;
            case Page.Settings:
                heading.text="設定 / SETTINGS";
                SetSoundText(); caption="音を切り替える";
                action.onClick.AddListener(() => { AudioListener.volume=AudioListener.volume>.01f?0f:1f; SetSoundText(); }); break;
            default:
                heading.text="遊び方 / HOW TO PLAY";
                body.text="1. ペダルを踏んで準備完了\n2. 2 人の準備ができたらスタート\n3. ハンドルで曲がり、ペダルで加速\n4. コースを周回してゴール！\n\nEsc キーでレース中のメニュー";
                caption="わかった！"; action.onClick.AddListener(Close); break;
        }
        action.transform.Find("Label").GetComponent<TMP_Text>().text=caption;
    }
    private void SetSoundText() => body.text="サウンド / SOUND\n\n"+(AudioListener.volume>.01f?"ON / 音あり":"OFF / 音なし");
    public void Close() { if(modal!=null) modal.gameObject.SetActive(false); }
    private void OnDisable() => Close();
    private void Exit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying=false;
#else
        Application.Quit();
#endif
    }
}
