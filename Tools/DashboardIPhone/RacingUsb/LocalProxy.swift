import Foundation
import Network
import Security

// Local WKWebView HTTP/WS traffic is multiplexed over the USB connection.
// Only loopback, a private cookie, and the WebView's exact origin are accepted.
final class LocalProxy {
    let queue: DispatchQueue
    let transport: UsbTransport
    var onReady: ((URL) -> Void)?
    private var listener: NWListener?
    private var port: UInt16 = 0
    private var token = "", active = false, nextChannel: UInt32 = 0
    private var clients: [UInt32: Client] = [:]
    init(queue: DispatchQueue, transport: UsbTransport) { self.queue = queue; self.transport = transport }
    func start() throws {
        let listener = try NWListener(using: UsbProtocol.tcp(), on: .any); self.listener = listener
        listener.stateUpdateHandler = { [weak self, weak listener] state in
            if case .ready = state, let value = listener?.port { self?.port = value.rawValue; self?.publish() }
        }
        listener.newConnectionHandler = { [weak self] connection in
            guard let self = self, self.active, self.clients.count < 24 else { connection.cancel(); return }
            repeat { self.nextChannel &+= 1 } while self.nextChannel == 0 || self.clients[self.nextChannel] != nil
            let client = Client(self, self.nextChannel, connection); self.clients[self.nextChannel] = client
            client.start()
        }
        listener.start(queue: queue)
    }
    func setConnected(_ connected: Bool) {
        active = connected
        for client in Array(clients.values) { client.close() }; clients.removeAll()
        if connected {
            var bytes = [UInt8](repeating: 0, count: 32)
            precondition(SecRandomCopyBytes(kSecRandomDefault, bytes.count, &bytes) == errSecSuccess)
            token = bytes.map { String(format: "%02x", $0) }.joined(); publish()
        } else { token = "" }
    }
    private func publish() {
        if active && port > 0 { onReady?(URL(string: "http://127.0.0.1:\(port)/pair?token=\(token)")!) }
    }
    func receive(_ frame: UsbFrame) {
        guard let client = clients[frame.channel] else { return }
        if frame.kind == .data { client.deliver(frame.data) }
        else if frame.kind == .close { client.finish() }
    }
    func stop() { setConnected(false); listener?.cancel(); listener = nil }

    private final class Client {
        unowned let owner: LocalProxy
        let id: UInt32, connection: NWConnection
        private var header = Data(), opened = false, closed = false, streaming = false, remaining = 0, pending = 0
        init(_ owner: LocalProxy, _ id: UInt32, _ connection: NWConnection) { self.owner = owner; self.id = id; self.connection = connection }
        func start() {
            connection.stateUpdateHandler = { [weak self] state in
                guard let self = self else { return }
                if case .ready = state { self.read() }
                if case .failed = state { self.close() }
                if case .cancelled = state { self.close() }
            }
            connection.start(queue: owner.queue)
            owner.queue.asyncAfter(deadline: .now() + 10) { [weak self] in if let self = self, !self.opened && !self.closed { self.close() } }
        }
        private func read() {
            connection.receive(minimumIncompleteLength: 1, maximumLength: 16384) { [weak self] data, _, ended, error in
                guard let self = self, !self.closed else { return }
                if let data = data, !data.isEmpty {
                    if self.opened { self.upload(data) } else { self.header.append(data); self.begin() }
                }
                if ended || error != nil { self.close() }
                else if !self.closed { self.read() }
            }
        }
        private func begin() {
            guard let split = header.range(of: Data("\r\n\r\n".utf8)) else { if header.count > 16384 { respond(431, "Request too large") }; return }
            guard split.lowerBound <= 16384, let text = String(data: header[..<split.lowerBound], encoding: .utf8) else { respond(400, "Invalid request"); return }
            let lines = text.components(separatedBy: "\r\n"), first = lines[0].split(separator: " ")
            guard first.count == 3, ["GET", "HEAD", "POST"].contains(String(first[0])), first[2] == "HTTP/1.1", first[1].hasPrefix("/"), !first[1].hasPrefix("//") else { respond(400, "Invalid request"); return }
            let method = String(first[0]), path = String(first[1]), authority = "127.0.0.1:\(owner.port)"
            var fields: [String: String] = [:]
            for line in lines.dropFirst() {
                let pair = line.split(separator: ":", maxSplits: 1, omittingEmptySubsequences: false)
                guard pair.count == 2, line.first != " ", line.first != "\t" else { respond(400, "Invalid headers"); return }
                let name = pair[0].lowercased(), value = pair[1].trimmingCharacters(in: .whitespaces)
                guard fields[name] == nil, !value.contains("\n"), !value.contains("\r") else { respond(400, "Duplicate headers"); return }
                fields[name] = value
            }
            streaming = fields["upgrade"]?.lowercased() == "websocket"
            guard fields["host"] == authority, fields["transfer-encoding"] == nil,
                fields["x-forwarded-for"] == nil, fields["tailscale-user-login"] == nil,
                (!(method == "POST" || streaming) && fields["origin"] == nil || fields["origin"] == "http://" + authority) else { respond(403, "Forbidden"); return }
            if method == "GET", let url = URLComponents(string: "http://" + authority + path), url.path == "/pair",
               url.queryItems?.first(where: { $0.name == "token" })?.value == owner.token {
                respond(302, "", headers: "Location: /\r\nSet-Cookie: racing_iphone_usb=\(owner.token); Path=/; HttpOnly; SameSite=Strict\r\n"); return
            }
            let cookies = (fields["cookie"] ?? "").split(separator: ";").map { $0.trimmingCharacters(in: .whitespaces) }
            guard !owner.token.isEmpty, cookies.contains("racing_iphone_usb=" + owner.token) else { respond(403, "Pairing required"); return }
            if let length = fields["content-length"] {
                guard let size = Int(length), size >= 0, size <= 4096 else { respond(413, "Request too large"); return }; remaining = size
            }
            guard !streaming || method == "GET" && path.hasPrefix("/api/events") else { respond(403, "Forbidden"); return }
            var forwarded = "\(method) \(path) HTTP/1.1\r\nHost: 127.0.0.1:8766\r\nOrigin: http://127.0.0.1:8766\r\n"
            for (name, value) in fields where !["host", "origin", "cookie", "connection", "proxy-connection"].contains(name) { forwarded += "\(name): \(value)\r\n" }
            forwarded += streaming ? "Connection: Upgrade\r\n\r\n" : "Connection: close\r\n\r\n"
            let body = Data(header[split.upperBound...]); header = Data()
            guard streaming || body.count <= remaining else { respond(400, "Invalid body length"); return }
            opened = true
            guard owner.transport.send(.open, id), owner.transport.send(.data, id, Data(forwarded.utf8)) else { close(); return }
            if !body.isEmpty { upload(body) }
        }
        private func upload(_ data: Data) {
            guard streaming || data.count <= remaining else { close(); return }
            if !streaming { remaining -= data.count }
            if !owner.transport.send(.data, id, data) { close() }
        }
        func deliver(_ data: Data) {
            guard !closed else { return }
            pending += data.count
            guard pending <= 2 * 1024 * 1024 else { close(); return }
            connection.send(content: data, completion: .contentProcessed { [weak self] error in
                guard let self = self else { return }; self.pending -= data.count; if error != nil { self.close() }
            })
        }
        func finish() {
            guard !closed else { return }; closed = true; owner.clients.removeValue(forKey: id)
            connection.send(content: nil, contentContext: .finalMessage, isComplete: true, completion: .contentProcessed { [connection] _ in connection.cancel() })
        }
        private func respond(_ code: Int, _ text: String, headers: String = "") {
            let bytes = Data(text.utf8)
            let head = "HTTP/1.1 \(code) Response\r\nContent-Length: \(bytes.count)\r\nContent-Type: text/plain; charset=utf-8\r\nCache-Control: no-store\r\nReferrer-Policy: no-referrer\r\nConnection: close\r\n\(headers)\r\n"
            deliver(Data(head.utf8) + bytes); finish()
        }
        func close() {
            guard !closed else { return }; closed = true; owner.clients.removeValue(forKey: id)
            if opened { owner.transport.send(.close, id) }; connection.cancel()
        }
    }
}
