using System;
using System.Diagnostics;
using System.Threading;

internal static class ReceiverProbe
{
    private static int Main(string[] args)
    {
        try
        {
            if (MacPseudoTerminalLineReader.IsSupportedPath("/dev/ttyUSB0") ||
                MacPseudoTerminalLineReader.IsSupportedPath("/dev/ttys000/../x") ||
                !MacPseudoTerminalLineReader.IsSupportedPath(args[0])) throw new Exception("PTY path validation failed.");
            var config = new Esp32SerialConfiguration().WithSimulatorPort(args[0], 6.5f);
            bool numeric=false, independent=false, atomicStop=false, resumed=false, stale=false, recovered=false;
            bool invalid=false, slowFragment=false, closed=false, session=false;
            using (var source = new TwoPlayerSerialInputSource(config, args[0]))
            {
                source.LineProcessed += (status, line) => {
                    invalid |= status=="INVALID";
                    session |= status=="ACK_OK" && line.Contains(",OPEN_SESSION,OK,");
                };
                if (!source.IsPortOpen) throw new Exception("PTY did not open.");
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
                    resumed |= atomicStop && both;
                    stale |= resumed && source.LastParseResult=="STALE" && !source.IsConnected(0)&&!source.IsConnected(1);
                    recovered |= stale && both;
                    slowFragment |= both && Math.Abs(p1.pedal-.25f)<.001f && Math.Abs(p1.steering+10f/6.5f)<.001f &&
                        Math.Abs(p2.pedal-.6f)<.001f && Math.Abs(p2.steering-20f/6.5f)<.001f;
                    if (!source.IsPortOpen) { closed=true; break; }
                    Thread.Sleep(5);
                }
                Console.WriteLine($"session={session} numeric={numeric} independent={independent} atomicStop={atomicStop} resumed={resumed} stale={stale} recovered={recovered} invalid={invalid} slowFragment={slowFragment} closed={closed} lines={source.LinesReceived}");
                if (!session || !numeric || !closed || source.PendingLineCount>8) throw new Exception("RGP session, numeric input, close detection or queue drain failed.");
                if (args[1]=="timeout" && !(independent&&atomicStop&&resumed&&stale&&recovered)) throw new Exception("Atomic/stale scenario failed.");
                if (args[1]=="fragments" && !(invalid&&slowFragment)) throw new Exception("CRC/fragment scenario failed.");
            }
            Console.WriteLine("MONO_RGP_PTY_RECEIVER_PASS: "+args[1]); return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
