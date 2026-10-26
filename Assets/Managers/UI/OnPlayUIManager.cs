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
    private readonly DriverNoticeQueue notices = new DriverNoticeQueue();
    private TMPro.TMP_Text assistance;
    private RacingIconGraphic assistanceIcon;
    private int player;

    public UIPosition Position => position;
    public UILap Lap => lap;
    public UITime TimeView => time;
    public UISpeed Speed => speed;
    public UIMiniMap MiniMap => miniMap;
    public string DriverNotice => displayedNotice ?? "";

    public void Init(Transform parent, RaceCourse course, int playerIndex, int totalLaps = LapManager.DefaultGoalLaps)
    {
        trans = parent;
        player = playerIndex;
        notices.Clear();
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
        RectTransform aid = NeonUI.Panel(hud, "Assistance", new Vector2(.72f,.61f), new Vector2(.92f,.655f));
        assistanceIcon = NeonUI.Icon(aid, "Icon", RacingIconGraphic.Icon.Gear, new Vector2(.02f,.12f), new Vector2(.09f,.88f));
        assistance = RacingHUDStyle.Label(aid, "Text", "", .12f,.04f,.98f,.96f, RacingHUDStyle.SecondarySize, RacingHUDStyle.Text);
        aid.gameObject.SetActive(false);
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
        float now = UnityEngine.Time.unscaledTime;
        Gmanager manager = Gmanager.Control;
        Transform car = manager != null ? manager.GetPlayerCarTransform(player) : null;
        if (!racing || gate?.IsBlocked == true)
        {
            notices.Clear();
            quiet?.Set(0, ""); quiet?.Set(4, "");
            car?.GetComponent<DriftCoachingUI>()?.Notices.Clear();
            car?.GetComponent<LandingRewardController>()?.Notices.Clear();
            car?.GetComponent<TubeChargeLineController>()?.Notices.Clear();
            displayedNotice = "";
            if (driverNotice != null) driverNotice.gameObject.SetActive(false);
            if (assistance != null) assistance.transform.parent.gameObject.SetActive(false);
            return;
        }
        Condition("connection", gate != null && !gate.IsConnected, DriverInputNotice.Connection(gate?.Source), DriverNoticePriority.Critical, now);
        Condition("recovery", resetter != null && resetter.IsRecoveryPending, "コース復帰待ち", DriverNoticePriority.Critical, now);
        Condition("pedal", gate != null && gate.IsConnected && gate.NeedsPedalRelease, DriverInputNotice.Release(gate?.Source), DriverNoticePriority.Critical, now);
        Condition("pedalMode", !string.IsNullOrEmpty(DriverInputNotice.Pedal(gate?.Source)), DriverInputNotice.Pedal(gate?.Source), DriverNoticePriority.Guidance, now);
        Condition("returned", resetter != null && UnityEngine.Time.time - resetter.LastRecoveryTime < 1.5f, "コースへ復帰しました", DriverNoticePriority.Guidance, now);
        Condition("jump", jumpAhead, "この先ジャンプ：まっすぐ加速", DriverNoticePriority.Safety, now);
        UpdateRoadNotices(manager, car, now);
        if (car != null)
        {
            var coach = car.GetComponent<DriftCoachingUI>();
            var landing = car.GetComponent<LandingRewardController>();
            var tube = car.GetComponent<TubeChargeLineController>();
            if (RaceFeatureController.Enabled(RaceFeature.DriftCoaching)) coach?.Notices.Drain(notices, "coaching", now);
            else notices.Cancel("coaching");
            if (RaceFeatureController.Enabled(RaceFeature.LandingFeedback)) landing?.Notices.Drain(notices, "landing", now);
            else notices.Cancel("landing");
            if (RaceFeatureController.Enabled(RaceFeature.TubeChargeLines)) tube?.Notices.Drain(notices, "tubeSuccess", now);
            else notices.Cancel("tubeSuccess");
            Condition("tube", !string.IsNullOrEmpty(tube?.Guidance), tube?.Guidance, DriverNoticePriority.Guidance, now);
        }
        else
        {
            Condition("coaching", !string.IsNullOrEmpty(coachingNotice), coachingNotice,
                coachingNotice == "ドリフト加速！" ? DriverNoticePriority.Success : DriverNoticePriority.Guidance, now);
            Condition("landing", !string.IsNullOrEmpty(challengeNotice), challengeNotice, DriverNoticePriority.Success, now);
            Condition("tube", !string.IsNullOrEmpty(tubeNotice), tubeNotice,
                tubeNotice == "ライン成功！チャージ増加" ? DriverNoticePriority.Success : DriverNoticePriority.Guidance, now);
        }
        Condition("slipstream", slipstreamBoosting, "追走で加速！", DriverNoticePriority.Success, now);
        UpdateAssistance(car, autodrive);
        DriverNoticeQueue.Notice current = notices.Tick(now);
        string text = current?.Text ?? "";
        bool critical = current != null && current.Priority == DriverNoticePriority.Critical;
        bool safety = current != null && current.Priority >= DriverNoticePriority.Safety;
        quiet?.Set(0, !critical && !safety ? text : "");
        quiet?.Set(4, !critical && safety ? text : "");
        if (text == displayedNotice || driverNotice == null) return;
        displayedNotice = text;
        RacingHUDStyle.SetText(driverNoticeText, text, true);
        driverNotice.gameObject.SetActive(critical);
    }

    private void Condition(string key, bool active, string text, DriverNoticePriority priority, float now)
    {
        if (active) notices.Submit(key, text, priority, now, condition: true);
        else notices.Release(key, now);
    }

    private void UpdateRoadNotices(Gmanager manager, Transform car, float now)
    {
        bool wrong = false, curve = false;
        RaceCourse course = manager != null ? manager.course : null;
        if (course != null && course.HasValidPath && car != null)
        {
            float progress = course.GetProgressDistance(car.position);
            float sign = manager.CourseProgressSign;
            if (course.TryGetSampleAtProgress(progress, out RaceCourse.CourseSample here))
            {
                DebugMover mover = car.GetComponent<DebugMover>();
                wrong = mover != null && mover.SpeedMetersPerSecond > 2f && Vector3.Dot(car.forward, here.forward * sign) < -.35f;
                // Sample successive tangents: endpoint angles alone miss S bends.
                Vector3 previous = here.forward;
                for (int i = 1; i <= 4; i++)
                    if (course.TryGetSampleAtProgress(progress + sign * i * 12f, out RaceCourse.CourseSample ahead))
                    {
                        curve |= course.HasRoadAtProgress(progress + sign * i * 12f) && Vector3.Angle(previous, ahead.forward) / 12f > 1.5f;
                        previous = ahead.forward;
                    }
            }
        }
        Condition("wrongWay", wrong, "逆走：進行方向を確認", DriverNoticePriority.Danger, now);
        Condition("curve", curve && !wrong, "急カーブ：手前で減速", DriverNoticePriority.Safety, now);
    }

    private void UpdateAssistance(Transform car, bool auto)
    {
        if (assistance == null) return;
        var mover = car != null ? car.GetComponent<DebugMover>() : null;
        var assist = car != null ? car.GetComponent<CourseSteeringAssist>() : null;
        bool correcting = assist != null && Mathf.Abs(assist.CorrectionDegrees) > .1f;
        bool braking = mover != null && mover.CorneringBrakingAcceleration > .1f;
        string text = auto ? "AUTO / 自動運転" : "";
        if (correcting) text += (text.Length > 0 ? "  /  " : "") + "アシスト";
        if (braking) text += (text.Length > 0 ? "  /  " : "") + "操舵減速";
        assistance.transform.parent.gameObject.SetActive(text.Length > 0);
        if (assistance.text != text) RacingHUDStyle.SetText(assistance, text);
        var symbol = auto ? RacingIconGraphic.Icon.Car : braking ? RacingIconGraphic.Icon.Decelerate : RacingIconGraphic.Icon.Gear;
        if (assistanceIcon.symbol != symbol) { assistanceIcon.symbol = symbol; assistanceIcon.SetVerticesDirty(); }
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
