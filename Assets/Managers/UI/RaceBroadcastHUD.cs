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
        var badge=NeonUI.Panel(parent,"BroadcastBadge",new Vector2(.018f,.86f),new Vector2(.982f,.97f));
        mode=NeonUI.Text(badge,"Mode","COURSE PV",new Vector2(.02f,.08f),new Vector2(.28f,.92f),38f,italic:true,tint:ArcadeUI.For(parent).Soft);
        NeonUI.Text(parent,"Circuit","TSUKUKOMA CIRCUIT",new Vector2(.52f,.88f),new Vector2(.96f,.95f),30f,TextAlignmentOptions.Right,true);
        var strip=NeonUI.Panel(parent,"BroadcastCaption",new Vector2(0,0),new Vector2(1,.17f));
        caption=NeonUI.Text(strip,"Action","CAMPUS MOTORSPORT",new Vector2(.02f,.43f),new Vector2(.98f,.95f),34f,italic:true);
        standings=NeonUI.Text(strip,"Standings","",new Vector2(.02f,.03f),new Vector2(.98f,.43f),23f,tint:ArcadeUI.For(parent).Soft);
    }
    public void Update(string label,string action,string order)
    {
        if(mode.text!=label)mode.text=label;
        if(caption.text!=action)caption.text=action;
        if(standings.text!=order)standings.text=order;
    }
}
