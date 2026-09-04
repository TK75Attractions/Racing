using System;

[Serializable]
public class RaceResultRecord
{
    public int playerNumber;
    public int finishPosition;
    public bool didFinish = true;
    public string carName;
    public int completedLaps;
    public int goalLap;
    public float totalRaceTime;
    public float finalLapTime;
    public float bestLapTime;
}

[Serializable]
public class RaceSessionResult
{
    public const int PlayerCount = 2;
    public RaceResultRecord[] playerResults = new RaceResultRecord[PlayerCount];

    public void SetPlayerResult(int playerIndex, RaceResultRecord result)
    {
        EnsurePlayerArray();
        if (playerIndex >= 0 && playerIndex < playerResults.Length)
        {
            playerResults[playerIndex] = result;
        }
    }

    public RaceResultRecord GetPlayerResult(int playerIndex)
    {
        EnsurePlayerArray();
        return playerIndex >= 0 && playerIndex < playerResults.Length
            ? playerResults[playerIndex]
            : null;
    }

    public RaceResultRecord GetResultAtPosition(int position)
    {
        EnsurePlayerArray();
        foreach (RaceResultRecord result in playerResults)
        {
            if (result != null && result.finishPosition == position)
            {
                return result;
            }
        }

        return null;
    }

    private void EnsurePlayerArray()
    {
        if (playerResults == null || playerResults.Length != PlayerCount)
        {
            playerResults = new RaceResultRecord[PlayerCount];
        }
    }
}
