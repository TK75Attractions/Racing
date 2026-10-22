using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>A large replay on the left, classification and today's rankings on the right.</summary>
public sealed class ResultReplayPanel : MonoBehaviour
{
    private readonly Dictionary<RectTransform,(Vector2 min,Vector2 max)> original=new Dictionary<RectTransform,(Vector2,Vector2)>();
    private RectTransform panel;
    private RawImage image;
    private TMP_Text caption,placement;
    private int player;
    private bool applied,initialized;
    public void Build(int playerNumber)
    {
        if(initialized)return;initialized=true;player=playerNumber;
        foreach(string name in new[]{"Table","TableHeader","ResultRow1","ResultRow2","ResultRow3","ResultRow4","RaceSummary","Medal","AchievementBackdrop","Achievement","DailyRanking"})
        {
            var rect=transform.Find(name) as RectTransform;if(rect!=null)original[rect]=(rect.anchorMin,rect.anchorMax);
        }
        panel=NeonUI.Panel(transform,"ReplayPanel",new Vector2(.03f,.215f),new Vector2(.625f,.81f));panel.SetAsFirstSibling();
        var video=RacingUITheme.Rect(panel,"ReplayVideo",new Vector2(.008f,.008f),new Vector2(.992f,.992f));
        image=video.gameObject.AddComponent<RawImage>();image.color=Color.white;image.raycastTarget=false;
        var fit=video.gameObject.AddComponent<AspectRatioFitter>();fit.aspectMode=AspectRatioFitter.AspectMode.FitInParent;fit.aspectRatio=16f/9f;
        var bar=NeonUI.Panel(panel,"Caption",new Vector2(.01f,.90f),new Vector2(.99f,.99f));
        NeonUI.Text(bar,"Title","ハイライト",new Vector2(.025f,.05f),new Vector2(.27f,.95f),28f,tint:NeonUI.Cyan);
        caption=NeonUI.Text(bar,"Action","COURSE PV",new Vector2(.28f,.05f),new Vector2(.74f,.95f),23f,italic:true);
        placement=NeonUI.Text(bar,"Placement","P"+player,new Vector2(.75f,.05f),new Vector2(.98f,.95f),25f,TextAlignmentOptions.Right,true,NeonUI.Pink);
        panel.gameObject.SetActive(false);
        Refresh();
    }
    private void LateUpdate()=>Refresh();
    public void Refresh()
    {
        if(!initialized)return;
        bool on=RaceFeatureController.Enabled(RaceFeature.RaceReplays);
        if(on!=applied)
        {
            applied=on;panel.gameObject.SetActive(on);
            if(on)
            {
                Place("Table",.65f,.49f,.974f,.80f);Place("TableHeader",.655f,.742f,.965f,.79f);
                for(int i=0;i<4;i++){float top=.735f-i*.060f;Place("ResultRow"+(i+1),.655f,top-.058f,.965f,top);}
                Place("RaceSummary",.045f,.145f,.61f,.205f);Place("Medal",.47f,.84f,.525f,.96f);
                Place("AchievementBackdrop",.045f,.225f,.61f,.272f);Place("Achievement",.045f,.225f,.61f,.272f);
                Place("DailyRanking",.65f,.17f,.974f,.47f);
            }
            else foreach(var pair in original){pair.Key.anchorMin=pair.Value.min;pair.Key.anchorMax=pair.Value.max;}
            foreach(string hidden in new[]{"PlacementRibbon","Winner"}){var item=transform.Find(hidden);if(item!=null)item.gameObject.SetActive(!on);}
        }
        var director=Gmanager.Control?.Broadcast;
        if(on&&director!=null)
        {
            image.texture=director.Feed;
            caption.text=director.Mode+" / "+director.Caption;
            var result=Gmanager.Control.LatestSessionResult?.GetPlayerResult(player-1);
            placement.text="P"+player+" / "+(result?.didFinish==true?result.finishPosition.ToString()+" FINISH":result==null?"RESULT":"DNF");
        }
    }
    private void Place(string name,float x0,float y0,float x1,float y1)
    {var rect=transform.Find(name) as RectTransform;if(rect!=null){rect.anchorMin=new Vector2(x0,y0);rect.anchorMax=new Vector2(x1,y1);}}
}
