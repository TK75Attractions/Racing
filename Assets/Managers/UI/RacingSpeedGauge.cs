/// <summary>Speed instrument shares its geometry with the tachometer.</summary>
public sealed class RacingSpeedGauge : ArcadeDialGraphic
{
    public void SetSpeed(float speed)=>SetTargetReading(speed, UnityEngine.Application.isPlaying);
}
