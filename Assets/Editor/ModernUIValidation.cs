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
        Require(RacingUIFontCatalog.GetHUD(false) != null && RacingUIFontCatalog.GetHUD(true) != null, "Missing medium/bold HUD fonts.");
        TMP_FontAsset japanese = RacingUIFontCatalog.Get(FontRole.Japanese);
        Require(japanese.HasCharacters("スタートリトライタイトルへハンドルで操作ペダルを踏み込んで決定準備完了相手待っています観戦中済終了前進後退走行方向切替操作一時停止あなた順位いまの周位人走行タイムこのスピードコースあいてプレイヤー加速秒もうすぐレース終了", out uint[] missing),
            "Japanese UI font is missing required characters.");
        foreach(string asset in new[]{"TitleBackground","ResultBackground","MedalGold","MedalSilver"})
            Require(Resources.Load<Texture2D>("UI/Neon/"+asset)!=null,"Missing neon art: "+asset);
        GameObject root = new GameObject("Modern UI validation", typeof(RectTransform), typeof(Canvas));
        root.hideFlags = HideFlags.HideAndDontSave;
        try
        {
            for (int player = 0; player < 2; player++)
            {
                RectTransform display = RacingUITheme.Rect(root.transform, $"Player{player}", Vector2.zero, Vector2.one);
                Transform hud = RacingHUDBuilder.Build(display, player, 5);
                Require(hud.Find("RearView") == null, "Rear-view UI/camera must not be created.");
                foreach(string island in new[]{"BoostBadge","Speed","Leaderboard"})
                    Require(hud.Find(island).GetComponent<Canvas>()!=null,"Heavy HUD geometry is not isolated from timer rebuilds: "+island);
                foreach (Graphic graphic in hud.GetComponentsInChildren<Graphic>(true))
                    Require(graphic.GetComponent<CanvasRenderer>() != null, $"Missing renderer: {graphic.name}");
                foreach (string panel in new[] { "Position", "Lap", "Time", "Speed", "PadBoost" })
                {
                    RacingHUDPlateGraphic plate = hud.Find(panel + "/ModernSurface")?.GetComponent<RacingHUDPlateGraphic>();
                    Require(plate != null && !plate.raycastTarget, $"Missing nonblocking HUD plate: {panel}");
                }
                var position = new UIPosition(); position.Init(hud.Find("Position")); position.SetPosition(2);
                var lap = new UILap(); lap.Init(hud.Find("Lap")); lap.SetLap(4);
                var time = new UITime(); time.Init(hud.Find("Time")); time.SetTotalTime(72.345f); time.SetLapTime(19.876f);
                var speed = new UISpeed(); speed.Init(hud.Find("Speed")); speed.UpdateSpeedMeter(127f, 0f);
                Require(Text(hud, "Position/Txt") == "2", "Position binding failed.");
                Require(Text(hud, "Lap/Txt") == "4" && Text(hud, "Lap/Total") == "/ 5", "Lap binding failed.");
                Require(Text(hud, "Time/Total") == "01:12" && Text(hud, "Time/TotalFraction") == ".34", "Total time binding failed.");
                Require(Text(hud, "Time/Lap") == "00:19" && Text(hud, "Time/LapFraction") == ".87", "Lap time binding failed.");
                Require(Text(hud, "Speed/Txt") == "127", "Speed binding failed.");
                Require(Text(hud, "Position/Heading") == "順位" && Text(hud, "Position/EnglishHeading") == "POSITION", "Bilingual position label failed.");
                Require(Text(hud, "Lap/Heading") == "いまの周" && Text(hud, "Speed/Heading") == "スピード", "Japanese instrument labels failed.");
                speed.UpdateBoostGauge(2.45f, 3f);
                Require(hud.Find("PadBoost").gameObject.activeSelf && Text(hud, "PadBoost/Remaining") == "2.5秒", "Boost seconds binding failed.");
                speed.UpdateBoostGauge(0f, 3f);
                Require(!hud.Find("PadBoost").gameObject.activeSelf, "Boost must hide when inactive.");
                Require(RacingHUDBuilder.Build(display, player, 5) == hud, "HUD initialization must be idempotent.");
            }
            ValidateOverlays(root.transform);
            ValidateNeonMenus(root.transform);
            ValidateIdleGraphics(root.transform);
            ValidateHUDHotPaths(root.transform);
            ValidateButtonStates(root.transform);
            Debug.Log("MODERN_UI_VALIDATION_PASS: fonts, backgrounds, both player HUDs, live timing, lap counts, speed, spectator and ESC actions.");
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }

    private static void ValidateIdleGraphics(Transform parent)
    {
        RectTransform fixture = RacingUITheme.Rect(parent, "IdleGraphicsValidation", Vector2.zero, Vector2.one);
        RectTransform ringRect = RacingUITheme.Rect(fixture, "Boost", Vector2.zero, Vector2.one);
        NeonRingGraphic ring = ringRect.gameObject.AddComponent<NeonRingGraphic>();
        ring.display = NeonRingGraphic.Display.Boost;
        ring.SetAmount(0f);
        int ringDirty = 0;
        ring.RegisterDirtyVerticesCallback(() => ringDirty++);
        for (int frame = 0; frame < 120; frame++) ring.SetAmount(0f);
        Require(ringDirty == 0, "An inactive boost ring rebuilds every frame.");
        ring.SetAmount(.5f);
        Require(ringDirty == 1, "A changed boost value must update its mesh.");

        RacingPanelGraphic button = fixture.gameObject.AddComponent<RacingPanelGraphic>();
        button.Configure(RacingPanelGraphic.SurfaceStyle.Primary, RacingUITheme.Cyan);
        int buttonDirty = 0;
        button.RegisterDirtyVerticesCallback(() => buttonDirty++);
        for (int frame = 0; frame < 120; frame++) button.SetState(0f, 0f, 0f, RacingUITheme.Cyan);
        Require(buttonDirty == 0, "Primary button tint normalization causes idle rebuilds.");
        button.SetState(1f, .5f, 0f, RacingUITheme.Cyan);
        Require(buttonDirty == 1, "An active button must still animate.");

        RectTransform boardRect = RacingUITheme.Rect(parent, "StableLeaderboardValidation", Vector2.zero, Vector2.one);
        RacingLeaderboardUI board = boardRect.gameObject.AddComponent<RacingLeaderboardUI>();
        board.Configure(0);
        board.SetPosition(1, 4.2f);
        TMP_Text gap = boardRect.Find("Player1/Gap").GetComponent<TMP_Text>();
        int gapDirty = 0;
        gap.RegisterDirtyVerticesCallback(() => gapDirty++);
        for (int frame = 0; frame < 120; frame++) board.SetPosition(1, 4.4f);
        Require(gapDirty == 0 && gap.text == "+4 m", "Unchanged visible standings are regenerated.");
        board.SetPosition(2, 5f);
        Require(gapDirty > 0 && gap.text == "−5 m", "Live standings must update rank and distance.");
        Debug.Log("UI_IDLE_VALIDATION_PASS: 120 unchanged frames produce zero ring/button/standings rebuilds; changed values still update.");
    }

    private static void ValidateHUDHotPaths(Transform parent)
    {
        RectTransform fixture = RacingUITheme.Rect(parent, "HUDHotPathValidation", Vector2.zero, Vector2.one);
        Transform hud = RacingHUDBuilder.Build(fixture, 0, 3);
        RacingSpeedGauge gauge = hud.Find("Speed/Gauge").GetComponent<RacingSpeedGauge>();
        int gaugeDirty = 0;
        gauge.RegisterDirtyVerticesCallback(() => gaugeDirty++);
        for (int i = 0; i <= 3600; i++) gauge.SetSpeed(i / 20f);
        Require(gaugeDirty == 50, "The 50-segment gauge must only rebuild when a visible segment changes.");

        NeonRingGraphic ring = hud.Find("BoostBadge").GetComponent<NeonRingGraphic>();
        RacingBoostArcGraphic arc = hud.Find("BoostBadge/Progress").GetComponent<RacingBoostArcGraphic>();
        int ringDirty = 0, arcDirty = 0;
        ring.RegisterDirtyVerticesCallback(() => ringDirty++);
        arc.RegisterDirtyVerticesCallback(() => arcDirty++);
        ring.SetAmount(.5f);
        Require(ringDirty == 0 && arcDirty == 1 && arc.Amount == .5f, "A changing boost must not regenerate the static glow/ticks.");

        UITime timer = new UITime(); timer.Init(hud.Find("Time"));
        UISpeed speed = new UISpeed(); speed.Init(hud.Find("Speed"));
        for (int i = 0; i < 200; i++)
        {
            timer.SetTotalTime(72f + i * .01f); timer.SetLapTime(19f + i * .01f);
            speed.UpdateSpeedMeter(120f + i % 60, 0f);
            speed.UpdateBoostGauge(2.45f, 3f);
        }
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++)
        {
            timer.SetTotalTime(72f + i * .01f); timer.SetLapTime(19f + i * .01f);
            speed.UpdateSpeedMeter(120f + i % 60, 0f);
            speed.UpdateBoostGauge(2.45f, 3f);
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Require(allocated == 0, "HUD formatting still allocates: " + allocated + " bytes / 1000 updates.");
        Debug.Log("UI_HOT_PATH_VALIDATION_PASS: 3601 speed inputs -> 50 gauge rebuilds; changing charge -> zero static-ring rebuilds; 1000 time/speed/boost updates -> " + allocated + " managed bytes.");
    }

    private static void ValidateButtonStates(Transform parent)
    {
        RacingMenuButton button=NeonUI.Button(parent,"ButtonStateValidation","スタート","START",RacingIconGraphic.Icon.Flag,Vector2.zero,Vector2.one,true,null);
        button.ConfigureHold(.65f,true);
        PedalButtonFeedback feedback=button.GetComponent<PedalButtonFeedback>();
        RacingPanelGraphic surface=button.transform.Find("ModernSurface").GetComponent<RacingPanelGraphic>();
        RacingButtonGaugeGraphic gauge=button.transform.Find("HoldGauge").GetComponent<RacingButtonGaugeGraphic>();
        feedback.SetState(true,.5f,NeonUI.Pink);
        for(int frame=0;frame<60;frame++)feedback.Tick(1f/60f);
        Require(Text(button.transform,"HoldPercent")=="50%","Hold percentage does not match actual progress.");
        feedback.SetConfirmed(true);feedback.SetState(false,0,NeonUI.Pink);
        for(int frame=0;frame<60;frame++)feedback.Tick(1f/60f);
        Require(feedback.IsRetained && Text(button.transform,"HoldPercent")=="100%","Start must remain selected after releasing the hold.");
        int dirty=0;
        surface.RegisterDirtyVerticesCallback(()=>dirty++);gauge.RegisterDirtyVerticesCallback(()=>dirty++);
        button.transform.Find("InteractionFX").GetComponent<RacingButtonFXGraphic>().RegisterDirtyVerticesCallback(()=>dirty++);
        long before=GC.GetAllocatedBytesForCurrentThread();
        for(int frame=0;frame<120;frame++)feedback.Tick(1f/60f);
        long bytes=GC.GetAllocatedBytesForCurrentThread()-before;
        Require(dirty==0 && bytes==0,"Settled button animation still rebuilds or allocates.");
        button.interactable=false;feedback.SetConfirmed(false);
        for(int frame=0;frame<60;frame++)feedback.Tick(1f/60f);
        int disabledDirty=0;surface.RegisterDirtyVerticesCallback(()=>disabledDirty++);
        feedback.SetPointerState(false,false,false);
        for(int frame=0;frame<120;frame++)feedback.Tick(1f/60f);
        Require(disabledDirty==0 && Text(button.transform,"HoldPercent")=="0%","Disabled buttons must not react or keep a charged gauge.");
        Debug.Log("BUTTON_STATE_VALIDATION_PASS: 50% hold, retained MAX after release, disabled state, settled 120 frames with zero rebuilds/allocations.");
    }

    private static void ValidateNeonMenus(Transform parent)
    {
        RectTransform host=RacingUITheme.Rect(parent,"ScreenValidation",Vector2.zero,Vector2.one);
        RectTransform title=RacingUITheme.Rect(host,"Title",Vector2.zero,Vector2.one);
        RectTransform play=RacingUITheme.Rect(host,"OnPlay",Vector2.zero,Vector2.one);
        RectTransform result=RacingUITheme.Rect(host,"Result",Vector2.zero,Vector2.one);
        ScreenTransitionController transition=host.gameObject.AddComponent<ScreenTransitionController>();
        transition.Initialize(title,play,result,"","準備",1);
        RacingMenuButton start=title.Find("Player2Pedal").GetComponent<RacingMenuButton>();
        Require(start.IsActive() && start.targetGraphic.enabled && start.targetGraphic.raycastTarget,"Title start has no active pointer hit target.");
        foreach(RacingMenuButton button in title.GetComponentsInChildren<RacingMenuButton>(true))
            Require(button.targetGraphic.enabled && button.targetGraphic.raycastTarget,"Menu control has no pointer hit target: "+button.name);
        Require(title.GetComponentsInChildren<RacingMenuButton>(false).Length==1 && Text(start.transform,"Label")=="はじめる","Title must expose only はじめる.");
        ResultUIManager results=new ResultUIManager();results.Init(result,2);
        RaceSessionResult session=new RaceSessionResult();
        session.SetPlayerResult(0,new RaceResultRecord{playerNumber=1,finishPosition=1,totalRaceTime=40});
        session.SetPlayerResult(1,new RaceResultRecord{playerNumber=2,finishPosition=2,totalRaceTime=42.125f});
        results.ShowResults(session);
        Transform card=result.Find("ResultPresentation/ResultCard");
        Require(Text(card,"ResultRow2/Gap")=="+2.125" && Text(card,"PlacementRibbon/Ordinal")=="2ND","Result times and local medal do not match real data.");
        Require(card.Find("ResultMenu").GetComponentsInChildren<RacingMenuButton>(false).Length==2,"Result must have two actions.");
        Require(Text(card,"ResultMenu/Option1/Label")=="もう一度" && Text(card,"ResultMenu/Option2/Label")=="メインメニュー","Result action labels are wrong.");
        foreach(RacingMenuButton button in card.Find("ResultMenu").GetComponentsInChildren<RacingMenuButton>(true))
            Require(button.targetGraphic.enabled && button.targetGraphic.raycastTarget,"Result action has no pointer hit target.");
        session.GetPlayerResult(1).didFinish=false;results.ShowResults(session);
        Require(Text(card,"ResultRow2/Time")=="DNF" && Text(card,"PlacementRibbon/Ordinal")=="DNF" && !card.Find("Medal/Artwork").gameObject.activeSelf,"DNF must not award a finish medal.");
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
