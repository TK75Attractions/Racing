using NUnit.Framework;

public class SerialInputProtocolTests
{
    [TestCase("0.5,invalid", true, false)]
    [TestCase("invalid,15", false, true)]
    public void PartialInput_PreservesTheValidAxis(string line, bool pedal, bool steering)
    {
        Assert.That(SerialInputProtocol.TryParsePartialInput(line, 3f, out SerialInputFrame frame,
            out bool pedalParsed, out bool steeringParsed), Is.True);
        Assert.That(pedalParsed, Is.EqualTo(pedal));
        Assert.That(steeringParsed, Is.EqualTo(steering));
        if (pedal) Assert.That(frame.Pedal, Is.EqualTo(0.5f));
        if (steering) Assert.That(frame.Steering, Is.EqualTo(5f));
        Assert.That(SerialInputProtocol.TryParseInput(line, 3f, out _), Is.False);
    }

    [Test]
    public void MainSteeringScale_RemainsConfigurable()
    {
        Assert.That(SerialInputProtocol.TryParseInput("0.5,15,1,1||debug", 3f, out SerialInputFrame frame), Is.True);
        Assert.That(frame.Steering, Is.EqualTo(5f));
        Assert.That(frame.ResetHeld && frame.ReadyHeld, Is.True);
    }

    [Test]
    public void PlayerOneOnly_PedalStillWorksWhenSteeringIsNan()
    {
        Assert.That(SerialInputProtocol.TryParsePartialInput("0.9,nan,nan,nan", 6.5f,
            out SerialInputFrame frame, out bool pedalValid, out bool steeringValid), Is.True);
        Assert.That(pedalValid, Is.True);
        Assert.That(steeringValid, Is.False);
        Assert.That(frame.Pedal, Is.EqualTo(0.9f).Within(0.0001f));
    }

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

    [Test]
    public void TwoPlayerInput_ParsesBothPlayers()
    {
        Assert.That(
            SerialInputProtocol.TryParseTwoPlayerInput("0.5,15,-0.25,-30", 3f, out TwoPlayerSerialInputFrame frame),
            Is.True);
        Assert.That(frame.PlayerOneValid, Is.True);
        Assert.That(frame.PlayerTwoValid, Is.True);
        Assert.That(frame.PlayerOne.Pedal, Is.EqualTo(0.5f));
        Assert.That(frame.PlayerOne.Steering, Is.EqualTo(5f));
        Assert.That(frame.PlayerTwo.Pedal, Is.EqualTo(-0.25f));
        Assert.That(frame.PlayerTwo.Steering, Is.EqualTo(-10f));
    }

    [TestCase("nan,15,0.25,-30", false, true, true, true)]
    [TestCase("0.5,nan,0.25,-30", true, false, true, true)]
    [TestCase("0.5,15,nan,-30", true, true, false, true)]
    [TestCase("0.5,15,0.25,nan", true, true, true, false)]
    [TestCase("nan,15,nan,-30", false, true, false, true)]
    [TestCase("0.08,nan,nan,nan", true, false, false, false)]
    public void TwoPlayerInput_TracksEachAxisIndependently(
        string line, bool p1Pedal, bool p1Steering, bool p2Pedal, bool p2Steering)
    {
        Assert.That(
            SerialInputProtocol.TryParseTwoPlayerInput(line, 3f, out TwoPlayerSerialInputFrame frame),
            Is.True);
        Assert.That(frame.PlayerOnePedalValid, Is.EqualTo(p1Pedal));
        Assert.That(frame.PlayerOneSteeringValid, Is.EqualTo(p1Steering));
        Assert.That(frame.PlayerTwoPedalValid, Is.EqualTo(p2Pedal));
        Assert.That(frame.PlayerTwoSteeringValid, Is.EqualTo(p2Steering));
        Assert.That(frame.PlayerOneValid, Is.EqualTo(p1Pedal || p1Steering));
        Assert.That(frame.PlayerTwoValid, Is.EqualTo(p2Pedal || p2Steering));
    }

    [TestCase("")]
    [TestCase("not-a-frame")]
    [TestCase("0.5,not-a-number")]
    public void InvalidInput_IsRejected(string line)
    {
        Assert.That(SerialInputProtocol.TryParseInput(line, 15f, out _), Is.False);
    }
}
