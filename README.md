# TopMod 3D — MASTER

**Current: v3.9 (build 10)** · bundle `com.amaximal.topmodjs` · team 7VP47VH46A

Every update starts here.

| Path | What |
|---|---|
| `TOPMOD.html` | **THE master.** The only file you edit. Single-file app (three.js r128 inlined). |
| `ios/` | Xcode project (`TOPMOD 3D.xcodeproj`). `Resources/topmodjs.html` is a copy made by ship.py; don't edit it. |
| `tools/ship.py` | One command that carries the master everywhere. |
| `dist/` | Shipped builds: `TopModJS-Xcode_v<ver>_b<build>.zip` + HTML snapshot. Not in git. |
| `tests/` | Test matrices and reports. |
| `archive/` | Older versions, read-only history. |

## Update loop
```
cd ~/AMAXiMAL/projects/topmod
# 1. edit TOPMOD.html  (bump the wordmark  <span class="ver">v3.8</span>  when it's a new version)
# 2. ship
python3 tools/ship.py            # copies into Xcode, build +1, zips to dist/, git commit + tag
python3 tools/ship.py --site     # same, plus updates amaximal.com's TopMod page
python3 tools/ship.py --version 3.8   # new version without hand-editing the wordmark
# 3. Xcode: open ios/TOPMOD 3D.xcodeproj → Product ▸ Archive → upload
# 4. Site live: cd ~/AMAXiMAL/website/portfolio && python3 tools/build_site.py --deploy
```

## History
- **v3.9 b10 (2026-10-06)**: on-screen (floating, hand-sized) sliders get typeable number boxes — Enter to apply; exact values beyond the slider range are honoured on modifier params.
- **v3.9 b9 (2026-10-06)**: Extrude is a live modifier with a Taper (end size) slider plus distance/twist/segments; works on a mesh that already has modifiers. Every mesh tool now lands in the modifier stack (live, reorderable, undoable) instead of being blocked; hand push/pull extrude also becomes a live Extrude modifier. Modifier stack moved to its own narrow, draggable, resizable window. Smaller tool icons. Topology readout shows the on-screen (modified) mesh.
- **v3.8 b8 (2026-10-01)**: fresh start from the unified v3.7 master; first App Store Connect upload from ~/AMAXiMAL.
- **v3.7 b7 (2026-10-01)**: unified release. Sep 30 master (v3.1 + Demo mode, boolean/greeble/metaballs/voxel/
  differential growth/measure/printer/hands) merged with the Sep 26 App Store work (topmodjs_210: Full Screen
  Shift+F, G = wide zoom-to-fit, in-app Render (orange glass, studio env, bloom) + MagicaCSG hand-off, floating
  hand-sized parameter panel + Apply/Cancel, hand palettes (primitives / remesh / modifiers), slider-ball pinch,
  fab replay, video edge compensation, LiDAR depth refinement). iOS shell from "TopModJS 10" (loopback server so
  camera/hand-tracking works, media-capture permission), corrected bundle ID. Fixed: Demo button threw in the
  menubar dropdown code (no dropdown list).
- v3.3 b6 (2026-09-26): App Store submission shell (Downloads/TopModJS 10).
- v3.1 (2026-09-30): Demo mode (auto-starts; add `#nodemo` to the URL to skip).
