using NUnit.Framework;

public sealed class SteeringResponseTests
{
    private static float Angle(float input,float speed)=>SteeringResponse.WheelAngle(input,30,.35f,speed,2.6f,1.6f,22,true);
    [Test] public void SerialFullLockUsesPhysicalAngleWithoutChangingTransportUnits()
    {
        Assert.That(SteeringResponse.CanonicalInput(90f/6.5f,90f/6.5f),Is.EqualTo(30f).Within(.0001f));
        Assert.That(SteeringResponse.CanonicalInput(-45f/6.5f,90f/6.5f),Is.EqualTo(-15f).Within(.0001f));
        Assert.That(SteeringResponse.CanonicalInput(10,30),Is.EqualTo(10).Within(.0001f));
    }
    [Test] public void HighSpeedSmallMediumAndLargeInputsKeepDistinctTurningAmounts()
    {
        foreach(float speed in new[]{8f,20f,40f})
        {
            float small=SteeringResponse.YawRate(Angle(5,speed),speed,2.6f);
            float medium=SteeringResponse.YawRate(Angle(15,speed),speed,2.6f);
            float full=SteeringResponse.YawRate(Angle(30,speed),speed,2.6f);
            Assert.That(small,Is.GreaterThan(0));Assert.That(medium,Is.GreaterThan(small));Assert.That(full,Is.GreaterThan(medium));
            Assert.That(medium/full,Is.EqualTo(.5f).Within(.0001f));
            Assert.That(full,Is.LessThanOrEqualTo(System.Math.Min(1.6f,22/speed)+.0001f));
        }
    }
    [Test] public void SteeringIsSymmetricAndCenterRequestsNoRotation()
    {
        Assert.That(Angle(0,40),Is.EqualTo(0));
        Assert.That(Angle(-15,40),Is.EqualTo(-Angle(15,40)).Within(.0001f));
        Assert.That(SteeringResponse.YawRate(Angle(15,8),-8,2.6f),Is.LessThan(0));
    }
    [Test] public void FullLockDoesNotGrowBeyondTheSelectedRange()
    {
        Assert.That(Angle(300,20),Is.EqualTo(Angle(30,20)).Within(.0001f));
        Assert.That(SteeringResponse.WheelAngle(30,36,1,0,2.6f,1.6f,22,true),Is.EqualTo(36).Within(.0001f));
    }
    [Test] public void InvalidInputCannotIntroduceNanIntoPhysics()
    {
        Assert.That(Angle(float.NaN,20),Is.EqualTo(0));
        Assert.That(SteeringResponse.CanonicalInput(float.PositiveInfinity,30),Is.EqualTo(0));
        Assert.That(SteeringResponse.CanonicalInput(10,float.NaN),Is.EqualTo(10));
    }
}
