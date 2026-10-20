using System;
using System.Diagnostics;
using System.Threading;

internal static class ReceiverProbe
{
    private static int Main(string[] args)
    {
        try
        {
            if (args[1] == "hitches") return ValidateFrameHitches(args[0]);
            if (args[1] == "queued-stale") return ValidateQueuedStaleTelemetry(args[0]);
            if (MacPseudoTerminalLineReader.IsSupportedPath("/dev/ttyUSB0") ||
                MacPseudoTerminalLineReader.IsSupportedPath("/dev/ttys000/../x") ||
                !MacPseudoTerminalLineReader.IsSupportedPath(args[0])) throw new Exception("PTY path validation failed.");
            var config = new Esp32SerialConfiguration().WithSimulatorPort(args[0], 6.5f);
            bool numeric=false, independent=false, atomicStop=false, resumed=false, stale=false, recovered=false;
            bool invalid=false, slowFragment=false, closed=false, session=false, preparingNotice=false, staleNotice=false;
            using (var source = new TwoPlayerSerialInputSource(config, args[0]))
            {
                source.LineProcessed += (status, line) => {
                    invalid |= status=="INVALID";
                    session |= status=="ACK_OK" && line.Contains(",OPEN_SESSION,OK,");
                };
                if (!source.IsPortOpen) throw new Exception("PTY did not open.");
                if (source.GetConnectionState(-1) != DriveConnectionState.Unavailable || source.GetConnectionState(2) != DriveConnectionState.Unavailable)
                    throw new Exception("Invalid players must not be reported as connected.");
                var timer=Stopwatch.StartNew();
                while (timer.Elapsed.TotalSeconds<10)
                {
                    source.UpdateInput(.005f);
                    DriveInputState p1=source.GetInputState(0), p2=source.GetInputState(1);
                    bool both=source.IsConnected(0)&&source.IsConnected(1);
                    numeric |= both && Math.Abs(p1.pedal-.75f)<.001f && Math.Abs(p1.steering-30f/6.5f)<.001f &&
                        Math.Abs(p2.pedal-.5f)<.001f && Math.Abs(p2.steering+15f/6.5f)<.001f;
                    independent |= both && Math.Abs(p1.pedal-.4f)<.001f && Math.Abs(p2.pedal+.55f)<.001f;
                    atomicStop |= !source.IsConnected(0)&&!source.IsConnected(1)&&source.LastSerialLine.Contains(",STATE,WAIT_ANGLE*");
                    preparingNotice |= atomicStop && source.GetConnectionState(0) == DriveConnectionState.SerialPreparing;
                    resumed |= atomicStop && both;
                    stale |= resumed && source.LastParseResult=="STALE" && !source.IsConnected(0)&&!source.IsConnected(1);
                    staleNotice |= stale && source.GetConnectionState(0) == DriveConnectionState.TelemetryStale;
                    recovered |= stale && both;
                    slowFragment |= both && Math.Abs(p1.pedal-.25f)<.001f && Math.Abs(p1.steering+10f/6.5f)<.001f &&
                        Math.Abs(p2.pedal-.6f)<.001f && Math.Abs(p2.steering-20f/6.5f)<.001f;
                    if (!source.IsPortOpen) { closed=source.GetConnectionState(0)==DriveConnectionState.SerialDisconnected; break; }
                    Thread.Sleep(5);
                }
                var journal = source.Monitor.Read("", 0);
                if (journal.Telemetry == null || Array.Exists(journal.Lines, line => line.Line.StartsWith("@TEL,") && line.Status != "INVALID") ||
                    !Array.Exists(journal.Lines, line => line.Direction == "TX" && line.Line.Contains(",OPEN_SESSION,")))
                    throw new Exception("Realtime journal lost telemetry/command or retained normal TEL history.");
                Console.WriteLine($"session={session} numeric={numeric} independent={independent} atomicStop={atomicStop} resumed={resumed} stale={stale} recovered={recovered} invalid={invalid} slowFragment={slowFragment} closed={closed} lines={source.LinesReceived}");
                if (!session || !numeric || !closed || source.PendingLineCount>8) throw new Exception("RGP session, numeric input, close detection or queue drain failed.");
                if (args[1]=="timeout" && !(independent&&atomicStop&&resumed&&stale&&recovered&&preparingNotice&&staleNotice)) throw new Exception("Atomic/stale scenario or connection diagnostics failed.");
                if (args[1]=="fragments" && !(invalid&&slowFragment)) throw new Exception("CRC/fragment scenario failed.");
            }
            Console.WriteLine("MONO_RGP_PTY_RECEIVER_PASS: "+args[1]); return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }

    private static int ValidateFrameHitches(string port)
    {
        using (var source = new TwoPlayerSerialInputSource(new Esp32SerialConfiguration().WithSimulatorPort(port, 6.5f), port))
        {
            var p1 = new PlayerDriveInputGate(new SharedPlayerDriveInputSource(source, 0));
            var p2 = new PlayerDriveInputGate(new SharedPlayerDriveInputSource(source, 1));
            var timer = Stopwatch.StartNew();
            while (timer.Elapsed.TotalSeconds < 4)
            {
                source.UpdateInput(.005f);
                if (source.IsConnected(0) && Math.Abs(source.GetInputState(0).pedal - .75f) < .001f) break;
                Thread.Sleep(5);
            }
            if (!source.IsConnected(0)) throw new Exception("Hitch test did not reach READY.");
            foreach (int milliseconds in new[] { 150, 350, 650 })
            {
                source.UpdateInput(.005f);
                long before = source.LinesReceived;
                // Unity runs catch-up FixedUpdate before the next Gmanager.Update after a slow frame.
                // USB reception continues on its thread while the main thread is busy.
                Thread.Sleep(milliseconds);
                int queued = source.PendingLineCount;
                DriveInputState a = p1.CurrentState, b = p2.CurrentState;
                source.UpdateInput(.005f);
                bool latched = p1.NeedsPedalRelease || p2.NeedsPedalRelease;
                Console.WriteLine($"HITCH {milliseconds}ms: queued={queued} received={source.LinesReceived-before} pedal={a.pedal}/{b.pedal} releaseRequired={latched} parseErrors={source.ParseErrorCount}");
                if (queued == 0 || source.LinesReceived <= before || source.ParseErrorCount != 0 ||
                    Math.Abs(a.pedal - .75f) > .001f || Math.Abs(b.pedal - .5f) > .001f || latched ||
                    Math.Abs(p1.CurrentState.pedal - .75f) > .001f)
                    throw new Exception("Normal queued telemetry falsely latched the pedal-release gate after a slow frame.");
            }
        }
        Console.WriteLine("MONO_RGP_PTY_RECEIVER_PASS: hitches (both players keep their held pedals)");
        return 0;
    }

    private static int ValidateQueuedStaleTelemetry(string port)
    {
        using (var source = new TwoPlayerSerialInputSource(new Esp32SerialConfiguration().WithSimulatorPort(port, 6.5f), port))
        {
            var p1 = new PlayerDriveInputGate(new SharedPlayerDriveInputSource(source, 0));
            var p2 = new PlayerDriveInputGate(new SharedPlayerDriveInputSource(source, 1));
            bool paused = false, heldBlocked = false, released = false, resumed = false;
            var timer = Stopwatch.StartNew();
            while (timer.Elapsed.TotalSeconds < 5)
            {
                source.UpdateInput(.005f);
                bool connected = source.IsConnected(0);
                float raw = source.GetInputState(0).pedal;
                if (!paused && connected && Math.Abs(raw - .65f) < .001f)
                {
                    Thread.Sleep(650);
                    int queued = source.PendingLineCount;
                    DriveInputState a = p1.CurrentState, b = p2.CurrentState;
                    if (queued == 0 || source.IsConnected(0) || a.pedal != 0 || b.pedal != 0 ||
                        !p1.NeedsPedalRelease || !p2.NeedsPedalRelease)
                        throw new Exception("Processing old queued TEL must not make a real outage fresh.");
                    Console.WriteLine($"QUEUED_STALE: queued={queued} pedal={a.pedal}/{b.pedal} releaseRequired=True");
                    paused = true;
                }
                else if (paused && connected)
                {
                    DriveInputState a = p1.CurrentState, b = p2.CurrentState;
                    if (Math.Abs(raw - .65f) < .001f)
                        heldBlocked |= a.pedal == 0 && b.pedal == 0 && p1.NeedsPedalRelease && p2.NeedsPedalRelease;
                    if (heldBlocked && raw == 0)
                        released |= !p1.NeedsPedalRelease && !p2.NeedsPedalRelease;
                    if (released && Math.Abs(raw - .75f) < .001f)
                    {
                        resumed = Math.Abs(a.pedal - .75f) < .001f && Math.Abs(b.pedal - .5f) < .001f;
                        break;
                    }
                }
                Thread.Sleep(5);
            }
            if (!paused || !heldBlocked || !released || !resumed || source.ParseErrorCount != 0)
                throw new Exception("Real outage must keep both held pedals blocked until release and repress.");
        }
        Console.WriteLine("MONO_RGP_PTY_RECEIVER_PASS: queued-stale (real outage, held-pedal block, release and repress)");
        return 0;
    }
}
