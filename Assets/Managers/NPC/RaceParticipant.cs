using UnityEngine;

/// <summary>A race entry, independent of player input, screens and audio devices.</summary>
public sealed class RaceParticipant
{
    public int Index;
    public bool IsCpu;
    public GameObject Car;
    public Rigidbody Body;
    public CarItemEffects Effects;
    public NpcRaceDriver Driver;
    public RaceResultRecord Result;
    public int Position;
    public float Progress;
    private string label;
    public string Label => label ?? (label = IsCpu ? $"CPU {Index - 1}" : $"P{Index + 1}");
    public bool IsRacing => Body != null && Result == null;
}
