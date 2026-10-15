import Foundation

struct LogEntry: Identifiable {
    let id: UInt64
    let time: String
    let text: String
}

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
}

/// All file descriptors, timers and pending writes belong to this one queue.
final class SimulatorEngine {
    private let queue = DispatchQueue(label: "racing.esp32-simulator.sender")
    private var timer: DispatchSourceTimer?
    private var terminal: PseudoTerminal?
    private var connection: ConnectionInfo?
    private var frame = InputFrame()
    private var divisor: Double = 6.5
    private var hz: Double = 60
    private var transmitting = false
    private var scenario: Scenario?
    private var scenarioStart: Double = 0
    private var nextFrame: Double = 0
    private var lastSnapshot: Double = 0
    private var rateStart = ProcessInfo.processInfo.systemUptime
    private var rateCount: UInt64 = 0
    private var logID: UInt64 = 0
    private var state = EngineSnapshot()
    private var packet: Packet?
    private let callback: (EngineSnapshot) -> Void
    private let publishesConnection: Bool

    private struct Packet {
        var bytes: Data
        var offset = 0
        var split: Int?
        var delay: Double
        var readyAt: Double
        var startedAt: Double
        var text: String
    }

    init(publishesConnection: Bool = false, onSnapshot: @escaping (EngineSnapshot) -> Void = { _ in }) {
        self.publishesConnection = publishesConnection
        callback = onSnapshot
    }

    func createPort() {
        queue.async {
            self.closePortOnQueue()
            do {
                self.terminal = try PseudoTerminal()
                self.state = EngineSnapshot()
                self.state.port = self.terminal!.path
                self.rateCount = 0; self.rateStart = self.now
                self.saveConnection()
                self.log("PORT \(self.state.port) — Unityの受信確認はUnity側で行ってください")
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
                guard divisor.isFinite, (0.0001...1000).contains(divisor), [30.0, 60.0, 120.0].contains(hz) else {
                    throw SimulatorError.message("倍率は0.0001〜1000、送信周期は30/60/120Hzで指定してください。")
                }
                self.cancelScenario()
                self.frame = value
                if self.divisor != divisor { self.divisor = divisor; self.saveConnection() }
                self.hz = hz
                self.state.error = ""
            } catch { self.fail(error) }
            self.publish()
        }
    }

    func start() {
        queue.async {
            guard self.terminal != nil else { self.fail(SimulatorError.message("先に仮想ポートを作成してください。")); return }
            self.terminal?.discardUnreadInput()
            self.transmitting = true
            self.nextFrame = self.now
            self.rateCount = self.state.sent; self.rateStart = self.now
            self.state.error = ""
            self.log("START \(Int(self.hz)) Hz")
            self.publish()
        }
    }

    func pause() {
        queue.async {
            self.cancelScenario()
            self.transmitting = false
            self.state.measuredHz = 0
            self.log("PAUSE — ポート維持、2秒超の無送信でUnityの入力が切断")
            self.publish()
        }
    }

    func run(_ value: Scenario) {
        queue.async {
            do {
                try value.validate()
                guard self.terminal != nil else { throw SimulatorError.message("先に仮想ポートを作成してください。") }
                self.cancelScenario()
                self.terminal?.discardUnreadInput()
                self.scenario = value; self.scenarioStart = self.now
                self.transmitting = true; self.nextFrame = self.now
                self.state.error = ""
                self.log("SCENARIO \(value.name)")
                self.publish()
            } catch { self.fail(error) }
        }
    }

    func snapshot() -> EngineSnapshot { queue.sync { state } }

    func shutdown() { queue.sync { closePortOnQueue() } }

    private var now: Double { ProcessInfo.processInfo.systemUptime }

    private func startTimer() {
        timer?.cancel()
        let source = DispatchSource.makeTimerSource(queue: queue)
        source.schedule(deadline: .now(), repeating: .milliseconds(2), leeway: .milliseconds(1))
        source.setEventHandler { [weak self] in self?.tick() }
        timer = source
        source.resume()
    }

    private func tick() {
        let time = now
        do {
            if transmitting {
                var step: Scenario.Step?
                if let active = scenario {
                    let elapsed = time - scenarioStart
                    var end: Double = 0
                    for candidate in active.steps {
                        end += candidate.duration
                        if elapsed < end { step = candidate; break }
                    }
                    state.progress = min(1, elapsed / active.duration)
                    if let selected = step { state.mode = "\(active.name) / \(selected.label ?? "再生中")" }
                    else {
                        cancelScenario(); frame = InputFrame()
                        log("SCENARIO END — ニュートラルに復帰")
                    }
                }
                if step?.silence == true {
                    discardPending(); nextFrame = time
                } else {
                    try flushPacket(time)
                    if time >= nextFrame {
                        if packet == nil {
                            let text = step?.raw ?? (step?.frame ?? frame).line(divisor: divisor)
                            let bytes = Data((text + "\n").utf8)
                            let delay = (step?.fragmentDelayMilliseconds ?? 0) / 1000
                            packet = Packet(bytes: bytes, split: delay > 0 ? max(1, bytes.count / 2) : nil,
                                            delay: delay, readyAt: time, startedAt: time, text: text)
                            try flushPacket(time)
                        } else { state.skipped += 1 }
                        nextFrame += 1 / hz
                        if nextFrame < time { nextFrame = time + 1 / hz }
                    }
                }
            }
        } catch { fail(error) }
        if time - rateStart >= 1 {
            state.measuredHz = transmitting ? Double(state.sent - rateCount) / (time - rateStart) : 0
            rateCount = state.sent; rateStart = time
        }
        if time - lastSnapshot >= 0.1 { publish() }
    }

    private func flushPacket(_ time: Double) throws {
        guard var pending = packet, let terminal = terminal, time >= pending.readyAt else { return }
        // A reader may be paused. Keep at most one packet, then stop with a useful error.
        if time - pending.startedAt > 2 {
            throw SimulatorError.message("書込みが2秒間進みません。Unityの実行状態を確認し、送信を再開してください。")
        }
        let end = pending.split ?? pending.bytes.count
        let written = try terminal.write(pending.bytes, offset: pending.offset, limit: end - pending.offset)
        pending.offset += written
        if pending.offset == end, pending.split != nil {
            pending.split = nil
            pending.readyAt = time + pending.delay
        }
        if pending.offset == pending.bytes.count {
            state.sent += 1; state.lastLine = pending.text
            log("TX \(pending.text)")
            packet = nil
        } else { packet = pending }
    }

    private func discardPending() {
        // Terminate an already-sent fragment so a later frame cannot be joined to it.
        if let pending = packet, pending.offset > 0, let terminal = terminal {
            _ = try? terminal.write(Data([10]), offset: 0, limit: 1)
            log("CANCEL partial frame — 改行で打切り（Unity側ではINVALIDになり得ます）")
        }
        packet = nil
    }

    private func cancelScenario() {
        discardPending()
        if scenario != nil { log("SCENARIO STOP") }
        scenario = nil; state.mode = "手動操作"; state.progress = 0
    }

    private func closePortOnQueue() {
        cancelScenario()
        if let terminal = terminal {
            let neutral = Data((InputFrame().line(divisor: divisor) + "\n").utf8)
            _ = try? terminal.write(neutral, offset: 0, limit: neutral.count)
            terminal.close()
            log("PORT CLOSED")
        }
        connection?.removeIfOwned(); connection = nil
        terminal = nil; transmitting = false; state.port = ""; state.measuredHz = 0
        timer?.cancel(); timer = nil
    }

    private func saveConnection() {
        guard publishesConnection, let path = terminal?.path else { return }
        let info = ConnectionInfo(port: path, divisor: divisor)
        do { try info.save(); connection = info }
        catch { state.error = "Unity接続情報を保存できません: \(error.localizedDescription)" }
    }

    private func fail(_ error: Error) {
        cancelScenario(); transmitting = false; state.measuredHz = 0
        state.error = error.localizedDescription; log("ERROR \(state.error)"); publish()
    }

    private func log(_ text: String) {
        logID += 1
        let stamp = String(format: "%.3f", now)
        state.logs.append(LogEntry(id: logID, time: stamp, text: text))
        if state.logs.count > 160 { state.logs.removeFirst(state.logs.count - 160) }
    }

    private func publish() {
        lastSnapshot = now
        state.transmitting = transmitting
        state.manualFrame = frame
        callback(state)
    }
}
