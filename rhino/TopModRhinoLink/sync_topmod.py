#!/usr/bin/env python3
"""
Refresh the plug-in's own copy of the TopMod app.

    python3 sync_topmod.py            # master -> ./TOPMOD.html, hooks applied
    python3 sync_topmod.py --check    # only verify ./TOPMOD.html has the hooks

The master TOPMOD.html (~/AMAXiMAL/projects/topmod/TOPMOD.html) knows nothing about Rhino. The
plug-in needs a few handles into the app's closure (mesh, layers, fabrication data, camera…), so this
script copies the master next to the project and inserts a `window.__tmBridge` export. The copy is what
gets embedded in the .rhp — the master can change freely without breaking the plug-in; run this to pick
up a new master.
"""
import os, sys, shutil

HERE = os.path.dirname(os.path.abspath(__file__))
DST = os.path.join(HERE, "TOPMOD.html")
MASTER = os.environ.get("TOPMOD_MASTER", os.path.expanduser("~/AMAXiMAL/projects/topmod/TOPMOD.html"))

ANCHOR = "function setRhinoLink(on){"
EXPORT = '''/* Rhino plug-in bridge: the app runs inside a closure, so expose what the in-process link needs */
window.__tmBridge={ get mesh(){return mesh}, set mesh(v){mesh=v}, get layers(){return layers}, get activeLayer(){return activeLayer},
  get rhinoLink(){return rhinoLink}, get TM(){return TM}, fabMesh:function(){return fabMesh()}, rebuild:function(o){return rebuild(o)},
  snapshot:function(){return snapshot()}, addLayer:function(m,b){return addLayer(m,b)}, clearSel:function(r){return clearSel(r)},
  updateCrustUI:function(){return updateCrustUI()}, renderLayerList:function(){return renderLayerList()}, renderModStack:function(){return renderModStack()},
  setRhinoLink:function(on){return setRhinoLink(on)}, setRhinoPush:function(f){rhinoPush=f}, clearCrust:function(){crustPairs=null}, toast:function(m,b){return toast(m,b)},
  cam:function(){return {p:[camera.position.x,camera.position.y,camera.position.z],t:[target.x,target.y,target.z],fov:camera.fov,a:camera.aspect}},
  get fabActive(){return !!fabActive},
  get fabLive(){try{return !!((slabData&&slabData.count)||liveNet)}catch(e){return false}},
  fabMode:function(){try{return plyMode()}catch(e){return null}},
  slabPairs:function(){try{return slabFlattenPairs()}catch(e){return []}},
  netMesh:function(){try{ if(!liveNet) return null; const L=layers.find(x=>x.id===liveNet.netId); return (L&&L.mesh)?TM.toOBJ(L.mesh,"net"):null; }catch(e){return null}},
  /* Field lines poles <-> Rhino points: world (model) coords, Y-up inches; [x,y,z,q] per pole (q>0 north) */
  _fieldSrc:function(){ const G=NEWGEN.ngField, FK=NG_MOD_KEY("ngField");
    if(ngActive==="ngField"&&G.poles) return {id:"tool",P:G.poles,f:null,md:null,flat:!!(document.getElementById("ng_dim")&&document.getElementById("ng_dim").value==="2d")};
    const L=layers[activeLayer]; if(!L||!L.modifiers) return null; let md=null;
    if(modEditId!=null) md=L.modifiers.find(x=>x.id===modEditId&&x.type===FK&&x.params&&x.params.poleData);
    if(!md) md=L.modifiers.find(x=>x.type===FK&&x.on!==false&&x.params&&x.params.poleData);
    return md?{id:"mod"+md.id,P:md.params.poleData,f:md.params._fit||null,md,flat:(md.params.dim===1||md.params.dim==="1"||md.params.dim==="2d")}:null; },
  fieldPoles:function(){ try{ const S=this._fieldSrc(); if(!S||!S.P||!S.P.length) return null; const f=S.f;
    return {id:S.id,pts:S.P.map(p=>{ const c=f?[(p.c[0]-f.gc[0])*f.k+f.mc[0],(p.c[1]-f.gc[1])*f.k+f.mc[1],(p.c[2]-f.gc[2])*f.k+f.mc[2]]:p.c; return [c[0],c[1],c[2],p.q]; })}; }catch(e){ return null; } },
  setFieldPoles:function(id,pts){ try{ const S=this._fieldSrc(); if(!S||S.id!==id||!S.P||S.P.length!==pts.length) return "stale"; const f=S.f;
    if(S.md) try{ snapshot(); }catch(e){}
    S.P.forEach((p,i)=>{ const w=pts[i]; const r=f?[(w[0]-f.mc[0])/f.k+f.gc[0],(w[1]-f.mc[1])/f.k+f.gc[1],(w[2]-f.mc[2])/f.k+f.gc[2]]:[w[0],w[1],w[2]]; p.c=[r[0],r[1],S.flat?0:r[2]]; });
    if(S.md){ rebuild(); } else { ngCompute(); }
    setTimeout(function(){ try{ window.__fieldHandles&&window.__fieldHandles(); }catch(e){} },0); return "ok"; }catch(e){ return String(e); } } };
'''
PUSH_OLD = "_rhinoT=setTimeout(rhinoPush,250)"
PUSH_NEW = "_rhinoT=setTimeout(rhinoPush,window.__tmPushDelay||250)"

# names the export block relies on inside the app's closure; a missing one means the master changed shape
NEEDED = ["function setRhinoLink(on){", "function rhinoPush(", "function fabMesh(", "function rebuild(", "function snapshot(",
          "function addLayer(", "function clearSel(", "function updateCrustUI(", "function renderLayerList(", "function renderModStack(",
          "crustPairs", "function toast(", "camera.position", "slabFlattenPairs", "liveNet", "plyMode", "fabActive", "slabData",
          "const NEWGEN=", "const NG_MOD_KEY=", "let modEditId", "function ngCompute(", "let ngActive"]


def apply(s):
    if "__tmBridge" in s:
        return s, "already has bridge hooks"
    missing = [n for n in NEEDED if n not in s]
    if missing:
        raise SystemExit("master has changed; these anchors are missing: " + ", ".join(missing))
    i = s.index(ANCHOR)
    s = s[:i] + EXPORT + s[i:]
    if PUSH_NEW not in s:
        if PUSH_OLD not in s:
            raise SystemExit("push debounce anchor not found: " + PUSH_OLD)
        s = s.replace(PUSH_OLD, PUSH_NEW, 1)
    return s, "bridge hooks applied"


def main():
    if "--check" in sys.argv:
        ok = os.path.exists(DST) and "__tmBridge" in open(DST, encoding="utf-8").read()
        print("TOPMOD.html: " + ("ok" if ok else "MISSING bridge hooks — run sync_topmod.py"))
        sys.exit(0 if ok else 1)
    src = sys.argv[1] if len(sys.argv) > 1 and not sys.argv[1].startswith("-") else MASTER
    if not os.path.exists(src):
        raise SystemExit("master not found: " + src)
    s = open(src, encoding="utf-8").read()
    s, msg = apply(s)
    if os.path.exists(DST):
        shutil.copy(DST, DST + ".bak")
    open(DST, "w", encoding="utf-8").write(s)
    print(f"{os.path.relpath(DST, HERE)}: {msg} (from {src}, {len(s):,} bytes)")


if __name__ == "__main__":
    main()
