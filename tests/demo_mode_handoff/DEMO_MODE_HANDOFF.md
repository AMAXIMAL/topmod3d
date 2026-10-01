# TopMod 3D — Demo mode handoff

Demo mode drives the real TopMod tools on its own: it presses toolbar buttons, opens the Options popover, sets a slider, presses **Apply**, and loops through a few builds while the model turns slowly. A pill at the top says **Demo mode on**, shows the current step, and has an **×** that stops the demo and hands control to the user.

Built and tested against `topmodjs.html` **v3.0** (the build in `topmod-test/www`).

## Files

| File | What it is |
|---|---|
| `demo-mode.css` | Pill, blinking dot, × button, orange "pressed" highlight on buttons and Apply |
| `demo-mode.js` | The demo engine and the four build sequences |

## How to add it

1. **CSS** — paste `demo-mode.css` into the app's `<style>` block (or a new `<style>` just before `</head>`).
2. **JS** — paste `demo-mode.js` **inside the app IIFE**, directly before its final `})();`.
   - The app script starts with `/* ===== TopModJS app ===== */` and `(function(){ "use strict"; ...`.
   - In v3.0 that script ends with `})();\n})();` — insert before the **last** `})();`.
   - It has to live inside the IIFE: `mesh`, `sel`, `yaw`, `applyCam` etc. are not globals, so a separate `<script>` can't reach them.
3. **Start it** by opening the app with `#demo` or `?demo` in the URL, e.g. `index.html#demo`. Without it, nothing runs and nothing is added to the page.

## What it relies on in the app

If any of these are renamed in v3.1, update the matching line in `demo-mode.js`.

| Name | Used for |
|---|---|
| `#toolbar [data-p="…"]` | Primitive buttons (dodecahedron, torus, cube, buckyball) |
| `#toolbar [data-op="…"]` | Tool buttons (catmull, extrude, stellate, doosabin, pentagonalize, dual, menger, wireframe, honeycomb, doubleStellate) |
| `#toolpop`, `#tpApply` | Options popover and its Apply button (checks for the `hiddenPanel` / `empty` classes) |
| `#pIters` | Catmull-Clark iterations slider (set to 1) |
| `#menubar [data-act="frameAll"]` | Zoom to fit after each build |
| `mesh.faces` | Face count guard, so heavy ops are skipped past 4,000–6,000 faces |
| `sel.faces`, `setMode("face")`, `rebuildOverlay()`, `updateHint()`, `clearSel()` | Selecting every third face before Extrude |
| `yaw`, `applyCam()` | Slow turntable while the demo runs |
| `closeToolPop()`, `toast()` | Closing the popover, the "your turn" message on stop |

## Behaviour

- **Start:** 1.2 s after load, the pill appears and the turntable starts.
- **Each step:** the button gets an orange outline for ~0.65 s and is clicked. If the Options popover opens, Apply is highlighted for 0.6 s and pressed. Then the popover closes.
- **Sequences (looping):**
  1. Dodecahedron → Catmull-Clark ×1 → select every third face → Extrude → Stellate
  2. Torus → Doo-Sabin → Pentagonalize → Dual (skipped above 4,000 faces)
  3. Cube → Menger sponge → Wireframe (skipped above 6,000 faces)
  4. Buckyball → Honeycomb → Double stellate
- **Stop:** the **×** or **Esc**. It cancels the pending step, stops the turntable, removes the highlights and the pill, closes the popover, and shows a toast. The current model stays so the user can keep working. A stopped demo does not restart until the page is reloaded with `#demo`.

## Customising

- **Add or change steps:** edit the `SEQS` array. Each entry is an `async` function built from:
  - `press(selector, label, holdMs)` — highlight a button, then click it
  - `apply(label)` — press Apply if the Options popover is open
  - `setParam(id, value)` — set a slider or input and fire `input`/`change`
  - `selectEvery(n)`, `clearSelection()`, `wait(ms)`, `say(text)`
- **Timing:** the `wait(...)` values between steps (1.2–2 s) and the `hold` in `press`.
- **Turntable speed:** `yaw += 0.0025` in `turn()`.
- **Reduced motion:** the blinking dot stops under `prefers-reduced-motion`. The turntable and steps still run, because the demo is the point of the page.

## Testing checklist

- `index.html` (no hash): no pill, app behaves as before.
- `index.html#demo`: pill appears, steps run, the face count in the status bar changes after each Apply.
- Press × mid-step: the pill disappears within one step, no further buttons are pressed, the model stays.
- Press Esc: same as ×.
