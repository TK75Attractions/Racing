using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Loopback-only HTTP bridge for the static race dashboard.</summary>
public sealed class RaceDashboardBridge : MonoBehaviour
{
    private const string Prefix = "http://127.0.0.1:8765/";
    private const int MaxSerialLines = 80;
    private readonly ConcurrentQueue<HttpListenerContext> requests = new ConcurrentQueue<HttpListenerContext>();
    private readonly Queue<SerialEntry> serialLines = new Queue<SerialEntry>();
    private HttpListener listener;
    private Thread listenerThread;
    private InputManager input;
    private Gmanager race;
    private int serialSequence;

    [Serializable] private sealed class SerialEntry
    {
        public int id;
        public string time;
        public string status;
        public string line;
    }

    [Serializable] private sealed class PlayerSnapshot
    {
        public int number;
        public bool connected;
        public float pedal;
        public float steering;
        public int lap;
        public float speed;
    }

    [Serializable] private sealed class SerialSnapshot
    {
        public bool portOpen;
        public string port;
        public long received;
        public int processed;
        public int errors;
        public int latestSerialId;
        public string lastResult;
        public SerialEntry[] lines;
    }

    [Serializable] private sealed class DashboardSnapshot
    {
        public string state;
        public float raceTime;
        public float countdown;
        public int goalLap;
        public string inputMode;
        public PlayerSnapshot[] players;
        public SerialSnapshot serial;
    }

    [Serializable] private sealed class RemoteSnapshot
    {
        public int schemaVersion = 1;
        public string sessionId;
        public int seq;
        public string state;
        public float raceTime;
        public float countdown;
        public int goalLap;
        public string inputMode;
        public PlayerSnapshot[] players;
        public SerialSnapshot serial;
    }

    private void Start()
    {
        race = GetComponent<Gmanager>();
        input = GetComponent<InputManager>();
        if (input != null) input.SerialLineProcessed += RecordSerialLine;
        try
        {
            listener = new HttpListener();
            listener.Prefixes.Add(Prefix);
            listener.Start();
            listenerThread = new Thread(Listen) { IsBackground = true, Name = "Race dashboard bridge" };
            listenerThread.Start();
            Debug.Log("Race dashboard bridge listening on " + Prefix);
        }
        catch (Exception error)
        {
            Debug.LogWarning("Race dashboard bridge could not start: " + error.Message);
            listener?.Close();
            listener = null;
        }
    }

    private void Listen()
    {
        while (listener != null && listener.IsListening)
        {
            try { requests.Enqueue(listener.GetContext()); }
            catch (HttpListenerException) { break; }
            catch (ObjectDisposedException) { break; }
        }
    }

    private void Update()
    {
        // All Unity objects, including camera capture and commands, stay on the main thread.
        for (int i = 0; i < 4 && requests.TryDequeue(out HttpListenerContext context); i++)
            Handle(context);
    }

    private void Handle(HttpListenerContext context)
    {
        try
        {
            string origin = context.Request.Headers["Origin"];
            string path = context.Request.Url.AbsolutePath;
            bool isGet = context.Request.HttpMethod == "GET";
            if (isGet && TryGetDashboardAsset(path, out string assetName, out string assetType))
            {
                string assetPath = Path.Combine(Application.streamingAssetsPath, "Dashboard", assetName);
                if (File.Exists(assetPath))
                    Respond(context, 200, assetType, File.ReadAllBytes(assetPath), null);
                else
                    Respond(context, 404, "text/plain; charset=utf-8",
                        Encoding.UTF8.GetBytes("Dashboard file not found: " + assetName), null);
                return;
            }
            // Browsers omit Origin on same-origin GET requests and address-bar navigation.
            bool readOnlyApi = isGet && (path == "/api/status" ||
                path == "/api/player/1/frame" || path == "/api/player/2/frame");
            if (!(readOnlyApi && string.IsNullOrEmpty(origin)) && !AllowedOrigin(origin))
            {
                Respond(context, 403, "text/plain", Encoding.UTF8.GetBytes("Origin denied"), null);
                return;
            }
            if (context.Request.HttpMethod == "OPTIONS") { Respond(context, 204, "text/plain", new byte[0], origin); return; }

            if (isGet && path == "/api/status")
                Respond(context, 200, "application/json", Encoding.UTF8.GetBytes(JsonUtility.ToJson(BuildSnapshot())), origin);
            else if (context.Request.HttpMethod == "GET" && (path == "/api/player/1/frame" || path == "/api/player/2/frame"))
            {
                byte[] frame = CapturePlayer(path.Contains("/1/") ? 0 : 1);
                if (frame == null) Respond(context, 503, "text/plain", Encoding.UTF8.GetBytes("Camera unavailable"), origin);
                else Respond(context, 200, "image/jpeg", frame, origin);
            }
            else if (context.Request.HttpMethod == "POST" && path.StartsWith("/api/action/", StringComparison.Ordinal))
            {
                string action = path.Substring("/api/action/".Length);
                bool accepted = ApplyAction(action);
                Respond(context, accepted ? 200 : 409, "application/json",
                    Encoding.UTF8.GetBytes(accepted ? "{\"ok\":true}" : "{\"ok\":false}"), origin);
            }
            else Respond(context, 404, "text/plain", Encoding.UTF8.GetBytes("Not found"), origin);
        }
        catch (Exception error)
        {
            Debug.LogWarning("Dashboard request failed: " + error.Message);
            try { Respond(context, 500, "text/plain", Encoding.UTF8.GetBytes("Bridge error"), null); } catch { }
        }
    }

    private static bool TryGetDashboardAsset(string path, out string fileName, out string contentType)
    {
        switch (path)
        {
            case "/":
            case "/index.html": fileName = "index.html"; contentType = "text/html; charset=utf-8"; return true;
            case "/app.js": fileName = "app.js"; contentType = "application/javascript; charset=utf-8"; return true;
            case "/style.css": fileName = "style.css"; contentType = "text/css; charset=utf-8"; return true;
            case "/favicon.svg": fileName = "favicon.svg"; contentType = "image/svg+xml"; return true;
            case "/firebase-config.json": fileName = "firebase-config.json"; contentType = "application/json; charset=utf-8"; return true;
            case "/remote.js": fileName = "remote.js"; contentType = "application/javascript; charset=utf-8"; return true;
            default: fileName = null; contentType = null; return false;
        }
    }

    private static bool AllowedOrigin(string origin)
    {
        if (string.IsNullOrEmpty(origin) || !Uri.TryCreate(origin, UriKind.Absolute, out Uri uri)) return false;
        bool local = uri.Scheme == "http" && (uri.Host == "localhost" || uri.Host == "127.0.0.1");
        bool pages = uri.Scheme == "https" && uri.Host == "tk75attractions.github.io";
        return local || pages;
    }

    private static void Respond(HttpListenerContext context, int status, string type, byte[] bytes, string origin)
    {
        HttpListenerResponse response = context.Response;
        response.StatusCode = status;
        response.ContentType = type;
        response.Headers["Cache-Control"] = "no-store";
        response.Headers["Access-Control-Allow-Private-Network"] = "true";
        if (origin != null)
        {
            response.Headers["Access-Control-Allow-Origin"] = origin;
            response.Headers["Vary"] = "Origin";
            response.Headers["Access-Control-Allow-Methods"] = "GET, POST, OPTIONS";
            response.Headers["Access-Control-Allow-Headers"] = "Content-Type";
        }
        response.ContentLength64 = bytes.Length;
        if (bytes.Length > 0) response.OutputStream.Write(bytes, 0, bytes.Length);
        response.Close();
    }

    private DashboardSnapshot BuildSnapshot()
    {
        TwoPlayerSerialInputSource serial = input?.SharedSerialInputSource;
        PlayerSnapshot[] players = new PlayerSnapshot[2];
        for (int i = 0; i < players.Length; i++)
        {
            DriveInputState controls = input != null ? input.GetInputState(i) : DriveInputState.Neutral;
            players[i] = new PlayerSnapshot
            {
                number = i + 1, connected = input != null && input.IsPlayerConnected(i),
                pedal = controls.pedal, steering = controls.steering,
                lap = race != null ? race.GetPlayerLap(i) : 0,
                speed = race != null ? race.GetPlayerSpeed(i) : 0f
            };
        }
        return new DashboardSnapshot
        {
            state = race != null ? race.state.ToString() : "Unknown",
            raceTime = race != null ? race.time : 0f,
            countdown = race != null ? race.CountdownTimeRemaining : 0f,
            goalLap = race != null ? race.GoalLap : 0,
            inputMode = input != null ? input.InputModeLabel : "Unavailable",
            players = players,
            serial = new SerialSnapshot
            {
                portOpen = serial != null && serial.IsPortOpen,
                port = serial?.PortName ?? "-", received = serial?.LinesReceived ?? 0,
                processed = serial?.LinesProcessed ?? 0, errors = serial?.ParseErrorCount ?? 0,
                latestSerialId = serialSequence,
                lastResult = serial?.LastParseResult ?? "Waiting for input",
                lines = serialLines.ToArray()
            }
        };
    }

    /// <summary>Creates a bounded, image-free snapshot for the remote viewer.</summary>
    public string BuildRemoteSnapshotJson(string sessionId, int sequence)
    {
        DashboardSnapshot source = BuildSnapshot();
        SerialEntry[] allLines = source.serial.lines;
        int count = Math.Min(10, allLines.Length);
        RemoteSnapshot remote = new RemoteSnapshot
        {
            sessionId = sessionId,
            seq = sequence,
            state = source.state,
            raceTime = source.raceTime,
            countdown = source.countdown,
            goalLap = source.goalLap,
            inputMode = Limit(source.inputMode, 64),
            players = source.players,
            serial = source.serial
        };
        foreach (PlayerSnapshot player in remote.players)
        {
            player.pedal = Mathf.Clamp(player.pedal, -1f, 1f);
            player.steering = Mathf.Clamp(player.steering, -1f, 1f);
            player.speed = Mathf.Clamp(player.speed, 0f, 1000f);
        }
        remote.serial.port = Limit(remote.serial.port, 64);
        remote.serial.lastResult = Limit(remote.serial.lastResult, 120);
        while (true)
        {
            remote.serial.lines = new SerialEntry[count];
            for (int i = 0; i < count; i++)
            {
                SerialEntry entry = allLines[allLines.Length - count + i];
                remote.serial.lines[i] = new SerialEntry
                {
                    id = entry.id, time = entry.time, status = Limit(entry.status, 16),
                    line = Limit(entry.line, 120)
                };
            }
            string json = "{\"updatedAt\":{\".sv\":\"timestamp\"}," + JsonUtility.ToJson(remote).Substring(1);
            if (Encoding.UTF8.GetByteCount(json) <= 4096) return json;
            if (count == 0) return null;
            count--;
        }
    }

    private static string Limit(string value, int maximum) =>
        string.IsNullOrEmpty(value) || value.Length <= maximum ? value : value.Substring(0, maximum);

    private void RecordSerialLine(string status, string line)
    {
        serialLines.Enqueue(new SerialEntry
        {
            id = ++serialSequence, time = DateTime.Now.ToString("HH:mm:ss.fff"),
            status = status, line = line.Length > 512 ? line.Substring(0, 512) : line
        });
        while (serialLines.Count > MaxSerialLines) serialLines.Dequeue();
    }

    private bool ApplyAction(string action)
    {
        if (race == null) return false;
        switch (action)
        {
            case "start": if (race.state != Gmanager.State.Title) return false; race.StartGame(); return true;
            case "result": if (race.state != Gmanager.State.Game) return false; race.ShowResult(); return true;
            case "retry": if (race.state != Gmanager.State.Result) return false; race.RetryGame(); return true;
            case "title": if (race.state != Gmanager.State.Result) return false; race.ResetGame(); return true;
            default: return false;
        }
    }

    private byte[] CapturePlayer(int index)
    {
        Camera camera = race?.GetPlayerCaptureCamera(index);
        if (camera == null) return null;
        const int width = 640, height = 360;
        RenderTexture target = RenderTexture.GetTemporary(width, height, 24);
        RenderTexture previousActive = RenderTexture.active;
        Texture2D pixels = new Texture2D(width, height, TextureFormat.RGB24, false);
        try
        {
            // URP StandardRequest captures the base camera together with its overlay stack.
            var request = new RenderPipeline.StandardRequest { destination = target };
            if (!RenderPipeline.SupportsRenderRequest(camera, request)) return null;
            RenderPipeline.SubmitRenderRequest(camera, request);
            RenderTexture.active = target;
            pixels.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            pixels.Apply();
            return pixels.EncodeToJPG(65);
        }
        finally
        {
            RenderTexture.active = previousActive;
            RenderTexture.ReleaseTemporary(target);
            Destroy(pixels);
        }
    }

    private void OnDestroy()
    {
        if (input != null) input.SerialLineProcessed -= RecordSerialLine;
        listener?.Close();
        listener = null;
        if (listenerThread != null && listenerThread.IsAlive) listenerThread.Join(500);
    }
}
