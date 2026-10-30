import Foundation
@main enum Harness {
    static func main() throws {
        var buffer = Data(); let frame = UsbProtocol.encode(.data, 42, Data([0, 255, 13, 10]))
        for byte in frame.dropLast() { buffer.append(byte); let partial = try UsbProtocol.next(&buffer); precondition(partial == nil) }
        buffer.append(frame.last!); let decoded = try UsbProtocol.next(&buffer)!
        precondition(decoded.channel == 42 && decoded.data == Data([0, 255, 13, 10]) && buffer.isEmpty)
        let queue = DispatchQueue(label: "usb-native-test")
        let actual = UsbTransport(queue: queue), proxy = LocalProxy(queue: queue, transport: actual)
        actual.onPort = { port in print("USB_TEST_PORT \(port)"); fflush(stdout) }
        actual.onFrame = { frame in proxy.receive(frame) }
        actual.onState = { active, _ in proxy.setConnected(active) }
        proxy.onReady = { url in print("USB_TEST_URL \(url.absoluteString)"); fflush(stdout) }
        try proxy.start(); try actual.start(port: 0)
        RunLoop.main.run()
    }
}
