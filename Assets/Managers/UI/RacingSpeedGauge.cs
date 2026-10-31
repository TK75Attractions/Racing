using TMPro;
using UnityEngine;

/// <summary>Speed instrument extends its range while the independent high-speed switch is enabled.</summary>
public sealed class RacingSpeedGauge : ArcadeDialGraphic
{
    public void SetSpeed(float speed)
    {
        float maximum=RaceFeatureController.Enabled(RaceFeature.HighSpeedDrive) && Gmanager.Control?.IsPreparing!=true ? 360f : 240f;
        if (SpeedScaleMaximum!=maximum)
        {
            SpeedScaleMaximum=maximum;
            int[] original={0,60,120,180,240};
            for(int i=0;i<original.Length;i++)
            {
                var label=transform.parent?.Find("Tick"+original[i])?.GetComponent<TMP_Text>();
                if(label!=null)label.text=(maximum*i/4).ToString("0");
            }
            var cap=transform.parent?.Find("Maximum")?.GetComponent<TMP_Text>();
            if(cap!=null)cap.text="MAX "+maximum.ToString("0");
            SetReading(Mathf.Min(speed,maximum));
            SetVerticesDirty();
        }
        SetTargetReading(speed,Application.isPlaying);
    }
}
