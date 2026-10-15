import Foundation
import Darwin

@main
enum SimulatorCLI {
    static func main() {
        var engine: SimulatorEngine?
        do {
            let args = Array(CommandLine.arguments.dropFirst())
            if args.contains("--help") {
                print("esp32-simulator-cli [--scenario file.json] [--duration seconds] [--rate 30|60|120] [--divisor 6.5] [--port-file path] [--start-file path] [--frame pedal,angle,pedal,angle]")
                print("Angles are Unity degrees; the sender multiplies them by --divisor. nan is supported. Ctrl-C closes the PTY.")
                return
            }
            let allowed = ["--scenario", "--duration", "--rate", "--divisor", "--port-file", "--start-file", "--frame"]
            guard args.count % 2 == 0 else { throw SimulatorError.message("Each option requires a value; see --help.") }
            var options: [String: String] = [:]
            for index in stride(from: 0, to: args.count, by: 2) {
                guard allowed.contains(args[index]), options[args[index]] == nil else { throw SimulatorError.message("Unknown or duplicate option: \(args[index])") }
                options[args[index]] = args[index + 1]
            }
            guard let divisor = Double(options["--divisor"] ?? "6.5"), divisor.isFinite, (0.0001...1000).contains(divisor),
                  let hz = Double(options["--rate"] ?? "60"), [30.0, 60.0, 120.0].contains(hz),
                  let duration = Double(options["--duration"] ?? "30"), duration.isFinite, (0.1...3600).contains(duration) else {
                throw SimulatorError.message("Invalid divisor, rate or duration.")
            }
            var frame = InputFrame()
            if let line = options["--frame"] {
                let values = line.split(separator: ",", omittingEmptySubsequences: false).map(String.init)
                guard values.count == 4 else { throw SimulatorError.message("--frame requires four columns.") }
                func axis(_ index: Int) throws -> Double {
                    if values[index].lowercased() == "nan" { return 0 }
                    guard let value = Double(values[index]) else { throw SimulatorError.message("Invalid --frame number.") }
                    return value
                }
                frame = InputFrame(p1: PlayerInput(pedal: try axis(0), steering: try axis(1),
                                                   pedalUnavailable: values[0].lowercased() == "nan", steeringUnavailable: values[1].lowercased() == "nan"),
                                   p2: PlayerInput(pedal: try axis(2), steering: try axis(3),
                                                   pedalUnavailable: values[2].lowercased() == "nan", steeringUnavailable: values[3].lowercased() == "nan"))
                try frame.p1.validate(); try frame.p2.validate()
            }
            let scenario = try options["--scenario"].map { try Scenario.load(URL(fileURLWithPath: $0)) }
            let sender = SimulatorEngine()
            engine = sender
            sender.setControls(frame, divisor: divisor, hz: hz)
            sender.createPort()
            let initial = sender.snapshot()
            guard !initial.port.isEmpty else { throw SimulatorError.message(initial.error) }
            if let path = options["--port-file"] { try (initial.port + "\n").write(toFile: path, atomically: true, encoding: .utf8) }
            print("PORT=\(initial.port)")
            fflush(stdout)

            // Dispatch sources handle signals without doing unsafe cleanup in a signal handler.
            signal(SIGINT, SIG_IGN); signal(SIGTERM, SIG_IGN)
            let finished = DispatchSemaphore(value: 0)
            let signals = [SIGINT, SIGTERM].map { number -> DispatchSourceSignal in
                let source = DispatchSource.makeSignalSource(signal: number, queue: .global())
                source.setEventHandler { finished.signal() }; source.resume(); return source
            }
            var shouldStart = true
            if let startFile = options["--start-file"] {
                print("WAITING_FOR=\(startFile)"); fflush(stdout)
                let deadline = ProcessInfo.processInfo.systemUptime + duration
                while !FileManager.default.fileExists(atPath: startFile) && ProcessInfo.processInfo.systemUptime < deadline {
                    if finished.wait(timeout: .now() + 0.05) == .success { shouldStart = false; break }
                }
                if shouldStart && !FileManager.default.fileExists(atPath: startFile) {
                    throw SimulatorError.message("Timed out waiting for --start-file.")
                }
            }
            if shouldStart {
                if let scenario = scenario { sender.run(scenario) } else { sender.start() }
                _ = finished.wait(timeout: .now() + duration)
            }
            signals.forEach { $0.cancel() }
            let final = sender.snapshot()
            sender.shutdown()
            print("SENT=\(final.sent) SKIPPED=\(final.skipped) HZ=\(String(format: "%.1f", final.measuredHz))")
            if !final.error.isEmpty { throw SimulatorError.message(final.error) }
        } catch {
            engine?.shutdown()
            fputs("ERROR: \(error.localizedDescription)\n", stderr)
            exit(1)
        }
    }
}
