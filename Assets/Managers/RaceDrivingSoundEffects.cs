using UnityEngine;

/// <summary>Human-owned driving cues, routed through the same player mix as UI and race announcements.</summary>
[DisallowMultipleComponent]
public sealed class RaceDrivingSoundEffects : MonoBehaviour
{
    private Gmanager manager;
    private RaceParticipant driver;
    private DebugMover mover;
    private CarItemEffects items;
    private CarCollisionSparks collisions;
    private bool drifting;
    private int chargeTier, completedLaps, boostMask;

    public void Configure(Gmanager race, RaceParticipant participant)
    {
        Unsubscribe();
        manager = race; driver = participant;
        mover = GetComponent<DebugMover>();
        items = GetComponent<CarItemEffects>();
        collisions = GetComponent<CarCollisionSparks>();
        if (mover != null) mover.DriftBoostReleased += OnDriftBoost;
        if (items != null)
        {
            items.ItemUsed += OnItem;
            items.ChargeCollected += OnCharge;
        }
        if (collisions != null) collisions.Impact += OnImpact;
        SyncTransitions();
    }

    private bool CanHear => manager != null && manager.state == Gmanager.State.Game &&
        driver != null && !driver.IsCpu && driver.IsRacing && Time.timeScale > 0f;
    private int BoostMask => mover == null ? 0 : (mover.IsAccelerationPadBoosting ? 1 : 0) |
        (mover.IsRocketBoosting ? 2 : 0) | (mover.IsSlipstreamBoosting ? 4 : 0);

    private void LateUpdate()
    {
        if (!CanHear || mover == null || !mover.isActiveAndEnabled)
        {
            SyncTransitions();
            return;
        }
        bool driftNow = mover.IsDrifting && !mover.IsInputSuppressed;
        if (driftNow && !drifting) Play(RaceSoundCue.TireSkid);
        int tier = mover.DriftChargeTier;
        if (driftNow && tier > chargeTier) Play(RaceSoundCue.DriftCharge);
        int boostNow = BoostMask;
        if ((boostNow & ~boostMask) != 0) Play(RaceSoundCue.Boost);
        int lap = manager.GetParticipantLap(driver);
        if (lap > completedLaps && manager.GoalLap > 1 && lap == manager.GoalLap - 1)
            Play(RaceSoundCue.FinalLap);
        drifting = driftNow; chargeTier = tier; boostMask = boostNow; completedLaps = lap;
    }

    private void SyncTransitions()
    {
        drifting = mover != null && mover.IsDrifting;
        chargeTier = mover != null ? mover.DriftChargeTier : 0;
        boostMask = BoostMask;
        completedLaps = manager != null && driver != null ? manager.GetParticipantLap(driver) : 0;
    }

    private void OnDriftBoost(float charge) { if (charge >= .1f) Play(RaceSoundCue.DriftBoost); }
    private void OnCharge()
    {
        chargeTier = mover != null ? mover.DriftChargeTier : 0;
        Play(RaceSoundCue.Charge);
    }
    private void OnItem(RaceItemType item)
    {
        Play(RaceSoundCue.ItemPickup);
        if (item == RaceItemType.Shield) Play(RaceSoundCue.Shield);
        // Rocket/pad/slipstream onset is detected once in LateUpdate.
    }
    private void OnImpact(float strength) { if (strength >= .18f) Play(RaceSoundCue.Impact); }
    private void Play(RaceSoundCue cue) { if (isActiveAndEnabled && CanHear) RaceSoundEffects.PlayForPlayer(cue, driver.Index); }

    private void Unsubscribe()
    {
        if (mover != null) mover.DriftBoostReleased -= OnDriftBoost;
        if (items != null) { items.ItemUsed -= OnItem; items.ChargeCollected -= OnCharge; }
        if (collisions != null) collisions.Impact -= OnImpact;
    }
    private void OnDestroy() => Unsubscribe();
}
