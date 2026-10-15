import Foundation
import Darwin

@main
enum CoreTests {
    static var count = 0
    static func check(_ condition: @autoclosure () -> Bool, _ message: String) throws {
        guard condition() else { throw SimulatorError.message("FAIL: \(message)") }; count += 1
    }
    static func rejects(_ block: () throws -> Void) throws {
        var rejected = false; do { try block() } catch { rejected = true }; try check(rejected, "invalid input rejected")
    }
    static func readAvailable(_ fd: Int32) -> String {
        var data = Data(); var buffer = [UInt8](repeating: 0, count: 4096)
        while true { let size = Darwin.read(fd, &buffer, buffer.count); if size <= 0 { break }; data.append(contentsOf: buffer.prefix(size)) }
        return String(decoding: data, as: UTF8.self)
    }
    static func command(_ body: String) -> Rgp.Frame { Rgp.parse(Rgp.wire(body))! }
    static func unescape(_ value: String) -> String {
        var bytes = [UInt8](); let input = Array(value.utf8); var i = 0
        while i < input.count {
            if input[i] == 92 && i+1 < input.count {
                i += 1
                if input[i] == 110 { bytes.append(10) }
                else if input[i] == 114 { bytes.append(13) }
                else if input[i] == 120 && i+2 < input.count { bytes.append(UInt8(String(decoding: input[(i+1)...(i+2)], as: UTF8.self), radix: 16)!); i += 2 }
                else { bytes.append(input[i]) }
            } else { bytes.append(input[i]) }; i += 1
        }
        return String(decoding: bytes, as: UTF8.self)
    }
    static func vectors() throws {
        let root = URL(fileURLWithPath: FileManager.default.currentDirectoryPath).appendingPathComponent("Tests~/Rgp/Vectors")
        for line in try String(contentsOf: root.appendingPathComponent("crc-vectors.txt"), encoding: .utf8).components(separatedBy: .newlines) where !line.isEmpty && !line.hasPrefix("#") {
            let v = line.components(separatedBy: "|")
            try check(String(format: "%04X", Rgp.crc(v[1] == "<EMPTY>" ? "" : v[1])) == v[2], "CRC vector \(v[0])")
        }
        for line in try String(contentsOf: root.appendingPathComponent("invalid-frames.txt"), encoding: .utf8).components(separatedBy: .newlines) where !line.isEmpty && !line.hasPrefix("#") {
            let v = line.components(separatedBy: "|"); guard v[1] == "HOST_TO_ESP" else { continue }
            let wire = unescape(v[3])
            if v[4].hasPrefix("ESP_DISCARD") { try check(Rgp.parse(wire) == nil, "discard vector \(v[0])") }
            else if v[4].hasPrefix("ESP_ACK_INVALID_ARG_") {
                let device = RgpDevice(); _ = device.boot()
                _ = device.handle(command("CMD,99,0,OPEN_SESSION,0123456789ABCDEF"), time: 0)
                guard let packet = Rgp.parse(wire) else { throw SimulatorError.message("lost CMD vector \(v[0])") }
                let ack = Rgp.parse(device.handle(packet, time: 1)[0])!
                try check(ack.data[2] == "INVALID_ARG" && ack.data[3] == String(v[4].suffix(1)), "argument vector \(v[0])")
            }
        }
    }
    static func main() throws {
        try vectors()
        let frame = InputFrame(p1: PlayerInput(accel: 0.75, steering: 30), p2: PlayerInput(accel: 0.5, steering: -15))
        try check(frame.fields.joined(separator: ",") == "30.00,0.750,0.000,-15.00,0.500,0.000", "six independent wire fields; no divisor scaling")
        try check(Rgp.wire("TEL,13,8210,-12.50,0.750,0.000,25.00,0.000,0.400") == "@TEL,13,8210,-12.50,0.750,0.000,25.00,0.000,0.400*7118\n", "TEL reference vector")
        try rejects { try PlayerInput(accel: .nan).validate() }
        try rejects { try PlayerInput(accel: 1.001).validate() }
        try rejects { try PlayerInput(brake: -0.001).validate() }
        try rejects { try PlayerInput(steering: 3600.01).validate() }
        try rejects { _ = try JSONDecoder().decode(Scenario.self, from: Data("{\"name\":\"old\",\"steps\":[{\"duration\":1,\"p1\":{\"pedal\":0.5}}]}".utf8)) }
        try rejects { try Scenario(name: "bad", steps: []).validate() }
        try rejects { try Scenario(name: "bad", steps: [.init(duration: 1, raw: "x\nx")]).validate() }
        try rejects { try Scenario(name: "bad", steps: [.init(duration: 1, raw: "x", silence: true)]).validate() }
        let decoded = try JSONDecoder().decode(Scenario.self, from: Data("{\"name\":\"minimal\",\"steps\":[{\"duration\":1,\"p1\":{\"accel\":0.5}}]}".utf8))
        try decoded.validate(); try check(decoded.steps[0].frame.p2 == PlayerInput(), "omitted scenario values neutral")

        let device = RgpDevice(); let boot = device.boot().compactMap(Rgp.parse)
        try check(boot[0].type == "SYS" && boot[0].seq == 0 && boot[0].time == 0 && boot.last!.data == ["STATE", "READY"], "HELLO and boot order")
        let open = command("CMD,0,0,OPEN_SESSION,0123456789ABCDEF")
        try check(Rgp.parse(device.handle(open, time: 10)[0])!.data == ["0","OPEN_SESSION","OK","0123456789ABCDEF"], "session ACK")
        let beta = command("CMD,1,0,SET_BETA,0.300")
        try check(device.handle(beta, time: 11).count == 2, "SET_BETA ACK before change event")
        try check(device.handle(beta, time: 12).count == 1, "duplicate command does not execute twice")
        _ = device.handle(command("CMD,2,0,OPEN_SESSION,0123456789ABCDEF"), time: 12)
        try check(device.handle(beta, time: 12).count == 1, "same session ID preserves previous command records")
        try check(Rgp.parse(device.handle(command("CMD,1,0,SET_BETA,0.400"), time: 13)[0])!.data.suffix(2) == ["FAILED","SEQ_REUSE"], "different body same sequence rejected")
        try check(Rgp.parse(device.handle(command("CMD,0,0,OPEN_SESSION,FEDCBA9876543210"), time: 14)[0])!.data[2] == "OK", "new session resets prior sequence cache")
        _ = device.handle(beta, time: 20)
        for i in 2...17 { _ = device.handle(command("CMD,\(i),0,PING"), time: 21) }
        try check(Rgp.parse(device.handle(command("CMD,1,0,SET_BETA,0.400"), time: 22)[0])!.data[2] == "FAILED", "16 commands alone cannot expire cache")
        try check(Rgp.parse(device.handle(command("CMD,1,0,SET_BETA,0.400"), time: 10021)[0])!.data[2] == "OK", "cache expires after both 16 commands and 10 seconds")
        var invalid = frame; invalid.p1.steeringUnavailable = true
        try check(!device.availability(invalid, time: 10022).isEmpty && device.state == "WAIT_ANGLE" && device.telemetry(invalid, time: 10023) == nil, "one invalid sensor stops all TEL")
        let zeroFailed = device.handle(command("CMD,19,0,ANGLE_ZERO"), time: 10023)
        try check(zeroFailed.count == 2 && Rgp.parse(zeroFailed[0])!.data.suffix(2) == ["FAILED","ANGLE_REFERENCE_INVALID"] &&
                  Rgp.parse(zeroFailed[1])!.type == "ERR", "unavailable angle zero returns failure ACK and WARN")
        _ = device.availability(frame, time: 10024)
        try check(device.state == "READY" && device.telemetry(frame, time: 10025) != nil, "valid sensors restore complete TEL")
        _ = device.handle(command("CMD,20,0,SET_STREAM,TEL,0"), time: 10026)
        try check(device.telemetry(frame, time: 10027) == nil, "SET_STREAM stops TEL")
        var decoder = RgpWireDecoder()
        let ping = Rgp.wire("CMD,3,0,PING")
        try check(decoder.push(Array(("garbage@broken"+ping).utf8), now: 0).count == 1, "new @ resynchronizes")
        _ = decoder.push(Array(ping.prefix(7).utf8), now: 1)
        try check(decoder.push(Array(ping.dropFirst(7).utf8), now: 2.1).isEmpty, "partial frame expires after 1 second")

        let engine = SimulatorEngine(); engine.setControls(frame, divisor: 6.5, hz: 100); engine.createPort()
        let initial = engine.snapshot(); try check(!initial.port.isEmpty && !initial.transmitting, "port creation waits for TEL start")
        let fd = Darwin.open(initial.port, O_RDWR | O_NONBLOCK | O_NOCTTY)
        guard fd >= 0 else { throw SimulatorError.message("reader open") }; defer { Darwin.close(fd); engine.shutdown() }
        Thread.sleep(forTimeInterval: 0.05)
        let first = readAvailable(fd); try check(first.contains("@SYS,0,0,HELLO,RGP,1.0,"), "real PTY sends HELLO before TEL")
        let request = Data(Rgp.wire("CMD,0,0,OPEN_SESSION,0123456789ABCDEF").utf8)
        _ = request.withUnsafeBytes { Darwin.write(fd, $0.baseAddress!, request.count) }
        Thread.sleep(forTimeInterval: 0.05)
        try check(readAvailable(fd).contains("0,OPEN_SESSION,OK,0123456789ABCDEF"), "real PTY bidirectional command ACK while paused")
        engine.start(); Thread.sleep(forTimeInterval: 0.3)
        let steady = readAvailable(fd)
        let telemetries = steady.components(separatedBy: "\n").dropLast().filter { $0.hasPrefix("@TEL,") }
        try check((15...35).contains(telemetries.count), "100 Hz telemetry near configured rate")
        try check(telemetries.allSatisfy { Rgp.parse($0+"\n")?.data == frame.fields }, "raw PTY preserves CRC and independent values: \(telemetries.filter { Rgp.parse($0+"\n")?.data != frame.fields }.prefix(2)) expected \(frame.fields)")
        engine.pause(); _ = engine.snapshot(); _ = readAvailable(fd); Thread.sleep(forTimeInterval: 0.1)
        try check(!readAvailable(fd).contains("@TEL,"), "pause stops TEL")
        engine.run(Scenario(name: "fragment", steps: [.init(duration: 1, p1: frame.p1, p2: frame.p2, fragmentDelayMilliseconds: 300)]))
        Thread.sleep(forTimeInterval: 0.05)
        let prefix = readAvailable(fd); try check(!prefix.isEmpty && !prefix.contains("\n"), "fragment prefix sent first")
        engine.setControls(InputFrame(), divisor: 6.5, hz: 100); _ = engine.snapshot(); Thread.sleep(forTimeInterval: 0.1)
        let recovered = readAvailable(fd)
        try check(recovered.hasPrefix("\n") && recovered.contains(",0.00,0.000,0.000,0.00,0.000,0.000*"), "cancel fragment and resume neutral TEL")
        engine.reboot(); _ = engine.snapshot(); Thread.sleep(forTimeInterval: 0.05)
        try check(readAvailable(fd).contains("@SYS,0,0,HELLO,RGP,1.0,"), "reboot resets HELLO sequence")
        engine.closePort(); _ = engine.snapshot()
        for _ in 0..<10 { engine.createPort(); try check(!engine.snapshot().port.isEmpty, "repeat create"); engine.closePort(); _ = engine.snapshot() }
        let congested = SimulatorEngine(); congested.createPort(); _ = congested.snapshot()
        congested.run(Scenario(name: "backpressure", steps: [.init(duration: 5, raw: String(repeating: "x", count: 512))]))
        Thread.sleep(forTimeInterval: 3.5)
        try check(!congested.snapshot().error.isEmpty && !congested.snapshot().transmitting && congested.snapshot().logs.count <= 160, "bounded writes and backpressure")
        congested.shutdown()
        print("SWIFT_RGP_TESTS_PASS: \(count) assertions")
    }
}
