using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>All race entries share one classification; only two entries own a screen.</summary>
public sealed class RacingLeaderboardUI : MonoBehaviour
{
    private int owner;
    private readonly int[] shownCounts={-1,-1,-1,-1};
    private readonly int[] shownRanks={-1,-1,-1,-1}, shownMetres={int.MinValue,int.MinValue,int.MinValue,int.MinValue}, shownStates={-1,-1,-1,-1};
    private readonly RaceParticipant[] shownEntries=new RaceParticipant[4];
    private bool snap=true;
    private int previewPosition = -1, previewMetres = int.MinValue;
    private readonly RectTransform[] rows = new RectTransform[4];
    private readonly TMP_Text[] ranks = new TMP_Text[4], names = new TMP_Text[4], gaps = new TMP_Text[4];
    public void Configure(int player)
    {
        snap=true;for(int i=0;i<4;i++){shownRanks[i]=-1;shownCounts[i]=-1;shownMetres[i]=int.MinValue;shownStates[i]=-1;shownEntries[i]=null;}
        owner = player; previewPosition = -1; previewMetres = int.MinValue;
        for (int i = 0; i < rows.Length; i++)
        {
            rows[i] = NeonUI.Panel(transform, "Player" + i, Vector2.zero, Vector2.one, i == owner);
            ranks[i] = NeonUI.Text(rows[i], "Rank", (i+1).ToString(), new Vector2(.035f,.09f), new Vector2(.12f,.91f), 25f, italic:true);
            names[i] = NeonUI.Text(rows[i], "Name", i < 2 ? "P"+(i+1) : "CPU "+(i-1), new Vector2(.20f,.14f), new Vector2(.74f,.88f), 19f);
            gaps[i] = NeonUI.Text(rows[i], "Gap", "—", new Vector2(.73f,.14f), new Vector2(.96f,.88f), 16f, TextAlignmentOptions.Right, true);
            var face=rows[i].Find("ModernSurface").GetComponent<RacingPanelGraphic>();face.HardShadow=true;
            if(i==owner)ranks[i].color=names[i].color=gaps[i].color=ArcadeUI.For(player).Ink;
        }
        SetPosition(owner + 1, 0);
    }
    public void SetParticipants(IReadOnlyList<RaceParticipant> entries)
    {
        for (int i = 0; i < rows.Length; i++)
        {
            bool active = i < entries.Count; rows[i].gameObject.SetActive(active); if (!active) continue;
            RaceParticipant entry = entries[i];
            Place(i, entry.Position, entries.Count);
            if(shownEntries[i]!=entry){names[i].text=entry.Label;shownEntries[i]=entry;}
            int state=entry.Result!=null?(entry.Result.didFinish?1:2):i==owner?3:0;
            int metres=Mathf.RoundToInt(Mathf.Abs(entry.Progress-entries[Mathf.Min(owner,entries.Count-1)].Progress));
            if(shownStates[i]!=state||(state==0&&shownMetres[i]!=metres))
            {
                if(state==0)gaps[i].SetText("{0} m",metres);else gaps[i].text=state==1?"GOAL":state==2?"DNF":"YOU";
                shownStates[i]=state;shownMetres[i]=metres;
            }
        }
        snap=false;
    }
    private void Place(int i, int rank, int count)
    {
        if(shownRanks[i]==rank&&shownCounts[i]==count)return;
        float oldY=(rows[i].anchorMin.y+rows[i].anchorMax.y)*.5f*GetComponent<RectTransform>().rect.height+rows[i].anchoredPosition.y;
        float height=1f/Mathf.Max(1,count),top=1f-(rank-1)*height;
        rows[i].anchorMin=new Vector2(0,top-height+.02f);rows[i].anchorMax=new Vector2(1,top-.02f);
        float newY=(rows[i].anchorMin.y+rows[i].anchorMax.y)*.5f*GetComponent<RectTransform>().rect.height;
        rows[i].anchoredPosition=new Vector2(0,Application.isPlaying&&!snap?oldY-newY:0);
        ranks[i].SetText("{0}",rank);shownRanks[i]=rank;shownCounts[i]=count;
    }
    // Compatibility for standalone HUD previews.
    public void SetPosition(int position, float distance)
    {
        position = Mathf.Clamp(position, 1, 4); int metres = Mathf.RoundToInt(Mathf.Abs(distance));
        if (previewPosition == position && previewMetres == metres) return;
        int other=1;
        for (int i = 0; i < rows.Length; i++)
        {
            rows[i].gameObject.SetActive(true);
            int rank;if(i==owner)rank=position;else{if(other==position)other++;rank=other++;}
            Place(i, rank, 4);
            gaps[i].text = i == owner ? "YOU" : (position == 1 ? "+" : "−") + metres + " m";
        }
        previewPosition = position; previewMetres = metres;snap=false;
    }
    public void Tick(float dt)
    {
        for(int i=0;i<rows.Length;i++)if(rows[i]!=null&&rows[i].anchoredPosition.y!=0)
        {
            Vector2 p=rows[i].anchoredPosition;p.y=UIMotion.Follow(p.y,0,dt,.065f,.025f);rows[i].anchoredPosition=p;
        }
    }
    private void Update()=>Tick(Time.unscaledDeltaTime);
    private void OnEnable(){snap=true;}
    private void OnDisable(){snap=true;for(int i=0;i<rows.Length;i++)if(rows[i]!=null)rows[i].anchoredPosition=Vector2.zero;}
}
