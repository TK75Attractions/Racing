using System;
using UnityEngine;

/// <summary>The production pickup trigger/visual lifecycle, scoped to one practice car.</summary>
public sealed class TutorialItemPickup : RaceItemPickup
{
    private Rigidbody owner;
    private Func<bool> collect;
    public void Configure(Rigidbody car, Func<bool> onCollected) { owner = car; collect = onCollected; }
    protected override bool OnCollected(DebugMover mover, Rigidbody body, CarItemEffects effects)
    {
        if (body != owner || effects == null || collect?.Invoke() != true) return false;
        effects.UseItem(RaceItemType.Shield, null);
        RaceSoundEffects.PlayForPlayer(RaceSoundCue.ItemPickup, effects.PlayerIndex);
        return true;
    }
    protected override Mesh CreateVisualMesh() => ItemVisualFactory.CreateBox(1.6f);
    protected override Material CreateVisualMaterial() => ItemVisualFactory.CreateEmissiveMaterial("Practice item box", Color.white, 1.3f, .65f);
    protected override Texture2D CreateVisualTexture() => ItemVisualFactory.CreateMysteryBoxTexture();
    protected override float GetVisualScale(float time) => 1f + .035f * Mathf.Sin(time * 3f);
    protected override Quaternion GetVisualTilt() => Quaternion.Euler(35f, 0f, 45f);
    protected override void AnimateVisual(float time) => ItemVisualFactory.SetEmissiveColor(VisualMaterial, Color.HSVToRGB(Mathf.Repeat(time / 2.5f, 1f), .14f, 1f), 1.15f + .3f * Mathf.Sin(time * 3f));
}
