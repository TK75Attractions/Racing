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
    private const int MaxSerialAlerts = 8;
    private readonly ConcurrentQueue<HttpListenerContext> requests = new ConcurrentQueue<HttpListenerContext>();
    private readonly Queue<SerialEntry> serialLines = new Queue<SerialEntry>();
    private readonly Queue<SerialEntry> serialAlerts = new Queue<SerialEntry>();
    private HttpListener listener;
    private Thread listenerThread;
    private InputManager input;
    private Gmanager race;
    private int serialSequence;
    private readonly string liveSession = Guid.NewGuid().ToString("N");
    private volatile string liveSnapshot;
    private volatile RgpSerialJournal liveJournal = new RgpSerialJournal();
    private long stateRevision;
    private string observedState;
    private float nextSnapshotAt;
    private int streamClients;
    private volatile bool stopping;
    private readonly Dictionary<string, string> commandResults = new Dictionary<string, string>();
    private readonly Dictionary<string, string> commandRequests = new Dictionary<string, string>();
    private readonly Queue<string> commandOrder = new Queue<string>();
    private SerialEntry latestTelemetry;

    [Serializable] private sealed class ActionRequest
    {
        public string id, targetSession, expectedState;
        public long stateRevision, observedAt;
        public string command;
        public string[] args;
    }

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

    [Serializable] private sealed class PlayerProgressSnapshot
    {
        public int number;
        public string state, carName, step;
        public float remaining, progress;
    }
    [Serializable] private sealed class HardwareSnapshot
    {
        public bool sessionOpen, recent, angleLocked, calibrated;
        public string state, ads, imu, adsAddress, imuAddress, imuIdentity;
        public int pending;
        public string lastCommand;
    }
    [Serializable] private sealed class RawEntry
    {
        public long id;
        public string time, direction, line;
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

    [Serializable] private sealed class ParticipantSnapshot
    {
        public int number;
        public bool isCpu;
        public int position;
        public int lap;
        public float speed;
        public float progress;
        public string status;
        public int rivalNumber;
        public float gap;
        public float targetSpeed;
        public int recoveries;
        public bool contactGrace;
    }

    [Serializable] private sealed class DashboardSnapshot
    {
        public string sessionId;
        public long stateRevision, serverTime;
        public SerialEntry telemetry;
        public RawEntry[] rawLines;
        public HardwareSnapshot hardware;
        public PlayerProgressSnapshot[] playerProgress;
        public string state;
        public float raceTime;
        public float countdown;
        public int goalLap;
        public string inputMode;
        public PlayerSnapshot[] players;
        public int participantCount;
        public ParticipantSnapshot[] participants;
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
        public int participantCount;
        public ParticipantSnapshot[] participants;
        public SerialSnapshot serial;
    }

    private void Start()
    {
        // HTTP actions and state snapshots must keep ticking while a browser has focus.
        Application.runInBackground = true;
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
            try
            {
                HttpListenerContext context = listener.GetContext();
                if (context.Request.HttpMethod == "GET" && context.Request.Url.AbsolutePath == "/api/events")
                    StartStream(context);
                else if (requests.Count < 64) requests.Enqueue(context);
                else { context.Response.StatusCode = 503; context.Response.Close(); }
            }
            catch (HttpListenerException) { break; }
            catch (ObjectDisposedException) { break; }
        }
    }

    private void Update()
    {
        ObserveState();
        RgpSerialJournal journal = input?.SharedSerialInputSource?.Monitor;
        if (journal != null) liveJournal = journal;
        if (Time.realtimeSinceStartup >= nextSnapshotAt)
        {
            DashboardSnapshot snapshot = BuildSnapshot();
            snapshot.serial.lines = new SerialEntry[0];
            snapshot.rawLines = new RawEntry[0];
            liveSnapshot = JsonUtility.ToJson(snapshot);
            nextSnapshotAt = Time.realtimeSinceStartup + .1f;
        }
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
                path == "/api/player/1/frame" || path == "/api/player/2/frame" || path.StartsWith("/api/command/", StringComparison.Ordinal));
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
                HandleAction(context, action, origin);
            }
            else if (isGet && path.StartsWith("/api/command/", StringComparison.Ordinal))
            {
                string id = path.Substring("/api/command/".Length);
                bool found = commandResults.TryGetValue(id, out string result);
                Respond(context, found ? 200 : 404, "application/json", Encoding.UTF8.GetBytes(result ?? "{\"known\":false}"), origin);
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
            case "/live.js": fileName = "live.js"; contentType = "application/javascript; charset=utf-8"; return true;
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
        return origin == "http://127.0.0.1:8765" || origin == "http://localhost:8765";
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
        ParticipantSnapshot[] participants = BuildParticipants();
        return new DashboardSnapshot
        {
            sessionId = liveSession, stateRevision = stateRevision, serverTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            telemetry = latestTelemetry,
            rawLines = Array.ConvertAll((serial?.Monitor ?? liveJournal).LatestRaw(), entry => new RawEntry
                { id = entry.Id, time = entry.Time, direction = entry.Direction, line = entry.Line }),
            hardware = new HardwareSnapshot
            {
                sessionOpen = serial?.Session.SessionOpen ?? false, recent = serial?.HasRecentDeviceData ?? false,
                state = serial?.Session.State ?? "DISCONNECTED", ads = serial?.Session.AdsStatus ?? "Unknown",
                imu = serial?.Session.ImuStatus ?? "Unknown", adsAddress = serial?.Session.AdsAddress ?? "",
                imuAddress = serial?.Session.ImuAddress ?? "", imuIdentity = serial?.Session.ImuIdentity ?? "",
                angleLocked = serial?.Session.AngleLocked ?? false, calibrated = serial?.Session.Calibrated ?? false,
                pending = serial?.Session.PendingCommands ?? 0, lastCommand = serial?.Session.LastCommandResult ?? ""
            },
            playerProgress = BuildPlayerProgress(),
            state = race != null ? race.state.ToString() : "Unknown",
            raceTime = race != null ? race.time : 0f,
            countdown = race != null ? race.CountdownTimeRemaining : 0f,
            goalLap = race != null ? race.GoalLap : 0,
            inputMode = input != null ? input.InputModeLabel : "Unavailable",
            players = players,
            participantCount = participants.Length,
            participants = participants,
            serial = new SerialSnapshot
            {
                portOpen = serial != null && serial.IsPortOpen,
                port = serial?.PortName ?? "-", received = serial?.LinesReceived ?? 0,
                processed = serial?.LinesProcessed ?? 0, errors = serial?.ParseErrorCount ?? 0,
                latestSerialId = serialSequence,
                lastResult = serial?.LastParseResult ?? "Waiting for input",
                lines = SelectSerialLines(MaxSerialLines)
            }
        };
    }

    private PlayerProgressSnapshot[] BuildPlayerProgress()
    {
        var result = new PlayerProgressSnapshot[2];
        for (int i = 0; i < 2; i++)
        {
            var player = new PlayerProgressSnapshot { number = i + 1, state = "Unknown", carName = "", step = "" };
            if (race != null) race.GetPlayerDashboardProgress(i, out player.state, out player.carName, out player.step,
                out player.remaining, out player.progress);
            result[i] = player;
        }
        return result;
    }

    private ParticipantSnapshot[] BuildParticipants()
    {
        int count = race != null ? Math.Min(4, race.Participants.Count) : 0;
        ParticipantSnapshot[] snapshots = new ParticipantSnapshot[count];
        for (int i = 0; i < count; i++)
        {
            RaceParticipant entry = race.Participants[i];
            NpcRaceDriver driver = entry.Driver;
            RaceParticipant rival = driver != null ? race.GetParticipant(driver.RivalIndex) : null;
            bool activeRival = entry.IsRacing && rival?.IsRacing == true;
            string status = entry.Result != null ? (entry.Result.didFinish ? "Finished" : "DNF") :
                race.state != Gmanager.State.Game ? "Stopped" : driver != null ? driver.DrivingStatus :
                entry.IsRacing ? "Driving" : "Stopped";
            snapshots[i] = new ParticipantSnapshot
            {
                number = entry.Index + 1, isCpu = entry.IsCpu,
                position = entry.Result?.finishPosition ?? entry.Position,
                lap = race.GetParticipantLap(entry), speed = Metric(race.GetParticipantSpeed(entry), 0f, 1000f),
                progress = Metric(entry.Progress, -100000f, 1000000f), status = status,
                rivalNumber = activeRival ? rival.Index + 1 : 0,
                gap = activeRival ? Metric(rival.Progress - entry.Progress, -1000000f, 1000000f) : 0f,
                targetSpeed = entry.IsRacing && race.state == Gmanager.State.Game && driver != null ? Metric(driver.TargetSpeed, 0f, 1000f) : 0f,
                recoveries = driver?.RecoveryCount ?? 0, contactGrace = driver?.HasContactGrace ?? false
            };
        }
        return snapshots;
    }

    private static float Metric(float value, float minimum, float maximum) =>
        float.IsNaN(value) || float.IsInfinity(value) ? 0f : Mathf.Round(Mathf.Clamp(value, minimum, maximum) * 10f) / 10f;

    /// <summary>Creates a bounded, image-free snapshot for the remote viewer.</summary>
    public string BuildRemoteSnapshotJson(string sessionId, int sequence)
    {
        DashboardSnapshot source = BuildSnapshot();
        int count = Math.Min(10, source.serial.lines.Length);
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
            participantCount = source.participantCount,
            participants = source.participants,
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
            SerialEntry[] selected = SelectSerialLines(count);
            remote.serial.lines = new SerialEntry[selected.Length];
            for (int i = 0; i < selected.Length; i++)
            {
                SerialEntry entry = selected[i];
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
        var entry = new SerialEntry
        {
            id = ++serialSequence, time = DateTime.Now.ToString("HH:mm:ss.fff"),
            status = status, line = line.Length > 512 ? line.Substring(0, 512) : line
        };
        if (SerialInputProtocol.TryParse(line + "\n", out RgpFrame parsed) && parsed.Type == "TEL")
        { latestTelemetry = entry; return; }
        serialLines.Enqueue(entry);
        if (IsSerialAlert(status, line))
        {
            serialAlerts.Enqueue(entry);
            while (serialAlerts.Count > MaxSerialAlerts) serialAlerts.Dequeue();
        }
        while (serialLines.Count > MaxSerialLines) serialLines.Dequeue();
    }

    private static bool IsSerialAlert(string status, string line)
    {
        switch (status)
        {
            case "WARN": case "ERROR": case "FATAL": case "INVALID": case "INCOMPATIBLE":
            case "ACK_FAILED": case "TIMEOUT": case "STALE": return true;
        }
        return status == "EVENT" && SerialInputProtocol.TryParse(line + "\n", out RgpFrame frame) &&
            frame.Type == "EVT" && frame.Data[0] == "STATE" &&
            (frame.Data[1] == "RECOVERING" || frame.Data[1] == "FATAL");
    }

    // Reserve space for recent alerts before filling with other non-TEL history, then restore time order.
    // The same bounded wire schema works with existing Firebase rules (10 lines, 120 chars, 4 KiB).
    private SerialEntry[] SelectSerialLines(int maximum)
    {
        var selected = new List<SerialEntry>();
        var ids = new HashSet<int>();
        SerialEntry[] alerts = serialAlerts.ToArray();
        for (int i = alerts.Length - 1; i >= 0 && selected.Count < maximum; i--)
            if (ids.Add(alerts[i].id)) selected.Add(alerts[i]);
        SerialEntry[] recent = serialLines.ToArray();
        for (int i = recent.Length - 1; i >= 0 && selected.Count < maximum; i--)
            if (!IsSerialAlert(recent[i].status, recent[i].line) && ids.Add(recent[i].id)) selected.Add(recent[i]);
        selected.Sort((a, b) => a.id.CompareTo(b.id));
        return selected.ToArray();
    }

    private bool ApplyAction(string action)
    {
        if (race == null || !race.CanUseDashboardControls) return false;
        switch (action)
        {
            case "start": if (!race.IsPreparing) return false; race.StartGame(); return true;
            case "result": if (race.state != Gmanager.State.Game) return false; race.ShowResult(); return true;
            case "retry": if (race.state != Gmanager.State.Result) return false; race.RetryGame(); return true;
            case "title": if (race.state != Gmanager.State.Result) return false; race.ResetGame(); return true;
            default: return false;
        }
    }

    /// <summary>Applies a remote command only while the observed race state still matches.</summary>
    public bool TryApplyRemoteAction(string action, string expectedState)
    {
        return race != null && race.state.ToString() == expectedState && ApplyAction(action);
    }

    private void ObserveState()
    {
        string current = race != null ? race.state.ToString() : "Unknown";
        if (current != observedState) { observedState = current; stateRevision++; }
    }

    private void HandleAction(HttpListenerContext context, string action, string origin)
    {
        if (context.Request.ContentLength64 < 0 || context.Request.ContentLength64 > 4096)
        { Respond(context, 413, "application/json", Encoding.UTF8.GetBytes("{\"ok\":false}"), origin); return; }
        string body;
        using (var reader = new StreamReader(context.Request.InputStream)) body = reader.ReadToEnd();
        ActionRequest request;
        try { request = JsonUtility.FromJson<ActionRequest>(body); }
        catch { request = null; }
        if (request == null || string.IsNullOrEmpty(request.id) || request.id.Length > 80)
        { Respond(context, 400, "application/json", Encoding.UTF8.GetBytes("{\"ok\":false}"), origin); return; }
        string signature = action + "|" + body;
        if (commandResults.TryGetValue(request.id, out string cached))
        {
            bool same = commandRequests[request.id] == signature;
            Respond(context, same ? 200 : 409, "application/json", Encoding.UTF8.GetBytes(same ? cached : "{\"ok\":false,\"reason\":\"id_conflict\"}"), origin);
            return;
        }
        ObserveState();
        long age = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - request.observedAt;
        bool accepted = request.targetSession == liveSession && request.stateRevision == stateRevision &&
            age >= -1000 && age <= 15000 && race != null && race.state.ToString() == request.expectedState;
        string reason = "state_changed"; uint serialSeq = 0;
        if (accepted)
        {
            if (action == "serial")
            {
                TwoPlayerSerialInputSource source = input?.SharedSerialInputSource;
                reason = "serial_disconnected";
                accepted = source != null && source.TrySendCommand(request.command, request.args, out serialSeq, out reason);
            }
            else accepted = TryApplyRemoteAction(action, request.expectedState);
        }
        ObserveState();
        string result = "{\"id\":" + RgpSerialJournal.Quote(request.id) + ",\"ok\":" + (accepted ? "true" : "false") +
            ",\"reason\":" + RgpSerialJournal.Quote(accepted ? "" : reason) +
            ",\"serialSequence\":" + serialSeq + "}";
        commandResults.Add(request.id, result); commandRequests.Add(request.id, signature); commandOrder.Enqueue(request.id);
        if (commandOrder.Count > 256) { string old = commandOrder.Dequeue(); commandResults.Remove(old); commandRequests.Remove(old); }
        nextSnapshotAt = 0;
        Respond(context, accepted ? 200 : 409, "application/json", Encoding.UTF8.GetBytes(result), origin);
    }

    private void StartStream(HttpListenerContext context)
    {
        string origin = context.Request.Headers["Origin"];
        if (!string.IsNullOrEmpty(origin) && !AllowedOrigin(origin))
        { context.Response.StatusCode = 403; context.Response.Close(); return; }
        if (Interlocked.Increment(ref streamClients) > 8)
        { Interlocked.Decrement(ref streamClients); context.Response.StatusCode = 429; context.Response.Close(); return; }
        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                context.Response.ContentType = "application/x-ndjson";
                context.Response.SendChunked = true;
                context.Response.Headers["Cache-Control"] = "no-store";
                string session = context.Request.QueryString["serialSession"] ?? "";
                long.TryParse(context.Request.QueryString["after"], out long cursor);
                long.TryParse(context.Request.QueryString["rawAfter"], out long rawCursor);
                string lastSnapshot = null;
                long lastTelemetry = -1;
                DateTime heartbeat = DateTime.MinValue;
                using (var writer = new StreamWriter(context.Response.OutputStream, new UTF8Encoding(false)))
                {
                    while (!stopping)
                    {
                        RgpSerialJournal.Batch batch = liveJournal.Read(session, cursor, rawAfter: rawCursor);
                        string snapshot = liveSnapshot;
                        bool changedSession = batch.Session != session;
                        bool changedTelemetry = batch.Telemetry != null && (changedSession || batch.Telemetry.Id != lastTelemetry);
                        bool changed = changedSession || batch.RawCursor != rawCursor || batch.Cursor != cursor || snapshot != lastSnapshot || changedTelemetry;
                        if (changed || DateTime.UtcNow >= heartbeat)
                        {
                            string json = "{\"sessionId\":" + RgpSerialJournal.Quote(liveSession) + ",\"serialSession\":" + RgpSerialJournal.Quote(batch.Session) +
                                ",\"cursor\":" + batch.Cursor + ",\"gap\":" + (batch.Gap ? "true" : "false") +
                                ",\"rawCursor\":" + batch.RawCursor + ",\"rawGap\":" + (batch.RawGap ? "true" : "false") +
                                ",\"rawLines\":[" + string.Join(",", Array.ConvertAll(batch.RawLines, entry => entry.Json)) + "]" +
                                ",\"snapshot\":" + (snapshot != lastSnapshot ? snapshot ?? "null" : "null") +
                                ",\"telemetry\":" + (changedTelemetry ? batch.Telemetry.Json : "null") +
                                ",\"alerts\":[" + string.Join(",", Array.ConvertAll(batch.Alerts, entry => entry.Json)) + "]" +
                                ",\"lines\":[" + string.Join(",", Array.ConvertAll(batch.Lines, entry => entry.Json)) + "]}";
                            writer.WriteLine(json); writer.Flush();
                            heartbeat = DateTime.UtcNow.AddSeconds(1);
                            lastSnapshot = snapshot; session = batch.Session; cursor = batch.Cursor; rawCursor = batch.RawCursor;
                            if (batch.Telemetry != null) lastTelemetry = batch.Telemetry.Id;
                        }
                        Thread.Sleep(20);
                    }
                }
            }
            catch (Exception error) when (error is IOException || error is HttpListenerException || error is ObjectDisposedException) { }
            finally { Interlocked.Decrement(ref streamClients); try { context.Response.Close(); } catch { } }
        });
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
        stopping = true;
        if (input != null) input.SerialLineProcessed -= RecordSerialLine;
        listener?.Close();
        listener = null;
        if (listenerThread != null && listenerThread.IsAlive) listenerThread.Join(500);
    }
}
