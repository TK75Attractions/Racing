using NUnit.Framework;

public sealed class EdgeSteeringAssistTests
{
    [Test] public void CentreAndInwardMotionReceiveNoCorrection()
    {
        Assert.AreEqual(0f, EdgeSteeringAssist.Correction(0, 1, 6, 20));
        Assert.AreEqual(0f, EdgeSteeringAssist.Correction(5, 4.8f, 6, 20));
        Assert.AreEqual(0f, EdgeSteeringAssist.Correction(-5, -4.8f, 6, 20));
    }
    [Test] public void PredictedDepartureIsSymmetricAndBounded()
    {
        float right = EdgeSteeringAssist.Correction(4, 6, 6, 20);
        float left = EdgeSteeringAssist.Correction(-4, -6, 6, 20);
        Assert.AreEqual(-3f, right); Assert.AreEqual(-right, left);
        Assert.AreEqual(-.7f, EdgeSteeringAssist.Correction(4, 20, 6, 2), .0001f);
    }
    [Test] public void AStationaryEdgeAndInvalidGeometryDoNotSteer()
    {
        Assert.AreEqual(0, EdgeSteeringAssist.Correction(6, 6, 6, 30));
        Assert.AreEqual(0, EdgeSteeringAssist.Correction(0, float.NaN, 6, 30));
        Assert.AreEqual(0, EdgeSteeringAssist.Correction(1, 3, 1, 30));
    }
}
