using System;
using UnityEngine;
using TMPro;
using UnityEngine.UI;

[Serializable]
public class UISpeed
{
    [SerializeField] private GameObject root;
    private RectTransform meter;
    private TMP_Text speedText;
    private RacingSpeedGauge gauge;
    private GameObject boostRoot;
    private TMP_Text boostRemainingText;
    private RectTransform boostFill;
    private RacingBoostGraphic boostBar;
    private RacingBoostGraphic boostScreen;
    private Image boostGlow;
    private float screenIntensity;
    private float entryFlash;
    private bool wasBoosting;
    private NeonRingGraphic boostBadge;
    private int displayedSpeed = int.MinValue, displayedBoostTenths = -1;

    [SerializeField] private float speedVelocity = 0f;
    [SerializeField] private float speedValue = 0f;

    private const float NeedleAccel = 80f;
    private const float MaxNeedleAngle = 103f;
    private const float friction = 160;
    private const float MaxSpeed = 180f;

    public void Init(Transform parent)
    {
        if (parent != null)
        {
            root = parent.gameObject;
        }

        Transform rootTransform = root != null ? root.transform : parent;
        if (rootTransform == null)
        {
            return;
        }

        boostBadge = rootTransform.parent.Find("BoostBadge")?.GetComponent<NeonRingGraphic>();
        gauge = rootTransform.GetComponentInChildren<RacingSpeedGauge>(true);
        Transform boostTransform = rootTransform.parent != null ? rootTransform.parent.Find("PadBoost") : null;
        boostRoot = boostTransform != null ? boostTransform.gameObject : null;
        boostRemainingText = boostTransform != null ? boostTransform.Find("Remaining")?.GetComponent<TMP_Text>() : null;
        boostFill = boostTransform != null ? boostTransform.Find("Fill")?.GetComponent<RectTransform>() : null;
        boostBar = boostFill != null ? boostFill.GetComponent<RacingBoostGraphic>() : null;
        boostGlow = boostTransform != null ? boostTransform.Find("Glow")?.GetComponent<Image>() : null;
        Transform screenTransform = rootTransform.parent != null ? rootTransform.parent.Find("BoostOverlay") : null;
        boostScreen = screenTransform != null ? screenTransform.GetComponent<RacingBoostGraphic>() : null;
        displayedSpeed = int.MinValue; displayedBoostTenths = -1;
        screenIntensity = 0f;
        entryFlash = 0f;
        wasBoosting = false;
        if (boostRoot != null) boostRoot.SetActive(false);
        if (boostScreen != null) boostScreen.gameObject.SetActive(false);
        if (meter == null)
        {
            Transform meterTransform = rootTransform.Find("parent");
            if (meterTransform == null)
            {
                meterTransform = rootTransform.Find("meter");
            }

            meter = meterTransform != null
                ? meterTransform.GetComponent<RectTransform>()
                : rootTransform.GetComponentInChildren<RectTransform>(true);
        }

        if (rootTransform != null)
        {
            Transform textTransform = rootTransform.Find("Txt");
            speedText = textTransform != null
                ? textTransform.GetComponent<TMP_Text>()
                : rootTransform.GetComponentInChildren<TMP_Text>(true);
        }

        UpdateSpeedMeter(speedValue, 0f);
    }

    public void UpdateSpeedMeter(float speed, float dt)
    {
        if (speedText == null) return;

        UpdateSpeedText(speed);
        if (gauge != null) gauge.SetSpeed(speed);
        else UpdateMeter(speed, dt);
    }

    public void UpdateBoostGauge(float remainingSeconds, float durationSeconds)
    {
        bool active = remainingSeconds > 0f && durationSeconds > 0f;
        if (active && !wasBoosting) entryFlash = 1f;
        wasBoosting = active;
        float dt = Time.deltaTime;
        screenIntensity = Mathf.MoveTowards(screenIntensity, active ? 1f : 0f, dt * (active ? 8f : 3f));
        entryFlash = Mathf.MoveTowards(entryFlash, 0f, dt * 2.5f);
        float phase = Mathf.Repeat(Time.time * 0.75f, 1f);
        if (boostScreen != null)
        {
            boostScreen.gameObject.SetActive(screenIntensity > 0.001f);
            if (boostScreen.gameObject.activeSelf)
                boostScreen.SetEffect(screenIntensity, entryFlash, phase);
        }

        boostBadge?.SetAmount(active ? Mathf.Clamp01(remainingSeconds / durationSeconds) : 0f);
        if (boostRoot == null) return;
        boostRoot.SetActive(active);
        if (!active) return;

        int tenths = Mathf.CeilToInt(remainingSeconds * 10f);
        if (boostRemainingText != null && tenths != displayedBoostTenths)
        {
            boostRemainingText.SetText("{0:0}.{1:0}秒", tenths / 10, tenths % 10);
            displayedBoostTenths = tenths;
        }
        if (boostFill != null)
        {
            Vector2 anchorMax = boostFill.anchorMax;
            anchorMax.x = Mathf.Lerp(0.07f, 0.93f, Mathf.Clamp01(remainingSeconds / durationSeconds));
            boostFill.anchorMax = anchorMax;
        }
        if (boostBar != null) boostBar.SetEffect(1f, entryFlash, phase);
        if (boostGlow != null)
            boostGlow.color = new Color(1f, .025f, .39f,
                .10f + .04f * Mathf.Sin(phase * Mathf.PI * 2f) + .08f * entryFlash);
    }

    private void UpdateSpeedText(float speed)
    {
        if (speedText != null)
        {
            int value = Mathf.RoundToInt(speed);
            if (displayedSpeed == value) return;
            speedText.SetText("{0}", value);
            displayedSpeed = value;
        }
    }

    private void UpdateMeter(float speed, float dt)
    {
        float d = speed - speedValue;
        speedVelocity += (d > 0 ? 1 : -1) * NeedleAccel * dt;
        speedValue += speedVelocity * dt;

        if (speedVelocity > 0 && speedValue > speed) speedVelocity += 1.6f * d * NeedleAccel * dt;
        if (speedVelocity < 0 && speedValue < speed) speedVelocity += 1.6f * d * NeedleAccel * dt;

        if (speedValue < 0) speedValue = 0;
        if (speedValue > MaxSpeed) speedValue = MaxSpeed;

        if (meter != null)
        {
            float angle = (2 * (speedValue / MaxSpeed) - 1) * MaxNeedleAngle;
            meter.localRotation = Quaternion.Euler(0f, 0f, -angle);
        }
    }

    public void SetActive(bool isActive)
    {
        if (root != null)
        {
            root.SetActive(isActive);
        }
    }
}
