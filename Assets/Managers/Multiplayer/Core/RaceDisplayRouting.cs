/// <summary>Reserve both player outputs before assigning the spectator feed.</summary>
public static class RaceDisplayRouting
{
    // Saved scene layouts may refer to a fourth monitor on another PC.
    // Keep that preference when present, otherwise use the first secondary screen.
    public static int PlayerTwoDisplay(int preferred, int connectedDisplays)
    {
        if (connectedDisplays < 2) return -1;
        return preferred >= 1 && preferred < connectedDisplays && preferred < 8 ? preferred : 1;
    }

    public static int BroadcastDisplay(int playerOne, int playerTwo)
    {
        if (playerOne != 2 && playerTwo != 2) return 2;
        for (int index = 0; index < 8; index++)
            if (index != playerOne && index != playerTwo) return index;
        return 2;
    }
}
