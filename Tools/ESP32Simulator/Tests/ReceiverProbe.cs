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
                !MacPseudoTerminalLineReader.IsSupportedPath(args[0]))
                throw new Exception("PTY path validation failed.");
            var config = new Esp32SerialConfiguration().WithSimulatorPort(args[0], 6.5f);
            bool numeric = false, partial = false, p1Timeout = false, silence = false, recovered = false, invalid = false;
            bool closed = false, slowFragment = false;
            using (var source = new TwoPlayerSerialInputSource(config, args[0]))
            {
                if (!source.IsPortOpen) throw new Exception("PTY did not open.");
                var timer = Stopwatch.StartNew();
                double limit = args[1] == "timeout" ? 15 : 9;
                while (timer.Elapsed.TotalSeconds < limit)
                {
                    source.UpdateInput(.01f);
                    DriveInputState p1 = source.GetInputState(0), p2 = source.GetInputState(1);
                    numeric |= Math.Abs(p1.pedal - .75f) < .001f && Math.Abs(p1.steering - 30f) < .001f &&
                               Math.Abs(p2.pedal - .5f) < .001f && Math.Abs(p2.steering + 15f) < .001f;
                    partial |= source.LastParseResult == "PARTIAL" && Math.Abs(p1.pedal - .75f) < .001f && p1.steering == 0;
                    p1Timeout |= !source.IsConnected(0) && source.IsConnected(1) && source.LastSerialLine.StartsWith("nan,nan,");
                    if (p1Timeout && source.LastSerialLine.StartsWith("0.0000,"))
                        recovered |= source.IsConnected(0) && source.IsConnected(1);
                    silence |= recovered && source.IsPortOpen && !source.IsConnected(0) && !source.IsConnected(1);
                    invalid |= source.LastParseResult == "INVALID";
                    slowFragment |= Math.Abs(p1.pedal - .25f) < .001f && Math.Abs(p1.steering + 10f) < .001f &&
                                    Math.Abs(p2.pedal - .6f) < .001f && Math.Abs(p2.steering - 20f) < .001f;
                    if (!source.IsPortOpen) { closed = true; break; }
                    Thread.Sleep(5);
                }
                Console.WriteLine($"numeric={numeric} partial={partial} p1Timeout={p1Timeout} silence={silence} recovered={recovered} invalid={invalid} slowFragment={slowFragment} closed={closed} lines={source.LinesReceived} pending={source.PendingLineCount}");
                if (!numeric || !closed || source.PendingLineCount > 8) throw new Exception("Numeric input, close detection or queue drain failed.");
                if (args[1] == "timeout" && !(partial && p1Timeout && silence && recovered)) throw new Exception("Timeout scenario failed.");
                if (args[1] == "fragments" && !(invalid && slowFragment)) throw new Exception("Boot/invalid lines or fragments across read timeouts failed.");
            }
            Console.WriteLine("MONO_PTY_RECEIVER_PASS: " + args[1]);
            return 0;
        }
        catch (Exception exception) { Console.Error.WriteLine(exception); return 1; }
    }
}
