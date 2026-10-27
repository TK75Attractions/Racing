using UnityEngine;
using TMPro;

/// <summary>A short item reference during stationary practice waiting; driving remains unobstructed.</summary>
public static class ItemGuideUI
{
    public static GameObject Create(Transform parent, int player)
    {
        var palette = ArcadeUI.For(player);
        var panel = ArcadeUI.Panel(parent, "ItemGuide", new Vector2(.30f,.25f), new Vector2(.70f,.75f), palette.Paper);
        ArcadeUI.Label(panel, "Heading", "ふれるだけで 自動発動！", new Vector2(.06f,.86f), new Vector2(.96f,.97f),
            30, palette.Ink);
        ArcadeUI.Label(panel, "Hint", "ボタン操作はいらないよ", new Vector2(.06f,.78f), new Vector2(.96f,.86f),
            22, palette.Ink);
        Row(panel, "Shield", RacingIconGraphic.Icon.Shield, "シールド", "守る！ 相手に当たるとスピン", .64f, palette);
        Row(panel, "Rocket", RacingIconGraphic.Icon.Rocket, "ロケット", "自動でハンドル操作 ＆ 超加速", .50f, palette);
        Row(panel, "Oil", RacingIconGraphic.Icon.Droplet, "オイル", "後ろに油！ 踏むとすべるよ", .36f, palette);
        Row(panel, "Confuse", RacingIconGraphic.Icon.Swap, "コンフューズ", "相手のハンドルを左右逆に！", .22f, palette);
        Row(panel, "Charge", RacingIconGraphic.Icon.Bolt, "青いクリスタル", "カーブでハンドルを戻すと加速", .08f, palette);
        return panel.gameObject;
    }

    private static void Row(Transform parent, string name, RacingIconGraphic.Icon icon, string title, string description,
        float y, ArcadeUI.Palette palette)
    {
        var row = RacingUITheme.Rect(parent, name, new Vector2(.04f,y), new Vector2(.96f,y+.13f));
        var symbol = NeonUI.Icon(row, "Icon", icon, new Vector2(0f,.17f), new Vector2(.13f,.85f));
        symbol.color = palette.Ink;
        ArcadeUI.Label(row, "Name", title, new Vector2(.17f,.44f), Vector2.one, 27, palette.Ink);
        ArcadeUI.Label(row, "Effect", description, new Vector2(.17f,0f), new Vector2(1f,.46f), 22, palette.Ink);
    }
}
