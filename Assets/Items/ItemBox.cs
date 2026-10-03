using UnityEngine;

/// <summary>
/// 触れると順位に応じてアイテムを抽選し、その場で発動するアイテムボックスです。
/// 取られると一定時間消えるため、2人対戦では先に取った側が相手の分を奪えます。
/// </summary>
[AddComponentMenu("Racing/Items/Item Box")]
public sealed class ItemBox : RaceItemPickup
{
    [Header("Item Table")]
    [SerializeField] private RaceItemTable itemTable = new RaceItemTable();

    [Header("Box Visual")]
    [SerializeField, Min(0.2f)] private float boxSize = 1.4f;
    [Tooltip("色相が一周する時間（秒）。")]
    [SerializeField, Min(0.1f)] private float hueCycleSeconds = 2.5f;

    protected override bool OnCollected(DebugMover mover, Rigidbody body, CarItemEffects effects)
    {
        if (effects == null) return false;

        int racePosition = 1;
        CarItemEffects opponent = null;
        if (Gmanager.Control != null) Gmanager.Control.TryGetItemContext(body, out racePosition, out opponent);
        RaceItemType item = itemTable.Roll(racePosition, opponent != null, Random.value);
        if (item == RaceItemType.None) return false;

        effects.UseItem(item, opponent);
        return true;
    }

    protected override Mesh CreateVisualMesh() => ItemVisualFactory.CreateBox(boxSize);

    protected override Material CreateVisualMaterial() =>
        ItemVisualFactory.CreateEmissiveMaterial("ItemBoxMaterial", Color.white, 1.4f, 0.8f);

    // 角を下にして傾けると、どの向きからでも箱だと分かりやすくなります。
    protected override Quaternion GetVisualTilt() => Quaternion.Euler(35f, 0f, 45f);

    protected override void AnimateVisual(float time)
    {
        Color color = Color.HSVToRGB(Mathf.Repeat(time / hueCycleSeconds, 1f), 0.55f, 1f);
        ItemVisualFactory.SetEmissiveColor(VisualMaterial, color, 1.4f);
    }
}
