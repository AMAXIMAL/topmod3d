p = "/Users/ions/AMAXiMAL/projects/topmod/TOPMOD.html"
s = open(p, encoding="utf-8").read()
old = "  get fabActive(){return !!fabActive}, fabMode:function(){try{return plyMode()}catch(e){return null}},"
new = ("  get fabActive(){return !!fabActive},\n"
       "  get fabLive(){try{return !!((slabData&&slabData.count)||liveNet)}catch(e){return false}},\n"
       "  fabMode:function(){try{return plyMode()}catch(e){return null}},")
if "get fabLive()" in s:
    print("fabLive already present")
elif old in s:
    s = s.replace(old, new, 1); open(p, "w", encoding="utf-8").write(s); print("fabLive added")
else:
    raise SystemExit("anchor not found")
# bridge.js: trigger on fabLive too
b = "/Users/ions/AMAXiMAL/projects/topmod/rhino/TopModRhinoLink/bridge.js"
t = open(b, encoding="utf-8").read()
t2 = t.replace("function fabOn(){ try{ return !!B.fabActive; }catch(e){ return false; } }",
               "function fabOn(){ try{ return !!B.fabActive || !!B.fabLive; }catch(e){ return false; } }")
open(b, "w", encoding="utf-8").write(t2); print("bridge patched" if t2 != t else "bridge already patched")
