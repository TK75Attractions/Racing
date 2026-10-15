import Foundation
import Darwin

final class PseudoTerminal {
    private(set) var master: Int32 = -1
    private(set) var slave: Int32 = -1
    private(set) var path = ""
    var isOpen: Bool { master >= 0 }

    init() throws {
        var name = [CChar](repeating: 0, count: 1024)
        guard openpty(&master, &slave, &name, nil, nil) == 0 else { throw nativeError("openpty") }
        do {
            var settings = termios()
            guard tcgetattr(slave, &settings) == 0 else { throw nativeError("tcgetattr") }
            cfmakeraw(&settings)
            cfsetspeed(&settings, speed_t(B115200))
            guard tcsetattr(slave, TCSANOW, &settings) == 0 else { throw nativeError("tcsetattr") }
            guard fcntl(master, F_SETFL, fcntl(master, F_GETFL) | O_NONBLOCK) >= 0 else { throw nativeError("fcntl") }
            path = String(cString: name)
        } catch {
            close(); throw error
        }
    }

    /// Nonblocking. Returns zero on backpressure; caller owns the bounded retry buffer.
    func write(_ bytes: Data, offset: Int, limit: Int) throws -> Int {
        guard isOpen else { throw SimulatorError.message("仮想ポートがありません。") }
        return try bytes.withUnsafeBytes { raw in
            let count = Darwin.write(master, raw.baseAddress!.advanced(by: offset), limit)
            if count < 0 {
                if errno == EAGAIN || errno == EINTR { return 0 }
                throw nativeError("write")
            }
            return count
        }
    }

    func discardUnreadInput() { if slave >= 0 { _ = tcflush(slave, TCIFLUSH) } }
    func close() {
        if master >= 0 { Darwin.close(master); master = -1 }
        if slave >= 0 { Darwin.close(slave); slave = -1 }
    }
    deinit { close() }

    private func nativeError(_ operation: String) -> SimulatorError {
        .message("PTY \(operation): \(String(cString: strerror(errno)))")
    }
}
