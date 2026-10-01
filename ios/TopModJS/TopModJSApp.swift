import SwiftUI
import UIKit

// Hard-locks the app to landscape. The Info.plist supported-orientation keys are
// advisory for SwiftUI lifecycle apps and don't reliably prevent rotation on their
// own; UIApplicationDelegate.supportedInterfaceOrientationsFor is the authoritative
// runtime source, so we pin it here (both landscape directions allowed; no portrait).
final class AppDelegate: NSObject, UIApplicationDelegate {
    func application(_ application: UIApplication,
                     supportedInterfaceOrientationsFor window: UIWindow?) -> UIInterfaceOrientationMask {
        .landscape
    }
}

@main
struct TopModJSApp: App {
    @UIApplicationDelegateAdaptor(AppDelegate.self) var appDelegate

    var body: some Scene {
        WindowGroup {
            ContentView()
                .ignoresSafeArea()
                .statusBarHidden(true)
                .persistentSystemOverlays(.hidden)
                .preferredColorScheme(.dark)
                .background(Color.black)
        }
    }
}
