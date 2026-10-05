using TMPro;
using UnityEngine;

/// <summary>Two real competitors, ordered by race progress; the local driver is pink.</summary>
public sealed class RacingLeaderboardUI : MonoBehaviour
{
    private int owner;
    private readonly RectTransform[] rows=new RectTransform[2];
    private readonly TMP_Text[] ranks=new TMP_Text[2],names=new TMP_Text[2],gaps=new TMP_Text[2];
    public void Configure(int player)
    {
        owner=player;
        for(int i=0;i<2;i++)
        {
            rows[i]=NeonUI.Panel(transform,"Player"+i,Vector2.zero,Vector2.one,i==owner);
            ranks[i]=NeonUI.Text(rows[i],"Rank",(i+1).ToString(),new Vector2(.035f,.09f),new Vector2(.12f,.91f),28f,italic:true);
            names[i]=NeonUI.Text(rows[i],"Name","プレイヤー "+(i+1),new Vector2(.20f,.14f),new Vector2(.74f,.88f),20f);
            gaps[i]=NeonUI.Text(rows[i],"Gap","—",new Vector2(.73f,.14f),new Vector2(.96f,.88f),18f,TextAlignmentOptions.Right,true);
        }
        SetPosition(owner+1,0f);
    }
    public void SetPosition(int position,float distance)
    {
        for(int i=0;i<2;i++)
        {
            int rank=i==owner?Mathf.Clamp(position,1,2):3-Mathf.Clamp(position,1,2);
            float top=rank==1?.98f:.48f;
            rows[i].anchorMin=new Vector2(0,top-.44f);rows[i].anchorMax=new Vector2(1,top);
            ranks[i].text=rank.ToString();
            gaps[i].text=i==owner?"YOU":(position==1?"+":"−")+Mathf.Abs(distance).ToString("0")+" m";
        }
    }
    private void LateUpdate()
    { if(Gmanager.Control!=null)SetPosition(Gmanager.Control.GetPlayerRacePosition(owner),Gmanager.Control.GetPlayerProgressGap(owner)); }
}
