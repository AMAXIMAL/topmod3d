# TopMod 3D

**Topological mesh modeling for 3D printing, fabrication and simulation — in one HTML file, on the web and on iPhone/iPad.**

[▶ Open the web app](https://amaximal.com/software/topmod) · [Product page](https://amaximal.com/topmod3d) · App Store: *TopMod 3D: Design Software* · by [AMAXIMAL](https://amaximal.com)

TopMod 3D is a modern reimagining of TopMod, the topological mesh modeler by Ergun Akleman, Vinod Srinivasan and Jianer Chen (Texas A&M; [TopModLab/TopMod](https://github.com/TopModLab/TopMod)). It keeps TopMod's genus-changing tools — handles, crusts, rinds, wireframes, Doo-Sabin and Catmull-Clark — and adds a live modifier stack, fabrication outputs and simulation.

## Highlights (v3.9)

- **Topology** — twisted handles, bridges, crust and rind modeling, wireframe, pipes, offsets
- **Subdivision** — Catmull-Clark (with creases), Doo-Sabin, Loop, √3, honeycomb, pentagonalize, dual, truncate
- **Live modifier stack and layers** — non-destructive deform, generate and topology modifiers
- **Generate** — Cage (box-morph lattice), TPMS infill, gyroid, Voronoi, metaballs, differential growth, mazes, text
- **Gems** — 11 parametric diamond cuts with ray-traced stone views; **Bejewel** sets gems with prongs on every face
- **Simulate** — XPBD cloth, rigid-body physics (grab and throw), melt, wind-tunnel aero (streamlines + pressure), FEA stress and deflection, topology optimization, shrink-wrap, granular pour
- **Fabricate** — papercraft unfold, plywood slab, laser, waffle, 3D-print prep with tree supports
- **Hand tracking** — pinch, extrude and orbit with your hands (Help ▸ Hand controls)
- **Import / export** — OBJ, STL, PLY, OFF, glTF/GLB, 3MF; native `.topmod` projects

## What's new in 3.9

- **Bejewel** (Gems) — gems on every face or vertex, Gem Cut shapes and sliders, optional prongs baked to their own layer, absolute same-size toggle, works as a live modifier
- **Cage** is now the first Generate tool; Generate ▸ Lattice is renamed **Infill**
- **Simulate** — Physics (grab/throw, Magnet packing), Melt presets (ice, jelly, plastic, wax, chocolate), Aero, FEA, **TopOpt** (worked examples, step-by-step tutorial, Size × slider), Shrink-wrap and Granular pour (3–5× faster)
- **AR** button on the bottom bar (AR Quick Look on iPhone); Credits moved to Help
- **Loop** face/edge-loop toggle on the bottom bar
- **Hand controls** animated guide (Help ▸ Hand controls) and an in-app **What's New** (Help ▸ What's New)
- Grey/orange and white/lime themes, collapsed layers, edge-to-edge panels, landscape safe-area fix, roomier sliders

## Run it

The whole app is [`TOPMOD.html`](TOPMOD.html). Open it in a modern browser — no build step, no server.

## Repository layout

| Path | What |
|---|---|
| `TOPMOD.html` | The app (single file; three.js inlined) |
| `ios/` | Xcode project for the iPhone/iPad app (wraps the same HTML) |
| `site/` | Copy of the amaximal.com TopMod 3D pages |
| `guides/`, `tests/` | Step guides and test matrices |
| `tools/` | Release scripts |
| `docs/WORKFLOW.md` | Internal build and release notes |

## Credits

TopMod: Ergun Akleman, Vinod Srinivasan, Jianer Chen. TopMod 3D: John Briscella / AMAXIMAL.
