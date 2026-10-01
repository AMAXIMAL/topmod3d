# TopMod JS — iOS wrapper

SwiftUI + WKWebView wrapper for the single-file TopMod.JS topological mesh modeler.

## Build
1. Open `TOPMOD 3D.xcodeproj` in Xcode 15+
2. Select your Team under Signing & Capabilities
3. Build & run (iOS 17+, iPhone landscape / iPad all orientations)

## Architecture notes
- `topmodjs.html` is fully self-contained (three.js r128 inlined — no network at runtime)
- Served via a `topmod://` custom scheme handler with **explicit** Content-Type headers
  (never rely on URLResponse.mimeType inference inside WKWebView)
- Page is loaded in `updateUIView` after the view enters the hierarchy, so the
  GPU/WebContent processes exist before the page's rAF loop starts
- Scroll/bounce/pinch disabled on the scrollView; the page scopes all touch
  handling to the canvas element
- **Export OBJ** posts to `window.webkit.messageHandlers.exportOBJ` → native
  share sheet (blob `<a download>` doesn't work in WKWebView). The same button
  falls back to a browser download when the bridge is absent, so the HTML file
  still works standalone in Safari/Chrome.
- **Import OBJ** uses `<input type="file">`, which WKWebView presents natively
  via the document picker — no bridge needed
- `PrivacyInfo.xcprivacy` included (no tracking, no collected data, no
  required-reason APIs)

## Updating the web app
Replace `TopModJS/Resources/topmodjs.html` with a new build — no Swift changes needed.
