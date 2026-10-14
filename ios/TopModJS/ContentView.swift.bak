import SwiftUI
import WebKit
import ARKit
import Photos
import Network

// MARK: - Custom scheme handler
// Serves the bundled single-file app over topmod:// with EXPLICIT Content-Type
// headers. (Lesson from ION3D: relying on URLResponse.mimeType inference inside
// WKWebView custom schemes fails intermittently — always set headers yourself.)
final class TopModSchemeHandler: NSObject, WKURLSchemeHandler {

    func webView(_ webView: WKWebView, start urlSchemeTask: WKURLSchemeTask) {
        guard
            let url = urlSchemeTask.request.url,
            let fileURL = Bundle.main.url(forResource: "topmodjs", withExtension: "html"),
            let data = try? Data(contentsOf: fileURL)
        else {
            urlSchemeTask.didFailWithError(URLError(.fileDoesNotExist))
            return
        }

        let headers: [String: String] = [
            "Content-Type": "text/html; charset=utf-8",
            "Content-Length": String(data.count),
            "Cache-Control": "no-cache",
        ]
        guard let response = HTTPURLResponse(
            url: url, statusCode: 200, httpVersion: "HTTP/1.1", headerFields: headers
        ) else {
            urlSchemeTask.didFailWithError(URLError(.badServerResponse))
            return
        }

        urlSchemeTask.didReceive(response)
        urlSchemeTask.didReceive(data)
        urlSchemeTask.didFinish()
    }

    func webView(_ webView: WKWebView, stop urlSchemeTask: WKURLSchemeTask) {}
}

// MARK: - JS -> Swift export bridge
// The page posts {filename, text} on:
//   window.webkit.messageHandlers.saveOBJ    -> native "Save to Files" document picker
//   window.webkit.messageHandlers.exportOBJ  -> system share sheet (fallback)
final class ExportBridge: NSObject, WKScriptMessageHandler, UIDocumentPickerDelegate {

    func userContentController(
        _ userContentController: WKUserContentController,
        didReceive message: WKScriptMessage
    ) {
        guard let body = message.body as? [String: Any] else { return }

        let tmpURL: URL

        if message.name == "saveImage", let dataURL = body["dataURL"] as? String {
            // strip the "data:image/jpeg;base64," prefix and decode
            let filename = (body["filename"] as? String) ?? "topmod.jpg"
            guard let comma = dataURL.firstIndex(of: ","),
                  let data = Data(base64Encoded: String(dataURL[dataURL.index(after: comma)...])) else { return }
            tmpURL = FileManager.default.temporaryDirectory.appendingPathComponent(filename)
            do { try data.write(to: tmpURL) } catch { return }
        } else {
            guard let text = body["text"] as? String else { return }
            let filename = (body["filename"] as? String) ?? "topmod.obj"
            tmpURL = FileManager.default.temporaryDirectory.appendingPathComponent(filename)
            do { try text.write(to: tmpURL, atomically: true, encoding: .utf8) } catch { return }
        }

        DispatchQueue.main.async {
            guard
                let scene = UIApplication.shared.connectedScenes.first as? UIWindowScene,
                let root = scene.keyWindow?.rootViewController
            else { return }
            var top = root
            while let presented = top.presentedViewController { top = presented }

            if message.name == "saveOBJ" || message.name == "saveImage" {
                // Native Save: let the user choose a location in Files.
                let picker = UIDocumentPickerViewController(forExporting: [tmpURL], asCopy: true)
                picker.shouldShowFileExtensions = true
                top.present(picker, animated: true)
            } else {
                let avc = UIActivityViewController(activityItems: [tmpURL], applicationActivities: nil)
                if let pop = avc.popoverPresentationController {
                    pop.sourceView = root.view
                    pop.sourceRect = CGRect(
                        x: root.view.bounds.midX, y: root.view.bounds.midY, width: 0, height: 0
                    )
                    pop.permittedArrowDirections = []
                }
                top.present(avc, animated: true)
            }
        }
    }
}


// MARK: - JS -> Swift Photos bridge
// Saves exports straight into the user's photo library (add-only permission):
//   window.webkit.messageHandlers.photoImage  body {dataURL}        -> screenshot
//   window.webkit.messageHandlers.photoVideo  body {dataURL, ext}   -> spin video (mp4)
// Reports back to the page via window.__onPhotoResult(ok, message).
final class PhotosBridge: NSObject, WKScriptMessageHandler {
    weak var webView: WKWebView?

    func userContentController(_ ucc: WKUserContentController, didReceive message: WKScriptMessage) {
        guard let body = message.body as? [String: Any],
              let dataURL = body["dataURL"] as? String,
              let comma = dataURL.firstIndex(of: ","),
              let data = Data(base64Encoded: String(dataURL[dataURL.index(after: comma)...]))
        else { report(false, "Nothing to save"); return }

        let isVideo = (message.name == "photoVideo")
        let ext = (body["ext"] as? String) ?? (isVideo ? "mp4" : "jpg")

        PHPhotoLibrary.requestAuthorization(for: .addOnly) { [weak self] status in
            guard let self = self else { return }
            guard status == .authorized || status == .limited else {
                self.report(false, "Photos access denied — enable it in Settings."); return
            }
            if isVideo {
                let tmp = FileManager.default.temporaryDirectory
                    .appendingPathComponent("topmod-spin." + ext)
                do { try data.write(to: tmp) }
                catch { self.report(false, "Couldn't write the video"); return }
                PHPhotoLibrary.shared().performChanges {
                    PHAssetCreationRequest.creationRequestForAssetFromVideo(atFileURL: tmp)
                } completionHandler: { ok, err in
                    try? FileManager.default.removeItem(at: tmp)
                    self.report(ok, ok ? "Video saved to Photos"
                                       : "Couldn't save video (\(err?.localizedDescription ?? "unsupported format"))")
                }
            } else {
                PHPhotoLibrary.shared().performChanges {
                    let req = PHAssetCreationRequest.forAsset()
                    req.addResource(with: .photo, data: data, options: nil)
                } completionHandler: { ok, err in
                    self.report(ok, ok ? "Screenshot saved to Photos"
                                       : "Couldn't save image (\(err?.localizedDescription ?? "error"))")
                }
            }
        }
    }

    private func report(_ ok: Bool, _ msg: String) {
        let esc = msg.replacingOccurrences(of: "\\", with: "\\\\").replacingOccurrences(of: "'", with: "\\'")
        DispatchQueue.main.async {
            self.webView?.evaluateJavaScript(
                "window.__onPhotoResult && window.__onPhotoResult(\(ok ? "true" : "false"), '\(esc)')",
                completionHandler: nil)
        }
    }
}


// MARK: - LiDAR depth provider
// Keeps MediaPipe (in the web view) as the 2D hand-landmark source, but augments it
// with real-world depth sampled from the LiDAR sceneDepth map. The web app sends
// fingertip points (normalized 0..1) via window.webkit.messageHandlers.lidarQuery;
// we sample depth (metres) at those points and post results back to window.__lidarDepth().
final class LiDARProvider: NSObject, ARSessionDelegate {
    private let session = ARSession()
    weak var webView: WKWebView?
    private(set) var available = false
    private var latestDepth: CVPixelBuffer?
    private var depthW = 0, depthH = 0

    func start() {
        guard ARWorldTrackingConfiguration.supportsFrameSemantics(.sceneDepth) else {
            available = false; return
        }
        available = true
        let cfg = ARWorldTrackingConfiguration()
        cfg.frameSemantics.insert(.sceneDepth)
        session.delegate = self
        session.run(cfg, options: [.resetTracking, .removeExistingAnchors])
    }

    func stop() { session.pause() }

    func session(_ session: ARSession, didUpdate frame: ARFrame) {
        guard let d = frame.sceneDepth?.depthMap else { return }
        latestDepth = d
        depthW = CVPixelBufferGetWidth(d)
        depthH = CVPixelBufferGetHeight(d)
    }

    // Sample depth (metres) at a normalized point (x,y in 0..1, origin top-left).
    func depth(atX nx: Double, y ny: Double) -> Float? {
        guard let buf = latestDepth, depthW > 0, depthH > 0 else { return nil }
        CVPixelBufferLockBaseAddress(buf, .readOnly)
        defer { CVPixelBufferUnlockBaseAddress(buf, .readOnly) }
        guard let base = CVPixelBufferGetBaseAddress(buf) else { return nil }
        let bpr = CVPixelBufferGetBytesPerRow(buf)
        let px = min(depthW - 1, max(0, Int(nx * Double(depthW))))
        let py = min(depthH - 1, max(0, Int(ny * Double(depthH))))
        let row = base.advanced(by: py * bpr).assumingMemoryBound(to: Float32.self)
        let v = row[px]
        return v.isFinite && v > 0 ? v : nil
    }
}

// Bridge that receives fingertip queries from the web app and returns LiDAR depth.
final class LiDARBridge: NSObject, WKScriptMessageHandler {
    weak var provider: LiDARProvider?
    weak var webView: WKWebView?

    func userContentController(_ ucc: WKUserContentController, didReceive message: WKScriptMessage) {
        guard message.name == "lidarQuery", let provider = provider, let webView = webView else { return }
        // body: { id: <n>, points: [{x,y}, ...] }  (x,y normalized 0..1)
        guard let body = message.body as? [String: Any],
              let id = body["id"],
              let pts = body["points"] as? [[String: Any]] else { return }
        var depths: [Any] = []
        for p in pts {
            let x = (p["x"] as? Double) ?? 0.5
            let y = (p["y"] as? Double) ?? 0.5
            if let d = provider.depth(atX: x, y: y) { depths.append(Double(d)) }
            else { depths.append(NSNull()) }
        }
        if let json = try? JSONSerialization.data(withJSONObject: ["id": id, "depths": depths]),
           let str = String(data: json, encoding: .utf8) {
            DispatchQueue.main.async {
                webView.evaluateJavaScript("window.__lidarDepth && window.__lidarDepth(\(str))", completionHandler: nil)
            }
        }
    }
}

// MARK: - WebView
// MARK: - Media capture permission
// WKWebView DENIES getUserMedia by default on iOS 15+. The page's hand-tracking
// (MediaPipe) calls navigator.mediaDevices.getUserMedia; without a WKUIDelegate that
// grants the request, the camera never opens. NSCameraUsageDescription is already set.
final class WebUIDelegate: NSObject, WKUIDelegate {
    func webView(_ webView: WKWebView,
                 requestMediaCapturePermissionFor origin: WKSecurityOrigin,
                 initiatedByFrame frame: WKFrameInfo,
                 type: WKMediaCaptureType,
                 decisionHandler: @escaping (WKPermissionDecision) -> Void) {
        decisionHandler(.grant)   // NSCameraUsageDescription still gates the real OS prompt
    }

    // JavaScript alert / confirm / prompt — WKWebView silently drops these unless the UI delegate shows them.
    // (Without this, confirm() returns false and prompt() returns nil: rename layer, save-as, etc. did nothing.)
    private func topVC() -> UIViewController? {
        let scene = UIApplication.shared.connectedScenes.compactMap { $0 as? UIWindowScene }.first
        var vc = scene?.windows.first(where: { $0.isKeyWindow })?.rootViewController ?? scene?.windows.first?.rootViewController
        while let p = vc?.presentedViewController { vc = p }
        return vc
    }
    func webView(_ webView: WKWebView, runJavaScriptAlertPanelWithMessage message: String,
                 initiatedByFrame frame: WKFrameInfo, completionHandler: @escaping () -> Void) {
        guard let vc = topVC() else { completionHandler(); return }
        let a = UIAlertController(title: nil, message: message, preferredStyle: .alert)
        a.addAction(UIAlertAction(title: "OK", style: .default) { _ in completionHandler() })
        vc.present(a, animated: true)
    }
    func webView(_ webView: WKWebView, runJavaScriptConfirmPanelWithMessage message: String,
                 initiatedByFrame frame: WKFrameInfo, completionHandler: @escaping (Bool) -> Void) {
        guard let vc = topVC() else { completionHandler(false); return }
        let a = UIAlertController(title: nil, message: message, preferredStyle: .alert)
        a.addAction(UIAlertAction(title: "Cancel", style: .cancel) { _ in completionHandler(false) })
        a.addAction(UIAlertAction(title: "OK", style: .default) { _ in completionHandler(true) })
        vc.present(a, animated: true)
    }
    func webView(_ webView: WKWebView, runJavaScriptTextInputPanelWithPrompt prompt: String, defaultText: String?,
                 initiatedByFrame frame: WKFrameInfo, completionHandler: @escaping (String?) -> Void) {
        guard let vc = topVC() else { completionHandler(nil); return }
        let a = UIAlertController(title: nil, message: prompt, preferredStyle: .alert)
        a.addTextField { $0.text = defaultText }
        a.addAction(UIAlertAction(title: "Cancel", style: .cancel) { _ in completionHandler(nil) })
        a.addAction(UIAlertAction(title: "OK", style: .default) { _ in completionHandler(a.textFields?.first?.text) })
        vc.present(a, animated: true)
    }
}

// MARK: - Loopback server (secure context for getUserMedia)
// navigator.mediaDevices only exists in a "secure context". A custom scheme (topmod://)
// is NOT one, so the page reports "Camera API not available". WebKit treats loopback
// (127.0.0.1) as trustworthy, so serving the bundled single-file app over http://127.0.0.1
// makes mediaDevices appear. No TLS/ATS entry needed — ATS exempts loopback. Loopback-only,
// so nothing is exposed on the network.
final class LocalServer {
    private var listener: NWListener?
    private(set) var port: UInt16 = 0
    private let html: Data

    init?() {
        guard let url = Bundle.main.url(forResource: "topmodjs", withExtension: "html"),
              let data = try? Data(contentsOf: url) else { return nil }
        html = data
    }

    /// Starts on the first free loopback port; returns it, or nil if none bound.
    func start() -> UInt16? {
        for p: UInt16 in [8763, 8764, 8765, 8811, 9137, 9723] {
            if startOn(port: p) { port = p; return p }
        }
        return nil
    }

    private func startOn(port p: UInt16) -> Bool {
        guard let nwPort = NWEndpoint.Port(rawValue: p) else { return false }
        let params = NWParameters.tcp
        params.requiredLocalEndpoint = .hostPort(host: "127.0.0.1", port: nwPort)  // loopback-only
        guard let l = try? NWListener(using: params) else { return false }
        l.newConnectionHandler = { [weak self] conn in self?.serve(conn) }
        l.start(queue: .global(qos: .userInitiated))
        listener = l
        return true
    }

    private func serve(_ conn: NWConnection) {
        conn.start(queue: .global())
        // Read (and ignore) the request line, then return the app for any GET.
        conn.receive(minimumIncompleteLength: 1, maximumLength: 65536) { [weak self] _, _, _, _ in
            guard let self = self else { conn.cancel(); return }
            let header = "HTTP/1.1 200 OK\r\n"
                + "Content-Type: text/html; charset=utf-8\r\n"
                + "Content-Length: \(self.html.count)\r\n"
                + "Cache-Control: no-cache\r\n"
                + "Connection: close\r\n\r\n"
            var out = Data(header.utf8); out.append(self.html)
            conn.send(content: out, completion: .contentProcessed { _ in conn.cancel() })
        }
    }
}

struct TopModWebView: UIViewRepresentable {

    func makeUIView(context: Context) -> WKWebView {
        let config = WKWebViewConfiguration()
        config.setURLSchemeHandler(TopModSchemeHandler(), forURLScheme: "topmod")
        let bridge = ExportBridge()
        config.userContentController.add(bridge, name: "exportOBJ")
        config.userContentController.add(bridge, name: "saveOBJ")
        config.userContentController.add(bridge, name: "saveImage")
        config.allowsInlineMediaPlayback = true
        // Shift+F / View > Full Screen calls requestFullscreen(); WKWebView ignores it unless this is on (iOS 15.4+).
        if #available(iOS 15.4, *) { config.preferences.isElementFullscreenEnabled = true }

        // Save screenshots & spin videos straight to the photo library.
        let photos = PhotosBridge()
        config.userContentController.add(photos, name: "photoImage")
        config.userContentController.add(photos, name: "photoVideo")
        context.coordinator.photos = photos

        // LiDAR depth provider — refines hand-tracking depth when available.
        let lidar = LiDARProvider()
        let lidarBridge = LiDARBridge()
        lidarBridge.provider = lidar
        config.userContentController.add(lidarBridge, name: "lidarQuery")
        context.coordinator.lidar = lidar
        context.coordinator.lidarBridge = lidarBridge

        let webView = WKWebView(frame: .zero, configuration: config)
        lidar.webView = webView
        lidarBridge.webView = webView
        context.coordinator.photos?.webView = webView

        // Grant camera/mic capture requests from the page (see WebUIDelegate).
        let uiDelegate = WebUIDelegate()
        webView.uiDelegate = uiDelegate
        context.coordinator.uiDelegate = uiDelegate
        webView.navigationDelegate = context.coordinator

        // Loopback server so the page runs in a secure context (enables the camera).
        context.coordinator.server = LocalServer()
        webView.isOpaque = false
        webView.backgroundColor = .black
        webView.scrollView.backgroundColor = .black

        // The page owns all gestures (canvas-scoped touch handling).
        webView.scrollView.isScrollEnabled = false
        webView.scrollView.bounces = false
        webView.scrollView.pinchGestureRecognizer?.isEnabled = false
        webView.scrollView.contentInsetAdjustmentBehavior = .never

        #if DEBUG
        if #available(iOS 16.4, *) { webView.isInspectable = true }
        #endif

        return webView
    }

    func updateUIView(_ webView: WKWebView, context: Context) {
        // Load once, after the view is in the hierarchy — gives the GPU/WebContent
        // processes time to exist before the page's rAF loop starts (ION3D lesson).
        guard webView.url == nil else { return }
        // Prefer the loopback origin (secure context → camera works); fall back to the
        // custom scheme so the app still loads even if the port can't be bound.
        if let port = context.coordinator.server?.start() {
            webView.load(URLRequest(url: URL(string: "http://127.0.0.1:\(port)/index.html")!))
        } else {
            webView.load(URLRequest(url: URL(string: "topmod://app/index.html")!))
        }
        // Start LiDAR and tell the web app whether depth refinement is available.
        let lidar = context.coordinator.lidar
        lidar?.start()
        let avail = lidar?.available ?? false
        DispatchQueue.main.asyncAfter(deadline: .now() + 1.0) {
            webView.evaluateJavaScript("window.__lidarAvailable && window.__lidarAvailable(\(avail))", completionHandler: nil)
        }
    }

    func makeCoordinator() -> Coordinator { Coordinator() }
    final class Coordinator: NSObject, WKNavigationDelegate {
        var lidar: LiDARProvider?
        var lidarBridge: LiDARBridge?
        var photos: PhotosBridge?
        var uiDelegate: WebUIDelegate?
        var server: LocalServer?
        var didFallback = false
        // If the loopback load fails (e.g. ATS blocks it), fall back to the bundled
        // custom scheme so the app still loads (camera stays off, but no blank screen).
        func webView(_ webView: WKWebView, didFailProvisionalNavigation navigation: WKNavigation!, withError error: Error) {
            guard !didFallback else { return }
            didFallback = true
            webView.load(URLRequest(url: URL(string: "topmod://app/index.html")!))
        }
    }
}

// MARK: - Root view
struct ContentView: View {
    var body: some View {
        TopModWebView()
            .ignoresSafeArea()
            .background(Color.black)
            .onAppear {
                // Force the initial frame to landscape (the AppDelegate hard-lock
                // disallows portrait entirely).
                guard let scene = UIApplication.shared.connectedScenes.first as? UIWindowScene
                else { return }
                scene.requestGeometryUpdate(.iOS(interfaceOrientations: .landscape))
            }
    }
}

#Preview {
    ContentView()
}
