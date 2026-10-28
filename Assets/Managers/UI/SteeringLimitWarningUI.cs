using UnityEngine;
using UnityEngine.UI;

/// <summary>Per-player physical wheel warning, visible in menus as well as during driving.</summary>
public sealed class SteeringLimitWarningUI : MonoBehaviour
{
    private readonly SteeringOverturnWarning warning = new SteeringOverturnWarning();
    private Gmanager manager;
    private int player;
    private RectTransform root;
    private CanvasGroup group;
    public bool IsWarningVisible => group != null && group.alpha > 0f;

    public void Configure(Gmanager race, int index)
    {
        manager = race; player = index;
        root = RacingUITheme.Rect(transform, "Physical steering warning", Vector2.zero, Vector2.one);
        group = root.gameObject.AddComponent<CanvasGroup>();
        group.alpha = 0f; group.interactable = false; group.blocksRaycasts = false;
        var wash = root.gameObject.AddComponent<Image>();
        wash.color = new Color(.85f, 0f, .025f, .12f);
        var edge = RacingUITheme.Rect(root, "Red edges", Vector2.zero, Vector2.one).gameObject.AddComponent<RivalWarningGraphic>();
        edge.color = new Color(1f, .03f, .06f, .85f);
        var banner = RacingUITheme.Rect(root, "Warning banner", new Vector2(.22f,.80f), new Vector2(.78f,.94f));
        banner.gameObject.AddComponent<Image>().color = new Color(.36f,.015f,.025f,.96f);
        ArcadeUI.Label(banner, "Instruction", "ハンドルをゆっくり戻してね", new Vector2(.03f,.40f), new Vector2(.97f,.94f), 42f, Color.white,
            alignment: TMPro.TextAlignmentOptions.Center);
        ArcadeUI.Label(banner, "Limit", "左右135°まで", new Vector2(.03f,.05f), new Vector2(.97f,.42f), 27f, Color.white,
            alignment: TMPro.TextAlignmentOptions.Center);
        foreach (Graphic graphic in root.GetComponentsInChildren<Graphic>()) graphic.raycastTarget = false;
    }

    private void LateUpdate()
    {
        float? angle = null;
        var serial = manager != null ? manager.IManager?.SharedSerialInputSource : null;
        if (serial != null && serial.TryGetPhysicalSteeringDegrees(player, out float degrees)) angle = degrees;
        Present(angle, manager == null || manager.IsMaintenanceActive);
    }

    public void Present(float? degrees, bool suppressed)
    {
        bool visible = warning.Update(suppressed ? null : degrees);
        if (group == null) return;
        group.alpha = visible ? 1f : 0f;
        if (visible) root.SetAsLastSibling();
    }
}
