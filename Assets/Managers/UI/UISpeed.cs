using System;
using UnityEngine;
using TMPro;

[Serializable]
public class UISpeed
{
    [SerializeField] private GameObject root;
    private RectTransform meter;
    private TMP_Text speedText;

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

        if (speedText == null)
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
        if (speedText == null || meter == null) return;

        UpdateSpeedText(speed);
        UpdateMeter(speed, dt);
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
