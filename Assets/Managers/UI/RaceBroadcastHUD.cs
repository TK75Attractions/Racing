using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class RaceBroadcastHUD
{
    private readonly TMP_Text mode,caption,standings;
    public RaceBroadcastHUD(Transform parent,RenderTexture feed)
    {
        var video=RacingUITheme.Rect(parent,"BroadcastVideo",Vector2.zero,Vector2.one);
        var image=video.gameObject.AddComponent<RawImage>();image.texture=feed;image.raycastTarget=false;
        var fit=video.gameObject.AddComponent<AspectRatioFitter>();fit.aspectMode=AspectRatioFitter.AspectMode.FitInParent;fit.aspectRatio=16f/9f;
        var badge=NeonUI.Panel(parent,"BroadcastBadge",new Vector2(.025f,.885f),new Vector2(.27f,.965f));
        mode=NeonUI.Text(badge,"Mode","COURSE PV",new Vector2(.06f,.08f),new Vector2(.94f,.92f),38f,italic:true,tint:NeonUI.Pink);
        NeonUI.Text(parent,"Circuit","TSUKUKOMA CIRCUIT",new Vector2(.52f,.90f),new Vector2(.97f,.96f),30f,TextAlignmentOptions.Right,true);
        var strip=NeonUI.Panel(parent,"BroadcastCaption",new Vector2(.025f,.035f),new Vector2(.975f,.145f));
        caption=NeonUI.Text(strip,"Action","CAMPUS MOTORSPORT",new Vector2(.02f,.43f),new Vector2(.98f,.95f),34f,italic:true);
        standings=NeonUI.Text(strip,"Standings","",new Vector2(.02f,.03f),new Vector2(.98f,.43f),23f,tint:NeonUI.Cyan);
    }
    public void Update(string label,string action,string order)
    {
        if(mode.text!=label)mode.text=label;
        if(caption.text!=action)caption.text=action;
        if(standings.text!=order)standings.text=order;
    }
}
