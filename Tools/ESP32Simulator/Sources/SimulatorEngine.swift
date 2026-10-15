import Foundation

struct LogEntry: Identifiable { let id: UInt64; let time: String; let text: String }
struct EngineSnapshot {
    var port = ""
    var transmitting = false
    var sent: UInt64 = 0
    var skipped: UInt64 = 0
    var measuredHz: Double = 0
    var lastLine = "—"
    var error = ""
    var mode = "手動操作"
    var progress: Double = 0
    var logs: [LogEntry] = []
    var manualFrame = InputFrame()
    var protocolState = "未接続"
    var sessionOpen = false
}

/// Serializes the device protocol, control replies, telemetry, and bounded nonblocking writes.
final class SimulatorEngine {
    private let queue = DispatchQueue(label: "racing.esp32-simulator.sender")
    private var timer: DispatchSourceTimer?
    private var terminal: PseudoTerminal?
    private var connection: ConnectionInfo?
    private var frame = InputFrame()
    private var divisor: Double = 6.5 // Metadata for the Unity game adapter; never scales wire angles.
    private var hz: Double = 100
    private var transmitting = false
    private var scenario: Scenario?
    private var scenarioStart: Double = 0
    private var nextFrame: Double = 0
    private var lastSnapshot: Double = 0
    private var rateStart = ProcessInfo.processInfo.systemUptime
    private var rateCount: UInt64 = 0
    private var logID: UInt64 = 0
    private var state = EngineSnapshot()
    private var device = RgpDevice()
    private var decoder = RgpWireDecoder()
    private var bootAt = ProcessInfo.processInfo.systemUptime
    private var outgoing = [String]()
    private var packet: Packet?
    private let callback: (EngineSnapshot) -> Void
    private let publishesConnection: Bool
    private struct Packet {
        var bytes: Data; var offset = 0; var split: Int?; var delay: Double
        var readyAt: Double; var startedAt: Double; var text: String; var control: Bool
    }
    init(publishesConnection: Bool = false, onSnapshot: @escaping (EngineSnapshot) -> Void = { _ in }) {
        self.publishesConnection = publishesConnection; callback = onSnapshot
    }
    private var now: Double { ProcessInfo.processInfo.systemUptime }
    private var milliseconds: UInt64 { UInt64(max(0, (now - bootAt) * 1000)) }

    func createPort() {
        queue.async {
            self.closePortOnQueue()
            do {
                self.terminal = try PseudoTerminal(); self.device = RgpDevice(); self.decoder = RgpWireDecoder()
                self.bootAt = self.now; self.state = EngineSnapshot(); self.state.port = self.terminal!.path
                self.rateCount = 0; self.rateStart = self.now; self.saveConnection()
                try self.enqueue(self.device.boot() + self.device.setRate(Int(self.hz), time: self.milliseconds))
                self.log("PORT \(self.state.port) — RGP/1 CMD受信待機 / TELは送信開始後")
                self.startTimer()
            } catch { self.fail(error) }
            self.publish()
        }
    }
    func closePort() { queue.async { self.closePortOnQueue(); self.publish() } }
    func setControls(_ value: InputFrame, divisor: Double, hz: Double) {
        queue.async {
            do {
                try value.p1.validate(); try value.p2.validate()
                guard divisor.isFinite, (0.0001...1000).contains(divisor), hz.isFinite, hz.rounded() == hz, (1...100).contains(hz) else {
                    throw SimulatorError.message("除算値は0.0001〜1000、TEL周期は1〜100Hzで指定してください。")
                }
                self.cancelScenario(); self.frame = value
                if self.divisor != divisor { self.divisor = divisor; self.saveConnection() }
                self.hz = hz
                if self.terminal != nil { try self.enqueue(self.device.setRate(Int(hz), time: self.milliseconds)) }
                self.state.error = ""
            } catch { self.fail(error) }
            self.publish()
        }
    }
    func start() {
        queue.async {
            guard self.terminal != nil else { self.fail(SimulatorError.message("先に仮想ポートを作成してください。")); return }
            self.transmitting = true; self.nextFrame = self.now
            self.rateCount = self.state.sent; self.rateStart = self.now; self.state.error = ""
            self.log("START TEL \(self.device.telRate) Hz / セッションCMDにも応答")
            self.publish()
        }
    }
    func pause() {
        queue.async {
            self.cancelScenario(); self.transmitting = false; self.state.measuredHz = 0
            self.log("PAUSE TEL — CMD応答は継続。無送信で両プレイヤーをstaleにします")
            self.publish()
        }
    }
    func run(_ value: Scenario) {
        queue.async {
            do {
                try value.validate()
                guard self.terminal != nil else { throw SimulatorError.message("先に仮想ポートを作成してください。") }
                self.cancelScenario(); self.scenario = value; self.scenarioStart = self.now
                self.transmitting = true; self.nextFrame = self.now; self.state.error = ""
                self.log("SCENARIO \(value.name)"); self.publish()
            } catch { self.fail(error) }
        }
    }
    func reboot() {
        queue.async {
            self.cancelScenario(); self.device = RgpDevice(); self.bootAt = self.now
            self.outgoing.removeAll(); self.packet = nil
            do { try self.enqueue(self.device.boot() + self.device.setRate(Int(self.hz), time: self.milliseconds)) }
            catch { self.fail(error) }
            self.log("REBOOT — HELLOとセッションを初期化"); self.publish()
        }
    }
    func snapshot() -> EngineSnapshot { queue.sync { state } }
    func shutdown() { queue.sync { closePortOnQueue() } }
    private func startTimer() {
        timer?.cancel()
        let source = DispatchSource.makeTimerSource(queue: queue)
        source.schedule(deadline: .now(), repeating: .milliseconds(2), leeway: .milliseconds(1))
        source.setEventHandler { [weak self] in self?.tick() }; timer = source; source.resume()
    }
    private func enqueue(_ frames: [String]) throws {
        guard outgoing.count + frames.count <= 64 else { throw SimulatorError.message("CMD応答待ちが64件を超えました。受信側の状態を確認してください。") }
        if !frames.isEmpty, let pending = packet, !pending.control { discardPending() }
        outgoing += frames
    }
    private func tick() {
        let time = now
        do {
            if let terminal = terminal {
                let bytes = try terminal.readAvailable()
                for command in decoder.push(bytes, now: time) {
                    log("RX \(command.body)")
                    try enqueue(device.handle(command, time: milliseconds))
                }
            }
            try flushPacket(time)
            // Control frames are sent even when the operator pauses telemetry.
            for _ in 0..<16 where packet == nil && !outgoing.isEmpty {
                let wire = outgoing.removeFirst()
                packet = makePacket(wire, delay: 0, control: true, time: time)
                try flushPacket(time)
            }
            if transmitting {
                var step: Scenario.Step?
                if let active = scenario {
                    let elapsed = time - scenarioStart; var end: Double = 0
                    for candidate in active.steps { end += candidate.duration; if elapsed < end { step = candidate; break } }
                    state.progress = min(1, elapsed / active.duration)
                    if let selected = step { state.mode = "\(active.name) / \(selected.label ?? "再生中")" }
                    else { cancelScenario(); frame = InputFrame(); log("SCENARIO END — ニュートラルに復帰") }
                }
                if step?.silence == true { discardPending(); nextFrame = time }
                else if time >= nextFrame {
                    let current = step?.frame ?? frame
                    try enqueue(device.availability(current, time: milliseconds))
                    if packet == nil && outgoing.isEmpty {
                        if let text = step?.raw {
                            packet = makePacket(text + "\n", delay: (step?.fragmentDelayMilliseconds ?? 0) / 1000, control: false, time: time)
                        } else if let wire = device.telemetry(current, time: milliseconds) {
                            packet = makePacket(wire, delay: (step?.fragmentDelayMilliseconds ?? 0) / 1000, control: false, time: time)
                        }
                        try flushPacket(time)
                    } else { state.skipped += 1 }
                    let period = 1 / Double(device.telRate)
                    nextFrame += period
                    if nextFrame < time { nextFrame = time + period }
                }
            }
        } catch { fail(error) }
        if time - rateStart >= 1 {
            state.measuredHz = transmitting ? Double(state.sent - rateCount) / (time - rateStart) : 0
            rateCount = state.sent; rateStart = time
        }
        if time - lastSnapshot >= 0.1 { publish() }
    }
    private func makePacket(_ wire: String, delay: Double, control: Bool, time: Double) -> Packet {
        let bytes = Data(wire.utf8)
        return Packet(bytes: bytes, split: delay > 0 ? max(1, bytes.count / 2) : nil, delay: delay,
                      readyAt: time, startedAt: time, text: wire.trimmingCharacters(in: .newlines), control: control)
    }
    private func flushPacket(_ time: Double) throws {
        guard var pending = packet, let terminal = terminal, time >= pending.readyAt else { return }
        if time - pending.startedAt > 2 { throw SimulatorError.message("書込みが2秒間進みません。Unityの実行状態を確認してください。") }
        let end = pending.split ?? pending.bytes.count
        pending.offset += try terminal.write(pending.bytes, offset: pending.offset, limit: end - pending.offset)
        if pending.offset == end, pending.split != nil { pending.split = nil; pending.readyAt = time + pending.delay }
        if pending.offset == pending.bytes.count {
            state.sent += 1; state.lastLine = pending.text; log("TX \(pending.text)"); packet = nil
        } else { packet = pending }
    }
    private func discardPending() {
        guard let pending = packet, !pending.control else { return }
        if pending.offset > 0, let terminal = terminal {
            _ = try? terminal.write(Data([10]), offset: 0, limit: 1); log("CANCEL partial frame — 次の@で再同期")
        }
        packet = nil
    }
    private func cancelScenario() {
        discardPending(); if scenario != nil { log("SCENARIO STOP") }
        scenario = nil; state.mode = "手動操作"; state.progress = 0
    }
    private func closePortOnQueue() {
        cancelScenario(); terminal?.close()
        connection?.removeIfOwned(); connection = nil
        terminal = nil; transmitting = false; state.port = ""; state.measuredHz = 0
        outgoing.removeAll(); packet = nil; timer?.cancel(); timer = nil
    }
    private func saveConnection() {
        guard publishesConnection, let path = terminal?.path else { return }
        let info = ConnectionInfo(port: path, divisor: divisor)
        do { try info.save(); connection = info } catch { state.error = "Unity接続情報を保存できません: \(error.localizedDescription)" }
    }
    private func fail(_ error: Error) {
        cancelScenario(); transmitting = false; state.measuredHz = 0
        outgoing.removeAll(); packet = nil
        state.error = error.localizedDescription; log("ERROR \(state.error)"); publish()
    }
    private func log(_ text: String) {
        logID += 1; state.logs.append(LogEntry(id: logID, time: String(format: "%.3f", now), text: text))
        if state.logs.count > 160 { state.logs.removeFirst(state.logs.count - 160) }
    }
    private func publish() {
        lastSnapshot = now; state.transmitting = transmitting; state.manualFrame = frame
        state.protocolState = terminal == nil ? "未接続" : device.state
        state.sessionOpen = device.sessionID != nil; callback(state)
    }
}
