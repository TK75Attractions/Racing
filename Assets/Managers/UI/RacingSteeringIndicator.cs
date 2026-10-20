using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Full-width handle travel with live speed-limited ends and the applied steering cursor.</summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class RacingSteeringIndicator : MaskableGraphic
{
    private int player;
    private Transform viewedCar;
    private DebugMover mover;
    private TMP_Text caption;
    private float requested, applied, allowed = 1f;
    private bool limited;
    private bool turnBraking;
    public float AllowedFraction => allowed;
    public float AppliedFraction => applied;
    public float RequestedFraction => requested;

    public void Configure(int index, TMP_Text text)
    {
        player = index;
        caption = text;
        raycastTarget = false;
        SetState(0f, 0f, 1f);
    }

    private void LateUpdate()
    {
        Gmanager manager = Gmanager.Control;
        if (manager == null) return;
        Transform car = manager.GetPlayerCarTransform(manager.GetAudioViewedPlayer(player));
        if (car != viewedCar)
        {
            viewedCar = car;
            mover = car != null ? car.GetComponent<DebugMover>() : null;
        }
        if (mover == null) { SetState(0f, 0f, 1f); return; }
        float range = Mathf.Max(.0001f, mover.SteeringInputRange);
        float gain = Mathf.Max(.0001f, mover.SteeringSpeedMultiplier);
        // Invert only the speed gain: the cursor shares the handle scale with the limit markers.
        SetState(mover.RawSteeringInput / range,
            mover.AppliedSteeringAngle / Mathf.Max(.0001f, mover.SteeringMaximumAngle * gain),
            mover.SteeringInputLimit / range);
    }

    public void SetState(float raw, float actual, float limit)
    {
        float nextLimit = Mathf.Clamp01(limit);
        float nextRaw = Mathf.Clamp(raw, -1f, 1f);
        float nextApplied = Mathf.Clamp(actual, -nextLimit, nextLimit);
        if (!Mathf.Approximately(requested, nextRaw) || !Mathf.Approximately(applied, nextApplied) ||
            !Mathf.Approximately(allowed, nextLimit))
        {
            requested = nextRaw; applied = nextApplied; allowed = nextLimit;
            SetVerticesDirty();
        }
        bool nextLimited = allowed < .98f;
        bool nextTurnBraking = RaceFeatureController.Enabled(RaceFeature.HighSpeedCornering);
        if (caption != null && (limited != nextLimited || turnBraking != nextTurnBraking || string.IsNullOrEmpty(caption.text)))
            caption.text = !nextLimited ? "ハンドル / 中央が直進" : nextTurnBraking
                ? "高速時は深く切ると減速 / 暗い範囲は切れ角上限"
                : "高速時は操舵角を制限 / 暗い範囲は上限外";
        limited = nextLimited;
        turnBraking = nextTurnBraking;
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        Rect r = rectTransform.rect;
        if (r.width < 2f || r.height < 2f) return;
        float left = r.xMin + 24f, right = r.xMax - 24f;
        float middle = (left + right) * .5f, half = (right - left) * .5f;
        float y = r.yMin + r.height * .43f;
        float aa = 1f / Mathf.Max(.1f, canvas != null ? canvas.scaleFactor : 1f);
        float low = middle - half * allowed, high = middle + half * allowed;
        Color active = new Color(.25f, .88f, 1f, .90f);
        Color dim = new Color(.48f, .65f, .72f, .16f);
        // A dark backing keeps the broad band readable over bright road surfaces.
        Line(vh, left, right, y, 17f, new Color(.005f, .015f, .035f, .8f), aa);
        Line(vh, left, low, y, 10f, dim, aa);
        Line(vh, high, right, y, 10f, dim, aa);
        Line(vh, low, high, y, 10f, active, aa);
        for (int i = -10; i <= 10; i++)
        {
            float fraction = i / 10f, x = middle + half * fraction;
            Color tint = Mathf.Abs(fraction) > allowed ? dim : active;
            RacingPanelGraphic.Line(vh, new Vector2(x, y - 6f), new Vector2(x, y + (i == 0 ? 11f : 6f)), 2f, tint, aa);
        }
        if (limited)
            for (int side = 0; side < 2; side++)
            {
                float x = side == 0 ? low : high;
                RacingPanelGraphic.Line(vh, new Vector2(x, y - 10f), new Vector2(x, y + 10f), 3f, active, aa);
            }
        float requestedX = middle + half * requested;
        Color rawColor = Mathf.Abs(requested) > allowed + .01f ? new Color(1f, .7f, .4f, .65f) : new Color(.85f, .94f, 1f, .45f);
        RacingPanelGraphic.Line(vh, new Vector2(requestedX, y + 12f), new Vector2(requestedX, y + 19f), 4f, rawColor, aa);
        float cursor = middle + half * applied;
        Line(vh, middle, cursor, y, 14f, NeonUI.Cyan, aa);
        RacingPanelGraphic.Line(vh, new Vector2(cursor, y - 12f), new Vector2(cursor, y + 12f), 11f, new Color(.005f, .015f, .035f, .9f), aa);
        RacingPanelGraphic.Line(vh, new Vector2(cursor, y - 11f), new Vector2(cursor, y + 11f), 7f, Color.white, aa);
    }

    private static void Line(VertexHelper vh, float from, float to, float y, float width, Color tint, float aa)
    {
        if (Mathf.Abs(to - from) < .01f) return;
        RacingPanelGraphic.Line(vh, new Vector2(from, y), new Vector2(to, y), width, tint, aa);
    }
}
