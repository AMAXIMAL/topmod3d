/* TopMod ↔ Rhino, in-process. Injected by the plug-in before </body>.
   The app lives in a closure; TOPMOD.html exposes window.__tmBridge (mesh/layers/TM/rebuild/…).
   The plug-in's window polls window.__tmPoll() every 250 ms and gets back JSON:
     { out: {v, obj} | null,  cmds: ["bake"|"bakeMesh"|"import", ...], ready, why }
   and calls window.__tmApply(kind, objText) to push Rhino edits ("sync") or Rhino selections ("import") in. */
(function(){
  var outV = 0, out = null, cmds = [], applying = false, ready = false, why = "waiting for app";
  var B = null;

  function init(){
    if(ready) return;
    B = window.__tmBridge;
    if(!B){ why = "window.__tmBridge missing (TOPMOD.html not patched)"; setTimeout(init, 300); return; }
    try{ if(!B.TM || !B.layers){ why = "app not initialised"; setTimeout(init, 300); return; } }
    catch(e){ why = String(e); setTimeout(init, 300); return; }
    ready = true; why = "";
    /* Replace the app's HTTP push with an in-memory handoff; the app's own 250 ms debounce (maybeRhinoPush) stays. */
    B.setRhinoPush(function(){
      if(applying) return;
      if(!B.rhinoLink) return;
      var obj; try{ obj = B.TM.toOBJ(B.fabMesh()); }catch(e){ return; }
      out = { v: ++outV, obj: obj };
      var b = document.getElementById("rhinoLink"); if(b) b.style.borderColor = "";
    });
    try{ B.setRhinoLink(true); }catch(e){}
    addBar();
    setTimeout(demoPrompt, 900);
    window.__tmReady = true;
  }

  var lastCam = "";
  window.__tmPoll = function(){
    var cam = null;
    if(ready && B.cam){
      try{
        var c = B.cam(); var key = JSON.stringify(c);
        if(key !== lastCam){ lastCam = key; cam = c; }
      }catch(e){}
    }
    var r = JSON.stringify({ out: out, cmds: cmds, ready: ready, why: why, cam: cam });
    out = null; cmds = [];
    return r;
  };

  window.__tmApply = function(kind, objText){
    if(!ready) return "not ready";
    var m;
    try{ m = B.TM.fromOBJ(objText); }catch(e){ B.toast("Rhino: bad OBJ", true); return "bad obj"; }
    if(!m || !m.faces || !m.faces.length) return "empty";
    try{ if(typeof B.TM.validate==="function"){ var err=B.TM.validate(m); if(err) B.toast("From Rhino: "+err+" (loaded anyway)"); } }catch(e){}
    if(kind==="import"){
      applying = false;   /* the new layer must be mirrored back into the live object */
      try{ B.addLayer(m, false); B.toast("Imported from Rhino → "+B.layers[B.activeLayer].name); return "ok"; }
      catch(e){ B.toast("Rhino import failed: "+e, true); return String(e); }
    }
    /* "sync": Rhino edited the live object, which mirrors the whole visible model → it replaces the active layer. */
    applying = true;
    try{
      B.snapshot();
      var layers = B.layers, L = layers[B.activeLayer], hidden = 0, mods = 0;
      layers.forEach(function(x,i){ if(i!==B.activeLayer && x.visible!==false){ x.visible=false; hidden++; } });
      if(L && L.modifiers) L.modifiers.forEach(function(x){ if(x.on){ x.on=false; mods++; } });
      B.mesh = m;
      if(L){ L.mesh = m; L._stamp = (L._stamp||0)+1; }
      try{ B.clearCrust(); }catch(e){}
      try{ B.clearSel(false); }catch(e){}
      B.rebuild();
      try{ B.updateCrustUI(); }catch(e){}
      try{ B.renderLayerList(); }catch(e){}
      try{ B.renderModStack(); }catch(e){}
      B.toast("Updated from Rhino"+(hidden?" · "+hidden+" other layer"+(hidden>1?"s":"")+" hidden":"")+(mods?" · "+mods+" modifier"+(mods>1?"s":"")+" baked in":""));
      return "ok";
    }catch(e){ try{ B.toast("Rhino sync failed: "+e, true); }catch(_){} return String(e); }
    finally{ setTimeout(function(){ applying=false; }, 500); }
  };

  /* ---- "Run the demo?" prompt: the page is loaded with #nodemo, so the demo only runs when asked ---- */
  function demoPrompt(){
    if(!window.__demo || window.__demo.on() || document.getElementById("rhinoDemoAsk")) return;
    var o = document.createElement("div");
    o.id = "rhinoDemoAsk";
    o.style.cssText = "position:fixed;inset:0;z-index:100000;display:flex;align-items:center;justify-content:center;background:rgba(4,8,14,0.55);backdrop-filter:blur(3px)";
    var card = document.createElement("div");
    card.style.cssText = "min-width:280px;max-width:90vw;padding:22px 26px;border-radius:14px;background:rgba(12,18,26,0.96);border:1px solid rgba(0,229,255,0.45);"+
      "box-shadow:0 20px 60px rgba(0,0,0,0.5);font:14px system-ui,sans-serif;color:#dff;text-align:center";
    var h = document.createElement("div"); h.textContent = "Run the TopMod demo?"; h.style.cssText = "font-size:17px;font-weight:600;margin-bottom:6px";
    var p = document.createElement("div"); p.textContent = "A guided tour of the tools. Everything it builds appears live in Rhino."; p.style.cssText = "opacity:.75;margin-bottom:18px;font-size:13px";
    var row = document.createElement("div"); row.style.cssText = "display:flex;gap:10px;justify-content:center";
    function b(t, primary, fn){
      var x = document.createElement("button"); x.textContent = t;
      x.style.cssText = "cursor:pointer;padding:8px 22px;border-radius:8px;font:14px system-ui,sans-serif;"+
        (primary ? "background:var(--cyan,#00e5ff);color:#04222a;border:1px solid transparent;font-weight:600" : "background:rgba(0,229,255,0.1);color:#dff;border:1px solid rgba(0,229,255,0.4)");
      x.onclick = function(e){ e.preventDefault(); e.stopPropagation(); o.remove(); fn(); };
      return x;
    }
    row.appendChild(b("Yes, run it", true, function(){ try{ window.__demo.start(); }catch(e){} }));
    row.appendChild(b("No", false, function(){}));
    card.appendChild(h); card.appendChild(p); card.appendChild(row); o.appendChild(card);
    document.body.appendChild(o);
  }

  /* ---- floating bar on the page: Bake / Mesh / Import (same actions as the window toolbar) ---- */
  function addBar(){
    if(document.getElementById("rhinoBridgeBar")) return;
    var bar = document.createElement("div");
    bar.id = "rhinoBridgeBar";
    bar.style.cssText = "position:fixed;right:10px;bottom:52px;z-index:99999;display:flex;gap:6px;align-items:center;padding:6px 8px;"+
      "background:rgba(10,14,20,0.82);border:1px solid rgba(0,229,255,0.35);border-radius:10px;font:12px system-ui,sans-serif;color:#cfe;backdrop-filter:blur(6px)";
    var lbl = document.createElement("span"); lbl.textContent = "Rhino"; lbl.style.opacity = ".8";
    bar.appendChild(lbl);
    bar.appendChild(btn("Bake ▸", "Bake the live model into Rhino as NURBS ('TopMod Baked' layer)", function(){ cmds.push("bake"); B.toast("Baking into Rhino"); }));
    bar.appendChild(btn("Mesh ▸", "Bake into Rhino as a mesh", function(){ cmds.push("bakeMesh"); B.toast("Baking mesh into Rhino"); }));
    bar.appendChild(btn("◂ Import", "Pick objects in Rhino → new layer here", function(){ cmds.push("import"); B.toast("Select objects in Rhino…"); }));
    document.body.appendChild(bar);
  }
  function btn(t, tip, fn){
    var b = document.createElement("button");
    b.textContent = t; b.title = tip;
    b.style.cssText = "cursor:pointer;background:rgba(0,229,255,0.12);border:1px solid rgba(0,229,255,0.4);color:#dff;border-radius:6px;padding:3px 8px;font:12px system-ui,sans-serif";
    b.onclick = function(e){ e.preventDefault(); e.stopPropagation(); fn(); };
    return b;
  }

  if(document.readyState==="complete" || document.readyState==="interactive") init(); else document.addEventListener("DOMContentLoaded", init);
})();
