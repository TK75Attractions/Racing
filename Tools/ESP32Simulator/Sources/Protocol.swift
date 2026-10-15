import Foundation

struct PlayerInput: Codable, Equatable {
    var pedal: Double = 0
    var steering: Double = 0
    var pedalUnavailable: Bool = false
    var steeringUnavailable: Bool = false

    init(pedal: Double = 0, steering: Double = 0,
         pedalUnavailable: Bool = false, steeringUnavailable: Bool = false) {
        self.pedal = pedal; self.steering = steering
        self.pedalUnavailable = pedalUnavailable; self.steeringUnavailable = steeringUnavailable
    }

    private enum CodingKeys: String, CodingKey { case pedal, steering, pedalUnavailable, steeringUnavailable }
    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        pedal = try c.decodeIfPresent(Double.self, forKey: .pedal) ?? 0
        steering = try c.decodeIfPresent(Double.self, forKey: .steering) ?? 0
        pedalUnavailable = try c.decodeIfPresent(Bool.self, forKey: .pedalUnavailable) ?? false
        steeringUnavailable = try c.decodeIfPresent(Bool.self, forKey: .steeringUnavailable) ?? false
    }

    func validate() throws {
        guard pedal.isFinite, (-1...1).contains(pedal), steering.isFinite, (-90...90).contains(steering) else {
            throw SimulatorError.message("ペダルは-1〜1、ハンドルは-90〜90度で指定してください。")
        }
    }
}

struct InputFrame: Equatable {
    var p1 = PlayerInput()
    var p2 = PlayerInput()

    func line(divisor: Double) -> String {
        func number(_ value: Double) -> String {
            String(format: "%.4f", locale: Locale(identifier: "en_US_POSIX"), value == 0 ? 0 : value)
        }
        return [p1.pedalUnavailable ? "nan" : number(p1.pedal),
                p1.steeringUnavailable ? "nan" : number(p1.steering * divisor),
                p2.pedalUnavailable ? "nan" : number(p2.pedal),
                p2.steeringUnavailable ? "nan" : number(p2.steering * divisor)].joined(separator: ",")
    }
}

enum SimulatorError: LocalizedError {
    case message(String)
    var errorDescription: String? { if case let .message(text) = self { return text }; return nil }
}

struct Scenario: Codable {
    var name: String
    var steps: [Step]

    struct Step: Codable {
        var duration: Double
        var label: String?
        var p1: PlayerInput?
        var p2: PlayerInput?
        var raw: String?
        var silence: Bool?
        var fragmentDelayMilliseconds: Double?

        var frame: InputFrame { InputFrame(p1: p1 ?? PlayerInput(), p2: p2 ?? PlayerInput()) }
    }

    var duration: Double { steps.reduce(0) { $0 + $1.duration } }

    func validate() throws {
        guard !name.isEmpty, name.count <= 120, (1...256).contains(steps.count), duration <= 3600 else {
            throw SimulatorError.message("シナリオは名前付き・1〜256ステップ・合計1時間以内で指定してください。")
        }
        for step in steps {
            guard step.duration.isFinite, (0.05...600).contains(step.duration) else {
                throw SimulatorError.message("各ステップのdurationは0.05〜600秒で指定してください。")
            }
            try step.frame.p1.validate(); try step.frame.p2.validate()
            if let raw = step.raw {
                guard raw.utf8.count <= 512, !raw.contains("\n"), !raw.contains("\r"), raw.unicodeScalars.allSatisfy({ $0.value < 128 }) else {
                    throw SimulatorError.message("rawは改行なし・512バイト以内のASCII 1行にしてください。")
                }
            }
            if let delay = step.fragmentDelayMilliseconds {
                guard delay.isFinite, (1...1000).contains(delay), step.silence != true else {
                    throw SimulatorError.message("分割間隔は1〜1000msで指定し、silenceと併用しないでください。")
                }
            }
            if step.silence == true && step.raw != nil {
                throw SimulatorError.message("rawとsilenceは同時に指定できません。")
            }
        }
    }

    static func load(_ url: URL) throws -> Scenario {
        let data = try Data(contentsOf: url)
        guard data.count <= 65536 else { throw SimulatorError.message("シナリオは64KiB以内にしてください。") }
        let value = try JSONDecoder().decode(Scenario.self, from: data)
        try value.validate()
        return value
    }

    static func titleStart(player: Int?, current: InputFrame) -> Scenario {
        var released = current
        if player != 1 { released.p1 = PlayerInput() }
        if player != 0 { released.p2 = PlayerInput() }
        var pressed = released
        if player != 1 { pressed.p1.pedal = 1 }
        if player != 0 { pressed.p2.pedal = 1 }
        return Scenario(name: "ペダル開始: 離す → 0.5秒踏む → 離す", steps: [
            Step(duration: 0.5, label: "ペダルを離す", p1: released.p1, p2: released.p2),
            Step(duration: 0.5, label: "ペダルを踏む", p1: pressed.p1, p2: pressed.p2),
            Step(duration: 0.3, label: "ペダルを離す", p1: released.p1, p2: released.p2)
        ])
    }

    static func pedalPress(player: Int, seconds: Double, current: InputFrame) -> Scenario {
        var released = current
        if player == 0 { released.p1.pedal = 0; released.p1.pedalUnavailable = false }
        else { released.p2.pedal = 0; released.p2.pedalUnavailable = false }
        var pressed = released
        if player == 0 { pressed.p1.pedal = 1 } else { pressed.p2.pedal = 1 }
        return Scenario(name: "P\(player + 1) ペダル \(seconds)秒", steps: [
            Step(duration: 0.3, p1: released.p1, p2: released.p2),
            Step(duration: seconds, p1: pressed.p1, p2: pressed.p2),
            Step(duration: 0.3, p1: released.p1, p2: released.p2)
        ])
    }
}

struct ConnectionInfo: Codable {
    var version = 1
    var port: String
    var divisor: Double
    var pid: Int32 = ProcessInfo.processInfo.processIdentifier

    static var url: URL { FileManager.default.temporaryDirectory.appendingPathComponent("racing-esp32-simulator.json") }
    func save() throws { try JSONEncoder().encode(self).write(to: Self.url, options: .atomic) }
    func removeIfOwned() {
        if let data = try? Data(contentsOf: Self.url),
           let info = try? JSONDecoder().decode(ConnectionInfo.self, from: data), info.pid == pid, info.port == port {
            try? FileManager.default.removeItem(at: Self.url)
        }
    }
}
