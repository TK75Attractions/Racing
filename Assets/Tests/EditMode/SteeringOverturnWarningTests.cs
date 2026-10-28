using NUnit.Framework;

public sealed class SteeringOverturnWarningTests
{
    [Test]
    public void BothDirectionsWarnAtPhysicalLimit()
    {
        foreach (float angle in new[] { 135f, -135f, 540f, -540f })
            Assert.That(new SteeringOverturnWarning().Update(angle), Is.True);
    }
    [Test]
    public void HysteresisAvoidsFlickerAndReleasesOnlyAfterReturning()
    {
        var warning = new SteeringOverturnWarning();
        Assert.That(warning.Update(134.99f), Is.False);
        Assert.That(warning.Update(135f), Is.True);
        Assert.That(warning.Update(130f), Is.True);
        Assert.That(warning.Update(125f), Is.True);
        Assert.That(warning.Update(124.99f), Is.False);
        Assert.That(warning.Update(-134f), Is.False);
    }
    [Test]
    public void MissingOrInvalidTelemetryClearsTheWarning()
    {
        foreach (float? value in new float?[] { null, float.NaN, float.PositiveInfinity, float.NegativeInfinity })
        {
            var warning = new SteeringOverturnWarning(); warning.Update(-200f);
            Assert.That(warning.Update(value), Is.False);
            Assert.That(warning.Update(130f), Is.False);
        }
    }
}
