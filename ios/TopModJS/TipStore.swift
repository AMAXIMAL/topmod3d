import Foundation
import StoreKit
import WebKit

// MARK: - Donations ("tips") — one-time consumable in-app purchases, StoreKit 2
// Page -> native:  window.webkit.messageHandlers.tipStore.postMessage({action: "prices"} | {action: "tip", amount: 25})
// Native -> page:  window.__tmTip.prices({1:"$0.99", ...}) · window.__tmTip.done(ok, message)
final class TipStore: NSObject, WKScriptMessageHandler {
    static let amounts = [1, 5, 10, 25, 50, 100, 250]
    static func productID(_ amount: Int) -> String { "com.amaximal.topmodjs.tip\(amount)" }
    weak var webView: WKWebView?
    private var products: [Int: Product] = [:]

    override init() {
        super.init()
        Task { await load() }
        Task.detached {   // finish any tip that completed while the app was closed (e.g. Ask to Buy approved later)
            for await r in Transaction.updates { if case .verified(let t) = r { await t.finish() } }
        }
    }

    func userContentController(_ ucc: WKUserContentController, didReceive message: WKScriptMessage) {
        let body = message.body as? [String: Any] ?? [:]
        let action = body["action"] as? String ?? "prices"
        Task {
            if products.isEmpty { await load() }
            if action == "tip", let amt = (body["amount"] as? NSNumber)?.intValue { await tip(amt) } else { pushPrices() }
        }
    }

    private func load() async {
        do {
            let list = try await Product.products(for: TipStore.amounts.map { TipStore.productID($0) })
            for p in list { if let a = TipStore.amounts.first(where: { TipStore.productID($0) == p.id }) { products[a] = p } }
        } catch { }
        pushPrices()
    }

    private func pushPrices() {
        let pairs = products.map { "\"\($0.key)\":\"\($0.value.displayPrice.replacingOccurrences(of: "\"", with: ""))\"" }.joined(separator: ",")
        js("window.__tmTip && window.__tmTip.prices({\(pairs)})")
    }

    private func tip(_ amount: Int) async {
        guard let p = products[amount] else { js("window.__tmTip && window.__tmTip.done(false,'The App Store is not reachable right now — please try again.')"); return }
        do {
            switch try await p.purchase() {
            case .success(let v):
                if case .verified(let t) = v { await t.finish() }
                js("window.__tmTip && window.__tmTip.done(true)")
            case .pending:
                js("window.__tmTip && window.__tmTip.done(false,'Waiting for approval.')")
            case .userCancelled:
                js("window.__tmTip && window.__tmTip.done(false,'')")
            @unknown default:
                js("window.__tmTip && window.__tmTip.done(false,'')")
            }
        } catch { js("window.__tmTip && window.__tmTip.done(false,'The donation did not go through — please try again.')") }
    }

    private func js(_ s: String) { DispatchQueue.main.async { self.webView?.evaluateJavaScript(s, completionHandler: nil) } }
}
