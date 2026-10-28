using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>A per-display thank-you screen. Leaving it never dismisses the other player's results.</summary>
public sealed class ArcadeEndingUI : MonoBehaviour
{
    private Action completed;
    private GameObject root;
    private bool armed;
    private float held;
    private PedalButtonFeedback feedback;
    public bool IsVisible=>root!=null&&root.activeSelf;
    public static ArcadeEndingUI Get(Transform parent)
    {
        var ui=parent.GetComponent<ArcadeEndingUI>()??parent.gameObject.AddComponent<ArcadeEndingUI>();
        if(ui.root==null)
        {
            var p=ArcadeUI.For(parent);var r=RacingUITheme.Rect(parent,"Ending",Vector2.zero,Vector2.one);ui.root=r.gameObject;
            var blocker=r.gameObject.AddComponent<Image>();blocker.color=new Color(p.Ink.r,p.Ink.g,p.Ink.b,.15f);blocker.raycastTarget=true;
            ArcadeUI.Logo(r,new Vector2(.33f,.71f),new Vector2(.67f,.88f));
            var thanks=ArcadeUI.Panel(r,"Thanks",new Vector2(.29f,.27f),new Vector2(.71f,.67f),p.Ink);
            var label=ArcadeUI.Label(thanks,"Message","プレイありがとう\nございました！",new Vector2(.06f,.29f),new Vector2(.94f,.92f),68,p.Paper,false,TextAlignmentOptions.Center);label.textWrappingMode=TextWrappingModes.Normal;
            ArcadeUI.Label(thanks,"Again","また遊びにきてね。",new Vector2(.08f,.08f),new Vector2(.92f,.27f),25,p.Soft,false,TextAlignmentOptions.Center);
            var back=NeonUI.Button(r,"Back","タイトルへ戻る","",RacingIconGraphic.Icon.Home,new Vector2(.38f,.115f),new Vector2(.62f,.215f),true,ui.Finish);back.ConfigureHold(.65f,false);ui.feedback=back.GetComponent<PedalButtonFeedback>();
            var entrance=r.gameObject.AddComponent<UIRevealSequence>();entrance.Configure(r,new[]{"CircuitLogo","Thanks"},new Vector2(0,-20),.045f,.30f);
            r.gameObject.SetActive(false);
        }
        return ui;
    }
    public void Show(Action done){completed=done;armed=false;held=0;ArcadeUI.Hide(transform,"Result","Title","OnPlay");root.SetActive(true);root.transform.SetAsLastSibling();}
    public void TickInput(float pedal,float dt)
    {
        if(!IsVisible)return;if(Mathf.Abs(pedal)<=.05f)armed=true;
        held=armed&&pedal>=.8f?held+Mathf.Max(0,dt):0;feedback.SetState(true,Mathf.Clamp01(held/.65f),ArcadeUI.For(transform).Primary);
        if(held>=.65f)Finish();
    }
    private void Finish(){if(!IsVisible)return;root.SetActive(false);var done=completed;completed=null;done?.Invoke();}
}
