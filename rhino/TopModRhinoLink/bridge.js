/* TopMod ↔ Rhino, in-process. Injected by the plug-in before </body>; TOPMOD.html itself is untouched.
   The plug-in's window polls window.__tmPoll() every 250 ms and gets back JSON:
     { out: {v, obj} | null,  cmds: ["bake"|"bakeMesh"|"import", ...] }
   and calls window.__tmApply(kind, objText) to push Rhino edits ("sync") or Rhino selections ("import") in. */
(function(){
  var outV = 0, out = null, cmds = [], applying = false, ready = false, pushT = null;

  function appReady(){
    return typeof rebuild==="function" && typeof TM!=="undefined" && typeof layers!=="undefined" &&
           typeof setRhinoLink==="function" && typeof snapshot==="function" && typeof fabMesh==="function";
  }

  function init(){
    if(ready) return;
    if(!appReady()){ setTimeout(init, 300); return; }
    ready = true;
    /* Replace the app's HTTP push with an in-memory handoff; the app's own debounce (maybeRhinoPush) stays. */
    rhinoPush = function(){
      if(applying) return;
      if(typeof rhinoLink!=="undefined" && !rhinoLink) return;
      var obj; try{ obj = TM.toOBJ(fabMesh()); }catch(e){ return; }
      out = { v: ++outV, obj: obj };
      var b = document.getElementById("rhinoLink"); if(b) b.style.borderColor = "";
    };
    try{ rhinoURL = "rhino://in-process"; }catch(e){}
    try{ setRhinoLink(true); }catch(e){}
    addBar();
    window.__tmReady = true;
  }

  window.__tmPoll = function(){
    var r = JSON.stringify({ out: out, cmds: cmds, ready: ready });
    out = null; cmds = [];
    return r;
  };

  window.__tmApply = function(kind, objText){
    var m;
    try{ m = TM.fromOBJ(objText); }catch(e){ toast&&toast("Rhino: bad OBJ", true); return "bad obj"; }
    if(!m || !m.faces || !m.faces.length) return "empty";
    try{ if(typeof TM.validate==="function"){ var err=TM.validate(m); if(err&&typeof toast==="function") toast("From Rhino: "+err+" (loaded anyway)"); } }catch(e){}
    if(kind==="import"){
      applying = false;   /* the new layer must be mirrored back into the live object */
      try{ addLayer(m, false); if(typeof toast==="function") toast("Imported from Rhino → "+layers[activeLayer].name); return "ok"; }
      catch(e){ if(typeof toast==="function") toast("Rhino import failed: "+e, true); return String(e); }
    }
    /* "sync": Rhino edited the live object, which mirrors the whole visible model → it replaces the active layer. */
    applying = true;
    try{
      snapshot();
      var L = layers[activeLayer], hidden = 0, mods = 0;
      layers.forEach(function(x,i){ if(i!==activeLayer && x.visible!==false){ x.visible=false; hidden++; } });
      if(L && L.modifiers) L.modifiers.forEach(function(x){ if(x.on){ x.on=false; mods++; } });
      mesh = m;
      if(L){ L.mesh = m; L._stamp = (L._stamp||0)+1; }
      try{ crustPairs = null; }catch(e){}
      if(typeof clearSel==="function") clearSel(false);
      rebuild();
      if(typeof updateCrustUI==="function") updateCrustUI();
      if(typeof renderLayerList==="function") renderLayerList();
      if(typeof renderModStack==="function") renderModStack();
      if(typeof toast==="function") toast("Updated from Rhino"+(hidden?" · "+hidden+" other layer"+(hidden>1?"s":"")+" hidden":"")+(mods?" · "+mods+" modifier"+(mods>1?"s":"")+" baked in":""));
      return "ok";
    }catch(e){ if(typeof toast==="function") toast("Rhino sync failed: "+e, true); return String(e); }
    finally{ setTimeout(function(){ applying=false; }, 500); }
  };

  /* ---- floating bar on the page: Bake / Mesh / Import (same actions as the window toolbar) ---- */
  function addBar(){
    if(document.getElementById("rhinoBridgeBar")) return;
    var bar = document.createElement("div");
    bar.id = "rhinoBridgeBar";
    bar.style.cssText = "position:fixed;right:10px;bottom:52px;z-index:99999;display:flex;gap:6px;align-items:center;padding:6px 8px;"+
      "background:rgba(10,14,20,0.82);border:1px solid rgba(0,229,255,0.35);border-radius:10px;font:12px system-ui,sans-serif;color:#cfe;backdrop-filter:blur(6px)";
    var lbl = document.createElement("span"); lbl.textContent = "Rhino"; lbl.style.opacity = ".8";
    bar.appendChild(lbl);
    bar.appendChild(btn("Bake ▸", "Bake the live model into Rhino as NURBS ('TopMod Baked' layer)", function(){ cmds.push("bake"); toast&&toast("Baking into Rhino"); }));
    bar.appendChild(btn("Mesh ▸", "Bake into Rhino as a mesh", function(){ cmds.push("bakeMesh"); toast&&toast("Baking mesh into Rhino"); }));
    bar.appendChild(btn("◂ Import", "Pick objects in Rhino → new layer here", function(){ cmds.push("import"); toast&&toast("Select objects in Rhino…"); }));
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
