using NUnit.Framework;

public sealed class RivalProximityTests
{
    [Test] public void AClosingFrontCarAndAFastRearCarBothWarn()
    {
        Assert.Greater(RivalProximity.Strength(30, 2, 0, 32, 31, 12, false, true), 0);
        Assert.Greater(RivalProximity.Strength(-25, 2, 0, -27, 26, 10, true, true), 0);
        Assert.AreEqual(0, RivalProximity.Strength(30, 2, 0, 32, 31, -2, false, true));
        Assert.AreEqual(0, RivalProximity.Strength(-25, 2, 0, -27, 26, -2, true, true));
    }
    [Test] public void VeryCloseCarsWarnEvenAtEqualSpeedButSideBySideDoesNotFlicker()
    {
        Assert.Greater(RivalProximity.Strength(-8, 2, 0, -8, 9, 0, true, true), 0);
        Assert.Greater(RivalProximity.Strength(8, 2, 0, 8, 9, 0, false, true), 0);
        foreach (bool rear in new[] { false, true })
            Assert.AreEqual(0, RivalProximity.Strength(.5f, 3, 0, 0, 3, 10, rear, true));
    }
    [Test] public void DifferentDecksParallelSectionsAndOccludedCarsDoNotWarn()
    {
        Assert.AreEqual(0, RivalProximity.Strength(10, 0, 8, 10, 12, 10, false, true));
        Assert.AreEqual(0, RivalProximity.Strength(10, 15, 0, 10, 18, 10, false, true));
        Assert.AreEqual(0, RivalProximity.Strength(10, 0, 0, 150, 10, 10, false, true));
        Assert.AreEqual(0, RivalProximity.Strength(10, 0, 0, 10, 10, 10, false, false));
    }
    [Test] public void ApproachingTheLineAndLappedCarsUsePhysicalCourseSeparation()
    {
        Assert.AreEqual(10, RivalProximity.ShortestCourseGap(995, 5, 1000));
        Assert.AreEqual(-10, RivalProximity.ShortestCourseGap(5, 995, 1000));
        Assert.AreEqual(10, RivalProximity.ShortestCourseGap(995, 2005, 1000));
    }
    [Test] public void CloserAndFasterThreatsHaveStrongerWarningsWithinBounds()
    {
        float far = RivalProximity.Strength(-35, 0, 0, -35, 35, 4, true, true);
        float close = RivalProximity.Strength(-8, 0, 0, -8, 8, 12, true, true);
        Assert.Greater(close, far); Assert.LessOrEqual(close, 1);
        Assert.AreEqual(0, RivalProximity.Strength(-60, 0, 0, -60, 60, 50, true, true));
    }
    [Test] public void InvalidGeometryCannotProduceAWarning()
    {
        Assert.AreEqual(0, RivalProximity.Strength(float.NaN, 0, 0, 0, 10, 5, false, true));
        Assert.AreEqual(0, RivalProximity.Strength(10, 0, 0, 10, -1, 5, false, true));
        Assert.AreEqual(0, RivalProximity.Strength(10, 0, 0, float.PositiveInfinity, 10, 5, false, true));
        Assert.IsTrue(float.IsPositiveInfinity(RivalProximity.ShortestCourseGap(10, 20, 0)));
    }
}
