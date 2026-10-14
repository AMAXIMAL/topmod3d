p = "/Users/ions/AMAXiMAL/projects/topmod/TOPMOD.html"
s = open(p, encoding="utf-8").read()
changed = False
old = "cam:function(){return {p:[camera.position.x,camera.position.y,camera.position.z],t:[target.x,target.y,target.z],fov:camera.fov}} };"
new = ("cam:function(){return {p:[camera.position.x,camera.position.y,camera.position.z],t:[target.x,target.y,target.z],fov:camera.fov}},\n"
       "  get fabActive(){return !!fabActive}, fabMode:function(){try{return plyMode()}catch(e){return null}},\n"
       "  slabPairs:function(){try{return slabFlattenPairs()}catch(e){return []}},\n"
       "  netMesh:function(){try{ if(!liveNet) return null; const L=layers.find(x=>x.id===liveNet.netId); return (L&&L.mesh)?TM.toOBJ(L.mesh,\"net\"):null; }catch(e){return null}} };")
if "fabActive(){return !!fabActive}" in s:
    print("fab export already present")
elif old in s:
    s = s.replace(old, new, 1); changed = True; print("fab export added")
else:
    raise SystemExit("cam export anchor not found")
old2 = "_rhinoT=setTimeout(rhinoPush,250)"
new2 = "_rhinoT=setTimeout(rhinoPush,window.__tmPushDelay||250)"
if new2 in s:
    print("push delay hook already present")
elif old2 in s:
    s = s.replace(old2, new2, 1); changed = True; print("push delay hook added")
else:
    raise SystemExit("push debounce anchor not found")
if changed:
    open(p, "w", encoding="utf-8").write(s)
