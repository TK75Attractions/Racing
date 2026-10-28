using TMPro;
using UnityEngine;

public sealed class PreparationTimerUI
{
    private readonly TMP_Text value;
    private readonly Color normal;
    private readonly UIValuePulse pulse;
    private int shownSeconds=-1;
    public PreparationTimerUI(Transform parent,Vector2 min,Vector2 max,bool tutorial=false)
    {
        var p=ArcadeUI.For(parent);normal=p.Paper;
        var panel=ArcadeUI.Panel(parent,"PreparationTimer",min,max,p.Ink);
        ArcadeUI.Label(panel,"Caption","残り時間",new Vector2(.07f,.10f),new Vector2(.40f,.90f),20,p.Paper);
        value=ArcadeUI.Label(panel,"Time","",new Vector2(.42f,.05f),new Vector2(.96f,.96f),55,p.Paper,true,TextAlignmentOptions.Right);
        pulse=value.gameObject.AddComponent<UIValuePulse>();
    }
    public void Update(float remainingSeconds,float durationSeconds)
    {
        int seconds=Mathf.CeilToInt(Mathf.Max(0,remainingSeconds));if(seconds==shownSeconds)return;
        value.SetText("{0:00}:{1:00}",seconds/60,seconds%60);shownSeconds=seconds;pulse.SetRestColor(seconds<=10?ArcadeUI.Hex("ffb67f"):normal);
        if(Application.isPlaying&&seconds>0&&seconds<=10)pulse.Play(ArcadeUI.For(value.transform).Accent,0);
    }
}
