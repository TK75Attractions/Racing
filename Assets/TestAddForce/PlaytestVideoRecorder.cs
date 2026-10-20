#if UNITY_EDITOR
using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using UnityEngine;

/// <summary>Opt-in editor evidence: the production camera stack and HUD, encoded as silent MP4.</summary>
[DefaultExecutionOrder(10000)]
public sealed class PlaytestVideoRecorder : MonoBehaviour
{
    private Process encoder;
    private StreamWriter timeline;
    private PlayerDisplayRig rig;
    private Gmanager manager;
    private string folder;
    private int frame, previousCaptureRate;
    private bool finished;
    private readonly System.Text.StringBuilder errors = new System.Text.StringBuilder();
    public void Begin(Gmanager race, string destination)
    {
        manager = race; folder = destination;
        rig = ((PlayerDisplayRig[])typeof(Gmanager).GetField("displayRigs", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(race))[0];
        previousCaptureRate = Time.captureFramerate; Time.captureFramerate = 24;
        var info = new ProcessStartInfo(Environment.GetEnvironmentVariable("RACING_FFMPEG") ?? "/opt/homebrew/bin/ffmpeg")
        {
            Arguments = "-hide_banner -loglevel warning -y -f image2pipe -framerate 24 -vcodec mjpeg -i pipe:0 -an -c:v libx264 -preset veryfast -crf 25 -maxrate 2500k -bufsize 5000k -pix_fmt yuv420p -movflags +faststart \"" + Path.Combine(folder, "gameplay-p1.mp4") + "\"",
            UseShellExecute = false, RedirectStandardInput = true, RedirectStandardError = true, CreateNoWindow = true
        };
        encoder = new Process { StartInfo = info };
        encoder.ErrorDataReceived += (_, e) => { if (e.Data != null) lock (errors) { if (errors.Length < 8000) errors.AppendLine(e.Data); } };
        encoder.Start(); encoder.BeginErrorReadLine();
        timeline = new StreamWriter(Path.Combine(folder, "video-timeline.csv"));
        timeline.WriteLine("frame,videoSeconds,state,raceSeconds,p1SpeedKph,p1Progress");
    }
    private void LateUpdate()
    {
        if (finished || encoder == null || manager == null) return;
        RenderMode canvasMode = rig.Canvas.renderMode;
        RenderTexture target = rig.MainCamera.targetTexture;
        byte[] jpeg = rig.CaptureJpeg(1280, 720);
        if (rig.Canvas.renderMode != canvasMode || rig.MainCamera.targetTexture != target)
            throw new InvalidOperationException("Video capture changed the live player's canvas or camera output.");
        if (jpeg == null) throw new InvalidOperationException("The real game camera did not produce a video frame.");
        encoder.StandardInput.BaseStream.Write(jpeg, 0, jpeg.Length);
        timeline.WriteLine(FormattableString.Invariant($"{frame},{frame / 24f:F3},{manager.state},{manager.time:F3},{manager.GetPlayerSpeed(0):F2},{manager.GetParticipant(0)?.Progress ?? 0:F2}"));
        if (frame == 0 || frame == 24 * 30 || frame == 24 * 60) File.WriteAllBytes(Path.Combine(folder, "video-frame-" + frame + ".jpg"), jpeg);
        frame++;
    }
    public void Finish()
    {
        if (finished) return; finished = true; Time.captureFramerate = previousCaptureRate;
        timeline?.Dispose(); timeline = null;
        if (encoder == null) return;
        encoder.StandardInput.Close();
        if (!encoder.WaitForExit(10000)) { encoder.Kill(); throw new InvalidOperationException("Playtest video encoding did not finish."); }
        encoder.WaitForExit(); File.WriteAllText(Path.Combine(folder, "video-encoder.txt"), errors.ToString());
        if (encoder.ExitCode != 0) throw new InvalidOperationException("Playtest video encoding failed: " + errors);
        encoder.Dispose(); encoder = null;
        UnityEngine.Debug.Log("PLAYTEST_VIDEO_PASS: " + folder + "/gameplay-p1.mp4; frames=" + frame + "; 1280x720 24fps, offline game-clock capture, silent.");
    }
    private void OnDestroy() { Finish(); }
}
#endif
