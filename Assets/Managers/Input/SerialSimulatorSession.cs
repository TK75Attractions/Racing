using System;
using System.Globalization;
using UnityEngine;

/// <summary>Explicit, non-serialized simulator overrides. Never changes the saved scene.</summary>
public static class SerialSimulatorSession
{
    public const string EnabledKey = "Racing.ESP32Simulator.Enabled";
    public const string PortKey = "Racing.ESP32Simulator.Port";
    public const string DivisorKey = "Racing.ESP32Simulator.Divisor";

    public static bool TryGetConfiguration(out string port, out float divisor)
    {
        port = null;
        divisor = 6.5f;
#if UNITY_EDITOR_OSX
        if (!UnityEditor.SessionState.GetBool(EnabledKey, false)) return false;
        port = UnityEditor.SessionState.GetString(PortKey, "");
        divisor = UnityEditor.SessionState.GetFloat(DivisorKey, 6.5f);
#elif UNITY_STANDALONE_OSX
        string[] args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--esp32-simulator-port")
            {
                if (i + 1 >= args.Length) throw new ArgumentException("--esp32-simulator-port requires a PTY path.");
                port = args[++i];
            }
            else if (args[i] == "--esp32-simulator-divisor")
            {
                if (i + 1 >= args.Length || !float.TryParse(args[++i], NumberStyles.Float, CultureInfo.InvariantCulture, out divisor))
                    throw new ArgumentException("--esp32-simulator-divisor requires a number.");
            }
        }
        if (port == null) return false;
#else
        return false;
#endif
#if UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX
        if (!MacPseudoTerminalLineReader.IsSupportedPath(port))
            throw new ArgumentException("Simulator port must be a macOS PTY such as /dev/ttys000.");
        if (float.IsNaN(divisor) || float.IsInfinity(divisor) || divisor < .0001f || divisor > 1000f)
            throw new ArgumentOutOfRangeException(nameof(divisor), "Simulator divisor must be between 0.0001 and 1000.");
        return true;
#endif
    }
}
