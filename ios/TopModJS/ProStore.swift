import Foundation
import StoreKit
import WebKit

// MARK: - TopMod Pro (one-time, non-consumable unlock) — StoreKit 2
// Page -> native:  window.webkit.messageHandlers.proStore.postMessage({action: "status" | "buy" | "restore"})
// Native -> page:  window.__tmPro.set(owned, displayPrice) · window.__tmPro.err(msg) · window.__tmPro.pending()
// The original TopMod tools stay free; the page locks the rest only when this bridge exists.
final class ProStore: NSObject, WKScriptMessageHandler {
    static let productID = "com.amaximal.topmodjs.pro"
    weak var webView: WKWebView?
    private var product: Product?
    private var owned = false
    private var updates: Task<Void, Never>?

    override init() {
        super.init()
        updates = Task.detached { [weak self] in
            for await result in Transaction.updates {
                if case .verified(let t) = result, t.productID == ProStore.productID { await t.finish() }
                await self?.refresh()
            }
        }
        Task { await self.loadProduct(); await self.refresh() }
    }
    deinit { updates?.cancel() }

    func userContentController(_ ucc: WKUserContentController, didReceive message: WKScriptMessage) {
        let action = ((message.body as? [String: Any])?["action"] as? String) ?? "status"
        Task {
            switch action {
            case "buy": await buy()
            case "restore":
                do { try await AppStore.sync() } catch { }
                await refresh()
            default:
                if product == nil { await loadProduct() }
                await refresh()
            }
        }
    }

    private func loadProduct() async {
        do { product = try await Product.products(for: [ProStore.productID]).first } catch { }
    }

    func refresh() async {
        var has = false
        for await result in Transaction.currentEntitlements {
            if case .verified(let t) = result, t.productID == ProStore.productID, t.revocationDate == nil { has = true }
        }
        owned = has
        push()
    }

    private func buy() async {
        if product == nil { await loadProduct() }
        guard let p = product else { js("window.__tmPro && window.__tmPro.err('The App Store is not reachable right now — try again in a moment.')"); return }
        do {
            switch try await p.purchase() {
            case .success(let verification):
                if case .verified(let t) = verification { await t.finish() }
                await refresh()
            case .pending:
                js("window.__tmPro && window.__tmPro.pending()")
            case .userCancelled:
                js("window.__tmPro && window.__tmPro.err('')")
            @unknown default:
                await refresh()
            }
        } catch {
            js("window.__tmPro && window.__tmPro.err('Purchase failed — please try again.')")
        }
    }

    private func push() {
        let price = (product?.displayPrice ?? "$3.99").replacingOccurrences(of: "'", with: "\\'")
        js("window.__tmPro && window.__tmPro.set(\(owned ? "true" : "false"), '\(price)')")
    }

    private func js(_ s: String) {
        DispatchQueue.main.async { self.webView?.evaluateJavaScript(s, completionHandler: nil) }
    }
}
