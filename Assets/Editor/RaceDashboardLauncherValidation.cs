using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

// Runs the real Editor launcher and verifies ownership/lifecycle without touching Tailscale.
public static class RaceDashboardLauncherValidation
{
    private static double deadline;
    private static int stage, relayPid;
    private static string previousCli, previousOrigin;
    private static readonly FieldInfo ProcessField = typeof(RaceDashboardLauncher).GetField("process", BindingFlags.Static | BindingFlags.NonPublic);
    public static void Run()
    {
        previousCli = Environment.GetEnvironmentVariable("RACING_TAILSCALE_CLI");
        previousOrigin = Environment.GetEnvironmentVariable("RACING_DASHBOARD_ORIGIN");
        Environment.SetEnvironmentVariable("RACING_TAILSCALE_CLI", Path.Combine(Path.GetTempPath(), "racing-validation-no-tailscale"));
        Environment.SetEnvironmentVariable("RACING_DASHBOARD_ORIGIN", "");
        deadline = EditorApplication.timeSinceStartup + 90;
        RaceDashboardLauncher.Start(false);
        EditorApplication.update += Check;
    }
    private static void Check()
    {
        try
        {
            if (EditorApplication.timeSinceStartup > deadline) throw new Exception("Launcher timed out: " + RaceDashboardLauncher.Message);
            var process = ProcessField.GetValue(null) as Process;
            if (stage == 0 && !string.IsNullOrEmpty(RaceDashboardLauncher.LocalUrl))
            {
                if (RaceDashboardLauncher.LocalUrl != "http://127.0.0.1:8766/" || process == null || process.HasExited)
                    throw new Exception("Editor did not start an owned relay process.");
                relayPid = process.Id;
                RaceDashboardLauncher.Start(false);
                if (((Process)ProcessField.GetValue(null)).Id != relayPid) throw new Exception("Starting twice created a second relay.");
                RaceDashboardLauncher.Stop(); stage = 1;
            }
            else if (stage == 1 && process == null && !RaceDashboardLauncher.Busy)
            {
                if (!string.IsNullOrEmpty(RaceDashboardLauncher.LocalUrl)) throw new Exception("Stopped relay URL was not cleared.");
                Directory.CreateDirectory("Artifacts/DashboardRealtime");
                File.WriteAllText("Artifacts/DashboardRealtime/editor-launcher.json", "{\"passed\":true,\"checks\":[\"build\",\"start\",\"ready URL\",\"duplicate suppression\",\"managed stop\"],\"tailscale\":\"not modified\"}\n");
                UnityEngine.Debug.Log("DASHBOARD_LAUNCHER_PASS: actual Editor build/start/ready/reuse/managed stop");
                Finish(0);
            }
        }
        catch (Exception error) { UnityEngine.Debug.LogException(error); RaceDashboardLauncher.Stop(); Finish(1); }
    }
    private static void Finish(int code)
    {
        EditorApplication.update -= Check;
        Environment.SetEnvironmentVariable("RACING_TAILSCALE_CLI", previousCli);
        Environment.SetEnvironmentVariable("RACING_DASHBOARD_ORIGIN", previousOrigin);
        if (Application.isBatchMode) EditorApplication.Exit(code);
    }
}
