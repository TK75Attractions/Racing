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
    private TMPro.TMP_Text instruction;
    private SteeringReturnGraphic returnWheel;
    private float animationBegan;
    private int returnDirection;
    public bool IsWarningVisible => group != null && group.alpha > 0f;

    public void Configure(Gmanager race, int index)
    {
        manager = race; player = index;
        root = RacingUITheme.Rect(transform, "Physical steering warning", Vector2.zero, Vector2.one);
        group = root.gameObject.AddComponent<CanvasGroup>();
        group.alpha = 0f; group.interactable = false; group.blocksRaycasts = false;
        var wash = root.gameObject.AddComponent<Image>();
        wash.color = new Color(1f, .78f, 0f, .06f);
        var edge = RacingUITheme.Rect(root, "Yellow edges", Vector2.zero, Vector2.one).gameObject.AddComponent<RivalWarningGraphic>();
        edge.color = new Color(1f, .82f, .04f, .92f);
        var banner = RacingUITheme.Rect(root, "Warning banner", new Vector2(.22f,.25f), new Vector2(.78f,.75f));
        banner.gameObject.AddComponent<Image>().color = new Color(.09f,.075f,.01f,.93f);
        instruction = ArcadeUI.Label(banner, "Instruction", "左にゆっくり戻してね", new Vector2(.03f,.79f), new Vector2(.97f,.97f), 52f, new Color(1f,.88f,.18f),
            alignment: TMPro.TextAlignmentOptions.Center);
        returnWheel = RacingUITheme.Rect(banner, "Return wheel animation", new Vector2(.25f,.23f), new Vector2(.75f,.79f)).gameObject.AddComponent<SteeringReturnGraphic>();
        returnWheel.color = new Color(1f,.88f,.18f);
        ArcadeUI.Label(banner, "Limit", "ハンドルをまっすぐに", new Vector2(.03f,.04f), new Vector2(.97f,.21f), 35f, Color.white,
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
        int direction = visible && degrees.Value > 0f ? 1 : -1;
        if (visible)
        {
            if (!IsWarningVisible || direction != returnDirection) animationBegan = Time.unscaledTime;
            returnDirection = direction;
            instruction.text = direction > 0 ? "左にゆっくり戻してね" : "右にゆっくり戻してね";
            returnWheel.SetAnimation(direction, Time.unscaledTime - animationBegan);
            root.SetAsLastSibling();
        }
        group.alpha = visible ? 1f : 0f;
    }
}
