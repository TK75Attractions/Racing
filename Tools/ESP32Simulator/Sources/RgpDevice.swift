import Foundation

enum Rgp {
    static func crc(_ body: String) -> UInt16 {
        var crc: UInt16 = 0xFFFF
        for byte in body.utf8 {
            crc ^= UInt16(byte) << 8
            for _ in 0..<8 { crc = crc & 0x8000 != 0 ? (crc &<< 1) ^ 0x1021 : crc &<< 1 }
        }
        return crc
    }
    static func wire(_ body: String) -> String { "@\(body)*\(String(format: "%04X", crc(body)))\n" }
    static func matches(_ value: String, _ pattern: String) -> Bool { value.range(of: pattern, options: .regularExpression) != nil }
    static func uint(_ value: String) -> UInt32? {
        guard value.utf8.count <= 24, matches(value, "\\A[0-9]+\\z") else { return nil }
        return UInt32(value)
    }
    static func number(_ value: String, min: Decimal, max: Decimal) -> Bool {
        guard value.utf8.count <= 24, matches(value, "\\A-?[0-9]+(?:\\.[0-9]+)?\\z"),
              let v = Decimal(string: value, locale: Locale(identifier: "en_US_POSIX")) else { return false }
        return v >= min && v <= max
    }
    struct Frame { var type: String; var seq: UInt32; var time: UInt32; var data: [String]; var body: String }
    static func parse(_ wire: String) -> Frame? {
        guard wire.hasPrefix("@"), wire.hasSuffix("\n") else { return nil }
        var line = String(wire.dropLast())
        if line.hasSuffix("\r") { line.removeLast() }
        guard line.utf8.count + 1 <= 256, line.utf8.allSatisfy({ $0 >= 33 && $0 <= 126 }),
              line.filter({ $0 == "*" }).count == 1, !line.dropFirst().contains("@") else { return nil }
        let sides = line.dropFirst().split(separator: "*", omittingEmptySubsequences: false).map(String.init)
        guard sides.count == 2, matches(sides[1], "\\A[0-9A-F]{4}\\z"), UInt16(sides[1], radix: 16) == crc(sides[0]) else { return nil }
        let fields = sides[0].components(separatedBy: ",")
        guard fields.count >= 3, matches(fields[0], "\\A[A-Z][A-Z0-9_.-]*\\z"),
              let seq = uint(fields[1]), let time = uint(fields[2]) else { return nil }
        let data = Array(fields.dropFirst(3))
        if fields[0] == "CMD" {
            guard let command = data.first, matches(command, "\\A[A-Z0-9_.-]+\\z") else { return nil }
        } else if fields.contains("") { return nil }
        return Frame(type: fields[0], seq: seq, time: time, data: data, body: sides[0])
    }
}

struct RgpWireDecoder {
    private var bytes = [UInt8]()
    private var lastByte: Double = 0
    mutating func push(_ chunk: [UInt8], now: Double) -> [Rgp.Frame] {
        var frames = [Rgp.Frame]()
        for byte in chunk {
            if now - lastByte >= 1 { bytes.removeAll(keepingCapacity: true) }
            if byte == 64 { bytes.removeAll(keepingCapacity: true) }
            else if bytes.isEmpty { continue }
            lastByte = now; bytes.append(byte)
            if byte == 10 {
                if let frame = Rgp.parse(String(decoding: bytes, as: UTF8.self)) { frames.append(frame) }
                bytes.removeAll(keepingCapacity: true)
            } else if bytes.count > 256 || bytes.count == 256 && byte != 13 { bytes.removeAll(keepingCapacity: true) }
        }
        return frames
    }
}

/// A deterministic simulated ESP32: shared TX sequence, session setup and idempotent CMD execution.
final class RgpDevice {
    private struct Cached { var body: String; var response: String; var at: UInt64; var order: UInt64 }
    private var cache = [UInt32: Cached]()
    private var order: UInt64 = 0
    private(set) var sequence: UInt32 = 0
    private(set) var sessionID: String?
    private(set) var state = "READY"
    private(set) var telRate = 100
    private(set) var telStream = true
    private var beta = "0.200"
    private var adsOK = true
    private var imuOK = true

    func emit(_ type: String, _ data: [String], time: UInt64) -> String {
        let body = ([type, String(sequence), String(UInt32(truncatingIfNeeded: time))] + data).joined(separator: ",")
        sequence &+= 1
        return Rgp.wire(body)
    }
    func boot() -> [String] {
        cache.removeAll(); order = 0; sequence = 0; sessionID = nil; state = "READY"
        telRate = 100; telStream = true; beta = "0.200"; adsOK = true; imuOK = true
        return [emit("SYS", ["HELLO", "RGP", "1.0", "SERIAL-LAB-2.0"], time: 0),
            emit("EVT", ["BOOT"], time: 0), emit("EVT", ["STATE", "BOOTING"], time: 0),
            emit("EVT", ["STATE", "INITIALIZING"], time: 0), emit("EVT", ["STATE", "CALIBRATING"], time: 0),
            emit("EVT", ["CAL_START", "AUTO", "ALL"], time: 0), emit("EVT", ["CAL_MAG_SKIPPED", "AUTO"], time: 0),
            emit("EVT", ["CAL_GYRO_START", "AUTO"], time: 0), emit("EVT", ["CAL_GYRO_DONE", "AUTO"], time: 0),
            emit("EVT", ["CAL_DONE", "AUTO", "ALL"], time: 0), emit("EVT", ["ANGLE_LOCKED"], time: 0),
            emit("EVT", ["STATE", "READY"], time: 0)]
    }
    func setRate(_ rate: Int, time: UInt64) -> [String] {
        guard telRate != rate else { return [] }
        telRate = rate
        return [emit("EVT", ["CONFIG_CHANGED", "TEL_RATE", String(rate)], time: time)]
    }
    func availability(_ frame: InputFrame, time: UInt64) -> [String] {
        adsOK = !frame.p1.accelUnavailable && !frame.p1.brakeUnavailable && !frame.p2.accelUnavailable && !frame.p2.brakeUnavailable
        imuOK = !frame.p1.steeringUnavailable && !frame.p2.steeringUnavailable
        let next = !adsOK ? "RECOVERING" : !imuOK ? "WAIT_ANGLE" : "READY"
        guard state != next else { return [] }
        let previous = state; state = next
        var frames = [String]()
        if previous == "RECOVERING" { frames.append(emit("EVT", ["I2C_RESTORED"], time: time)) }
        if next == "RECOVERING" {
            frames += [emit("ERR", ["ERROR", "I2C", "CONNECTION_LOST"], time: time), emit("EVT", ["I2C_LOST"], time: time)]
        } else if next == "WAIT_ANGLE" {
            frames += [emit("ERR", ["WARN", "HANDLE", "ANGLE_REFERENCE_INVALID"], time: time), emit("EVT", ["ANGLE_UNLOCKED"], time: time)]
        } else if previous != "RECOVERING" {
            frames.append(emit("EVT", ["ANGLE_LOCKED"], time: time))
        }
        frames.append(emit("EVT", ["STATE", next], time: time)); return frames
    }
    func telemetry(_ frame: InputFrame, time: UInt64) -> String? {
        guard frame.available, state == "READY", telStream else { return nil }
        return emit("TEL", frame.fields, time: time)
    }

    func handle(_ frame: Rgp.Frame, time: UInt64) -> [String] {
        guard frame.type == "CMD", let command = frame.data.first else { return [] }
        let args = Array(frame.data.dropFirst())
        func ack(_ response: String) -> String { emit("ACK", [String(frame.seq), command] + response.components(separatedBy: ","), time: time) }
        cache = cache.filter { !($0.value.order + 16 <= order && time >= $0.value.at + 10000) }
        if command == "OPEN_SESSION", args.count == 1, Rgp.matches(args[0], "\\A[0-9A-F]{16}\\z"), sessionID != args[0] { cache.removeAll() }
        if let old = cache[frame.seq] {
            if old.body == frame.body { return [ack(old.response)] }
            return [ack("FAILED,SEQ_REUSE"), emit("ERR", ["ERROR", "PROTOCOL", "SEQ_REUSE", String(frame.seq)], time: time)]
        }
        var response = "UNSUPPORTED"
        var follow = [(String, [String])]()
        let counts = ["OPEN_SESSION":1, "PING":0, "GET_STATUS":0, "GET_CONFIG":0, "ANGLE_ZERO":0,
                      "SET_BETA":1, "SET_RATE":2, "SET_STREAM":2, "CALIBRATE":1, "SCAN_I2C":0]
        var invalid: Int?
        if let expected = counts[command], args.count != expected { invalid = min(args.count, expected) + 1 }
        if invalid == nil, let empty = args.firstIndex(of: "") { invalid = empty + 1 }
        if invalid == nil {
            switch command {
            case "OPEN_SESSION": if !Rgp.matches(args[0], "\\A[0-9A-F]{16}\\z") { invalid = 1 }
            case "SET_BETA": if !Rgp.number(args[0], min: 0.010, max: 1) { invalid = 1 }
            case "SET_RATE":
                if !["TEL", "IMU"].contains(args[0]) { invalid = 1 }
                else if let hz = Rgp.uint(args[1]), hz >= 1, hz <= (args[0] == "TEL" ? 100 : 50) {} else { invalid = 2 }
            case "SET_STREAM":
                if !["TEL", "IMU", "DBG"].contains(args[0]) { invalid = 1 }
                else if !["0", "1"].contains(args[1]) { invalid = 2 }
            case "CALIBRATE": if !["GYRO", "MAG", "ALL"].contains(args[0]) { invalid = 1 }
            default: break
            }
        }
        if let invalid = invalid { response = "INVALID_ARG,\(invalid)" }
        else if command == "OPEN_SESSION" {
            if sessionID != args[0] { cache.removeAll(); sessionID = args[0] }
            response = "OK,\(args[0])"
        } else if sessionID == nil { response = "NOT_READY,\(state)" }
        else {
            switch command {
            case "PING": response = "OK,PONG"
            case "GET_STATUS": response = "OK,\(state),\(adsOK ? 1 : 0),\(imuOK ? 1 : 0),\(imuOK ? 1 : 0),1"
            case "GET_CONFIG": response = "OK,TEL_RATE,\(telRate),IMU_RATE,20,TEL_STREAM,\(telStream ? 1 : 0),IMU_STREAM,0,DBG_STREAM,0,BETA,\(beta)"
            case "ANGLE_ZERO", "SET_BETA":
                if !["READY", "WAIT_ANGLE"].contains(state) { response = "NOT_READY,\(state)" }
                else if command == "ANGLE_ZERO" {
                    if !imuOK { response = "FAILED,ANGLE_REFERENCE_INVALID"; follow = [("ERR", ["WARN", "HANDLE", "ANGLE_REFERENCE_INVALID"])] }
                    else { response = "OK"; follow = [("EVT", ["ANGLE_RESET"]), ("EVT", ["ANGLE_LOCKED"])] }
                } else {
                    let value = String(format: "%.3f", locale: Locale(identifier: "en_US_POSIX"), Double(args[0])!)
                    if beta != value { beta = value; follow = [("EVT", ["CONFIG_CHANGED", "BETA", value])] }
                    response = "OK,\(value)"
                }
            case "SET_RATE":
                if args[0] == "TEL" {
                    let rate = Int(args[1])!
                    if telRate != rate { telRate = rate; follow = [("EVT", ["CONFIG_CHANGED", "TEL_RATE", String(rate)])] }
                    response = "OK,TEL,\(rate)"
                }
            case "SET_STREAM":
                if args[0] == "TEL" {
                    let enabled = args[1] == "1"
                    if telStream != enabled { telStream = enabled; follow = [("EVT", ["CONFIG_CHANGED", "TEL_STREAM", args[1]])] }
                    response = "OK,TEL,\(args[1])"
                }
            default: break // Optional calibration/scan/IMU/DBG commands are explicitly unsupported.
            }
        }
        order += 1
        cache[frame.seq] = Cached(body: frame.body, response: response, at: time, order: order)
        return [ack(response)] + follow.map { emit($0.0, $0.1, time: time) }
    }
}
