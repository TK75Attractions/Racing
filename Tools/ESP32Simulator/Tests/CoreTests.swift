import Foundation
import Darwin

@main
enum CoreTests {
    static var count = 0
    static func check(_ condition: @autoclosure () -> Bool, _ message: String) throws {
        guard condition() else { throw SimulatorError.message("FAIL: \(message)") }
        count += 1
    }

    static func rejects(_ block: () throws -> Void) throws {
        var rejected = false
        do { try block() } catch { rejected = true }
        try check(rejected, "invalid input must be rejected")
    }

    static func readAvailable(_ fd: Int32) -> String {
        var data = Data()
        var buffer = [UInt8](repeating: 0, count: 4096)
        while true {
            let size = Darwin.read(fd, &buffer, buffer.count)
            if size <= 0 { break }
            data.append(contentsOf: buffer.prefix(size))
        }
        return String(decoding: data, as: UTF8.self)
    }

    static func main() throws {
        let frame = InputFrame(p1: PlayerInput(pedal: 0.75, steering: 30), p2: PlayerInput(pedal: 0.5, steering: -15))
        try check(frame.line(divisor: 6.5) == "0.7500,195.0000,0.5000,-97.5000", "wire values and steering divisor")
        var unavailable = frame; unavailable.p1.steeringUnavailable = true; unavailable.p2.pedalUnavailable = true
        try check(unavailable.line(divisor: 6.5) == "0.7500,nan,nan,-97.5000", "axis-level nan")
        try rejects { try PlayerInput(pedal: .nan).validate() }
        try rejects { try PlayerInput(steering: 91).validate() }
        try rejects { try Scenario(name: "bad", steps: []).validate() }
        try rejects { try Scenario(name: "bad", steps: [.init(duration: -1)]).validate() }
        try rejects { try Scenario(name: "bad", steps: [.init(duration: 1, raw: "0\n0")]).validate() }
        try rejects { try Scenario(name: "bad", steps: [.init(duration: 1, raw: "x", silence: true)]).validate() }
        try rejects { try Scenario(name: "bad", steps: [.init(duration: 1, fragmentDelayMilliseconds: .infinity)]).validate() }
        let decoded = try JSONDecoder().decode(Scenario.self, from: Data("{\"name\":\"minimal\",\"steps\":[{\"duration\":1,\"p1\":{\"pedal\":0.5}}]}".utf8))
        try decoded.validate()
        try check(decoded.steps[0].frame.p1.pedal == 0.5 && decoded.steps[0].frame.p2 == PlayerInput(), "optional axes and players default to neutral")

        let terminal = try PseudoTerminal()
        let fd = Darwin.open(terminal.path, O_RDONLY | O_NONBLOCK | O_NOCTTY)
        guard fd >= 0 else { throw SimulatorError.message("test reader open") }
        let line = Data((frame.line(divisor: 6.5) + "\n").utf8)
        let written = try terminal.write(line, offset: 0, limit: line.count)
        try check(written == line.count, "raw PTY write")
        Thread.sleep(forTimeInterval: 0.01)
        try check(readAvailable(fd) == String(decoding: line, as: UTF8.self), "PTY preserves exact bytes / LF / no echo")
        Darwin.close(fd); terminal.close()

        let engine = SimulatorEngine()
        engine.setControls(frame, divisor: 6.5, hz: 60)
        engine.createPort()
        let initial = engine.snapshot()
        try check(!initial.port.isEmpty && !initial.transmitting && initial.sent == 0, "port creation does not start transmission")
        let reader = Darwin.open(initial.port, O_RDONLY | O_NONBLOCK | O_NOCTTY)
        guard reader >= 0 else { throw SimulatorError.message("engine reader open") }
        engine.start()
        Thread.sleep(forTimeInterval: 0.25)
        let steady = readAvailable(reader)
        try check(steady.contains("0.7500,195.0000,0.5000,-97.5000\n"), "engine emits configured frame")
        let sentBeforePause = engine.snapshot().sent
        try check((10...20).contains(sentBeforePause), "60Hz sender is near target rate")
        engine.pause(); let pausedSent = engine.snapshot().sent; _ = readAvailable(reader)
        Thread.sleep(forTimeInterval: 0.1)
        try check(readAvailable(reader).isEmpty && engine.snapshot().sent == pausedSent, "pause keeps port but stops bytes")

        let fragment = Scenario(name: "cancel fragment", steps: [.init(duration: 1, p1: frame.p1, p2: frame.p2, fragmentDelayMilliseconds: 300)])
        engine.run(fragment)
        Thread.sleep(forTimeInterval: 0.05)
        let prefix = readAvailable(reader)
        try check(!prefix.isEmpty && !prefix.contains("\n"), "fragment delay sends a prefix first")
        engine.setControls(InputFrame(), divisor: 6.5, hz: 60)
        _ = engine.snapshot()
        Thread.sleep(forTimeInterval: 0.1)
        let recovered = readAvailable(reader)
        try check(recovered.hasPrefix("\n") && recovered.contains("0.0000,0.0000,0.0000,0.0000\n"), "manual override terminates orphan prefix and emits new frames")
        Thread.sleep(forTimeInterval: 0.35)
        let later = readAvailable(reader)
        try check(!later.contains("-97.5000"), "cancelled fragment does not fire later")

        engine.run(Scenario(name: "silence", steps: [.init(duration: 0.15, silence: true)]))
        _ = engine.snapshot(); _ = readAvailable(reader)
        Thread.sleep(forTimeInterval: 0.07)
        try check(readAvailable(reader).isEmpty, "silence sends no frame")
        Thread.sleep(forTimeInterval: 0.2)
        try check(readAvailable(reader).contains("0.0000,0.0000,0.0000,0.0000\n"), "scenario ends in neutral")
        engine.closePort(); _ = engine.snapshot()
        try check(engine.snapshot().port.isEmpty && !engine.snapshot().transmitting, "port close stops timer and clears state")
        Darwin.close(reader)
        for _ in 0..<10 {
            engine.createPort(); let state = engine.snapshot()
            try check(!state.port.isEmpty, "repeat port creation")
            engine.closePort(); _ = engine.snapshot()
        }
        engine.shutdown()

        // With no reader draining the slave, writes must stay bounded and stop.
        let congested = SimulatorEngine()
        congested.createPort(); _ = congested.snapshot()
        congested.run(Scenario(name: "backpressure", steps: [.init(duration: 5, raw: String(repeating: "x", count: 512))]))
        Thread.sleep(forTimeInterval: 3.5)
        let blocked = congested.snapshot()
        try check(!blocked.error.isEmpty && !blocked.transmitting, "backpressure stops with an actionable error")
        try check(blocked.logs.count <= 160, "history is bounded")
        congested.shutdown()
        print("SWIFT_CORE_TESTS_PASS: \(count) assertions")
    }
}
