/// <summary>Reserve both player outputs before assigning the spectator feed.</summary>
public static class RaceDisplayRouting
{
    public static int BroadcastDisplay(int playerOne, int playerTwo)
    {
        if (playerOne != 2 && playerTwo != 2) return 2;
        for (int index = 0; index < 8; index++)
            if (index != playerOne && index != playerTwo) return index;
        return 2;
    }
}
