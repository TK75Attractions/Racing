import UIKit
import WebKit

@main final class AppDelegate: UIResponder, UIApplicationDelegate {
    var window: UIWindow?
    func application(_ application: UIApplication, didFinishLaunchingWithOptions options: [UIApplication.LaunchOptionsKey: Any]?) -> Bool {
        let window = UIWindow(frame: UIScreen.main.bounds); window.rootViewController = ConsoleController(); window.makeKeyAndVisible(); self.window = window; return true
    }
}

final class ConsoleController: UIViewController, WKNavigationDelegate {
    private let queue = DispatchQueue(label: "jp.racing.usb")
    private var transport: UsbTransport?, proxy: LocalProxy?
    private let setup = UIScrollView()
    private var allowedPort: Int?
    private let code = UITextField(), message = UILabel(), connect = UIButton(type: .system), overlay = UIStackView()
    private var web: WKWebView!
    override func viewDidLoad() {
        super.viewDidLoad(); view.backgroundColor = UIColor(red: 0.06, green: 0.08, blue: 0.12, alpha: 1)
        let config = WKWebViewConfiguration(); config.websiteDataStore = .nonPersistent()
        config.userContentController.addUserScript(WKUserScript(source: "window.racingUsbNative = true;", injectionTime: .atDocumentStart, forMainFrameOnly: true))
        web = WKWebView(frame: .zero, configuration: config); web.navigationDelegate = self; web.isOpaque = false; web.backgroundColor = view.backgroundColor
        web.translatesAutoresizingMaskIntoConstraints = false; view.addSubview(web)
        NSLayoutConstraint.activate([web.leadingAnchor.constraint(equalTo: view.leadingAnchor), web.trailingAnchor.constraint(equalTo: view.trailingAnchor), web.topAnchor.constraint(equalTo: view.safeAreaLayoutGuide.topAnchor), web.bottomAnchor.constraint(equalTo: view.safeAreaLayoutGuide.bottomAnchor)])
        let title = UILabel(); title.text = "RACING CONTROL"; title.font = .boldSystemFont(ofSize: 28); title.textColor = .white; title.adjustsFontSizeToFitWidth = true; title.minimumScaleFactor = 0.7
        let subtitle = UILabel(); subtitle.text = "USBだけで接続"; subtitle.textColor = .systemTeal; subtitle.font = .boldSystemFont(ofSize: 18)
        message.text = "iPhoneをMacにUSBでつなぎ、Macの準備画面にある8けたの接続コードを入力してください。"; message.numberOfLines = 0; message.textColor = .lightGray
        code.placeholder = "8けたの接続コード"; code.keyboardType = .numberPad; code.borderStyle = .roundedRect; code.backgroundColor = .white; code.textColor = .black; code.textContentType = .oneTimeCode
        let keyboard = UIToolbar(); keyboard.sizeToFit(); keyboard.items = [UIBarButtonItem(barButtonSystemItem: .flexibleSpace, target: nil, action: nil), UIBarButtonItem(title: "接続", style: .done, target: self, action: #selector(start))]; code.inputAccessoryView = keyboard
        connect.setTitle("USBで接続", for: .normal); connect.titleLabel?.font = .boldSystemFont(ofSize: 18); connect.addTarget(self, action: #selector(start), for: .touchUpInside)
        overlay.axis = .vertical; overlay.spacing = 20; overlay.addArrangedSubview(title); overlay.addArrangedSubview(subtitle); overlay.addArrangedSubview(message); overlay.addArrangedSubview(code); overlay.addArrangedSubview(connect)
        overlay.backgroundColor = view.backgroundColor; overlay.isLayoutMarginsRelativeArrangement = true; overlay.layoutMargins = UIEdgeInsets(top: 28, left: 24, bottom: 28, right: 24)
        setup.backgroundColor = view.backgroundColor; setup.keyboardDismissMode = .interactive
        setup.translatesAutoresizingMaskIntoConstraints = false; view.addSubview(setup)
        overlay.translatesAutoresizingMaskIntoConstraints = false; setup.addSubview(overlay)
        NSLayoutConstraint.activate([
            setup.leadingAnchor.constraint(equalTo: view.leadingAnchor), setup.trailingAnchor.constraint(equalTo: view.trailingAnchor),
            setup.topAnchor.constraint(equalTo: view.safeAreaLayoutGuide.topAnchor), setup.bottomAnchor.constraint(equalTo: view.keyboardLayoutGuide.topAnchor),
            setup.contentLayoutGuide.widthAnchor.constraint(equalTo: setup.frameLayoutGuide.widthAnchor),
            overlay.centerXAnchor.constraint(equalTo: setup.frameLayoutGuide.centerXAnchor), overlay.widthAnchor.constraint(lessThanOrEqualToConstant: 500),
            overlay.leadingAnchor.constraint(greaterThanOrEqualTo: setup.contentLayoutGuide.leadingAnchor, constant: 16), overlay.trailingAnchor.constraint(lessThanOrEqualTo: setup.contentLayoutGuide.trailingAnchor, constant: -16),
            overlay.topAnchor.constraint(equalTo: setup.contentLayoutGuide.topAnchor, constant: 20), overlay.bottomAnchor.constraint(equalTo: setup.contentLayoutGuide.bottomAnchor, constant: -20)
        ])
        let width = overlay.widthAnchor.constraint(equalTo: setup.frameLayoutGuide.widthAnchor, constant: -32); width.priority = .defaultHigh; width.isActive = true
        NotificationCenter.default.addObserver(self, selector: #selector(background), name: UIApplication.didEnterBackgroundNotification, object: nil)
    }
    @objc private func start() {
        guard let value = code.text, value.utf8.count == 8, value.utf8.allSatisfy({ $0 >= 48 && $0 <= 57 }) else { message.text = "Macに表示された8けたの数字を入力してください。"; return }
        view.endEditing(true); message.text = "USB接続を待っています。MacでStart USB Dashboardを開いてください。"
        queue.async { [weak self] in
            guard let self = self else { return }
            self.transport?.stop(); self.proxy?.stop()
            let transport = UsbTransport(queue: self.queue), proxy = LocalProxy(queue: self.queue, transport: transport)
            self.transport = transport; self.proxy = proxy
            transport.onFrame = { [weak proxy] frame in proxy?.receive(frame) }
            transport.onState = { [weak self, weak proxy] active, text in
                proxy?.setConnected(active)
                DispatchQueue.main.async { guard let self = self else { return }; self.message.text = text; self.setup.isHidden = active; UIApplication.shared.isIdleTimerDisabled = active
                    if !active { self.web.stopLoading(); self.setup.isHidden = false }
                }
            }
            proxy.onReady = { [weak self] url in DispatchQueue.main.async { self?.allowedPort = url.port; self?.web.load(URLRequest(url: url)); self?.setup.isHidden = true } }
            do { try proxy.start(); try transport.start(code: value) }
            catch { DispatchQueue.main.async { self.message.text = "USB接続を開始できませんでした。もう一度接続してください。"; self.setup.isHidden = false } }
        }
    }
    @objc private func background() {
        queue.async { [weak self] in self?.transport?.stop(); self?.proxy?.stop() }
        message.text = "再開するには「USBで接続」を押してください。"; setup.isHidden = false; UIApplication.shared.isIdleTimerDisabled = false
    }
    func webView(_ webView: WKWebView, decidePolicyFor action: WKNavigationAction, decisionHandler: @escaping (WKNavigationActionPolicy) -> Void) {
        guard let url = action.request.url else { decisionHandler(.cancel); return }
        decisionHandler(url.scheme == "http" && url.host == "127.0.0.1" && url.port == allowedPort ? .allow : .cancel)
    }
    func webView(_ webView: WKWebView, didFailProvisionalNavigation navigation: WKNavigation!, withError error: Error) {
        if (error as NSError).code == NSURLErrorCancelled { return }
        message.text = "管理画面の読み込みを確認できません。Macの通常のDashboard中継を確認してから、もう一度接続してください。"; setup.isHidden = false
    }
}
