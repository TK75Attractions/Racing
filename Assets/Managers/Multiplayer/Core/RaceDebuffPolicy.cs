/// <summary>Do not pile onto the last active racer in a field with CPUs. Duels retain contact items.</summary>
public static class RaceDebuffPolicy
{
    public static bool IsProtected(int position, int fieldSize, int lastRacingPosition)
        => fieldSize > 2 && position > 0 && position == lastRacingPosition;

    public static bool CanTarget(int ownerPosition, int targetPosition, int fieldSize, int lastRacingPosition)
        => targetPosition > 0 && targetPosition < ownerPosition &&
            !IsProtected(targetPosition, fieldSize, lastRacingPosition);
}
