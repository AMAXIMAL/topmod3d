import sys, shutil
p = "/Users/ions/AMAXiMAL/projects/topmod/TOPMOD.html"
s = open(p, encoding="utf-8").read()
if "__tmBridge" in s:
    print("already patched"); sys.exit(0)
shutil.copy(p, p.replace(".html", ".pre-bridge.bak"))
anchor = "function setRhinoLink(on){"
i = s.index(anchor)
exp = '''/* Rhino plug-in bridge: the app runs inside a closure, so expose what the in-process link needs */
window.__tmBridge={ get mesh(){return mesh}, set mesh(v){mesh=v}, get layers(){return layers}, get activeLayer(){return activeLayer},
  get rhinoLink(){return rhinoLink}, get TM(){return TM}, fabMesh:function(){return fabMesh()}, rebuild:function(o){return rebuild(o)},
  snapshot:function(){return snapshot()}, addLayer:function(m,b){return addLayer(m,b)}, clearSel:function(r){return clearSel(r)},
  updateCrustUI:function(){return updateCrustUI()}, renderLayerList:function(){return renderLayerList()}, renderModStack:function(){return renderModStack()},
  setRhinoLink:function(on){return setRhinoLink(on)}, setRhinoPush:function(f){rhinoPush=f}, clearCrust:function(){crustPairs=null}, toast:function(m,b){return toast(m,b)} };
'''
s = s[:i] + exp + s[i:]
open(p, "w", encoding="utf-8").write(s)
print("patched")
