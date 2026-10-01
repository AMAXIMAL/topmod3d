# TopMod 3D — MASTER

**Current: v3.8 (build 8)** · bundle `com.amaximal.topmodjs` · team 7VP47VH46A

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
