using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Measured road-speed acceleration: clockwise cyan, counterclockwise red.</summary>
[DefaultExecutionOrder(100)]
[RequireComponent(typeof(CanvasRenderer))]
public sealed class RacingAccelerationGauge : MaskableGraphic
{
    private const int SegmentsPerSide = 30;
    private const float FullScaleAcceleration = 20f;
    private const float ResponseSeconds = .12f;
    private int player, displayedSegments;
    private Transform viewedCar;
    private DebugMover mover;
    private Rigidbody body;
    private TMP_Text caption;
    private bool hasSpeed;
    private float previousSpeed, filteredAcceleration;

    public float AccelerationMetersPerSecondSquared => filteredAcceleration;
    public float SignedAmount => displayedSegments / (float)SegmentsPerSide;

    public void Configure(int index, TMP_Text text)
    {
        player = index;
        caption = text;
        raycastTarget = false;
        ResetMeasurement();
    }

    private void FixedUpdate()
    {
        Gmanager manager = Gmanager.Control;
        if (manager == null || !manager.IsDrivingEnabled) { ResetMeasurement(); return; }
        Transform car = manager.GetPlayerCarTransform(manager.GetAudioViewedPlayer(player));
        if (car != viewedCar)
        {
            viewedCar = car;
            body = car != null ? car.GetComponent<Rigidbody>() : null;
            mover = car != null ? car.GetComponent<DebugMover>() : null;
            ResetMeasurement();
        }
        if (body == null) { ResetMeasurement(); return; }
        // DebugMover follows the road plane and excludes falling velocity while airborne.
        float speed = mover != null ? mover.SpeedMetersPerSecond : Vector3.ProjectOnPlane(body.linearVelocity, Vector3.up).magnitude;
        SampleSpeed(speed, Time.fixedDeltaTime, mover != null && mover.IsInputSuppressed);
    }

    protected override void OnDisable()
    {
        ResetMeasurement();
        base.OnDisable();
    }

    public void ResetMeasurement()
    {
        hasSpeed = false;
        previousSpeed = 0f;
        filteredAcceleration = 0f;
        SetAcceleration(0f);
    }

    public void SampleSpeed(float metersPerSecond, float deltaTime, bool reset = false)
    {
        if (float.IsNaN(metersPerSecond) || float.IsInfinity(metersPerSecond) ||
            float.IsNaN(deltaTime) || float.IsInfinity(deltaTime) || deltaTime <= 0f)
        {
            ResetMeasurement();
            return;
        }
        float speed = Mathf.Max(0f, metersPerSecond);
        if (reset || !hasSpeed)
        {
            previousSpeed = speed;
            hasSpeed = true;
            filteredAcceleration = 0f;
            SetAcceleration(0f);
            return;
        }
        float acceleration = (speed - previousSpeed) / deltaTime;
        previousSpeed = speed;
        // Bound transient impulses before filtering so impacts cannot leave a long-lived full gauge.
        filteredAcceleration = Mathf.Lerp(filteredAcceleration, Mathf.Clamp(acceleration, -40f, 40f),
            1f - Mathf.Exp(-deltaTime / ResponseSeconds));
        SetAcceleration(filteredAcceleration);
    }

    public void SetAcceleration(float metersPerSecondSquared)
    {
        float value = float.IsNaN(metersPerSecondSquared) || float.IsInfinity(metersPerSecondSquared) ? 0f : metersPerSecondSquared;
        int next = Mathf.Abs(value) < .35f ? 0 : Mathf.RoundToInt(Mathf.Clamp(value / FullScaleAcceleration, -1f, 1f) * SegmentsPerSide);
        if (next != displayedSegments)
        {
            displayedSegments = next;
            SetVerticesDirty();
        }
        if (caption != null)
        {
            string text = next > 0 ? "加速" : next < 0 ? "減速" : "一定速";
            if (caption.text != text) caption.text = text;
            caption.color = next > 0 ? NeonUI.Cyan : next < 0 ? NeonUI.Red : RacingHUDStyle.Muted;
        }
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        Rect r = rectTransform.rect;
        if (r.width < 2f || r.height < 2f) return;
        Vector2 center = new Vector2(r.center.x, r.yMin + r.height * .035f);
        float radius = Mathf.Min(r.width * .47f, r.height * .92f) + 25f;
        float aa = 1f / Mathf.Max(.1f, canvas != null ? canvas.scaleFactor : 1f);
        // Two quiet quadrants share their zero at twelve o'clock.
        for (int side = -1; side <= 1; side += 2)
        {
            Color tint = side < 0 ? NeonUI.Red : NeonUI.Cyan;
            for (int i = 0; i < SegmentsPerSide; i++)
            {
                Vector2 a = Point(center, radius, side * i / (float)SegmentsPerSide);
                Vector2 b = Point(center, radius, side * (i + 1) / (float)SegmentsPerSide);
                RacingPanelGraphic.Line(vh, a, b, 18f, new Color(.005f, .015f, .035f, .8f), aa);
                RacingPanelGraphic.Line(vh, a, b, 10f, new Color(tint.r, tint.g, tint.b, .18f), aa);
                if (displayedSegments * side <= 0 || i >= Mathf.Abs(displayedSegments)) continue;
                RacingPanelGraphic.Line(vh, a, b, 22f, new Color(tint.r, tint.g, tint.b, .14f), aa);
                RacingPanelGraphic.Line(vh, a, b, 12f, tint, aa);
            }
        }
        Vector2 zero = Point(center, radius, 0f);
        RacingPanelGraphic.Line(vh, zero + Vector2.down * 9f, zero + Vector2.up * 9f, 3f, Color.white, aa);
        if (displayedSegments == 0) return;
        float amount = SignedAmount;
        Vector2 tip = Point(center, radius, amount);
        float radians = (90f - amount * 90f) * Mathf.Deg2Rad;
        Vector2 tangent = new Vector2(Mathf.Sin(radians), -Mathf.Cos(radians)) * Mathf.Sign(amount);
        Vector2 normal = new Vector2(-tangent.y, tangent.x);
        Color active = displayedSegments > 0 ? NeonUI.Cyan : NeonUI.Red;
        RacingPanelGraphic.Line(vh, tip - tangent * 10f + normal * 7f, tip + tangent * 3f, 4f, active, aa);
        RacingPanelGraphic.Line(vh, tip - tangent * 10f - normal * 7f, tip + tangent * 3f, 4f, active, aa);
    }

    private static Vector2 Point(Vector2 center, float radius, float signedAmount)
    {
        float radians = (90f - signedAmount * 90f) * Mathf.Deg2Rad;
        return center + new Vector2(Mathf.Cos(radians), Mathf.Sin(radians)) * radius;
    }
}
