using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>An opaque service screen inside each real output canvas, including native windows and previews.</summary>
public static class RaceMaintenanceScreen
{
    public const string RootName = "Maintenance Screen";
    public static GameObject Create(Canvas canvas, int player)
    {
        var palette = ArcadeUI.For(player);
        var root = RacingUITheme.Rect(canvas.transform, RootName, Vector2.zero, Vector2.one);
        root.SetAsLastSibling();
        var background = root.gameObject.AddComponent<Image>(); background.color = palette.Paper; background.raycastTarget = true;
        ArcadeUI.Panel(root, "Top rail", new Vector2(0,.974f), Vector2.one, palette.Ink);
        ArcadeUI.Panel(root, "Bottom rail", Vector2.zero, new Vector2(1,.026f), palette.Ink);
        ArcadeUI.Panel(root, "Accent rail", new Vector2(.07f,.13f), new Vector2(.085f,.87f), palette.Primary);
        ArcadeUI.Label(root, "Brand", "TSUKUKOMA CIRCUIT", new Vector2(.13f,.87f), new Vector2(.87f,.93f), 27, palette.Ink);
        ArcadeUI.Panel(root, "Service badge", new Vector2(.13f,.70f), new Vector2(.39f,.77f), palette.Accent);
        ArcadeUI.Label(root, "Badge", "PIT STOP / MAINTENANCE", new Vector2(.145f,.705f), new Vector2(.38f,.765f), 22, palette.Ink);
        ArcadeUI.Label(root, "Heading", "ただいまメンテナンス中", new Vector2(.13f,.53f), new Vector2(.91f,.66f), 72, palette.Ink);
        ArcadeUI.Label(root, "Description", "安全に楽しんでもらえるよう、機器を調整しています。", new Vector2(.13f,.43f), new Vector2(.89f,.49f), 31, palette.Ink);
        ArcadeUI.Label(root, "Wait", "もう少し待っていてね。", new Vector2(.13f,.32f), new Vector2(.88f,.39f), 37, palette.Ink);
        ArcadeUI.Label(root, "Restart", "再開はスタッフがお知らせします。", new Vector2(.13f,.25f), new Vector2(.88f,.31f), 28, palette.Ink);
        ArcadeUI.Label(root, "Footer", "ハンドル・ペダルを操作してもゲームは始まりません", new Vector2(.13f,.08f), new Vector2(.89f,.14f), 23, palette.Ink);
        root.gameObject.SetActive(false);
        return root.gameObject;
    }
}
