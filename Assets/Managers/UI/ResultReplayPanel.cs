using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class ResultReplayPanel : MonoBehaviour
{
    private RawImage image;
    private TMP_Text mode,empty;
    public void Build(int playerNumber)
    {
        if(image!=null)return;var p=ArcadeUI.For(transform);
        var panel=ArcadeUI.Panel(transform,"ReplayPanel",new Vector2(.03f,.215f),new Vector2(.60f,.83f),p.Ink);
        var video=RacingUITheme.Rect(panel,"Video",new Vector2(0,.035f),new Vector2(1,.88f));
        image=video.gameObject.AddComponent<RawImage>();image.color=Color.white;image.raycastTarget=false;
        var fit=video.gameObject.AddComponent<AspectRatioFitter>();fit.aspectMode=AspectRatioFitter.AspectMode.FitInParent;fit.aspectRatio=16f/9;
        var heading=ArcadeUI.Panel(panel,"Header",new Vector2(0,.86f),Vector2.one,p.Ink);
        ArcadeUI.Label(heading,"Title","ハイライト",new Vector2(.025f,.08f),new Vector2(.6f,.92f),23,p.Soft);
        mode=ArcadeUI.Label(heading,"Mode","REPLAY",new Vector2(.65f,.08f),new Vector2(.975f,.92f),23,p.Paper,true,TextAlignmentOptions.Right);
        empty=ArcadeUI.Label(panel,"Empty","リプレイはありません",new Vector2(.1f,.4f),new Vector2(.9f,.6f),26,p.Soft,false,TextAlignmentOptions.Center);
    }
    private void LateUpdate()=>Refresh();
    public void Refresh()
    {
        if(image==null)return;var director=Gmanager.Control?.Broadcast;bool enabled=RaceFeatureController.Enabled(RaceFeature.RaceReplays)&&director!=null&&director.Feed!=null;
        image.gameObject.SetActive(enabled);empty.gameObject.SetActive(!enabled);
        if(enabled){image.texture=director.Feed;mode.text=director.Mode;}
    }
}
