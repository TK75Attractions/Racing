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
        Require(japanese.HasCharacters("スタートリトライタイトルへハンドルで操作ペダルを踏み込んで決定準備完了相手待っています", out uint[] missing),
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
            Debug.Log("MODERN_UI_VALIDATION_PASS: fonts, backgrounds, both player HUDs, live timing, lap counts and speed.");
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

    private static string Text(Transform root, string path) => root.Find(path).GetComponent<TMP_Text>().text;
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
#endif
