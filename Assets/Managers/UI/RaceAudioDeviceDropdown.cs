using TMPro;
using UnityEngine;

/// <summary>Keep dropdown popups and their click blocker on the owning player's display.</summary>
public sealed class RaceAudioDeviceDropdown : TMP_Dropdown
{
    protected override GameObject CreateDropdownList(GameObject template)
    {
        GameObject list = base.CreateDropdownList(template);
        MatchDisplay(list);
        return list;
    }

    protected override GameObject CreateBlocker(Canvas rootCanvas)
    {
        GameObject blocker = base.CreateBlocker(rootCanvas);
        MatchDisplay(blocker);
        return blocker;
    }

    private void MatchDisplay(GameObject popup)
    {
        Canvas owner = GetComponentInParent<Canvas>();
        if (owner == null) return;
        Canvas root = owner.rootCanvas;
        foreach (Canvas canvas in popup.GetComponentsInChildren<Canvas>(true))
            canvas.targetDisplay = root.renderMode == RenderMode.ScreenSpaceCamera && root.worldCamera != null
                ? root.worldCamera.targetDisplay : root.targetDisplay;
    }
}
