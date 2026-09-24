using UnityEngine;

/// <summary>Shared antialiased surface for pedal-operated menu buttons.</summary>
public sealed class PedalButtonSurface : RacingPanelGraphic
{
    public void SetVisual(float amount, bool active, float confirmation, Color accent)
    {
        SetState(active ? 1f : 0f, amount, confirmation, accent);
    }
}
