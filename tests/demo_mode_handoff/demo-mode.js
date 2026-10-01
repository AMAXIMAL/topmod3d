// TopMod 3D — Demo mode.
// Paste INSIDE the TopModJS app IIFE, just before its final "})();",
// so it can reach the app's internal state (mesh, sel, yaw, applyCam, closeToolPop, ...).
// Starts only when the URL contains "demo" in the hash or query (index.html#demo or ?demo).

(function(){
  if (!/demo/i.test(location.hash + location.search)) return;
  let alive = true, timer = null, spin = null;
  const q = s => document.querySelector(s);
  const pill = document.createElement("div");
  pill.id = "amxDemo"; pill.setAttribute("role","status");
  pill.innerHTML = '<span class="dot"></span><b>Demo mode on</b><span class="step">Starting…</span><button type="button" aria-label="Stop and close demo mode" title="Stop demo">&times;</button>';
  const say = t => { const s = pill.querySelector(".step"); if (s) s.textContent = t; };
  const wait = ms => new Promise((ok, no) => { timer = setTimeout(() => alive ? ok() : no("stopped"), ms); });
  function stop(){
    if (!alive) return; alive = false; clearTimeout(timer); cancelAnimationFrame(spin);
    document.querySelectorAll(".amxPulse").forEach(el => el.classList.remove("amxPulse"));
    try { typeof closeToolPop === "function" && closeToolPop(); } catch(e){}
    pill.remove();
    try { toast && toast("Demo stopped — your turn. Pick a primitive or a tool."); } catch(e){}
  }
  pill.querySelector("button").addEventListener("click", stop);
  addEventListener("keydown", e => { if (e.key === "Escape" && alive) stop(); });

  // slow turntable while the demo runs
  function turn(){ if (!alive) return; try { yaw += 0.0025; applyCam(); } catch(e){} spin = requestAnimationFrame(turn); }

  async function press(sel, label, hold = 650){
    const b = q(sel); if (!b) return false;
    say(label);
    try { b.scrollIntoView({ block:"nearest", inline:"nearest" }); } catch(e){}
    b.classList.add("amxPulse"); await wait(hold); b.classList.remove("amxPulse");
    if (!alive) return false;
    b.click(); return true;
  }
  async function apply(label){
    await wait(900);
    const pop = q("#toolpop"), ap = q("#tpApply");
    if (pop && !pop.classList.contains("hiddenPanel") && ap && !pop.classList.contains("empty")) {
      if (label) say(label);
      ap.classList.add("amxPulse"); await wait(600); ap.classList.remove("amxPulse");
      if (!alive) return; ap.click();
    }
    await wait(250);
    try { closeToolPop(); } catch(e){}
  }
  function setParam(id, v){ const el = q("#" + id); if (!el) return; el.value = v; el.dispatchEvent(new Event("input", { bubbles:true })); el.dispatchEvent(new Event("change", { bubbles:true })); }
  function faces(){ try { return mesh.faces.length; } catch(e){ return 0; } }
  function selectEvery(n){
    try { setMode("face"); mesh.faces.forEach((_, i) => { if (i % n === 0) sel.faces.add(i); }); rebuildOverlay(); updateHint && updateHint(); } catch(e){}
  }
  function clearSelection(){ try { clearSel(); } catch(e){} }
  async function frame(){ try { const it = q('#menubar [data-act="frameAll"]'); it && it.click(); } catch(e){} }

  const SEQS = [
    async () => {
      await press('#toolbar [data-p="dodecahedron"]', "Primitive · Dodecahedron"); await apply();
      await wait(1400);
      await press('#toolbar [data-op="catmull"]', "Remesh · Catmull-Clark"); setParam("pIters", 1); await apply("Catmull-Clark × 1");
      await wait(1400);
      say("Select faces · every third face"); selectEvery(3); await wait(1300);
      await press('#toolbar [data-op="extrude"]', "Extrude selected faces"); await apply("Extrude · apply");
      clearSelection(); await wait(1400);
      await press('#toolbar [data-op="stellate"]', "Stellate every face"); await apply("Stellate · apply");
      await wait(1800);
    },
    async () => {
      await press('#toolbar [data-p="torus"]', "Primitive · Torus"); await apply();
      await wait(1400);
      await press('#toolbar [data-op="doosabin"]', "Remesh · Doo-Sabin"); await apply("Doo-Sabin · apply");
      await wait(1400);
      await press('#toolbar [data-op="pentagonalize"]', "Remesh · Pentagonalize"); await apply("Pentagonalize · apply");
      await wait(1400);
      if (faces() < 4000) { await press('#toolbar [data-op="dual"]', "Remesh · Dual"); await apply("Dual · apply"); await wait(1800); }
    },
    async () => {
      await press('#toolbar [data-p="cube"]', "Primitive · Cube"); await apply();
      await wait(1200);
      await press('#toolbar [data-op="menger"]', "Menger sponge"); await apply("Menger · apply");
      await wait(1800);
      if (faces() < 6000) { await press('#toolbar [data-op="wireframe"]', "Wireframe modeling"); await apply("Wireframe · apply"); await wait(2000); }
    },
    async () => {
      await press('#toolbar [data-p="buckyball"]', "Primitive · Buckyball"); await apply();
      await wait(1400);
      await press('#toolbar [data-op="honeycomb"]', "Remesh · Honeycomb"); await apply("Honeycomb · apply");
      await wait(1400);
      await press('#toolbar [data-op="doubleStellate"]', "Double stellate"); await apply("Double stellate · apply");
      await wait(2000);
    }
  ];

  async function run(){
    await wait(1200);
    turn();
    for (let k = 0; alive; k = (k + 1) % SEQS.length) {
      try { await SEQS[k](); await frame(); } catch(e){ if (e === "stopped") return; }
    }
  }
  function boot(){ document.body.appendChild(pill); run().catch(() => {}); }
  if (document.readyState === "complete") boot(); else addEventListener("load", boot);
})();
