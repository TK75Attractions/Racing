using NUnit.Framework;

public class SerialInputProtocolTests
{
    [TestCase("DEVICE,P1", "P1")]
    [TestCase("device: P2", "P2")]
    public void DeviceIdentity_AcceptsCommaAndColon(string line, string expected)
    {
        Assert.That(SerialInputProtocol.TryReadDeviceId(line, out string actual), Is.True);
        Assert.That(actual, Is.EqualTo(expected));
    }

    [Test]
    public void LegacyInput_ParsesPedalAndScaledSteering()
    {
        Assert.That(SerialInputProtocol.TryParseInput("0.75,-15", 15f, out SerialInputFrame frame), Is.True);
        Assert.That(frame.Pedal, Is.EqualTo(0.75f).Within(0.0001f));
        Assert.That(frame.Steering, Is.EqualTo(-1f).Within(0.0001f));
        Assert.That(frame.ResetHeld, Is.False);
        Assert.That(frame.ReadyHeld, Is.False);
    }

    [Test]
    public void ExtendedInput_ParsesButtonsAndIgnoresDiagnosticSuffix()
    {
        Assert.That(
            SerialInputProtocol.TryParseInput("1.2,30,true,on||adc=4095", 15f, out SerialInputFrame frame),
            Is.True);
        Assert.That(frame.Pedal, Is.EqualTo(1f));
        Assert.That(frame.Steering, Is.EqualTo(2f));
        Assert.That(frame.ResetHeld, Is.True);
        Assert.That(frame.ReadyHeld, Is.True);
    }

    [TestCase("")]
    [TestCase("not-a-frame")]
    [TestCase("0.5,not-a-number")]
    public void InvalidInput_IsRejected(string line)
    {
        Assert.That(SerialInputProtocol.TryParseInput(line, 15f, out _), Is.False);
    }
}
