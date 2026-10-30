import Foundation
import Network

// Shared, versioned framing with the Mac helper. No network discovery or Wi-Fi fallback.
enum UsbKind: UInt8 { case hello = 1, open = 2, data = 3, close = 4, ping = 5 }
struct UsbFrame { let kind: UsbKind; let channel: UInt32; let data: Data }
enum UsbProtocol {
    static let name = "racing-usb-v2"
    static func encode(_ kind: UsbKind, _ channel: UInt32, _ payload: Data = Data()) -> Data {
        var size = UInt32(payload.count).bigEndian, id = channel.bigEndian
        var bytes = withUnsafeBytes(of: &size) { Data($0) }; bytes.append(kind.rawValue)
        bytes.append(withUnsafeBytes(of: &id) { Data($0) }); bytes.append(payload); return bytes
    }
    static func next(_ buffer: inout Data) throws -> UsbFrame? {
        if buffer.count < 9 { return nil }
        let head = [UInt8](buffer.prefix(9))
        let size = head.prefix(4).reduce(UInt32(0)) { ($0 << 8) | UInt32($1) }
        guard size <= 65536, let kind = UsbKind(rawValue: head[4]) else { throw UsbFailure.protocolError }
        if buffer.count < 9 + Int(size) { return nil }
        let channel = head[5...8].reduce(UInt32(0)) { ($0 << 8) | UInt32($1) }
        let frame = UsbFrame(kind: kind, channel: channel, data: buffer.subdata(in: 9..<(9 + Int(size))))
        buffer = Data(buffer.dropFirst(9 + Int(size))); return frame
    }
    static func tcp() -> NWParameters {
        let tcp = NWProtocolTCP.Options(); tcp.noDelay = true
        let parameters = NWParameters(tls: nil, tcp: tcp)
        parameters.requiredInterfaceType = .loopback
        parameters.allowLocalEndpointReuse = true
        return parameters
    }
}
enum UsbFailure: Error { case protocolError }

final class UsbTransport {
    let queue: DispatchQueue
    var onFrame: ((UsbFrame) -> Void)?
    var onState: ((Bool, String) -> Void)?
    var onPort: ((UInt16) -> Void)?
    private var listener: NWListener?
    private var connection: NWConnection?
    private var buffer = Data()
    private var ready = false, pendingBytes = 0
    private var lastReceived = Date(), heartbeat: DispatchSourceTimer?
    init(queue: DispatchQueue) { self.queue = queue }
    func start(port: UInt16 = 30220) throws {
        let listener = try NWListener(using: UsbProtocol.tcp(), on: NWEndpoint.Port(rawValue: port)!)
        self.listener = listener
        listener.newConnectionHandler = { [weak self] incoming in self?.accept(incoming) }
        listener.stateUpdateHandler = { [weak self, weak listener] state in
            if case .ready = state, let port = listener?.port { self?.onPort?(port.rawValue) }
            if case .failed = state { self?.stop(); self?.onState?(false, "USB用の待受を開始できません。アプリを開き直してください。") }
        }
        listener.start(queue: queue)
    }
    private func accept(_ incoming: NWConnection) {
        guard connection == nil else { incoming.cancel(); return }
        connection = incoming; buffer = Data(); pendingBytes = 0; ready = false; lastReceived = Date()
        incoming.stateUpdateHandler = { [weak self, weak incoming] state in
            guard let self = self, let incoming = incoming, self.connection === incoming else { return }
            switch state {
            case .ready:
                let hello: [String: Any] = ["protocol": UsbProtocol.name, "authentication": "trusted-usb"]
                self.send(.hello, 0, try! JSONSerialization.data(withJSONObject: hello)); self.receive(incoming)
                let timer = DispatchSource.makeTimerSource(queue: self.queue)
                timer.schedule(deadline: .now() + 1, repeating: 1)
                timer.setEventHandler { [weak self] in
                    guard let self = self else { return }
                    if Date().timeIntervalSince(self.lastReceived) > 8 { self.disconnect("USB通信が途切れました。ケーブルとMacを確認してください。") }
                }
                self.heartbeat = timer; timer.resume()
            case .failed, .cancelled: self.disconnect("USB接続を待っています。Macの補助アプリを確認してください。")
            default: break
            }
        }
        incoming.start(queue: queue)
    }
    private func receive(_ current: NWConnection) {
        current.receive(minimumIncompleteLength: 1, maximumLength: 65536) { [weak self, weak current] data, _, ended, error in
            guard let self = self, let current = current, self.connection === current else { return }
            do {
                if let data = data {
                    self.buffer.append(data)
                    while let frame = try UsbProtocol.next(&self.buffer) {
                        self.lastReceived = Date()
                        if !self.ready {
                            guard frame.kind == .hello, frame.channel == 0,
                                let hello = try JSONSerialization.jsonObject(with: frame.data) as? [String: Any],
                                hello["protocol"] as? String == UsbProtocol.name else { throw UsbFailure.protocolError }
                            guard hello["accepted"] as? Bool == true else { self.disconnect("MacのUSB補助アプリと端末のアプリを最新版へ更新してください。"); return }
                            self.ready = true; self.onState?(true, "USB直通で接続中")
                        } else if frame.kind == .ping && frame.channel == 0 && frame.data.isEmpty {
                            self.send(.ping, 0)
                        } else {
                            guard frame.channel > 0, frame.kind == .data || frame.kind == .close else { throw UsbFailure.protocolError }
                            self.onFrame?(frame)
                        }
                    }
                }
                if ended || error != nil { self.disconnect("USBが切断されました。ケーブルをつなぎ直してください。") }
                else { self.receive(current) }
            } catch { self.disconnect("USB通信の形式を確認できません。Macとアプリを更新してください。") }
        }
    }
    @discardableResult func send(_ kind: UsbKind, _ channel: UInt32, _ data: Data = Data()) -> Bool {
        guard let connection = connection, data.count <= 65536 else { return false }
        let encoded = UsbProtocol.encode(kind, channel, data)
        guard pendingBytes + encoded.count <= 4 * 1024 * 1024 else { disconnect("USB通信が詰まりました。接続し直しています。"); return false }
        pendingBytes += encoded.count
        connection.send(content: encoded, completion: .contentProcessed { [weak self, weak connection] error in
            guard let self = self, let connection = connection, self.connection === connection else { return }
            self.pendingBytes -= encoded.count
            if error != nil { self.disconnect("USB通信が切断されました。") }
        })
        return true
    }
    private func disconnect(_ message: String) {
        guard connection != nil else { return }
        let old = connection; connection = nil; ready = false; heartbeat?.cancel(); heartbeat = nil; buffer = Data(); pendingBytes = 0
        old?.cancel(); onState?(false, message)
    }
    func stop() { disconnect("USB接続を停止しました。"); listener?.cancel(); listener = nil }
}
