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
  netMesh:function(){try{ if(!liveNet) return null; const L=layers.find(x=>x.id===liveNet.netId); return (L&&L.mesh)?TM.toOBJ(L.mesh,"net"):null; }catch(e){return null}} };
'''
PUSH_OLD = "_rhinoT=setTimeout(rhinoPush,250)"
PUSH_NEW = "_rhinoT=setTimeout(rhinoPush,window.__tmPushDelay||250)"

# names the export block relies on inside the app's closure; a missing one means the master changed shape
NEEDED = ["function setRhinoLink(on){", "function rhinoPush(", "function fabMesh(", "function rebuild(", "function snapshot(",
          "function addLayer(", "function clearSel(", "function updateCrustUI(", "function renderLayerList(", "function renderModStack(",
          "crustPairs", "function toast(", "camera.position", "slabFlattenPairs", "liveNet", "plyMode", "fabActive", "slabData"]


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
