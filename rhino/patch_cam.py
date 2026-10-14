p = "/Users/ions/AMAXiMAL/projects/topmod/TOPMOD.html"
s = open(p, encoding="utf-8").read()
old = "toast:function(m,b){return toast(m,b)} };"
new = ("toast:function(m,b){return toast(m,b)},\n"
       "  cam:function(){return {p:[camera.position.x,camera.position.y,camera.position.z],t:[target.x,target.y,target.z],fov:camera.fov}} };")
if "cam:function()" in s:
    print("cam already exported")
elif old in s:
    s = s.replace(old, new, 1)
    open(p, "w", encoding="utf-8").write(s)
    print("cam export added")
else:
    raise SystemExit("bridge export anchor not found")
