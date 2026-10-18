using System;

/// <summary>Arrival order includes CPUs; only human completion starts the human grace period.</summary>
public sealed class RaceParticipantSession
{
    private readonly int humanCount, participantCount;
    private readonly float graceSeconds, maximumSeconds;
    private int finishedCount;
    private float elapsed;
    public RaceSessionResult Result { get; private set; }
    public bool IsComplete { get; private set; }
    public bool HasTimedOut { get; private set; }
    public bool WaitingForSecondPlace { get; private set; }
    public float SecondPlaceTimeRemaining { get; private set; }
    public int UnfinishedPlayerIndex
    {
        get { for (int i = 0; i < humanCount; i++) if (Result.GetPlayerResult(i) == null) return i; return -1; }
    }
    public RaceParticipantSession(int humans, int cpus, float grace, float maximum)
    {
        humanCount = Math.Max(1, humans); participantCount = humanCount + Math.Max(0, cpus);
        graceSeconds = Math.Max(0, grace); maximumSeconds = Math.Max(1, maximum);
        Start();
    }
    public void Start()
    {
        Result = new RaceSessionResult(participantCount); finishedCount = 0; elapsed = 0;
        IsComplete = HasTimedOut = WaitingForSecondPlace = false; SecondPlaceTimeRemaining = 0;
    }
    public RaceFinishRegistration RegisterFinish(int index, RaceResultRecord record)
    {
        if (!CanRecord(index, record) || HasTimedOut) return RaceFinishRegistration.Ignored;
        record.playerNumber = index + 1; record.isCpu = index >= humanCount;
        record.finishPosition = ++finishedCount; record.didFinish = true;
        Result.SetPlayerResult(index, record); ReorderDnf();
        if (UnfinishedPlayerIndex < 0) { IsComplete = true; WaitingForSecondPlace = false; SecondPlaceTimeRemaining = 0; return RaceFinishRegistration.RaceComplete; }
        if (index < humanCount && !WaitingForSecondPlace)
        { WaitingForSecondPlace = true; SecondPlaceTimeRemaining = graceSeconds; return RaceFinishRegistration.FirstPlace; }
        return RaceFinishRegistration.Recorded;
    }
    public bool Tick(float dt)
    {
        if (IsComplete || HasTimedOut) return false;
        dt = Math.Max(0, dt); elapsed += dt;
        if (WaitingForSecondPlace) SecondPlaceTimeRemaining = Math.Max(0, SecondPlaceTimeRemaining - dt);
        if (elapsed < maximumSeconds && (!WaitingForSecondPlace || SecondPlaceTimeRemaining > 0)) return false;
        HasTimedOut = true; WaitingForSecondPlace = false; return true;
    }
    public bool RegisterDnf(int index, RaceResultRecord record) => HasTimedOut && RecordDnf(index, record);
    public bool RegisterWithdrawal(int index, RaceResultRecord record) => RecordDnf(index, record);
    // Called in progress order at human completion or timeout to classify remaining CPUs/DNFs.
    public bool CloseParticipant(int index, RaceResultRecord record) => RecordDnf(index, record, true);
    private bool CanRecord(int index, RaceResultRecord record) => !IsComplete && record != null && index >= 0 && index < participantCount && Result.GetPlayerResult(index) == null;
    private bool RecordDnf(int index, RaceResultRecord record, bool closing = false)
    {
        if (!(closing ? record != null && index >= 0 && index < participantCount && Result.GetPlayerResult(index) == null : CanRecord(index, record))) return false;
        record.playerNumber = index + 1; record.isCpu = index >= humanCount; record.didFinish = false;
        record.finishPosition = participantCount; Result.SetPlayerResult(index, record); ReorderDnf();
        if (UnfinishedPlayerIndex < 0) { IsComplete = true; WaitingForSecondPlace = false; SecondPlaceTimeRemaining = 0; }
        return true;
    }
    private void ReorderDnf()
    {
        int position = finishedCount;
        for (int rank = 1; rank <= participantCount; rank++)
        for (int i = 0; i < participantCount; i++)
        {
            RaceResultRecord r = Result.GetPlayerResult(i);
            if (r != null && !r.didFinish && r.finishPosition == rank) r.finishPosition = -(++position);
        }
        for (int i = 0; i < participantCount; i++)
        { RaceResultRecord r = Result.GetPlayerResult(i); if (r != null && r.finishPosition < 0) r.finishPosition = -r.finishPosition; }
    }
}
