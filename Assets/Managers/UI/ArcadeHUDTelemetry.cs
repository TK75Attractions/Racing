using TMPro;
using UnityEngine;

/// <summary>Reads the viewed vehicle, including spectating, without synthesizing rpm from speed.</summary>
public sealed class ArcadeHUDTelemetry : MonoBehaviour
{
    private int player,shownRpm=-1,shownBoost=-1,shownDelta=int.MinValue;
    private Transform viewedCar;
    private CarSoundController engine;
    private DebugMover mover;
    private ArcadeDialGraphic tach;
    private TMP_Text rpm,boost,delta;
    public void Configure(int index)
    {
        player=index;tach=transform.Find("Tachometer/Gauge").GetComponent<ArcadeDialGraphic>();
        rpm=transform.Find("Tachometer/Txt").GetComponent<TMP_Text>();boost=transform.Find("Tachometer/Boost/Value").GetComponent<TMP_Text>();delta=transform.Find("LapDelta/Value").GetComponent<TMP_Text>();
    }
    private void LateUpdate()
    {
        var manager=Gmanager.Control;if(manager==null)return;
        int viewed=manager.GetAudioViewedPlayer(player);var car=manager.GetPlayerCarTransform(viewed);
        if(car!=viewedCar){viewedCar=car;engine=car!=null?car.GetComponentInChildren<CarSoundController>():null;mover=car!=null?car.GetComponent<DebugMover>():null;}
        float charge=mover!=null?Mathf.Max(mover.NormalizedDriftCharge,mover.AccelerationPadBoostDuration>0?mover.AccelerationPadBoostTimeRemaining/mover.AccelerationPadBoostDuration:0):0;
        SetTelemetry(engine!=null?engine.CurrentRpm:0,charge);
    }
    public void SetTelemetry(float revolutions,float charge)
    {
        if(tach==null)return;tach.SetReading(revolutions);tach.SetBoost(charge);
        int next=Mathf.RoundToInt(revolutions/100);if(next!=shownRpm){rpm.SetText("{0:0}.{1:0}",next/10,next%10);shownRpm=next;}
        next=Mathf.RoundToInt(Mathf.Clamp01(charge)*100);if(next!=shownBoost){boost.SetText("BOOST  {0}%",next);shownBoost=next;}
    }
    public void SetLapDelta(float? seconds)
    {
        int next=seconds.HasValue?Mathf.RoundToInt(seconds.Value*100):int.MinValue;if(next==shownDelta)return;shownDelta=next;
        if(next==int.MinValue)delta.text="—";else delta.SetText(next<0?"−{0:0}.{1:00}":"+{0:0}.{1:00}",Mathf.Abs(next)/100,Mathf.Abs(next)%100);
        delta.color=next<=0?ArcadeUI.For(transform).Secondary:ArcadeUI.Hex("ff876e");
    }
}
