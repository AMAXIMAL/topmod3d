# TopMod 3D for Rhino

Rhino 8 (Mac, .NET 7, Eto) plug-in. **TopMod is part of the plug-in**: `TOPMOD.html` is compiled into the `.rhp` and runs in a Rhino window. No server, no browser, no network — the two talk in-process.

- **TopMod → Rhino**: every edit lands in Rhino as a NURBS polysurface (one planar face per polygon, n-gons kept) on layer **TopMod Live**. One object, same id throughout.
- **Rhino → TopMod**: move / scale / rotate / gumball / point-edit that live object (or undo) and TopMod picks it up within ~0.3 s.
- **Bake ▸ Rhino** / **Bake as Mesh**: copies the live model to layer **TopMod Baked** as a normal object that stops following TopMod.
- **Import ◂ Rhino**: pick meshes / polysurfaces / surfaces / SubDs → new TopMod layer, in place.

Units: TopMod works in inches; the plug-in converts to and from the document's model units.

## Build & install (Mac)

```
cd ~/AMAXiMAL/projects/topmod/rhino/TopModRhinoLink
python3 sync_topmod.py           # only when a new TOPMOD.html master ships: copies it here + adds the Rhino hooks
dotnet build -c Release          # embeds ./TOPMOD.html (the build refuses a copy without the hooks)
cp bin/Release/net7.0/TopModRhinoLink.rhp ~/Library/Application\ Support/McNeel/Rhinoceros/8.0/MacPlugIns/TopModRhinoLink.rhp/
```
Restart Rhino, run `TopMod`.

**The plug-in owns its copy of the app.** `./TOPMOD.html` is a snapshot of the master (`~/AMAXiMAL/projects/topmod/TOPMOD.html`) with a `window.__tmBridge` export inserted so the plug-in can reach into the app's closure. The master stays untouched and can be rebuilt freely; the plug-in only changes when you run `sync_topmod.py`. `sync_topmod.py --check` tells you whether the current copy has the hooks.

## Commands

| Command | What it does |
|---|---|
| `TopMod` | Opens the TopMod window (toolbar: Bake ▸ Rhino, Bake as Mesh, Import ◂ Rhino, Reload). |
| `TopModBake` | Bake the live model (option: NURBS / Mesh). |
| `TopModImport` | Select Rhino objects → new TopMod layer. |
| `TopModSetHtml` | Dev override: run an external TOPMOD.html instead of the embedded copy (Enter = embedded). |

The page also gets a small floating **Rhino** bar (bottom-right) with Bake / Mesh / Import. TopMod's own bottom-bar **⇄ Rhino** toggle is switched on automatically; switch it off to pause pushing.

## How it works

`bridge.js` is injected before `</body>` when the page is written to `~/Library/Application Support/TopModRhinoLink/TOPMOD.html` and loaded in an Eto `WebView`. The window polls the page every 250 ms with `ExecuteScript`:

- `__tmPoll()` → `{ out:{v,obj}|null, cmds:[...], ready }` — the app's `rhinoPush()` is replaced with one that stores the visible model as OBJ instead of POSTing it; the app's own 250 ms debounce (`maybeRhinoPush`) stays.
- `__tmApply(kind, obj)` — `"sync"` replaces the active layer (other visible layers hidden, enabled modifiers on that layer turned off, since what came back is already flattened); `"import"` calls `addLayer(m, false)`.

Rhino-side changes are detected with `RhinoDoc.ReplaceRhinoObject` on the tagged live object (user string `TopModLive=1`). The Brep is turned back into polygons by reading each face's outer loop as a polyline (exact for anything TopMod made, even after transforms); faces with curved edges are meshed instead.

## Files

- `TopModLinkPlugIn.cs` — plug-in, settings, page preparation
- `TopModForm.cs` — Eto window: toolbar, WebView, the 250 ms poll/apply loop
- `LiveSync.cs` — live object, doc events, bake, version queue
- `ObjConvert.cs` — OBJ ⇄ PolyMesh ⇄ Brep/Mesh
- `Commands.cs` — the four commands
- `bridge.js` — injected page-side code
- `TOPMOD.html` — the plug-in's copy of the app (master + bridge hooks); refreshed by `sync_topmod.py`
- `sync_topmod.py` — copies the master TOPMOD.html here and inserts the `__tmBridge` export (idempotent)
