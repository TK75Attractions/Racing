using UnityEngine;

/// <summary>
/// 触れるとドリフトチャージを増やすクリスタルです。チャージはドリフトを解放するまで保持されます。
/// チャージが既に満タンの車は取らずに通過するため、相手に残すか奪うかの駆け引きになります。
/// </summary>
[AddComponentMenu("Racing/Items/Charge Crystal")]
public sealed class ChargeCrystal : RaceItemPickup
{
    [Header("Charge")]
    [Tooltip("増やすチャージ量（最大チャージに対する割合）。1で満タンになります。")]
    [SerializeField, Range(0f, 1f)] private float chargeAmount = 1f;
    [SerializeField] private Color crystalColor = new Color(0.35f, 0.95f, 1f, 1f);

    protected override bool OnCollected(DebugMover mover, Rigidbody body, CarItemEffects effects)
    {
        if (!mover.AddDriftCharge(chargeAmount)) return false;
        effects?.NotifyChargeCollected();
        return true;
    }

    protected override Mesh CreateVisualMesh() => ItemVisualFactory.CreateCrystal(0.55f, 1.6f);

    protected override Material CreateVisualMaterial() =>
        ItemVisualFactory.CreateEmissiveMaterial("ChargeCrystalMaterial", crystalColor, 2.2f, 0.9f);

    protected override void AnimateVisual(float time)
    {
        float glow = 1.8f + Mathf.Sin(time * 5f) * 0.6f;
        ItemVisualFactory.SetEmissiveColor(VisualMaterial, crystalColor, glow);
    }
}
