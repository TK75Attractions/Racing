using System;
using UnityEngine;
using TMPro;

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

        gauge = rootTransform.GetComponentInChildren<RacingSpeedGauge>(true);
        Transform boostTransform = rootTransform.parent != null ? rootTransform.parent.Find("PadBoost") : null;
        boostRoot = boostTransform != null ? boostTransform.gameObject : null;
        boostRemainingText = boostTransform != null ? boostTransform.Find("Remaining")?.GetComponent<TMP_Text>() : null;
        boostFill = boostTransform != null ? boostTransform.Find("Fill")?.GetComponent<RectTransform>() : null;
        if (boostRoot != null) boostRoot.SetActive(false);
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
        if (boostRoot == null) return;

        bool active = remainingSeconds > 0f && durationSeconds > 0f;
        boostRoot.SetActive(active);
        if (!active) return;

        if (boostRemainingText != null)
            boostRemainingText.text = (Mathf.Ceil(remainingSeconds * 10f) / 10f).ToString("F1") + "s";
        if (boostFill != null)
        {
            Vector2 anchorMax = boostFill.anchorMax;
            anchorMax.x = Mathf.Lerp(0.08f, 0.92f, Mathf.Clamp01(remainingSeconds / durationSeconds));
            boostFill.anchorMax = anchorMax;
        }
    }

    private void UpdateSpeedText(float speed)
    {
        if (speedText != null)
        {
            speedText.text = Mathf.RoundToInt(speed).ToString();
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
