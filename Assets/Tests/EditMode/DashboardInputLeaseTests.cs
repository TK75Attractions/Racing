using NUnit.Framework;

public sealed class DashboardInputLeaseTests
{
    [Test] public void IndependentLeasesExpireAndDelayedPressCannotUndoRelease()
    {
        var p1 = new DashboardInputLease(); var p2 = new DashboardInputLease();
        Assert.IsTrue(p1.TryApply("page",1,"begin",DashboardInputButtons.Accelerate,0,out _));
        Assert.IsTrue(p2.TryApply("page",1,"begin",DashboardInputButtons.Right,0,out _));
        Assert.AreEqual(DashboardInputButtons.Accelerate,p1.Sample(.2));
        Assert.AreEqual(DashboardInputButtons.Right,p2.Sample(.2));
        Assert.IsTrue(p1.TryApply("page",3,"release",0,.3,out _));
        Assert.IsFalse(p1.TryApply("page",2,"hold",DashboardInputButtons.Accelerate,.4,out string reason));
        Assert.AreEqual("input_outdated",reason);Assert.IsFalse(p1.IsActive(.4));
        Assert.IsFalse(p2.IsActive(1.01));Assert.AreEqual(DashboardInputButtons.None,p2.Sample(1.01));
        Assert.IsFalse(p2.TryApply("page",2,"hold",DashboardInputButtons.Right,1.02,out reason));
        Assert.AreEqual("input_expired",reason);Assert.IsFalse(p2.IsActive(1.02));
    }
    [Test] public void OwnershipAndReacquisitionNeverReuseOldHeldInput()
    {
        var lease = new DashboardInputLease();
        Assert.IsTrue(lease.TryApply("first",1,"begin",DashboardInputButtons.Accelerate,0,out _));
        long generation=lease.Generation;
        Assert.IsFalse(lease.TryApply("second",1,"hold",0,.2,out string reason));Assert.AreEqual("input_busy",reason);
        Assert.IsFalse(lease.TryApply("second",2,"release",0,.3,out _));
        Assert.IsTrue(lease.IsActive(.3));
        Assert.IsTrue(lease.TryApply("second",3,"begin",0,1.1,out _));
        Assert.Greater(lease.Generation,generation);Assert.AreEqual(DashboardInputButtons.None,lease.Sample(1.2));
        Assert.IsFalse(lease.TryApply("first",2,"release",0,1.3,out _));
        Assert.AreEqual("second",lease.Owner(1.3));
    }
    [Test] public void PulsesAreOneSampleAndHeartbeatDoesNotRepeatThem()
    {
        var lease = new DashboardInputLease();
        Assert.IsTrue(lease.TryApply("page",1,"begin",DashboardInputButtons.Left,0,out _));
        Assert.IsTrue(lease.TryApply("page",2,"pulse",DashboardInputButtons.Reset,.1,out _));
        Assert.IsTrue(lease.TryApply("page",3,"hold",DashboardInputButtons.Left,.2,out _));
        Assert.AreEqual(DashboardInputButtons.Left|DashboardInputButtons.Reset,lease.Sample(.3));
        Assert.AreEqual(DashboardInputButtons.Left,lease.Sample(.4));
        Assert.IsTrue(lease.TryApply("page",4,"pulse",DashboardInputButtons.Ready,.5,out _));
        lease.Clear();Assert.AreEqual(DashboardInputButtons.None,lease.Sample(.6));
        Assert.IsFalse(lease.TryApply("page",3,"hold",DashboardInputButtons.Left,.7,out _));
    }
    [Test] public void InvalidKeysArgumentsAndTimestampsCannotAcquireControl()
    {
        var lease = new DashboardInputLease();
        Assert.IsFalse(lease.TryApply("page",1,"begin",DashboardInputButtons.Reset,0,out _));
        Assert.IsFalse(lease.TryApply("page",2,"pulse",DashboardInputButtons.Accelerate,0,out _));
        Assert.IsFalse(lease.TryApply("page",3,"release",DashboardInputButtons.Left,0,out _));
        Assert.IsFalse(lease.TryApply("",1,"hold",0,0,out _));
        Assert.IsFalse(lease.TryApply("page",0,"hold",0,0,out _));
        Assert.IsFalse(lease.TryApply("page",4,"hold",0,double.NaN,out _));
        Assert.IsFalse(lease.IsActive(0));
    }
}
