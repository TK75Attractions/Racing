import Foundation

struct PlayerInput: Codable, Equatable {
    var accel: Double = 0
    var brake: Double = 0
    var steering: Double = 0
    var accelUnavailable = false
    var brakeUnavailable = false
    var steeringUnavailable = false
    var available: Bool { !accelUnavailable && !brakeUnavailable && !steeringUnavailable }

    init(accel: Double = 0, brake: Double = 0, steering: Double = 0,
         accelUnavailable: Bool = false, brakeUnavailable: Bool = false, steeringUnavailable: Bool = false) {
        self.accel = accel; self.brake = brake; self.steering = steering
        self.accelUnavailable = accelUnavailable; self.brakeUnavailable = brakeUnavailable; self.steeringUnavailable = steeringUnavailable
    }
    private enum CodingKeys: String, CodingKey { case accel, brake, steering, accelUnavailable, brakeUnavailable, steeringUnavailable, pedal, pedalUnavailable }
    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        guard !c.contains(.pedal), !c.contains(.pedalUnavailable) else {
            throw SimulatorError.message("旧pedal形式は使えません。accelとbrakeを0〜1で指定してください。")
        }
        accel = try c.decodeIfPresent(Double.self, forKey: .accel) ?? 0
        brake = try c.decodeIfPresent(Double.self, forKey: .brake) ?? 0
        steering = try c.decodeIfPresent(Double.self, forKey: .steering) ?? 0
        accelUnavailable = try c.decodeIfPresent(Bool.self, forKey: .accelUnavailable) ?? false
        brakeUnavailable = try c.decodeIfPresent(Bool.self, forKey: .brakeUnavailable) ?? false
        steeringUnavailable = try c.decodeIfPresent(Bool.self, forKey: .steeringUnavailable) ?? false
    }
    func encode(to encoder: Encoder) throws {
        var c = encoder.container(keyedBy: CodingKeys.self)
        try c.encode(accel, forKey: .accel); try c.encode(brake, forKey: .brake); try c.encode(steering, forKey: .steering)
        try c.encode(accelUnavailable, forKey: .accelUnavailable); try c.encode(brakeUnavailable, forKey: .brakeUnavailable)
        try c.encode(steeringUnavailable, forKey: .steeringUnavailable)
    }
    func validate() throws {
        guard accel.isFinite, (0...1).contains(accel), brake.isFinite, (0...1).contains(brake),
              steering.isFinite, (-3600...3600).contains(steering) else {
            throw SimulatorError.message("アクセル・ブレーキは0〜1、ハンドル実角度は±3600度で指定してください。")
        }
    }
}

struct InputFrame: Equatable {
    var p1 = PlayerInput()
    var p2 = PlayerInput()
    var available: Bool { p1.available && p2.available }
    var fields: [String] {
        func n(_ value: Double, _ digits: Int) -> String {
            String(format: "%.*f", locale: Locale(identifier: "en_US_POSIX"), digits, value == 0 ? 0 : value)
        }
        return [n(p1.steering, 2), n(p1.accel, 3), n(p1.brake, 3), n(p2.steering, 2), n(p2.accel, 3), n(p2.brake, 3)]
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
        if player != 1 { pressed.p1.accel = 1 }
        if player != 0 { pressed.p2.accel = 1 }
        return Scenario(name: "ペダル開始: 離す → 0.5秒踏む → 離す", steps: [
            Step(duration: 0.5, label: "ペダルを離す", p1: released.p1, p2: released.p2),
            Step(duration: 0.5, label: "ペダルを踏む", p1: pressed.p1, p2: pressed.p2),
            Step(duration: 0.3, label: "ペダルを離す", p1: released.p1, p2: released.p2)
        ])
    }

    static func pedalPress(player: Int, seconds: Double, current: InputFrame) -> Scenario {
        var released = current
        if player == 0 { released.p1.accel = 0; released.p1.brake = 0; released.p1.accelUnavailable = false; released.p1.brakeUnavailable = false }
        else { released.p2.accel = 0; released.p2.brake = 0; released.p2.accelUnavailable = false; released.p2.brakeUnavailable = false }
        var pressed = released
        if player == 0 { pressed.p1.accel = 1 } else { pressed.p2.accel = 1 }
        return Scenario(name: "P\(player + 1) ペダル \(seconds)秒", steps: [
            Step(duration: 0.3, p1: released.p1, p2: released.p2),
            Step(duration: seconds, p1: pressed.p1, p2: pressed.p2),
            Step(duration: 0.3, p1: released.p1, p2: released.p2)
        ])
    }
}

struct ConnectionInfo: Codable {
    var version = 2
    var wireProtocol = "RGP/1"
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
