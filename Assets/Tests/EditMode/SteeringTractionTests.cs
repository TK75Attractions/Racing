using NUnit.Framework;

public sealed class SteeringTractionTests
{
    [Test] public void SmallSlipIsLeftAloneAndExcessIsRecoveredSymmetrically()
    {
        Assert.AreEqual(0,SteeringTraction.ExcessSlip(1));
        Assert.AreEqual(0,SteeringTraction.ExcessSlip(-1));
        Assert.AreEqual(8.5f,SteeringTraction.ExcessSlip(10));
        Assert.AreEqual(-8.5f,SteeringTraction.ExcessSlip(-10));
    }
    [Test] public void GripCorrectionIsBoundedBySpeedAndTimeWithoutOvershooting()
    {
        float fast=SteeringTraction.RecoveryDegrees(20,60,.02f);
        float slow=SteeringTraction.RecoveryDegrees(20,20,.02f);
        Assert.Greater(slow,fast);Assert.Greater(fast,0);
        Assert.AreEqual(-fast,SteeringTraction.RecoveryDegrees(-20,60,.02f),.0001f);
        Assert.AreEqual(fast*2,SteeringTraction.RecoveryDegrees(20,60,.04f),.0001f);
        Assert.LessOrEqual(SteeringTraction.RecoveryDegrees(2,20,1),.5f);
    }
    [Test] public void YawFeedbackDampsRunawayRotationWithABoundedSymmetricResponse()
    {
        Assert.AreEqual(0,SteeringTraction.YawAcceleration(.5f,.5f));
        Assert.AreEqual(-30,SteeringTraction.YawAcceleration(5,.5f));
        Assert.AreEqual(30,SteeringTraction.YawAcceleration(-5,-.5f));
        Assert.Greater(SteeringTraction.YawAcceleration(0,.5f),0);
    }
    [Test] public void InvalidAndStationaryInputsCannotInjectMotion()
    {
        Assert.AreEqual(0,SteeringTraction.RecoveryDegrees(20,0,.02f));
        Assert.AreEqual(0,SteeringTraction.RecoveryDegrees(20,40,float.NaN));
        Assert.AreEqual(0,SteeringTraction.RecoveryDegrees(20,40,-1));
        Assert.AreEqual(0,SteeringTraction.RecoveryDegrees(float.PositiveInfinity,40,.02f));
        Assert.AreEqual(0,SteeringTraction.YawAcceleration(float.NaN,1));
    }
}
