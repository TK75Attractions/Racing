using System;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>Editor-session settings only; saved scenes and PlayerSettings stay untouched.</summary>
public sealed class ESP32SimulatorSetupWindow : EditorWindow
{
    [Serializable]
    private sealed class ConnectionInfo
    {
        public int version;
        public string port;
        public float divisor;
        public int pid;
    }

    private string port;
    private float divisor;
    private string message;
    [SerializeField] private bool showMonitor = true;

    [MenuItem("Racing/ESP32 Simulator")]
    public static void Open() => GetWindow<ESP32SimulatorSetupWindow>("ESP32 Simulator");

    private void OnEnable()
    {
        port = SessionState.GetString(SerialSimulatorSession.PortKey, "");
        divisor = SessionState.GetFloat(SerialSimulatorSession.DivisorKey, 6.5f);
        minSize = new Vector2(480, 370);
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
    }

    private void OnDisable() => EditorApplication.playModeStateChanged -= OnPlayModeChanged;

    private void OnPlayModeChanged(PlayModeStateChange change)
    {
        if (change != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(SerialSimulatorSession.EnabledKey, false)) return;
        InputManager input = FindFirstObjectByType<InputManager>();
        if (input != null) { input.SerialDebugMode = showMonitor; input.SetSerialDebugDisplayVisible(showMonitor); }
    }

    [MenuItem("Racing/Build/ESP32 Simulator Player")]
    public static void BuildSimulatorPlayer()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play Mode before building.");
        string path = Path.GetFullPath("Builds/ESP32Simulator/Player/Race.app");
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { "Assets/Scenes/SampleScene.unity" },
            locationPathName = path,
            target = BuildTarget.StandaloneOSX,
            options = BuildOptions.Development
        });
        if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
            throw new InvalidOperationException("ESP32 simulator Player build failed: " + report.summary.result);
        UnityEngine.Debug.Log("ESP32_SIMULATOR_PLAYER_BUILD_PASS: " + path);
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("ESP32なしでシリアル入力を検証", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("Macアプリで仮想ポートを作成し、接続情報を読み込んで有効化してください。設定はEditorセッション内だけに適用されます。", MessageType.Info);
        if (GUILayout.Button("ESP32 Serial Lab を起動")) LaunchApp();
        if (GUILayout.Button("アプリの接続情報を読み込む")) ReadConnection();
        port = EditorGUILayout.TextField("PTY Port", port);
        divisor = EditorGUILayout.FloatField("Steering Divisor", divisor);
        showMonitor = EditorGUILayout.Toggle("F8モニターを表示", showMonitor);
        EditorGUILayout.LabelField("状態", SessionState.GetBool(SerialSimulatorSession.EnabledKey, false) ? "シミュレーター有効" : "通常入力");
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button(EditorApplication.isPlaying ? "適用 / 再接続" : "有効化してPlay")) Enable();
            if (GUILayout.Button("無効化して通常入力に戻す")) Disable();
        }
        if (!string.IsNullOrEmpty(message)) EditorGUILayout.HelpBox(message, MessageType.Info);

        InputManager input = FindFirstObjectByType<InputManager>();
        if (EditorApplication.isPlaying && input != null)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField(input.InputModeLabel, EditorStyles.boldLabel);
            TwoPlayerSerialInputSource source = input.SharedSerialInputSource;
            if (source != null)
            {
                EditorGUILayout.LabelField("ポート", $"{source.PortName} / {(source.IsPortOpen ? "Open" : "Closed")}");
                EditorGUILayout.LabelField("受信 / 解析 / 待ち", $"{source.LinesReceived} / {source.LinesProcessed} / {source.PendingLineCount}");
                EditorGUILayout.LabelField("解析結果", source.LastParseResult);
                EditorGUILayout.SelectableLabel(source.LastSerialLine, EditorStyles.textField, GUILayout.Height(20));
                for (int i = 0; i < 2; i++)
                {
                    DriveInputState state = input.GetInputState(i);
                    EditorGUILayout.LabelField($"P{i + 1}", $"{(input.IsPlayerConnected(i) ? "Connected" : "Waiting")}  Pedal {state.pedal:F2}  Steering {state.steering:F2}");
                }
            }
        }
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("接続確認はUnityの受信数とP1/P2の値で行います。", EditorStyles.wordWrappedMiniLabel);
        EditorGUILayout.LabelField("ポート再作成後は、新しい接続情報を読み込んで再接続します。", EditorStyles.wordWrappedMiniLabel);
    }

    private void OnInspectorUpdate() { if (EditorApplication.isPlaying) Repaint(); }

    private void ReadConnection()
    {
        try
        {
            string file = Path.Combine(Path.GetTempPath(), "racing-esp32-simulator.json");
            var info = JsonUtility.FromJson<ConnectionInfo>(File.ReadAllText(file));
            if (info == null || info.version != 1) throw new InvalidDataException("Unsupported connection format.");
            // A stale descriptor must not point at a PTY reallocated by another process.
            using (Process owner = Process.GetProcessById(info.pid))
                if (owner.HasExited) throw new IOException("Simulator process is no longer running.");
            port = info.port; divisor = info.divisor;
            Validate();
            message = "接続情報を読み込みました。有効化してからアプリで送信を開始してください。";
        }
        catch (Exception exception) { message = $"接続情報を読み込めません。アプリでポートを作成してください。\n{exception.Message}"; }
    }

    private void Enable()
    {
        try
        {
            Validate();
            SessionState.SetString(SerialSimulatorSession.PortKey, port);
            SessionState.SetFloat(SerialSimulatorSession.DivisorKey, divisor);
            SessionState.SetBool(SerialSimulatorSession.EnabledKey, true);
            if (EditorApplication.isPlaying) Reinitialize();
            else EditorApplication.isPlaying = true;
            message = "シミュレーターを有効化しました。保存済みのキーボード設定より優先します。";
        }
        catch (Exception exception) { message = exception.Message; }
    }

    private void Disable()
    {
        SessionState.SetBool(SerialSimulatorSession.EnabledKey, false);
        if (EditorApplication.isPlaying) Reinitialize();
        message = "通常入力に戻しました。シーンに保存済みの入力設定が使われます。";
    }

    private void Reinitialize()
    {
        InputManager input = FindFirstObjectByType<InputManager>();
        if (input == null) throw new InvalidOperationException("InputManagerが見つかりません。SampleSceneを開いてください。");
        input.Init();
        input.SerialDebugMode = showMonitor;
        input.SetSerialDebugDisplayVisible(showMonitor);
    }

    private void Validate()
    {
        if (!Application.platform.Equals(RuntimePlatform.OSXEditor))
            throw new PlatformNotSupportedException("ESP32 SimulatorはmacOS用です。");
        // Reader is internal to Assembly-CSharp, so validate the public session config here.
        if (string.IsNullOrEmpty(port) || !System.Text.RegularExpressions.Regex.IsMatch(port, @"\A/dev/ttys[0-9]+\z"))
            throw new ArgumentException("PTY Portには /dev/ttys000 の形式を指定してください。");
        if (!File.Exists(port)) throw new FileNotFoundException("仮想ポートがありません。アプリで作成し直してください。", port);
        if (float.IsNaN(divisor) || float.IsInfinity(divisor) || divisor < .0001f || divisor > 1000f)
            throw new ArgumentOutOfRangeException(nameof(divisor), "Steering Divisorは0.0001〜1000で指定してください。");
    }

    private void LaunchApp()
    {
        string path = Path.GetFullPath(Path.Combine(Application.dataPath, "../Builds/ESP32Simulator/ESP32 Simulator.app"));
        if (!Directory.Exists(path)) { message = "先に bash Tools/ESP32Simulator/build.sh を実行してください。"; return; }
        EditorUtility.RevealInFinder(path);
        Application.OpenURL(new Uri(path).AbsoluteUri);
    }
}
