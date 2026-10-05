using System;
using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>Publishes race state and applies authorized, session-bound remote commands.</summary>
public sealed class RaceRemoteFirebasePublisher : MonoBehaviour
{
    private const float IntervalSeconds = 5f;
    private const int RequestTimeoutSeconds = 10;
    private const string WriterFileName = "race-dashboard-writer.json";

    [Serializable] private sealed class FirebaseConfig
    {
        public string apiKey;
        public string databaseURL;
    }

    [Serializable] private sealed class WriterCredentials
    {
        public string email;
        public string password;
    }

    [Serializable] private sealed class SignInRequest
    {
        public string email;
        public string password;
        public bool returnSecureToken = true;
    }

    [Serializable] private sealed class SignInResponse
    {
        public string idToken;
        public string expiresIn;
    }

    [Serializable] private sealed class RemoteCommand
    {
        public string id;
        public string action;
        public string targetSession;
        public int targetSeq;
        public string expectedState;
        public long createdAt;
    }

    [Serializable] private sealed class CommandAcknowledgement
    {
        public string id;
        public string result;
        public string state;
    }

    private FirebaseConfig config;
    private WriterCredentials writer;
    private RaceDashboardBridge bridge;
    private string idToken;
    private float tokenExpiresAt;
    private readonly string sessionId = Guid.NewGuid().ToString("N");
    private int sequence;
    private int lastPublishedSequence;
    private string lastHandledCommandId;
    private string pendingAcknowledgement;

    private IEnumerator Start()
    {
        bridge = GetComponent<RaceDashboardBridge>();
        string publicPath = Path.Combine(Application.streamingAssetsPath, "Dashboard", "firebase-config.json");
        string writerPath = Path.Combine(Application.persistentDataPath, WriterFileName);
        if (bridge == null || !File.Exists(publicPath)) yield break;

        try
        {
            config = JsonUtility.FromJson<FirebaseConfig>(File.ReadAllText(publicPath));
        }
        catch (Exception)
        {
            Debug.LogWarning("Remote dashboard configuration could not be read.");
            yield break;
        }

        if (config == null || string.IsNullOrWhiteSpace(config.apiKey)) yield break;
        if (!ValidDatabaseUrl(config.databaseURL))
        {
            Debug.LogWarning("Remote dashboard database URL is invalid.");
            yield break;
        }
        if (!File.Exists(writerPath))
        {
            Debug.LogWarning("Remote dashboard writer settings not found at " + writerPath);
            yield break;
        }
        try { writer = JsonUtility.FromJson<WriterCredentials>(File.ReadAllText(writerPath)); }
        catch (Exception)
        {
            Debug.LogWarning("Remote dashboard writer settings could not be read.");
            yield break;
        }
        if (writer == null || string.IsNullOrWhiteSpace(writer.email) || string.IsNullOrWhiteSpace(writer.password))
        {
            Debug.LogWarning("Remote dashboard writer settings are incomplete.");
            yield break;
        }

        StartCoroutine(PollRemoteCommands());
        float retrySeconds = IntervalSeconds;
        while (true)
        {
            if (string.IsNullOrEmpty(idToken) || Time.realtimeSinceStartup >= tokenExpiresAt)
                yield return SignIn();

            if (string.IsNullOrEmpty(idToken))
            {
                yield return new WaitForSecondsRealtime(retrySeconds);
                retrySeconds = Math.Min(60f, retrySeconds * 2f);
                continue;
            }

            string json = bridge.BuildRemoteSnapshotJson(sessionId, ++sequence);
            if (json == null)
            {
                Debug.LogWarning("Remote dashboard snapshot exceeded the 4 KiB limit.");
                yield return new WaitForSecondsRealtime(IntervalSeconds);
                continue;
            }

            string url = config.databaseURL.TrimEnd('/') + "/live.json?auth=" +
                Uri.EscapeDataString(idToken) + "&print=silent";
            bool succeeded;
            using (UnityWebRequest request = new UnityWebRequest(url, "PUT"))
            {
                request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                request.timeout = RequestTimeoutSeconds;
                yield return request.SendWebRequest();
                succeeded = request.result == UnityWebRequest.Result.Success && request.responseCode == 204;
                if (!succeeded)
                {
                    if (request.responseCode == 401) idToken = null;
                    // Do not log request.error or URL: the Firebase ID token is in the query string.
                    Debug.LogWarning("Remote dashboard upload failed (HTTP " + request.responseCode + ").");
                }
            }

            retrySeconds = succeeded ? IntervalSeconds : Math.Min(60f, retrySeconds * 2f);
            if (succeeded) lastPublishedSequence = sequence;
            yield return new WaitForSecondsRealtime(retrySeconds);
        }
    }

    private IEnumerator PollRemoteCommands()
    {
        while (true)
        {
            if (!string.IsNullOrEmpty(idToken))
            {
                if (pendingAcknowledgement != null)
                    yield return SendAcknowledgement();

                if (pendingAcknowledgement == null && lastPublishedSequence > 0)
                {
                    string url = DatabasePath("control/request") + "?auth=" + Uri.EscapeDataString(idToken);
                    using (UnityWebRequest request = UnityWebRequest.Get(url))
                    {
                        request.timeout = RequestTimeoutSeconds;
                        yield return request.SendWebRequest();
                        if (request.responseCode == 401) idToken = null;
                        else if (request.result == UnityWebRequest.Result.Success && request.responseCode == 200 &&
                                 request.downloadHandler.text != "null")
                        {
                            RemoteCommand command = null;
                            try { command = JsonUtility.FromJson<RemoteCommand>(request.downloadHandler.text); }
                            catch (Exception) { }
                            if (command != null && !string.IsNullOrEmpty(command.id) &&
                                command.id != lastHandledCommandId)
                            {
                                // Remember the ID before applying the action: a failed acknowledgement must not replay it.
                                lastHandledCommandId = command.id;
                                long serverNow = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                                if (DateTimeOffset.TryParse(request.GetResponseHeader("Date"), out DateTimeOffset responseTime))
                                    serverNow = responseTime.ToUnixTimeMilliseconds();
                                long ageMs = serverNow - command.createdAt;
                                bool current = command.targetSession == sessionId &&
                                    command.targetSeq <= lastPublishedSequence &&
                                    command.targetSeq >= lastPublishedSequence - 4 &&
                                    ageMs >= -5000 && ageMs <= 15000;
                                bool applied = false;
                                if (current)
                                {
                                    try { applied = bridge.TryApplyRemoteAction(command.action, command.expectedState); }
                                    catch (Exception error)
                                    {
                                        Debug.LogWarning("Remote dashboard action failed: " + error.GetType().Name);
                                    }
                                }
                                var acknowledgement = new CommandAcknowledgement
                                {
                                    id = command.id,
                                    result = applied ? "applied" : "rejected",
                                    state = GetComponent<Gmanager>().state.ToString()
                                };
                                pendingAcknowledgement = "{\"completedAt\":{\".sv\":\"timestamp\"}," +
                                    JsonUtility.ToJson(acknowledgement).Substring(1);
                                yield return SendAcknowledgement();
                            }
                        }
                    }
                }
            }
            yield return new WaitForSecondsRealtime(1.5f);
        }
    }

    private IEnumerator SendAcknowledgement()
    {
        string url = DatabasePath("control/ack") + "?auth=" +
            Uri.EscapeDataString(idToken) + "&print=silent";
        using (UnityWebRequest request = new UnityWebRequest(url, "PUT"))
        {
            request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(pendingAcknowledgement));
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.timeout = RequestTimeoutSeconds;
            yield return request.SendWebRequest();
            if (request.result == UnityWebRequest.Result.Success && request.responseCode == 204)
                pendingAcknowledgement = null;
            else if (request.responseCode == 401)
                idToken = null;
            else if (request.responseCode == 403)
            {
                // A newer request may have replaced this one after it expired.
                pendingAcknowledgement = null;
            }
        }
    }

    private string DatabasePath(string path) => config.databaseURL.TrimEnd('/') + "/" + path + ".json";

    private IEnumerator SignIn()
    {
        string url = "https://identitytoolkit.googleapis.com/v1/accounts:signInWithPassword?key=" +
            Uri.EscapeDataString(config.apiKey);
        SignInRequest body = new SignInRequest { email = writer.email, password = writer.password };
        using (UnityWebRequest request = new UnityWebRequest(url, "POST"))
        {
            request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(JsonUtility.ToJson(body)));
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.timeout = RequestTimeoutSeconds;
            yield return request.SendWebRequest();
            if (request.result != UnityWebRequest.Result.Success || request.responseCode != 200)
            {
                Debug.LogWarning("Remote dashboard sign-in failed (HTTP " + request.responseCode + ").");
                yield break;
            }

            SignInResponse result = null;
            try { result = JsonUtility.FromJson<SignInResponse>(request.downloadHandler.text); }
            catch (Exception) { }
            if (result == null || string.IsNullOrEmpty(result.idToken))
            {
                Debug.LogWarning("Remote dashboard sign-in returned no token.");
                yield break;
            }
            idToken = result.idToken;
            int lifetime = int.TryParse(result.expiresIn, out int parsed) ? parsed : 3600;
            tokenExpiresAt = Time.realtimeSinceStartup + Math.Max(60, lifetime - 60);
        }
    }

    private static bool ValidDatabaseUrl(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri uri)) return false;
        return uri.Scheme == Uri.UriSchemeHttps && uri.IsDefaultPort &&
            string.IsNullOrEmpty(uri.UserInfo) && string.IsNullOrEmpty(uri.Query) &&
            string.IsNullOrEmpty(uri.Fragment) && uri.AbsolutePath == "/" &&
            (uri.Host.EndsWith(".firebaseio.com", StringComparison.OrdinalIgnoreCase) ||
             uri.Host.EndsWith(".firebasedatabase.app", StringComparison.OrdinalIgnoreCase));
    }

    private void OnDisable() => StopAllCoroutines();
}
