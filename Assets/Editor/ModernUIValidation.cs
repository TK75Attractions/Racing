#if UNITY_EDITOR
using System;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Checks resource availability and live HUD bindings without modifying the scene.</summary>
public static class ModernUIValidation
{
    [MenuItem("Racing/UI/Validate Modern UI")]
    public static void Run()
    {
        foreach (FontRole role in Enum.GetValues(typeof(FontRole)))
            Require(RacingUIFontCatalog.Get(role) != null, $"Missing UI font: {role}");
        TMP_FontAsset japanese = RacingUIFontCatalog.Get(FontRole.Japanese);
        Require(japanese.HasCharacters("スタートリトライタイトルへハンドルで操作ペダルを踏み込んで決定準備完了相手待っています観戦中済終了前進後退走行方向切替操作一時停止あなた", out uint[] missing),
            "Japanese UI font is missing required characters.");
        Require(Resources.Load<GameObject>("UI/TitleScreenBackground") != null, "Missing title background.");
        Require(Resources.Load<GameObject>("UI/ResultScreenBackground") != null, "Missing result background.");
        GameObject root = new GameObject("Modern UI validation", typeof(RectTransform), typeof(Canvas));
        root.hideFlags = HideFlags.HideAndDontSave;
        try
        {
            for (int player = 0; player < 2; player++)
            {
                RectTransform display = RacingUITheme.Rect(root.transform, $"Player{player}", Vector2.zero, Vector2.one);
                Transform hud = RacingHUDBuilder.Build(display, player, 5);
                foreach (Graphic graphic in hud.GetComponentsInChildren<Graphic>(true))
                    Require(graphic.GetComponent<CanvasRenderer>() != null, $"Missing renderer: {graphic.name}");
                var position = new UIPosition(); position.Init(hud.Find("Position")); position.SetPosition(2);
                var lap = new UILap(); lap.Init(hud.Find("Lap")); lap.SetLap(4);
                var time = new UITime(); time.Init(hud.Find("Time")); time.SetTotalTime(72.345f); time.SetLapTime(19.876f);
                var speed = new UISpeed(); speed.Init(hud.Find("Speed")); speed.UpdateSpeedMeter(127f, 0f);
                Require(Text(hud, "Position/Txt") == "2", "Position binding failed.");
                Require(Text(hud, "Lap/Txt") == "4" && Text(hud, "Lap/Total") == "/ 5", "Lap binding failed.");
                Require(Text(hud, "Time/Total") == "01:12" && Text(hud, "Time/TotalFraction") == ".34", "Total time binding failed.");
                Require(Text(hud, "Time/Lap") == "00:19" && Text(hud, "Time/LapFraction") == ".87", "Lap time binding failed.");
                Require(Text(hud, "Speed/Txt") == "127", "Speed binding failed.");
                Require(RacingHUDBuilder.Build(display, player, 5) == hud, "HUD initialization must be idempotent.");
            }
            ValidateOverlays(root.transform);
            Debug.Log("MODERN_UI_VALIDATION_PASS: fonts, backgrounds, both player HUDs, live timing, lap counts, speed, spectator and ESC actions.");
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }

    [MenuItem("Racing/UI/Preview HUD")]
    public static void PreviewHUD()
    {
        if (!Application.isPlaying) { Debug.LogWarning("HUD preview is available in Play Mode."); return; }
        foreach (ScreenTransitionController transition in UnityEngine.Object.FindObjectsByType<ScreenTransitionController>(FindObjectsSortMode.None))
        {
            transition.ApplyStateImmediate(Gmanager.State.Game);
            transition.ClearRaceStatus();
            Transform hud = transition.transform.Find("OnPlay/ModernHUD");
            if (hud == null) continue;
            var speed = new UISpeed(); speed.Init(hud.Find("Speed")); speed.UpdateSpeedMeter(127f, 0f);
            var time = new UITime(); time.Init(hud.Find("Time")); time.SetTotalTime(72.345f); time.SetLapTime(19.876f);
        }
    }

    [MenuItem("Racing/UI/Preview Spectator")]
    public static void PreviewSpectator()
    {
        if (!Application.isPlaying) return;
        foreach (ScreenTransitionController transition in UnityEngine.Object.FindObjectsByType<ScreenTransitionController>(FindObjectsSortMode.None))
        {
            transition.GetComponent<InterruptionMenuUI>()?.Hide();
            transition.ApplyStateImmediate(Gmanager.State.Game);
            transition.ShowSpectator(1);
        }
    }

    [MenuItem("Racing/UI/Preview ESC Menu")]
    public static void PreviewESC()
    {
        if (!Application.isPlaying) return;
        foreach (ScreenTransitionController transition in UnityEngine.Object.FindObjectsByType<ScreenTransitionController>(FindObjectsSortMode.None))
        {
            transition.ApplyStateImmediate(Gmanager.State.Game);
            transition.HideSpectator();
            transition.GetComponent<InterruptionMenuUI>()?.Show(false);
        }
    }

    private static void ValidateOverlays(Transform parent)
    {
        RectTransform host = RacingUITheme.Rect(parent, "OverlayValidation", Vector2.zero, Vector2.one);
        SpectatorOverlayUI spectator = SpectatorOverlayUI.Create(host, null);
        spectator.Show(0);
        Require(Text(spectator.transform, "PlayerPlate/PlayerLabel") == "プレイヤー 1 を観戦中", "Spectator player one label failed.");
        spectator.Show(1);
        Require(Text(spectator.transform, "PlayerPlate/PlayerLabel") == "プレイヤー 2 を観戦中", "Spectator player two label failed.");
        foreach (Graphic graphic in spectator.GetComponentsInChildren<Graphic>(true))
            Require(!graphic.raycastTarget, "Spectator decorations must not intercept input.");
        spectator.Hide();
        Require(!spectator.gameObject.activeSelf, "Spectator hide failed.");
        int interrupted = 0, toggled = 0, restarted = 0;
        InterruptionMenuUI menu = null;
        for (int build = 0; build < 2; build++)
            menu = InterruptionMenuUI.Create(host, () => interrupted++, () => toggled++, () => restarted++);
        menu.Show(false);
        Transform card = host.Find("InterruptionMenu/MenuCard");
        Require(menu.IsOpen && Text(card, "ToggleDirection/DirectionStatus") == "前進", "Forward status failed.");
        menu.SetDirectionStatus(true);
        Require(Text(card, "ToggleDirection/DirectionStatus") == "後退", "Reverse status failed.");
        foreach (string name in new[] { "Interrupt", "ToggleDirection", "Restart" })
        {
            RacingMenuButton button = card.Find(name).GetComponent<RacingMenuButton>();
            Require(button != null && button.interactable && button.targetGraphic.enabled && button.targetGraphic.raycastTarget,
                $"Menu action lacks an active hit target: {name}");
            Require(button.GetComponentInChildren<RacingPanelGraphic>(true) != null, $"Missing vector button: {name}");
            button.onClick.Invoke();
        }
        Require(interrupted == 1 && toggled == 1 && restarted == 1, "Rebuilding the menu must not duplicate action listeners.");
        menu.Hide();
        Require(!menu.IsOpen, "Menu hide failed.");
    }

    private static string Text(Transform root, string path) => root.Find(path).GetComponent<TMP_Text>().text;
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
#endif
