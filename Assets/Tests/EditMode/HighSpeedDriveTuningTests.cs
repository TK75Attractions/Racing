using NUnit.Framework;
public sealed class HighSpeedDriveTuningTests
{
    [Test] public void FullPedalHasStrongerAccelerationAndAThreeHundredKmhEquilibrium()
    {
        float force=HighSpeedDriveTuning.Motor(15,1,true),drag=HighSpeedDriveTuning.Resistance(.6f,15,true);
        Assert.AreEqual(30f,force);Assert.That(2f*force/drag*3.6f,Is.EqualTo(300f).Within(.01f));
    }
    [Test] public void OffDoesNotAlterDefaultsOrAccumulateMultipliers()
    {
        Assert.AreEqual(15f,HighSpeedDriveTuning.Motor(15,1,false));
        Assert.AreEqual(.6f,HighSpeedDriveTuning.Resistance(.6f,15,false));
    }
    [Test] public void ReverseAndBrakingDoNotReceiveDoubleMotorForce()
    {
        Assert.AreEqual(15f,HighSpeedDriveTuning.Motor(15,-1,true));
        Assert.AreEqual(15f,HighSpeedDriveTuning.Motor(15,0,true));
    }
    [Test] public void FasterVehicleGradesCannotRaiseTheFlatRoadEquilibriumBeyondThreeHundred()
    {
        foreach(float motor in new[]{10.5f,15f,19.5f})foreach(float resistance in new[]{.1f,.6f,1.1f})
            Assert.LessOrEqual(2*HighSpeedDriveTuning.Motor(motor,1,true)/HighSpeedDriveTuning.Resistance(resistance,motor,true)*3.6f,300.01f);
    }
}
