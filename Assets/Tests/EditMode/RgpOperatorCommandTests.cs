using System;
using System.Collections.Generic;
using NUnit.Framework;

public sealed class RgpOperatorCommandTests
{
    private static RgpFrame Frame(string body)
    {
        Assert.That(SerialInputProtocol.TryParse(SerialInputProtocol.Encode(body), out RgpFrame frame), Is.True);
        return frame;
    }
    private static RgpHostSession Ready(out List<string> sent)
    {
        var output = new List<string>(); sent = output;
        var session = new RgpHostSession(output.Add); session.Start(0);
        SerialInputProtocol.TryParse(output[0], out RgpFrame open);
        session.Receive(Frame("ACK,1,0,0,OPEN_SESSION,OK,"+open.Data[1]),1);
        session.Receive(Frame("ACK,2,0,1,PING,OK,PONG"),2);
        session.Receive(Frame("ACK,3,0,2,GET_STATUS,OK,READY,1,1,1,1"),3);
        session.Receive(Frame("ACK,4,0,3,GET_CONFIG,OK,TEL_RATE,100,IMU_RATE,20,TEL_STREAM,1,IMU_STREAM,0,DBG_STREAM,0,BETA,0.200"),4);
        return session;
    }
    [Test] public void CommandsUseOneSequenceAndRetryExactWireUntilMatchedAck()
    {
        var session = Ready(out var sent);
        Assert.That(session.TryCommand("ANGLE_ZERO",Array.Empty<string>(),10,out uint seq,out _),Is.True);
        Assert.That(seq,Is.EqualTo(4)); string wire=sent[sent.Count-1];
        Assert.That(wire,Does.StartWith("@CMD,4,0,ANGLE_ZERO*"));
        session.Tick(1010);Assert.That(sent[sent.Count-1],Is.EqualTo(wire));
        Assert.That(session.TryCommand("ANGLE_ZERO",Array.Empty<string>(),1020,out _,out string reason),Is.False);
        Assert.That(reason,Is.EqualTo("command_pending"));
        Assert.That(session.Receive(Frame("ACK,5,0,99,ANGLE_ZERO,OK"),1021),Is.EqualTo("UNMATCHED_ACK"));
        Assert.That(session.PendingCommands,Is.EqualTo(1));
        session.Receive(Frame("ACK,6,0,4,ANGLE_ZERO,OK"),1022);
        Assert.That(session.PendingCommands,Is.Zero);
    }
    [Test] public void CommandsValidateArgumentCountRangeAndDirectionBeforeWriting()
    {
        var session = Ready(out var sent); int before=sent.Count;
        string[][] invalid = {
            new[]{"SET_RATE","TEL","101"},new[]{"SET_RATE","IMU","51"},new[]{"SET_RATE","DBG","1"},new[]{"SET_RATE","TEL","1.5"},
            new[]{"SET_BETA","0.009"},new[]{"SET_BETA","1,0"},new[]{"SET_STREAM","TEL","2"},new[]{"CALIBRATE","PEDAL"},
            new[]{"PING","extra"},new[]{"OPEN_SESSION","0123456789ABCDEF"},new[]{"TEL","0","1","0","0","1","0"},new[]{"ANGLE_ZERO","\\n"}};
        foreach(var request in invalid)
        {
            var args=new string[request.Length-1];Array.Copy(request,1,args,0,args.Length);
            Assert.That(session.TryCommand(request[0],args,10,out _,out _),Is.False,request[0]);
        }
        Assert.That(sent.Count,Is.EqualTo(before));
        Assert.That(session.TryCommand("SET_RATE",new[]{"TEL","1"},10,out _,out _),Is.True);
        Assert.That(session.TryCommand("SET_BETA",new[]{"0.010"},10,out _,out _),Is.True);
        Assert.That(session.TryCommand("SET_STREAM",new[]{"DBG","1"},10,out _,out _),Is.True);
        Assert.That(session.TryCommand("CALIBRATE",new[]{"GYRO"},10,out _,out _),Is.True);
        session.Disconnect();Assert.That(session.TryCommand("PING",null,11,out _,out _),Is.False);
    }
    [Test] public void FailedOperatorQueriesDoNotDestroyTheEstablishedSession()
    {
        var session=Ready(out _);
        session.TryCommand("GET_STATUS",null,10,out _,out _);
        session.Tick(1010);session.Tick(2010);session.Tick(3010);
        Assert.That(session.SessionOpen,Is.True);Assert.That(session.AdsStatus,Is.EqualTo("Unknown"));
        Assert.That(session.TryCommand("GET_STATUS",null,3011,out _,out _),Is.True);
    }
    [Test] public void HardwareEvidenceResetsAndUnmatchedAcksCannotClaimConnections()
    {
        var session=Ready(out _);Assert.That(session.AdsStatus,Is.EqualTo("Ok"));Assert.That(session.AngleLocked,Is.True);
        session.Receive(Frame("SYS,8,0,DEVICE,IMU,0x68,0x68"),10);
        Assert.That(session.ImuIdentity,Is.EqualTo("0x68"));
        session.Receive(Frame("EVT,9,0,I2C_LOST"),11);
        Assert.That(session.AdsStatus,Is.EqualTo("Unknown"));Assert.That(session.ImuStatus,Is.EqualTo("Unknown"));
        session.Receive(Frame("ACK,10,0,99,GET_STATUS,OK,READY,1,1,1,1"),12);
        Assert.That(session.ImuStatus,Is.EqualTo("Unknown"));
        session.TryCommand("GET_STATUS",null,13,out uint seq,out _);
        session.Receive(Frame("ACK,11,0,"+seq+",GET_STATUS,OK,RECOVERING,1,0,0,0"),14);
        Assert.That(session.AdsStatus,Is.EqualTo("Ok"));Assert.That(session.ImuStatus,Is.EqualTo("Fault"));
        Assert.That(session.TryCommand("CALIBRATE",new[]{"ALL"},15,out _,out _),Is.False);
        session.Disconnect();Assert.That(session.ImuStatus,Is.EqualTo("Unknown"));Assert.That(session.ImuAddress,Is.Empty);
    }

    [Test] public void TwoImusKeepSeparateIdentityFaultAndRecoveryEvidence()
    {
        var session=Ready(out _);
        session.Receive(Frame("SYS,8,0,DEVICE,IMU,0x68,0x68"),10);
        session.Receive(Frame("SYS,9,0,DEVICE,IMU,0x69,0x68"),11);
        Assert.That(session.Imus[0].Address,Is.EqualTo("0x68"));
        Assert.That(session.Imus[1].Address,Is.EqualTo("0x69"));
        Assert.That(session.Imus[1].Identity,Is.EqualTo("0x68"));
        session.Receive(Frame("ERR,10,0,ERROR,IMU,CONNECTION_LOST,0x69"),12);
        Assert.That(session.Imus[0].Status,Is.EqualTo("Ok"));
        Assert.That(session.Imus[1].Status,Is.EqualTo("Fault"));
        session.TryCommand("GET_STATUS",null,13,out uint seq,out _);
        session.Receive(Frame("ACK,11,0,"+seq+",GET_STATUS,OK,RECOVERING,1,0,0,0"),14);
        Assert.That(session.Imus[0].Status,Is.EqualTo("Unknown"));
        Assert.That(session.Imus[1].Status,Is.EqualTo("Fault"));
        session.Receive(Frame("EVT,12,0,IMU_FOUND,0x69"),15);
        Assert.That(session.Imus[1].Status,Is.EqualTo("Ok"));
        session.Receive(Frame("EVT,13,0,I2C_LOST"),16);
        Assert.That(session.Imus[0].Status,Is.EqualTo("Unknown"));
        Assert.That(session.Imus[1].Status,Is.EqualTo("Unknown"));
        session.Disconnect();
        Assert.That(session.Imus[0].Address,Is.Empty);
        Assert.That(session.Imus[1].Address,Is.Empty);
    }
    [Test] public void AggregateImuHealthCannotInventAnUndiscoveredSecondDevice()
    {
        var session=Ready(out _);
        session.Receive(Frame("EVT,8,0,IMU_FOUND"),10);
        session.Receive(Frame("SYS,9,0,DEVICE,IMU,0x68,0x68"),11);
        Assert.That(session.Imus[0].Status,Is.EqualTo("Ok"));
        Assert.That(session.Imus[1].Status,Is.EqualTo("Unknown"));
    }
    [Test] public void OperatorResultsMatchRequestsAndWaitForAsyncCompletion()
    {
        var session=Ready(out _);
        session.TryCommand("CALIBRATE",new[]{"GYRO"},10,out uint seq,out _);
        RgpHostSession.CommandResult result=Array.Find(session.CommandResults,item=>item.Sequence==seq);
        session.Receive(Frame("ACK,8,0,99,CALIBRATE,ACCEPTED,GYRO"),11);
        Assert.That(result.Status,Is.EqualTo("SENT"));
        session.Receive(Frame("ACK,9,0,"+seq+",CALIBRATE,ACCEPTED,GYRO"),12);
        Assert.That(result.Status,Is.EqualTo("ACCEPTED"));
        Assert.That(session.TryCommand("CALIBRATE",new[]{"GYRO"},13,out _,out _),Is.False);
        session.Receive(Frame("EVT,10,0,CAL_DONE,AUTO,ALL"),14);
        Assert.That(result.Status,Is.EqualTo("ACCEPTED"));
        session.Receive(Frame("EVT,11,0,CAL_DONE,"+seq+",GYRO"),15);
        Assert.That(result.Status,Is.EqualTo("DONE"));
        Assert.That(result.Data[2],Is.EqualTo("GYRO"));
        session.TryCommand("SCAN_I2C",null,16,out seq,out _);
        result=Array.Find(session.CommandResults,item=>item.Sequence==seq);
        session.Receive(Frame("ACK,12,0,"+seq+",SCAN_I2C,ACCEPTED"),17);
        session.Receive(Frame("SYS,13,0,I2C_DEVICE,"+seq+",0x48"),18);
        session.Receive(Frame("SYS,14,0,I2C_DEVICE,"+seq+",0x68"),19);
        session.Receive(Frame("EVT,15,0,I2C_SCAN_END,"+seq+",2"),20);
        Assert.That(result.Status,Is.EqualTo("DONE"));
        Assert.That(result.Devices,Is.EqualTo(new[]{"0x48","0x68"}));
    }
    [Test] public void AsyncFailureEarlyCompletionTimeoutAndResetHaveDefiniteResults()
    {
        var session=Ready(out _);
        session.TryCommand("SCAN_I2C",null,10,out uint seq,out _);
        var result=Array.Find(session.CommandResults,item=>item.Sequence==seq);
        session.Receive(Frame("ERR,8,0,ERROR,I2C,READ_FAILED,I2C_SCAN,REQ,"+seq),11);
        Assert.That(result.Status,Is.EqualTo("FAILED"));
        Assert.That(session.PendingCommands,Is.Zero);
        session.Receive(Frame("ACK,9,0,"+seq+",SCAN_I2C,ACCEPTED"),12);
        Assert.That(result.Status,Is.EqualTo("FAILED"));
        session.TryCommand("SCAN_I2C",null,13,out seq,out _);
        result=Array.Find(session.CommandResults,item=>item.Sequence==seq);
        session.Receive(Frame("EVT,10,0,I2C_SCAN_END,"+seq+",0"),14);
        session.Tick(5000);
        Assert.That(result.Status,Is.EqualTo("DONE"));
        session.TryCommand("PING",null,5001,out seq,out _);
        result=Array.Find(session.CommandResults,item=>item.Sequence==seq);
        session.Tick(6001);session.Tick(7001);session.Tick(8001);
        Assert.That(result.Status,Is.EqualTo("TIMEOUT"));
        session.TryCommand("SCAN_I2C",null,8002,out seq,out _);
        result=Array.Find(session.CommandResults,item=>item.Sequence==seq);
        session.Receive(Frame("ACK,11,0,"+seq+",SCAN_I2C,ACCEPTED"),8003);
        session.Tick(128003);Assert.That(result.Status,Is.EqualTo("TIMEOUT"));
        int generation=session.Generation;session.Start(128004);
        Assert.That(session.Generation,Is.GreaterThan(generation));
        Assert.That(session.CommandResults.Length,Is.EqualTo(1));
        Assert.That(session.CommandResults[0].Command,Is.EqualTo("OPEN_SESSION"));
        generation=session.Generation;
        session.Receive(Frame("SYS,0,0,HELLO,RGP,2.0,2.0.0"),128005);
        Assert.That(session.Generation,Is.GreaterThan(generation));
        Assert.That(session.CommandResults,Is.Empty);
    }

    [Test] public void StartupCalibratesOnceAndWaitsForVerifiedStatusBeforeZeroingAngle()
    {
        var session = Ready(out var sent);
        session.TryCommand("GET_STATUS", null, 10, out uint status, out _);
        session.Receive(Frame("ACK,10,0,"+status+",GET_STATUS,OK,WAIT_ANGLE,1,1,0,0"), 11);
        session.Tick(12);
        Assert.That(sent[sent.Count-1], Does.Contain(",CALIBRATE,ALL*"));
        SerialInputProtocol.TryParse(sent[sent.Count-1], out RgpFrame calibration);
        session.Receive(Frame("ACK,11,0,"+calibration.Sequence+",CALIBRATE,ACCEPTED,ALL"), 13);
        int count = sent.Count;
        session.Tick(14); session.Tick(4000);
        Assert.That(sent.Count, Is.EqualTo(count), "ACCEPTED must not trigger repeated calibration or imply completion");
        session.TryCommand("GET_STATUS", null, 4001, out status, out _);
        session.Receive(Frame("ACK,12,0,"+status+",GET_STATUS,OK,WAIT_ANGLE,1,1,0,1"), 4002);
        session.Tick(4003);
        Assert.That(sent[sent.Count-1], Does.Contain(",ANGLE_ZERO*"));
        Assert.That(session.IsFresh(4003), Is.False);
        SerialInputProtocol.TryParse(sent[sent.Count-1], out RgpFrame zero);
        session.Receive(Frame("ACK,13,0,"+zero.Sequence+",ANGLE_ZERO,OK"), 4004);
        session.Tick(4005);
        Assert.That(sent.Count, Is.EqualTo(count+2));
    }

    [Test] public void FailedAutomaticCalibrationIsBoundedAndReadyHardwareIsUntouched()
    {
        var session = Ready(out var sent);
        int count = sent.Count; session.Tick(10);
        Assert.That(sent.Count, Is.EqualTo(count));
        session.TryCommand("GET_STATUS", null, 11, out uint status, out _);
        session.Receive(Frame("ACK,10,0,"+status+",GET_STATUS,OK,WAIT_ANGLE,1,1,0,0"), 12);
        session.Tick(13); string wire = sent[sent.Count-1];
        session.Tick(1013); session.Tick(2013); session.Tick(3013); session.Tick(4013);
        Assert.That(sent.FindAll(s => s.Contains(",CALIBRATE,")).Count, Is.EqualTo(3));
        Assert.That(sent[sent.Count-1], Is.EqualTo(wire));
    }

    [Test] public void ManualStartupCalibrationDoesNotTriggerAnAdditionalAutomaticRequest()
    {
        var session = Ready(out var sent);
        session.TryCommand("GET_STATUS", null, 10, out uint status, out _);
        session.Receive(Frame("ACK,10,0,"+status+",GET_STATUS,OK,WAIT_ANGLE,1,1,0,0"), 11);
        session.TryCommand("CALIBRATE", new[] { "ALL" }, 12, out uint sequence, out _);
        session.Receive(Frame("ACK,11,0,"+sequence+",CALIBRATE,ACCEPTED,ALL"), 13);
        int count = sent.Count; session.Tick(14);
        Assert.That(sent.Count, Is.EqualTo(count));
    }
}
