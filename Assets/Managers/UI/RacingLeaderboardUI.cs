using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>All race entries share one classification; only two entries own a screen.</summary>
public sealed class RacingLeaderboardUI : MonoBehaviour
{
    private int owner;
    private int previewPosition = -1, previewMetres = int.MinValue;
    private readonly RectTransform[] rows = new RectTransform[4];
    private readonly TMP_Text[] ranks = new TMP_Text[4], names = new TMP_Text[4], gaps = new TMP_Text[4];
    public void Configure(int player)
    {
        owner = player; previewPosition = -1; previewMetres = int.MinValue;
        for (int i = 0; i < rows.Length; i++)
        {
            rows[i] = NeonUI.Panel(transform, "Player" + i, Vector2.zero, Vector2.one, i == owner);
            ranks[i] = NeonUI.Text(rows[i], "Rank", (i+1).ToString(), new Vector2(.035f,.09f), new Vector2(.12f,.91f), RacingHUDStyle.JapaneseSize, italic:false);
            names[i] = NeonUI.Text(rows[i], "Name", i < 2 ? "P"+(i+1) : "CPU "+(i-1), new Vector2(.20f,.14f), new Vector2(.74f,.88f), RacingHUDStyle.SecondarySize);
            gaps[i] = NeonUI.Text(rows[i], "Gap", "—", new Vector2(.73f,.14f), new Vector2(.96f,.88f), RacingHUDStyle.CaptionSize, TextAlignmentOptions.Right, false);
        }
        SetPosition(owner + 1, 0);
    }
    public void SetParticipants(IReadOnlyList<RaceParticipant> entries)
    {
        for (int i = 0; i < rows.Length; i++)
        {
            bool active = i < entries.Count; rows[i].gameObject.SetActive(active); if (!active) continue;
            RaceParticipant entry = entries[i];
            Place(i, entry.Position, entries.Count); names[i].text = entry.Label;
            gaps[i].text = entry.Result != null ? (entry.Result.didFinish ? "GOAL" : "DNF") : i == owner ? "YOU" :
                Mathf.Abs(entry.Progress - entries[owner].Progress).ToString("0") + " m";
        }
    }
    private void Place(int i, int rank, int count)
    {
        float height = 1f / Mathf.Max(1, count), top = 1f - (rank - 1) * height;
        rows[i].anchorMin = new Vector2(0, top - height + .02f); rows[i].anchorMax = new Vector2(1, top - .02f);
        ranks[i].text = rank.ToString();
    }
    // Compatibility for standalone HUD previews.
    public void SetPosition(int position, float distance)
    {
        position = Mathf.Clamp(position, 1, 2); int metres = Mathf.RoundToInt(Mathf.Abs(distance));
        if (previewPosition == position && previewMetres == metres) return;
        for (int i = 0; i < rows.Length; i++)
        {
            rows[i].gameObject.SetActive(i < 2); if (i >= 2) continue;
            Place(i, i == owner ? position : 3-position, 2);
            gaps[i].text = i == owner ? "YOU" : (position == 1 ? "+" : "−") + metres + " m";
        }
        previewPosition = position; previewMetres = metres;
    }
}
