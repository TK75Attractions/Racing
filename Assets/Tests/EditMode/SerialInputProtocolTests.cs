using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;

public class SerialInputProtocolTests
{
    private static IEnumerable<string[]> Vectors(string name)
    {
        string root = Environment.GetEnvironmentVariable("RGP_VECTOR_ROOT") ?? Path.Combine(Directory.GetCurrentDirectory(), "Tests~/Rgp/Vectors");
        foreach (string line in File.ReadAllLines(Path.Combine(root, name)))
            if (line.Length > 0 && line[0] != '#') yield return line.Split('|');
    }
    private static string Unescape(string value) => Regex.Unescape(value);
    private static RgpFrame Frame(string body)
    {
        Assert.That(SerialInputProtocol.TryParse(SerialInputProtocol.Encode(body), out RgpFrame frame), Is.True, body);
        return frame;
    }
    private static List<RgpFrame> Decode(string wire)
    {
        var decoder = new RgpStreamDecoder();
        var frames = new List<RgpFrame>();
        foreach (char c in wire)
        {
            string result = decoder.Push((byte)c, 0);
            if (result != null && SerialInputProtocol.TryParse(result, out RgpFrame frame)) frames.Add(frame);
        }
        decoder.Expire(1000);
        return frames;
    }
    [Test]
    public void AllCrcVectors()
    {
        int count = 0;
        foreach (string[] v in Vectors("crc-vectors.txt"))
        {
            Assert.That(SerialInputProtocol.Crc(v[1] == "<EMPTY>" ? "" : v[1]).ToString("X4"), Is.EqualTo(v[2]), v[0]); count++;
        }
        Console.WriteLine("CRC vectors passed: " + count);
    }
    [Test]
    public void AllHostValidVectors()
    {
        int count = 0;
        foreach (string[] v in Vectors("valid-frames.txt"))
        {
            if (v[1] != "ESP_TO_HOST") continue;
            List<RgpFrame> frames = Decode(Unescape(v[3]));
            Assert.That(frames.Count, Is.EqualTo(1), v[0]);
            if (frames[0].Type == "TEL")
            {
                RgpHostSession session = Ready(out _);
                Assert.That(session.Receive(frames[0], 10), Is.EqualTo("OK"), v[0]);
                Assert.That(session.IsFresh(10), Is.True, v[0]);
                Assert.That(session.PlayerTwo.Brake, Is.EqualTo(frames[0].Player(1).Brake), v[0]);
            }
            count++;
        }
        Console.WriteLine("Host valid vectors passed: " + count);
    }
    [Test]
    public void AllHostInvalidVectors()
    {
        int count = 0;
        foreach (string[] v in Vectors("invalid-frames.txt"))
        {
            if (v[1] != "ESP_TO_HOST") continue;
            Assert.That(Decode(Unescape(v[3])).Count, Is.Zero, v[0]); count++;
        }
        Console.WriteLine("Host invalid vectors passed: " + count);
    }
    [Test]
    public void SharedFramingNegativesInEitherDirectionAreRejected()
    {
        int count = 0;
        foreach (string[] v in Vectors("invalid-frames.txt"))
        {
            // Command argument/execution errors belong to ESP firmware, not this host.
            if (v[1] != "HOST_TO_ESP" || !v[4].StartsWith("ESP_DISCARD")) continue;
            Assert.That(Decode(Unescape(v[3])).Count, Is.Zero, v[0]);
            count++;
        }
        Console.WriteLine("Shared malformed framing vectors rejected: " + count);
    }
    private static RgpHostSession Ready(out List<string> sent)
    {
        var output = new List<string>(); sent = output;
        var session = new RgpHostSession(output.Add);
        session.Start(0);
        RgpFrame open;
        Assert.That(SerialInputProtocol.TryParse(output[0], out open), Is.True);
        session.Receive(Frame("ACK,1,0,0,OPEN_SESSION,OK," + open.Data[1]), 1);
        session.Receive(Frame("ACK,2,0,1,PING,OK,PONG"), 2);
        session.Receive(Frame("ACK,3,0,2,GET_STATUS,OK,READY,1,1,1,1"), 3);
        session.Receive(Frame("ACK,4,0,3,GET_CONFIG,OK,TEL_RATE,100,IMU_RATE,20,TEL_STREAM,1,IMU_STREAM,0,DBG_STREAM,0,BETA,0.200"), 4);
        return session;
    }
    [Test]
    public void HandshakeWorksWithoutBootEventsAndUsesOwnSequence()
    {
        var session = Ready(out List<string> sent);
        Assert.That(session.SessionOpen, Is.True);
        Assert.That(session.State, Is.EqualTo("READY"));
        Assert.That(session.PendingCommands, Is.Zero);
        Assert.That(sent.Count, Is.EqualTo(4));
        for (int i = 0; i < sent.Count; i++)
        {
            Assert.That(SerialInputProtocol.TryParse(sent[i], out RgpFrame cmd), Is.True);
            Assert.That(cmd.Sequence, Is.EqualTo((uint)i));
            Assert.That(cmd.Type, Is.EqualTo("CMD"));
        }
        Assert.That(session.IsFresh(4), Is.False);
    }
    [Test]
    public void TelemetryKeepsSixValuesAndRejectsPartialUpdate()
    {
        var session = Ready(out _);
        session.Receive(Frame("TEL,60,9000,-45,0.7,0.3,45,0.25,0.8"), 10);
        Assert.That(session.PlayerOne.Accel, Is.EqualTo(0.7f));
        Assert.That(session.PlayerOne.Brake, Is.EqualTo(0.3f));
        Assert.That(session.PlayerTwo.Handle, Is.EqualTo(45));
        Assert.That(session.PlayerTwo.Accel, Is.EqualTo(0.25f));
        foreach (string[] v in Vectors("invalid-frames.txt"))
        {
            if (v[1] != "ESP_TO_HOST" || !v[0].StartsWith("tel_")) continue;
            Assert.That(Decode(Unescape(v[3])).Count, Is.Zero, v[0]);
            Assert.That(session.PlayerTwo.Brake, Is.EqualTo(0.8f), v[0]);
        }
    }
    [Test]
    public void DuplicateOldAndWrappedTelSequences()
    {
        var session = Ready(out _);
        session.Receive(Frame("TEL,4294967295,4294967295,0,1,0,0,0,1"), 10);
        Assert.That(session.Receive(Frame("TEL,4294967295,0,0,0,0,0,0,0"), 20), Is.EqualTo("OLD_TEL"));
        Assert.That(session.Receive(Frame("TEL,4294967294,0,0,0,0,0,0,0"), 30), Is.EqualTo("OLD_TEL"));
        Assert.That(session.PlayerOne.Accel, Is.EqualTo(1));
        Assert.That(session.Receive(Frame("TEL,0,0,0,0.5,0,0,0.25,0"), 40), Is.EqualTo("OK"));
        Assert.That(session.PlayerTwo.Accel, Is.EqualTo(0.25f));
    }
    [Test]
    public void StaleUsesReceiveTimeAndConfiguredRate()
    {
        var session = Ready(out _);
        session.Receive(Frame("TEL,10,0,0,1,0,0,1,0"), 10);
        Assert.That(session.IsFresh(110), Is.True);
        Assert.That(session.IsFresh(110.001), Is.False);
        session.Receive(Frame("SYS,11,0,CONFIG,TEL_RATE,1"), 120);
        Assert.That(session.StaleAfterMilliseconds, Is.EqualTo(3000));
        Assert.That(session.IsFresh(3010), Is.True);
        Assert.That(session.IsFresh(3011), Is.False);
        session.Receive(Frame("SYS,12,0,CONFIG,TEL_STREAM,0"), 130);
        Assert.That(session.IsFresh(130), Is.False);
        Assert.That(session.PlayerOne.Accel, Is.Zero);
    }
    [Test]
    public void StateChangesErrorsAndDisconnectImmediatelyInvalidateBothPlayers()
    {
        foreach (string body in new[] { "EVT,11,0,STATE,WAIT_ANGLE", "EVT,11,0,STATE,FUTURE_STATE",
            "EVT,11,0,ANGLE_UNLOCKED", "EVT,11,0,I2C_LOST", "ERR,11,0,ERROR,SYSTEM,FUTURE_ERROR,X", "ERR,11,0,FATAL,IMU,INIT_FAILED,1" })
        {
            var session = Ready(out _);
            session.Receive(Frame("TEL,10,0,0,1,0,0,1,0"), 10);
            session.Receive(Frame(body), 11);
            Assert.That(session.IsFresh(11), Is.False, body);
            Assert.That(session.PlayerOne.Accel + session.PlayerTwo.Accel, Is.Zero, body);
        }
        var disconnected = Ready(out _);
        disconnected.Receive(Frame("TEL,10,0,0,1,0,0,1,0"), 10);
        disconnected.Disconnect();
        Assert.That(disconnected.IsFresh(11), Is.False);
    }
    [Test]
    public void RetriesUseIdenticalFrameAtOneSecondAndStopAfterTwo()
    {
        var sent = new List<string>(); var session = new RgpHostSession(sent.Add);
        session.Start(0); session.Tick(999); Assert.That(sent.Count, Is.EqualTo(1));
        session.Tick(1000); session.Tick(2000); session.Tick(3000); session.Tick(10000);
        Assert.That(sent.Count, Is.EqualTo(3));
        Assert.That(sent[1], Is.EqualTo(sent[0])); Assert.That(sent[2], Is.EqualTo(sent[0]));
        Assert.That(session.PendingCommands, Is.Zero); Assert.That(session.SessionOpen, Is.False);
    }
    [Test]
    public void CommandTimeoutEmitsOneNotificationPerFailedRequest()
    {
        var notifications = new List<string>();
        var session = new RgpHostSession(_ => { });
        session.CommandTimedOut += notifications.Add;
        session.Start(0); session.Tick(1000); session.Tick(2000);
        Assert.That(notifications.Count, Is.Zero);
        session.Tick(3000); session.Tick(4000);
        Assert.That(notifications, Is.EqualTo(new[] { "OPEN_SESSION" }));
    }

    [Test]
    public void UnknownAckStatusCompletesWithoutRetryAndUnmatchedAckDoesNot()
    {
        var sent = new List<string>(); var session = new RgpHostSession(sent.Add);
        session.Start(0);
        session.Receive(Frame("ACK,1,0,99,PING,OK,PONG"), 1);
        Assert.That(session.PendingCommands, Is.EqualTo(1));
        session.Receive(Frame("ACK,2,0,0,OPEN_SESSION,FUTURE_STATUS"), 2);
        Assert.That(session.PendingCommands, Is.Zero);
        session.Tick(2000); Assert.That(sent.Count, Is.EqualTo(1));
    }
    [Test]
    public void HelloResetsInputPendingCommandsAndSequenceTracking()
    {
        var session = Ready(out List<string> sent);
        session.Receive(Frame("TEL,100,0,0,1,0,0,1,0"), 10);
        string previous = sent[0];
        session.Receive(Frame("SYS,0,0,HELLO,RGP,1.0,1.0.0"), 11);
        Assert.That(session.IsFresh(11), Is.False); Assert.That(session.PendingCommands, Is.EqualTo(1));
        Assert.That(sent[4], Is.Not.EqualTo(previous));
        SerialInputProtocol.TryParse(sent[4], out RgpFrame open);
        Assert.That(open.Sequence, Is.Zero);
        session.Receive(Frame("ACK,1,0,0,OPEN_SESSION,OK," + open.Data[1]), 12);
        session.Receive(Frame("EVT,2,0,STATE,READY"), 13);
        Assert.That(session.Receive(Frame("TEL,3,0,0,1,0,0,1,0"), 14), Is.EqualTo("OK"));
    }
    [Test]
    public void PreviousSessionAckDoesNotConsumeNewOpenRequest()
    {
        var session = Ready(out List<string> sent);
        SerialInputProtocol.TryParse(sent[0], out RgpFrame oldOpen);
        session.Receive(Frame("SYS,0,0,HELLO,RGP,1.0,1.0.0"), 10);
        Assert.That(session.Receive(Frame("ACK,1,0,0,OPEN_SESSION,OK," + oldOpen.Data[1]), 11), Is.EqualTo("UNMATCHED_ACK"));
        Assert.That(session.PendingCommands, Is.EqualTo(1));
        SerialInputProtocol.TryParse(sent[4], out RgpFrame newOpen);
        session.Receive(Frame("ACK,2,0,0,OPEN_SESSION,OK," + newOpen.Data[1]), 12);
        Assert.That(session.SessionOpen, Is.True);
    }
    [Test]
    public void DuplicateTelCannotRefreshTimeoutAndStateRecoveryNeedsNewTel()
    {
        var session = Ready(out _);
        RgpFrame tel = Frame("TEL,10,0,0,1,0,0,1,0");
        session.Receive(tel, 10);
        session.Receive(tel, 100);
        Assert.That(session.IsFresh(111), Is.False);
        session.Receive(Frame("EVT,11,0,STATE,WAIT_ANGLE"), 112);
        session.Receive(Frame("EVT,12,0,STATE,READY"), 113);
        Assert.That(session.IsFresh(113), Is.False);
        session.Receive(Frame("TEL,13,0,0,1,0,0,1,0"), 114);
        Assert.That(session.IsFresh(114), Is.True);
    }

    [Test]
    public void DifferentMajorStopsInputAndCommands()
    {
        var session = Ready(out List<string> sent);
        session.Receive(Frame("SYS,0,0,HELLO,RGP,2.0,2.0.0"), 10);
        int count = sent.Count;
        session.Tick(10000);
        Assert.That(sent.Count, Is.EqualTo(count)); Assert.That(session.Compatible, Is.False);
        Assert.That(session.Receive(Frame("TEL,1,0,0,1,0,0,1,0"), 11), Is.EqualTo("INCOMPATIBLE"));
    }
    [Test]
    public void DecoderResynchronizesAndExpiresPartialFrames()
    {
        string ping = SerialInputProtocol.Encode("CMD,1,0,PING");
        Assert.That(Decode("garbage@BROKEN" + ping).Count, Is.EqualTo(1));
        var decoder = new RgpStreamDecoder();
        foreach (char c in ping.Substring(0, 5)) decoder.Push((byte)c, 0);
        decoder.Expire(1000);
        foreach (char c in ping.Substring(5)) Assert.That(decoder.Push((byte)c, 1001), Is.Null);
        Assert.That(Decode(new string('x', 1000) + ping).Count, Is.EqualTo(1));
    }
    [Test]
    public void LegacyAndNoncanonicalInvalidNumbersAreRejected()
    {
        foreach (string wire in new[] { "0.5,15\n", "0.5,15,0.2,-15\n", "DEVICE,P1\n" })
            Assert.That(SerialInputProtocol.TryParse(wire, out _), Is.False);
        foreach (string value in new[] { "+0.5", "5e-1", ".5", "1.", "NaN", "Infinity", " 0.5", "0.5 " })
            Assert.That(SerialInputProtocol.TryParse(SerialInputProtocol.Encode("TEL,1,0,0," + value + ",0,0,0,0"), out _), Is.False, value);
        Assert.That(SerialInputProtocol.TryDecodeText("%2520", out string text), Is.True);
        Assert.That(text, Is.EqualTo("%20"));
    }
}
