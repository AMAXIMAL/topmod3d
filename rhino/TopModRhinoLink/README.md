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
dotnet build -c Release          # embeds ~/AMAXiMAL/projects/topmod/TOPMOD.html
cp bin/Release/net7.0/TopModRhinoLink.rhp ~/Library/Application\ Support/McNeel/Rhinoceros/8.0/Plug-ins/TopModRhinoLink/
```
Restart Rhino, run `TopMod`. Rebuild + copy again whenever TOPMOD.html ships.

## Commands

| Command | What it does |
|---|---|
| `TopMod` | Opens the TopMod window (toolbar: Bake ▸ Rhino, Bake as Mesh, Import ◂ Rhino, Reload). |
| `TopModBake` | Bake the live model (option: NURBS / Mesh). |
| `TopModImport` | Select Rhino objects → new TopMod layer. |
| `TopModSetHtml` | Dev override: run an external TOPMOD.html instead of the embedded copy (Enter = embedded). |

The page also gets a small floating **Rhino** bar (bottom-right) with Bake / Mesh / Import. TopMod's bottom-bar **⇄ Rhino** toggle is switched on automatically; switch it off to pause pushing.

## How it works

`bridge.js` is injected before `</body>` when the page is written to `~/Library/Application Support/TopModRhinoLink/TOPMOD.html` and loaded in an Eto `WebView`. The window polls the page every 250 ms with `ExecuteScript`:

- `__tmPoll()` → `{ out:{v,obj}|null, cmds:[...], ready }` — the app's `rhinoPush()` is replaced with one that stores the visible model as OBJ instead of POSTing it.
- `__tmApply(kind, obj)` — `"sync"` replaces the active layer (other visible layers hidden, enabled modifiers on that layer turned off); `"import"` calls `addLayer(m, false)`.

Rhino-side changes are detected with `RhinoDoc.ReplaceRhinoObject` on the tagged live object (user string `TopModLive=1`). The Brep is turned back into polygons by reading each face's outer loop as a polyline; faces with curved edges are meshed instead.
