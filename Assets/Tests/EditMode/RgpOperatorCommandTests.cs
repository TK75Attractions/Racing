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
    [Test] public void PlayerSlotsFollowAddressesAndAnUnusedLostWheelCannotDisableTheOther()
    {
        var session = Ready(out _);
        session.Receive(Frame("SYS,8,0,DEVICE,IMU,0x68,0x68"), 10);
        Assert.That(session.IsPlayerSteeringAvailable(0), Is.True);
        Assert.That(session.IsPlayerSteeringAvailable(1), Is.False);
        session.Receive(Frame("SYS,9,0,DEVICE,IMU,0x69,0x68"), 11);
        Assert.That(session.IsPlayerSteeringAvailable(1), Is.True);
        session.Receive(Frame("EVT,10,0,IMU_LOST,0x68"), 12);
        Assert.That(session.IsPlayerSteeringAvailable(0), Is.False);
        Assert.That(session.IsPlayerSteeringAvailable(1), Is.True);
        session.TryCommand("GET_STATUS", null, 13, out uint status, out _);
        session.Receive(Frame("ACK,11,0," + status + ",GET_STATUS,OK,READY,1,1,1,1"), 14);
        Assert.That(session.IsPlayerSteeringAvailable(0), Is.False, "Aggregate READY cannot revive a known disconnected player.");
        session.Receive(Frame("SYS,12,0,DEVICE,IMU,0x68,0x68"), 15);
        Assert.That(session.IsPlayerSteeringAvailable(0), Is.True);
    }
    [Test] public void SteeringOnlyModeNeedsAMatchedAcknowledgementAndNewTelemetryButNoPedals()
    {
        var session = Ready(out var sent);
        session.RequestInputMode(true); session.Tick(10);
        SerialInputProtocol.TryParse(sent[sent.Count - 1], out RgpFrame mode);
        Assert.That(mode.Data[0], Is.EqualTo("SET_INPUT_MODE"));
        session.Receive(Frame("ACK,8,0,999,SET_INPUT_MODE,OK,P1_STEERING"), 11);
        Assert.That(session.InputModeReady, Is.False);
        Assert.That(session.Receive(Frame("TEL,9,0,78,0,0,0,0,0"), 12), Is.EqualTo("NOT_READY"));
        session.Receive(Frame("ACK,10,0," + mode.Sequence + ",SET_INPUT_MODE,OK,P1_STEERING"), 13);
        session.TryCommand("GET_STATUS", null, 14, out uint status, out _);
        session.Receive(Frame("ACK,11,0," + status + ",GET_STATUS,OK,READY,0,1,1,1"), 15);
        Assert.That(session.AdsStatus, Is.EqualTo("Fault"));
        Assert.That(session.State, Is.EqualTo("READY"));
        Assert.That(session.IsFresh(15), Is.False);
        Assert.That(session.Receive(Frame("TEL,12,0,78,0,0,0,0,0"), 16), Is.EqualTo("OK"));
        Assert.That(session.IsFresh(16), Is.True);
        session.RequestInputMode(false);
        Assert.That(session.IsFresh(17), Is.False);
        session.Tick(18); SerialInputProtocol.TryParse(sent[sent.Count - 1], out mode);
        session.Receive(Frame("ACK,13,0," + mode.Sequence + ",SET_INPUT_MODE,OK,ALL"), 19);
        session.TryCommand("GET_STATUS", null, 20, out status, out _);
        session.Receive(Frame("ACK,14,0," + status + ",GET_STATUS,OK,READY,0,1,1,1"), 21);
        Assert.That(session.State, Is.EqualTo("NOT_READY"), "Normal mode must restore real pedal requirements.");
    }
    [Test] public void LegacyFirmwareCannotSilentlyPretendToEnableSteeringOnly()
    {
        var session = Ready(out var sent);
        session.RequestInputMode(true); session.Tick(10);
        SerialInputProtocol.TryParse(sent[sent.Count - 1], out RgpFrame mode);
        session.Receive(Frame("ACK,8,0," + mode.Sequence + ",SET_INPUT_MODE,UNSUPPORTED"), 11);
        Assert.That(session.InputModeUnsupported, Is.True);
        Assert.That(session.IsFresh(12), Is.False);
        int count = sent.Count; session.Tick(5000); session.Tick(6000);
        Assert.That(sent.Count, Is.EqualTo(count), "Unsupported firmware cannot trigger an endless command loop.");
        session.Start(7000);
        Assert.That(session.InputMode, Is.EqualTo("ALL"));
        Assert.That(session.InputModeUnsupported, Is.False);
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
        Assert.That(sent.Count, Is.EqualTo(count+1), "Let the orientation filters settle after calibration, including a missed CAL_DONE.");
        session.Tick(10002);
        Assert.That(sent[sent.Count-1], Does.Contain(",ANGLE_ZERO*"));
        Assert.That(session.IsFresh(10002), Is.False);
        SerialInputProtocol.TryParse(sent[sent.Count-1], out RgpFrame zero);
        session.Receive(Frame("ACK,13,0,"+zero.Sequence+",ANGLE_ZERO,OK"), 10003);
        session.Tick(10004);
        Assert.That(sent.Count, Is.EqualTo(count+2));
    }

    [Test] public void FirmwareCanLockItsReferenceAfterCalibrationWithoutPrematureAngleZero()
    {
        var session = Ready(out var sent);
        session.Receive(Frame("EVT,10,0,CAL_START,AUTO,ALL"), 10);
        session.Receive(Frame("EVT,11,0,STATE,CALIBRATING"), 11);
        session.Receive(Frame("EVT,12,0,CAL_DONE,AUTO,ALL"), 16000);
        session.Receive(Frame("EVT,13,0,STATE,WAIT_ANGLE"), 16001);
        session.TryCommand("GET_STATUS", null, 16002, out uint status, out _);
        session.Receive(Frame("ACK,14,0,"+status+",GET_STATUS,OK,WAIT_ANGLE,1,1,0,1"), 16003);
        int count = sent.Count;
        session.Tick(16004); session.Tick(20999);
        Assert.That(sent.Count, Is.EqualTo(count), "The physical firmware needs five seconds after CAL_DONE to settle.");
        session.Receive(Frame("EVT,15,0,ANGLE_LOCKED"), 21000);
        session.Receive(Frame("EVT,16,0,STATE,READY"), 21001);
        session.Receive(Frame("TEL,17,0,0,0,0,0,0,0"), 21002);
        Assert.That(session.IsFresh(21003), Is.True);
        session.Tick(23000);
        Assert.That(sent.Count, Is.EqualTo(count), "A reference locked by the device must not be reset by the host.");
    }

    [Test] public void LegacyFirmwareGetsOneAngleRequestAfterPostCalibrationSettling()
    {
        var session = Ready(out var sent);
        session.Receive(Frame("EVT,10,0,CAL_START,AUTO,ALL"), 10);
        session.Receive(Frame("EVT,11,0,CAL_DONE,AUTO,ALL"), 100);
        session.TryCommand("GET_STATUS", null, 101, out uint status, out _);
        session.Receive(Frame("ACK,12,0,"+status+",GET_STATUS,OK,WAIT_ANGLE,1,1,0,1"), 102);
        int count = sent.Count;
        session.Tick(6099); Assert.That(sent.Count, Is.EqualTo(count));
        session.Tick(6100); Assert.That(sent[sent.Count-1], Does.Contain(",ANGLE_ZERO*"));
        SerialInputProtocol.TryParse(sent[sent.Count-1], out RgpFrame zero);
        session.Receive(Frame("ACK,13,0,"+zero.Sequence+",ANGLE_ZERO,FAILED,ANGLE_REFERENCE_INVALID"), 6101);
        session.Tick(13000); Assert.That(sent.Count, Is.EqualTo(count+1), "Failure must not cause repeated reference resets.");
    }

    [Test] public void TwoPlayerCalibrationWaitsForTheLateHandleDespiteAggregateImuOk()
    {
        var session = Ready(out var sent);
        session.Receive(Frame("SYS,10,0,DEVICE,IMU,0x69,0x75"), 10);
        session.Receive(Frame("EVT,11,0,IMU_FOUND"), 11);
        session.TryCommand("GET_STATUS", null, 12, out uint status, out _);
        session.Receive(Frame("ACK,12,0,"+status+",GET_STATUS,OK,WAIT_ANGLE,1,1,0,0"), 13);
        int count = sent.Count;
        session.Tick(14); session.Tick(1000);
        Assert.That(sent.Count, Is.EqualTo(count), "P2 alone must not freeze the calibration targets before P1 reconnects.");
        session.Receive(Frame("SYS,13,0,DEVICE,IMU,0x68,0x68"), 1500);
        session.Receive(Frame("EVT,14,0,IMU_FOUND"), 1501);
        session.Tick(1502);
        Assert.That(sent[sent.Count-1], Does.Contain(",CALIBRATE,ALL*"));
        Assert.That(sent.Count, Is.EqualTo(count+1));
    }

    [Test] public void P1OnlyCalibrationDoesNotWaitForUnusedP2OrPedalHardware()
    {
        var session = Ready(out var sent);
        session.RequestInputMode(true); session.Tick(10);
        SerialInputProtocol.TryParse(sent[sent.Count-1], out RgpFrame mode);
        session.Receive(Frame("ACK,10,0,"+mode.Sequence+",SET_INPUT_MODE,OK,P1_STEERING"), 11);
        session.Receive(Frame("SYS,11,0,DEVICE,IMU,0x68,0x68"), 12);
        session.TryCommand("GET_STATUS", null, 13, out uint status, out _);
        session.Receive(Frame("ACK,12,0,"+status+",GET_STATUS,OK,WAIT_ANGLE,0,1,0,0"), 14);
        session.Tick(15);
        Assert.That(sent[sent.Count-1], Does.Contain(",CALIBRATE,ALL*"));
    }

    [Test] public void SlowDiagnosticAckCannotDelayVerifiedStartupAnglePreparation()
    {
        var sent = new List<string>();
        var session = new RgpHostSession(sent.Add); session.Start(0);
        SerialInputProtocol.TryParse(sent[0], out RgpFrame open);
        session.Receive(Frame("ACK,1,0,0,OPEN_SESSION,OK," + open.Data[1]), 1);
        // PING and GET_CONFIG replies are delayed, but the authoritative hardware status is ready.
        session.Receive(Frame("ACK,2,0,2,GET_STATUS,OK,WAIT_ANGLE,1,1,0,1"), 2);
        session.Tick(3);
        Assert.That(sent[sent.Count - 1], Does.Contain(",ANGLE_ZERO*"),
            "The next input update must prepare the angle without waiting for unrelated 1-second retries.");
        Assert.That(session.IsFresh(3), Is.False, "Preparation must never bypass READY plus new TEL.");
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
