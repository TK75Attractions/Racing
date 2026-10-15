import SwiftUI
import AppKit

@MainActor
final class SimulatorModel: ObservableObject {
    @Published var frame = InputFrame() { didSet { applyControls() } }
    @Published var divisor = 6.5 { didSet { applyControls() } }
    @Published var hz = 100.0 { didSet { applyControls() } }
    @Published var snapshot = EngineSnapshot()
    @Published var scenario: Scenario?
    @Published var scenarioFile = ""
    @Published var uiError = ""
    @Published var keyboardEnabled = false { didSet { releaseKeys() } }
    private var keys = Set<UInt16>()
    private var eventMonitor: Any?
    private var focusObserver: NSObjectProtocol?
    private var engine: SimulatorEngine!

    init() {
        engine = SimulatorEngine(publishesConnection: true) { [weak self] state in
            DispatchQueue.main.async {
                guard let self = self else { return }
                let finishedScenario = self.snapshot.mode != "手動操作" && state.mode == "手動操作"
                self.snapshot = state
                if finishedScenario && self.frame != state.manualFrame { self.frame = state.manualFrame }
            }
        }
        eventMonitor = NSEvent.addLocalMonitorForEvents(matching: [.keyDown, .keyUp]) { [weak self] event in
            guard let self = self, self.keyboardEnabled,
                  !(NSApp.keyWindow?.firstResponder is NSTextView),
                  event.modifierFlags.intersection([.command, .control, .option]).isEmpty,
                  [UInt16(0), 1, 2, 13, 123, 124, 125, 126].contains(event.keyCode) else { return event }
            if event.type == .keyDown { self.keys.insert(event.keyCode) }
            else { self.keys.remove(event.keyCode) }
            self.updateKeys()
            return nil
        }
        focusObserver = NotificationCenter.default.addObserver(forName: NSApplication.didResignActiveNotification,
                                                               object: nil, queue: .main) { [weak self] _ in
            DispatchQueue.main.async { self?.releaseKeys() }
        }
    }

    func createPort() { uiError = ""; engine.createPort() }
    func closePort() { engine.closePort() }
    func start() { engine.start() }
    func pause() { engine.pause() }
    func shutdown() { releaseKeys(); engine.shutdown() }

    func neutral() { releaseKeys(); frame = InputFrame() }
    func run(_ scenario: Scenario) { uiError = ""; engine.run(scenario) }
    func titleStart(_ player: Int?) { run(.titleStart(player: player, current: frame)) }
    func press(_ player: Int, seconds: Double) { run(.pedalPress(player: player, seconds: seconds, current: frame)) }

    func copyPort() {
        NSPasteboard.general.clearContents()
        NSPasteboard.general.setString(snapshot.port, forType: .string)
    }

    func loadScenario() {
        let panel = NSOpenPanel()
        panel.allowedContentTypes = [.json]; panel.allowsMultipleSelection = false
        panel.message = "ESP32模擬入力のシナリオJSONを選択してください。"
        if let bundled = Bundle.main.resourceURL?.appendingPathComponent("Scenarios") { panel.directoryURL = bundled }
        if panel.runModal() == .OK, let url = panel.url {
            do { scenario = try Scenario.load(url); scenarioFile = url.lastPathComponent; uiError = "" }
            catch { uiError = error.localizedDescription }
        }
    }

    func fault(_ kind: Int) {
        var steps: [Scenario.Step] = []
        switch kind {
        case 0:
            steps = [.init(duration: 3, label: "無送信 / 3秒", silence: true)]
        case 1:
            var missing = frame; missing.p1.accelUnavailable = true; missing.p1.brakeUnavailable = true; missing.p1.steeringUnavailable = true
            steps = [.init(duration: 3, label: "P1センサー無効 / 3秒", p1: missing.p1, p2: missing.p2)]
        case 2:
            steps = [.init(duration: 3, label: "不正TEL / 3秒", raw: Rgp.wire("TEL,400,0,0,NaN,0,0,0,0").trimmingCharacters(in: .newlines))]
        case 3:
            steps = [.init(duration: 0.3, label: "不正データ", raw: "@TEL,1,0,0,1,0,0,1,0*0000")]
        case 4:
            engine.reboot(); return
        default:
            steps = [.init(duration: 2, label: "10ms分割送信", p1: frame.p1, p2: frame.p2,
                           fragmentDelayMilliseconds: 10)]
        }
        run(Scenario(name: "異常通信テスト", steps: steps))
    }

    private func applyControls() { engine?.setControls(frame, divisor: divisor, hz: hz) }

    private func updateKeys() {
        var value = frame
        value.p1.accel = keys.contains(13) ? 1 : 0
        value.p1.brake = keys.contains(1) ? 1 : 0
        value.p1.steering = (keys.contains(2) ? 30 : 0) - (keys.contains(0) ? 30 : 0)
        value.p2.accel = keys.contains(126) ? 1 : 0
        value.p2.brake = keys.contains(125) ? 1 : 0
        value.p2.steering = (keys.contains(124) ? 30 : 0) - (keys.contains(123) ? 30 : 0)
        frame = value
    }

    private func releaseKeys() {
        guard !keys.isEmpty else { return }
        keys.removeAll(); updateKeys()
    }
}

struct PlayerPanel: View {
    @EnvironmentObject var model: SimulatorModel
    let player: Int
    let tint: Color
    @Binding var input: PlayerInput

    var body: some View {
        GroupBox {
            VStack(alignment: .leading, spacing: 12) {
                HStack {
                    Label("PLAYER \(player + 1)", systemImage: "steeringwheel")
                        .font(.headline).foregroundStyle(tint)
                    Spacer()
                    Button("開始入力") { model.titleStart(player) }
                }
                axis("アクセル", value: $input.accel, unavailable: $input.accelUnavailable,
                     range: 0...1, format: "%.3f", unit: "未入力 0 / 最大 1（ブレーキと独立）")
                axis("ブレーキ", value: $input.brake, unavailable: $input.brakeUnavailable,
                     range: 0...1, format: "%.3f", unit: "未入力 0 / 最大 1（アクセルと独立）")
                axis("ハンドル実角度", value: $input.steering, unavailable: $input.steeringUnavailable,
                     range: -3600...3600, format: "%.2f°", unit: "左が負 / 右が正。累積角度・wrapなし")
                HStack {
                    Button("左30°") { input.steering = -30 }
                    Button("直進") { input.steering = 0 }
                    Button("右30°") { input.steering = 30 }
                    Spacer()
                    Button("中立") { input = PlayerInput() }
                }
                HStack {
                    Button("ブレーキ") { input.brake = 1 }
                    Button("アクセル") { input.accel = 1 }
                    Button("短押し") { model.press(player, seconds: 0.15) }
                    Button("長押し") { model.press(player, seconds: 1.2) }
                }
            }.padding(8)
        }
        .tint(tint)
    }

    private func axis(_ title: String, value: Binding<Double>, unavailable: Binding<Bool>,
                      range: ClosedRange<Double>, format: String, unit: String) -> some View {
        VStack(alignment: .leading, spacing: 4) {
            HStack {
                Text(title).fontWeight(.medium)
                Text(String(format: format, value.wrappedValue)).monospacedDigit().foregroundStyle(tint)
                Spacer()
                Toggle("センサー無効", isOn: unavailable).toggleStyle(.checkbox)
            }
            HStack {
                Slider(value: value, in: range).disabled(unavailable.wrappedValue)
                TextField("値", value: value, format: .number.precision(.fractionLength(0...3)))
                    .textFieldStyle(.roundedBorder).frame(width: 76).disabled(unavailable.wrappedValue)
            }
            Text(unit).font(.caption).foregroundStyle(.secondary)
        }
    }
}

struct SimulatorView: View {
    @EnvironmentObject var model: SimulatorModel
    var body: some View {
        VStack(alignment: .leading, spacing: 14) {
            HStack(alignment: .top) {
                VStack(alignment: .leading, spacing: 4) {
                    Text("ESP32 Serial Lab").font(.system(size: 25, weight: .bold, design: .rounded))
                    Text("RGP/1：CRC・双方向セッション・2人分の独立ペダルを検証").foregroundStyle(.secondary)
                }
                Spacer()
                Label(model.snapshot.transmitting ? "送信中" : "送信停止",
                      systemImage: model.snapshot.transmitting ? "waveform.path" : "pause.circle")
                    .foregroundStyle(model.snapshot.transmitting ? Color.green : Color.secondary)
                    .padding(9).background(.quaternary, in: RoundedRectangle(cornerRadius: 8))
            }
            GroupBox {
                VStack(alignment: .leading, spacing: 9) {
                    HStack {
                        Text(model.snapshot.port.isEmpty ? "仮想ポート未作成" : model.snapshot.port)
                            .font(.system(.title3, design: .monospaced)).textSelection(.enabled)
                        Spacer()
                        Button("ポートを作成") { model.createPort() }.disabled(!model.snapshot.port.isEmpty)
                        Button("パスをコピー") { model.copyPort() }.disabled(model.snapshot.port.isEmpty)
                        Button("ポート破棄", role: .destructive) { model.closePort() }.disabled(model.snapshot.port.isEmpty)
                    }
                    HStack(spacing: 14) {
                        Button("送信開始") { model.start() }.buttonStyle(.borderedProminent)
                            .disabled(model.snapshot.port.isEmpty || model.snapshot.transmitting)
                        Button("送信停止") { model.pause() }.disabled(!model.snapshot.transmitting)
                        Button("全て中立") { model.neutral() }
                        Divider().frame(height: 20)
                        Picker("周期", selection: $model.hz) {
                            Text("30 Hz").tag(30.0); Text("60 Hz").tag(60.0); Text("100 Hz").tag(100.0)
                        }.frame(width: 145)
                        Text("Unityの除算値")
                        TextField("6.5", value: $model.divisor, format: .number)
                            .textFieldStyle(.roundedBorder).frame(width: 65)
                        Spacer()
                        Text(String(format: "実測 %.1f Hz", model.snapshot.measuredHz)).monospacedDigit()
                    }
                    Text("装置: \(model.snapshot.protocolState) / セッション: \(model.snapshot.sessionOpen ? "確立" : "待機") / 除算値はUnity接続情報のみ。送信角度は実角度です。")
                        .font(.caption).foregroundStyle(.secondary)
                    Text("Unity: Racing → ESP32 Simulator → アプリの接続情報を読み込む → 有効化してPlay。送信成功はUnity受信の確認ではありません。")
                        .font(.caption).foregroundStyle(.secondary)
                }.padding(7)
            }
            HStack(alignment: .top, spacing: 14) {
                PlayerPanel(player: 0, tint: .cyan, input: $model.frame.p1)
                PlayerPanel(player: 1, tint: .pink, input: $model.frame.p2)
            }
            HStack {
                Toggle("キーボード操作", isOn: $model.keyboardEnabled).toggleStyle(.checkbox)
                Text("P1: WASD / P2: 矢印キー（このアプリが前面のとき）").font(.caption).foregroundStyle(.secondary)
                Spacer()
                Button("両者の開始入力") { model.titleStart(nil) }.disabled(model.snapshot.port.isEmpty)
            }
            GroupBox {
                VStack(alignment: .leading, spacing: 8) {
                    HStack {
                        Text("シナリオ / 異常通信").font(.headline)
                        Spacer()
                        Button("JSONを開く…") { model.loadScenario() }
                        Text(model.scenarioFile.isEmpty ? "未選択" : model.scenarioFile).font(.caption).foregroundStyle(.secondary)
                        Button("再生") { if let scenario = model.scenario { model.run(scenario) } }
                            .disabled(model.scenario == nil || model.snapshot.port.isEmpty)
                    }
                    HStack {
                        ForEach(Array(["3秒TEL停止", "P1センサー無効", "不正TEL", "CRCエラー", "再起動/HELLO", "分割送信"].enumerated()), id: \.offset) { item in
                            Button(item.element) { model.fault(item.offset) }
                        }
                    }.disabled(model.snapshot.port.isEmpty)
                    HStack {
                        Text(model.snapshot.mode).font(.caption).lineLimit(1)
                        ProgressView(value: model.snapshot.progress).frame(maxWidth: 220)
                        Spacer()
                        Text("完了後は中立。手動操作で再生を中止。").font(.caption).foregroundStyle(.secondary)
                    }
                }.padding(7)
            }
            if !model.uiError.isEmpty || !model.snapshot.error.isEmpty {
                Label(model.uiError.isEmpty ? model.snapshot.error : model.uiError, systemImage: "exclamationmark.triangle")
                    .foregroundStyle(.orange).font(.callout).textSelection(.enabled)
            }
            HStack {
                Text("RGP/1 送受信モニター").font(.headline)
                Text("\(model.snapshot.sent) 行 / 待ちで省略 \(model.snapshot.skipped) 回").font(.caption).monospacedDigit().foregroundStyle(.secondary)
                Spacer()
                Text("Unity受信は F8 / ローカルダッシュボードで確認").font(.caption).foregroundStyle(.secondary)
            }
            Text(model.snapshot.lastLine).font(.system(.body, design: .monospaced))
                .textSelection(.enabled).frame(maxWidth: .infinity, alignment: .leading)
                .padding(10).background(.quaternary, in: RoundedRectangle(cornerRadius: 6))
            ScrollView {
                LazyVStack(alignment: .leading, spacing: 3) {
                    ForEach(model.snapshot.logs.reversed()) { row in
                        Text("\(row.time)  \(row.text)").font(.system(size: 11, design: .monospaced))
                            .frame(maxWidth: .infinity, alignment: .leading)
                    }
                }.textSelection(.enabled)
            }.frame(minHeight: 80).padding(8).background(.quaternary, in: RoundedRectangle(cornerRadius: 6))
        }
        .padding(20).frame(minWidth: 1080, minHeight: 850)
    }
}

@MainActor
final class SimulatorAppDelegate: NSObject, NSApplicationDelegate {
    var model: SimulatorModel?
    func applicationShouldTerminate(_ sender: NSApplication) -> NSApplication.TerminateReply {
        model?.shutdown(); return .terminateNow
    }
    func applicationShouldTerminateAfterLastWindowClosed(_ sender: NSApplication) -> Bool { true }
}

@main
struct ESP32SimulatorApp: App {
    @NSApplicationDelegateAdaptor(SimulatorAppDelegate.self) private var delegate
    @StateObject private var model = SimulatorModel()
    var body: some Scene {
        WindowGroup("ESP32 Serial Lab") {
            SimulatorView().environmentObject(model)
                .onAppear { delegate.model = model }
        }.windowResizability(.contentMinSize)
    }
}
