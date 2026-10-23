using System;
using UnityEngine;

[Serializable]
public class OnPlayUIManager
{
    private Transform trans;
    private CanvasGroup CG;
    [SerializeField] private UIPosition position = new UIPosition();
    [SerializeField] private UILap lap = new UILap();
    [SerializeField] private UITime time = new UITime();
    [SerializeField] private UISpeed speed = new UISpeed();
    [SerializeField] private UIMiniMap miniMap = new UIMiniMap();

    private bool initialized = false;
    private RacingLeaderboardUI leaderboard;
    private TMPro.TMP_Text fieldSize;
    private int displayedFieldSize = -1;
    private RectTransform driverNotice;
    private TMPro.TMP_Text driverNoticeText;
    private string displayedNotice;
    private ArcadeNoticeRail quiet;
    private ArcadeHUDTelemetry telemetry;
    private readonly LapComparison lapComparison=new LapComparison();
    private int comparedPlayer=-1;

    public UIPosition Position => position;
    public UILap Lap => lap;
    public UITime TimeView => time;
    public UISpeed Speed => speed;
    public UIMiniMap MiniMap => miniMap;
    public string DriverNotice => displayedNotice ?? "";

    public void Init(Transform parent, RaceCourse course, int playerIndex, int totalLaps = LapManager.DefaultGoalLaps)
    {
        trans = parent;
        if (parent == null)
        {
            Debug.LogWarning("OnPlayUIManager requires a parent Transform.");
            return;
        }

        if (position == null) position = new UIPosition();
        if (lap == null) lap = new UILap();
        if (time == null) time = new UITime();
        if (speed == null) speed = new UISpeed();
        if (miniMap == null) miniMap = new UIMiniMap();

        Transform hud = RacingHUDBuilder.Build(parent, playerIndex, totalLaps);
        quiet=ArcadeNoticeRail.Get(parent.parent);telemetry=hud.GetComponent<ArcadeHUDTelemetry>();
        driverNotice = NeonUI.Panel(hud, "DriverNotice", new Vector2(.32f,.58f), new Vector2(.70f,.72f));
        driverNoticeText = NeonUI.Text(driverNotice, "Text", "", new Vector2(.04f,.08f), new Vector2(.96f,.92f),
            32f, TMPro.TextAlignmentOptions.Center, true, RacingUITheme.Gold);
        driverNotice.gameObject.SetActive(false);
        displayedNotice = null;
        fieldSize = hud.Find("Position/Field")?.GetComponent<TMPro.TMP_Text>();
        leaderboard = hud.Find("Leaderboard")?.GetComponent<RacingLeaderboardUI>();
        position.Init(hud.Find("Position"));
        lap.Init(hud.Find("Lap"));
        time.Init(hud.Find("Time"));
        speed.Init(hud.Find("Speed"));
        // ミニマップはノードが無ければ自前で生成するため、OnPlay ルートごと渡します。
        // The approved HUD has no minimap; retain the data adapter for display ownership.
        initialized = true;
    }

    public void UpdateUI(int positionValue, int lapValue, float totalSeconds, float lapSeconds, float speedValue,
        float padBoostRemainingSeconds, float padBoostDurationSeconds, int leaderboardPosition = 0, float progressGap = 0f)
    {
        SetPosition(positionValue);

        SetLap(lapValue);
        SetTime(totalSeconds, lapSeconds);
        SetSpeed(speedValue);
        speed.UpdateBoostGauge(padBoostRemainingSeconds, padBoostDurationSeconds);
        miniMap.UpdateMarkers();
    }

    public void UpdateParticipants(System.Collections.Generic.IReadOnlyList<RaceParticipant> entries)
    {
        leaderboard?.SetParticipants(entries);
        if (fieldSize != null && displayedFieldSize != entries.Count)
        { fieldSize.text = "/ " + entries.Count; displayedFieldSize = entries.Count; }
    }

    public void UpdateDriverNotice(PlayerDriveInputGate gate, CarResetter resetter, bool racing, bool jumpAhead = false, string coachingNotice = "", bool slipstreamBoosting = false, string challengeNotice = "", string tubeNotice = "", bool autodrive = false)
    {
        string inputNotice = gate == null || gate.IsBlocked ? "" : DriverInputNotice.Connection(gate.Source);
        string pedalNotice = DriverInputNotice.Pedal(gate?.Source);
        string text = !racing || gate?.IsBlocked == true ? "" : inputNotice.Length > 0 ? inputNotice :
            resetter != null && resetter.IsRecoveryPending ? "コース復帰待ち" :
            gate != null && gate.NeedsPedalRelease ? DriverInputNotice.Release(gate.Source) :
            pedalNotice.Length > 0 ? pedalNotice :
            resetter != null && UnityEngine.Time.time - resetter.LastRecoveryTime < 1.5f ? "コースへ復帰しました" :

            !string.IsNullOrEmpty(challengeNotice) ? challengeNotice :
            !string.IsNullOrEmpty(tubeNotice) ? tubeNotice :
            !string.IsNullOrEmpty(coachingNotice) ? coachingNotice : slipstreamBoosting ? "追走で加速！" : autodrive ? "P2 自動運転テスト中" : "";
        quiet?.Set(0,racing&&jumpAhead?"この先ジャンプ":"");
        if (text == displayedNotice || driverNotice == null) return;
        displayedNotice = text;
        driverNoticeText.text = text;
        driverNotice.gameObject.SetActive(text.Length > 0);
    }

    public void SetLapDelta(float? seconds)=>telemetry?.SetLapDelta(seconds);
    public void UpdateLapComparison(int completedLaps,float fraction,float lapSeconds,float completedSeconds,int viewedPlayer=-1)
    {
        if(comparedPlayer!=viewedPlayer){lapComparison.Reset();comparedPlayer=viewedPlayer;}
        SetLapDelta(lapComparison.Sample(completedLaps,fraction,lapSeconds,completedSeconds));
    }

    public void SetMiniMapCars(Transform playerOneCar, Transform playerTwoCar)
    {
        miniMap.SetCars(playerOneCar, playerTwoCar);
    }

    public void SetActive(bool isActive)
    {
        trans.gameObject.SetActive(isActive);
    }

    private void SetPosition(int value)
    {
        position.SetPosition(value);
    }

    private void SetLap(int value)
    {
        lap.SetLap(value);
    }

    private void SetTime(float totalSeconds, float lapSeconds)
    {
        time.SetTotalTime(totalSeconds);
        time.SetLapTime(lapSeconds);
    }

    private void SetSpeed(float speedValue)
    {
        speed.UpdateSpeedMeter(speedValue, Time.deltaTime);
    }
}
