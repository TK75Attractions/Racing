using TMPro;
using UnityEngine;

/// <summary>Reads the viewed vehicle, including spectating, without synthesizing rpm from speed.</summary>
public sealed class ArcadeHUDTelemetry : MonoBehaviour
{
    private int player,shownRpm=-1,shownBoost=-1,shownLap1=-1,shownLap2=-1;
    private Transform viewedCar;
    private CarSoundController engine;
    private DebugMover mover;
    private CarItemEffects effects;
    private bool wasBoosting;
    private ArcadeDialGraphic tach;
    private TMP_Text rpm,boost,lap1,lap2;
    private GameObject lapTimes;
    public bool HasLapTimes {get;private set;}
    public void Configure(int index)
    {
        player=index;tach=transform.Find("Tachometer/Gauge").GetComponent<ArcadeDialGraphic>();
        rpm=transform.Find("Tachometer/Txt").GetComponent<TMP_Text>();boost=transform.Find("Tachometer/Boost/Value").GetComponent<TMP_Text>();lap1=transform.Find("LapTimes/Lap1Time").GetComponent<TMP_Text>();lap2=transform.Find("LapTimes/Lap2Time").GetComponent<TMP_Text>();lapTimes=lap1.transform.parent.gameObject;
    }
    private void LateUpdate()
    {
        var manager=Gmanager.Control;if(manager==null)return;
        int viewed=manager.GetAudioViewedPlayer(player);var car=manager.GetPlayerCarTransform(viewed);
        bool changed=car!=viewedCar;
        if(changed){viewedCar=car;engine=car!=null?car.GetComponentInChildren<CarSoundController>():null;mover=car!=null?car.GetComponent<DebugMover>():null;effects=car!=null?car.GetComponent<CarItemEffects>():null;tach.SnapNextSample();}
        float charge=mover!=null?Mathf.Max(mover.NormalizedDriftCharge,mover.NormalizedActiveBoostRemaining):0;
        if(effects!=null&&effects.IsRocketActive&&effects.RocketDuration>0)charge=Mathf.Max(charge,effects.RocketTimeRemaining/effects.RocketDuration);
        bool boosting=mover!=null&&(mover.IsDriftBoosting||mover.IsAccelerationPadBoosting||mover.IsSlipstreamBoosting)||effects!=null&&effects.IsRocketActive;
        if(boosting&&!wasBoosting&&!changed)tach.FlashBoost();wasBoosting=boosting;
        SetTelemetry(engine!=null?engine.CurrentRpm:0,charge);
    }
    public void SetTelemetry(float revolutions,float charge)
    {
        if(tach==null)return;tach.SetTargetReading(revolutions,Application.isPlaying);tach.SetTargetBoost(charge,Application.isPlaying);
        int next=Mathf.RoundToInt(revolutions/100);if(next!=shownRpm){rpm.SetText("{0:0}.{1:0}",next/10,next%10);shownRpm=next;}
        next=Mathf.RoundToInt(Mathf.Clamp01(charge)*100);if(next!=shownBoost){boost.SetText("BOOST  {0}%",next);shownBoost=next;}
    }
    public void SetLapTimes(int completedLaps,float firstLapSeconds,float secondLapSeconds)
    {
        HasLapTimes=completedLaps>=1;
        lapTimes.SetActive(HasLapTimes);
        if(!HasLapTimes)return;
        SetLapTime(lap1,firstLapSeconds,ref shownLap1);
        SetLapTime(lap2,secondLapSeconds,ref shownLap2);
    }
    private static void SetLapTime(TMP_Text text,float seconds,ref int shown)
    {
        int value=Mathf.FloorToInt(Mathf.Max(0f,seconds)*100f+.0001f);
        if(value==shown)return;shown=value;
        text.SetText("{0:00}:{1:00}.{2:00}",value/6000,value/100%60,value%100);
    }
}
