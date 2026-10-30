using NUnit.Framework;
public sealed class TubeSteeringAssistTests
{
    [Test] public void NeutralHandleCorrectsBothHeadingDirections()
    {
        Assert.Less(TubeSteeringAssist.Blend(0,-12,0,30),-1);
        Assert.Greater(TubeSteeringAssist.Blend(0,12,0,30),1);
        Assert.AreEqual(0,TubeSteeringAssist.Blend(0,0,0,30));
    }
    [Test] public void StrongHandleInputRetainsDriverControlAndCorrectionIsBounded()
    {
        Assert.Greater(TubeSteeringAssist.Blend(30,-20,0,30),15);
        Assert.Less(TubeSteeringAssist.Blend(-30,20,0,30),-15);
        Assert.LessOrEqual(System.Math.Abs(TubeSteeringAssist.Blend(0,90,0,90)),12);
    }
    [Test] public void YawIsDampedAndInvalidInputDoesNotSteer()
    {
        Assert.Less(TubeSteeringAssist.Blend(0,0,30,30),0);
        Assert.AreEqual(5,TubeSteeringAssist.Blend(5,float.NaN,0,30));
        Assert.AreEqual(5,TubeSteeringAssist.Blend(5,0,0,0));
    }
}
