using System;

public enum RaceFinishRegistration
{
    Ignored,
    FirstPlace,
    RaceComplete
}

/// <summary>
/// Unityのシーンや物理状態に依存しない、2人レースの完走順と制限時間の状態機械です。
/// </summary>
public sealed class TwoPlayerRaceSession
{
    private readonly float secondPlaceTimeoutSeconds;
    private int finishedPlayerCount;

    public RaceSessionResult Result { get; private set; } = new RaceSessionResult();
    public bool IsStarted { get; private set; }
    public bool IsComplete { get; private set; }
    public bool WaitingForSecondPlace { get; private set; }
    public bool HasTimedOut { get; private set; }
    public float SecondPlaceTimeRemaining { get; private set; }

    public int UnfinishedPlayerIndex
    {
        get
        {
            for (int index = 0; index < RaceSessionResult.PlayerCount; index++)
            {
                if (Result.GetPlayerResult(index) == null)
                {
                    return index;
                }
            }

            return -1;
        }
    }

    public TwoPlayerRaceSession(float secondPlaceTimeoutSeconds)
    {
        this.secondPlaceTimeoutSeconds = Math.Max(0f, secondPlaceTimeoutSeconds);
    }

    public void Start()
    {
        Result = new RaceSessionResult();
        finishedPlayerCount = 0;
        IsStarted = true;
        IsComplete = false;
        WaitingForSecondPlace = false;
        HasTimedOut = false;
        SecondPlaceTimeRemaining = 0f;
    }

    public RaceFinishRegistration RegisterFinish(int playerIndex, RaceResultRecord result)
    {
        if (!IsStarted || IsComplete || !IsValidPlayerIndex(playerIndex) || result == null ||
            Result.GetPlayerResult(playerIndex) != null)
        {
            return RaceFinishRegistration.Ignored;
        }

        finishedPlayerCount++;
        result.playerNumber = playerIndex + 1;
        result.finishPosition = finishedPlayerCount;
        result.didFinish = true;
        Result.SetPlayerResult(playerIndex, result);

        if (finishedPlayerCount == 1)
        {
            WaitingForSecondPlace = true;
            SecondPlaceTimeRemaining = secondPlaceTimeoutSeconds;
            return RaceFinishRegistration.FirstPlace;
        }

        WaitingForSecondPlace = false;
        SecondPlaceTimeRemaining = 0f;
        IsComplete = true;
        return RaceFinishRegistration.RaceComplete;
    }

    public bool Tick(float deltaTime)
    {
        if (!WaitingForSecondPlace || IsComplete || HasTimedOut)
        {
            return false;
        }

        SecondPlaceTimeRemaining = Math.Max(0f, SecondPlaceTimeRemaining - Math.Max(0f, deltaTime));
        if (SecondPlaceTimeRemaining > 0f)
        {
            return false;
        }

        WaitingForSecondPlace = false;
        HasTimedOut = true;
        return true;
    }

    public bool RegisterDnf(int playerIndex, RaceResultRecord result)
    {
        if (!IsStarted || IsComplete || !HasTimedOut || !IsValidPlayerIndex(playerIndex) ||
            result == null || Result.GetPlayerResult(playerIndex) != null)
        {
            return false;
        }

        result.playerNumber = playerIndex + 1;
        result.finishPosition = 2;
        result.didFinish = false;
        Result.SetPlayerResult(playerIndex, result);
        IsComplete = true;
        return true;
    }

    private static bool IsValidPlayerIndex(int playerIndex)
    {
        return playerIndex >= 0 && playerIndex < RaceSessionResult.PlayerCount;
    }
}
