using TMPro;
using UnityEngine;

/// <summary>Large, player-owned recovery warning, visible only during reverse travel.</summary>
public sealed class WrongWayNotice : MonoBehaviour
{
    private TMP_Text countdown;
    private RectTransform arrow;
    public static WrongWayNotice Create(Transform hud, int player)
    {
        var p = ArcadeUI.For(player);
        var panel = ArcadeUI.Panel(hud, "WrongWayNotice", new Vector2(.28f,.34f), new Vector2(.72f,.76f), p.Ink);
        var notice = panel.gameObject.AddComponent<WrongWayNotice>();
        var icon = NeonUI.Icon(panel, "TurnArrow", RacingIconGraphic.Icon.Retry, new Vector2(.42f,.60f),new Vector2(.58f,.96f));
        icon.color = p.Accent; notice.arrow = icon.rectTransform;
        ArcadeUI.Label(panel,"Heading","逆走！",new Vector2(.05f,.28f),new Vector2(.95f,.60f),90,p.Paper,true,TextAlignmentOptions.Center);
        notice.countdown = ArcadeUI.Label(panel,"Recovery","2秒でコースに戻ります",new Vector2(.04f,.08f),new Vector2(.96f,.28f),32,p.Accent,alignment:TextAlignmentOptions.Center);
        panel.gameObject.SetActive(false);
        return notice;
    }
    private void Update(){if(Gmanager.Control!=null&&Gmanager.Control.state!=Gmanager.State.Game)gameObject.SetActive(false);}
    public void SetState(bool active, float elapsed)
    {
        gameObject.SetActive(active);
        if (!active) return;
        transform.SetAsLastSibling();
        arrow.localRotation = Quaternion.Euler(0,0,-elapsed * 180f);
        countdown.SetText("{0}秒でコースに戻ります", Mathf.Max(1,Mathf.CeilToInt(2f-elapsed)));
    }
}
