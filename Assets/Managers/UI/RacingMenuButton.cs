using UnityEngine;
using UnityEngine.UI;

/// <summary>Native pointer/keyboard button states rendered by the shared vector surface.</summary>
public sealed class RacingMenuButton : Button
{
    private RacingPanelGraphic surface;

    public void Configure(RacingPanelGraphic value)
    {
        surface = value;
        transition = Transition.None;
        DoStateTransition(currentSelectionState, true);
    }

    protected override void DoStateTransition(SelectionState state, bool instant)
    {
        base.DoStateTransition(state, instant);
        if (surface == null) return;
        bool focused = state == SelectionState.Highlighted || state == SelectionState.Selected;
        bool pressed = state == SelectionState.Pressed;
        surface.color = state == SelectionState.Disabled ? new Color(0.55f, 0.55f, 0.55f, 0.65f) : Color.white;
        surface.SetState(focused || pressed ? 1f : 0f, pressed ? 1f : 0f, 0f, RacingUITheme.Cyan);
    }
}
