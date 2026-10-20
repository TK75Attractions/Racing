using NUnit.Framework;

public sealed class HitProtectionWindowTests
{
    [Test] public void HitsCannotExtendAnEffectOrTheFollowingProtection()
    {
        var window = new HitProtectionWindow();
        Assert.IsTrue(window.TryBegin(2.2f));
        window.Tick(1f);
        Assert.IsFalse(window.TryBegin(2.5f));
        window.Tick(1.2f);
        Assert.AreEqual(1f, window.ProtectionRemaining, .0001f);
        Assert.IsFalse(window.TryBegin(1f));
        window.Tick(1f);
        Assert.IsTrue(window.TryBegin(1f));
    }
    [Test] public void LongEffectsAndTimeStepsHaveBoundedProtection()
    {
        var window = new HitProtectionWindow();
        Assert.IsTrue(window.TryBegin(100));
        Assert.AreEqual(3, window.EffectRemaining);
        window.Tick(3.5f);
        Assert.AreEqual(.5f, window.ProtectionRemaining, .0001f);
        window.Tick(2f);
        Assert.IsTrue(window.CanAccept);
    }
    [Test] public void DisablingAndInvalidValuesNeverLeaveHiddenImmunity()
    {
        var window = new HitProtectionWindow();
        Assert.IsFalse(window.TryBegin(float.NaN));
        Assert.IsFalse(window.TryBegin(float.PositiveInfinity));
        window.TryBegin(2f); window.Clear();
        Assert.IsTrue(window.CanAccept);
        Assert.IsTrue(window.TryBegin(1f));
        window.Tick(-10f);
        Assert.AreEqual(1f, window.EffectRemaining);
    }
}
