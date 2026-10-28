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
        Assert.AreEqual(-18f, right); Assert.AreEqual(-right, left);
        Assert.AreEqual(-2f, EdgeSteeringAssist.Correction(4, 20, 6, 2), .0001f);
    }
    [Test] public void AnUnintendedDepartureFacesTheRoad()
    {
        Assert.AreEqual(-10f, EdgeSteeringAssist.Blend(0, -10, 4, 6, 6, 30));
        Assert.AreEqual(10f, EdgeSteeringAssist.Blend(0, 10, -4, -6, 6, 30));
        Assert.AreEqual(20f, EdgeSteeringAssist.Blend(20, -10, 0, 1, 6, 30));
    }
    [Test] public void AStationaryEdgeAndInvalidGeometryDoNotSteer()
    {
        Assert.AreEqual(0, EdgeSteeringAssist.Correction(6, 6, 6, 30));
        Assert.AreEqual(0, EdgeSteeringAssist.Correction(0, float.NaN, 6, 30));
        Assert.AreEqual(0, EdgeSteeringAssist.Correction(1, 3, 1, 30));
    }
    [Test] public void DeliberateWallEntryPreservesEitherSidesHandleAtAnySpeedLimit()
    {
        foreach (float limit in new[] { 2f, 10f, 30f })
        {
            Assert.AreEqual(limit, EdgeSteeringAssist.Blend(limit, -10, 9, 12, 10, limit), .0001f);
            Assert.AreEqual(-limit, EdgeSteeringAssist.Blend(-limit, 10, -9, -12, 10, limit), .0001f);
            Assert.AreEqual(0, EdgeSteeringAssist.WallRideIntent(0, 9, 12, 10, limit));
            Assert.AreEqual(0, EdgeSteeringAssist.WallRideIntent(-limit, 9, 12, 10, limit));
        }
    }
}
