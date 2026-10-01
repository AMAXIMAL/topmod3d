#!/usr/bin/env python3
"""Unified interactive explorer for all TOPMOD 3D test matrices.
Image-grid map (left) + draggable/resizable preview window with zoom magnifier.
Dropdown switches between Primitive×Operator, Pairwise sequences, and Tool×Modifier."""
import json, os
from collections import Counter, OrderedDict

BASE="/Users/ions/topmod-test"; SHOTS=os.path.join(BASE,"shots")
def load(f):
    p=os.path.join(BASE,f); return json.load(open(p)) if os.path.exists(p) else []
pxo=load("primitive_x_operator.json"); pairs=load("pairs.json")
tm=load("results.json"); ox=load("operators_extra.json")

PL={'cube':'Cube','tetrahedron':'Tetrahedron','octahedron':'Octahedron','icosahedron':'Icosahedron','dodecahedron':'Dodecahedron','cuboctahedron':'Cuboctahedron','buckyball':'Buckyball','torus':'Torus','cylinder':'Cylinder','mobius':'Möbius','spiral':'Spiral','polygon':'Polygon','rossler':'Rössler','sineWave':'Sine Wave','cymatics':'Cymatics','pseudospherical':'Pseudospherical','weave':'Weave','chainmail':'Chain mail','voronoi':'Voronoi','tree':'Tree','gem':'GEM','maze':'Maze','maze3d':'Maze 3D'}
OL={'triangulate':'Triangulate','stellate':'Stellate','doubleStellate':'Double Stellate','extrude':'Extrude','dome':'Dome','crust':'Crust','wireframe':'Wireframe','menger':'Menger','catmull':'Catmull-Clark','doosabin':'Doo-Sabin','loop':'Loop','honeycomb':'Honeycomb','dual':'Dual','truncate':'Truncate','pentagonalize':'Pentagonalize','root3':'√3 Subdivision',
 'selAll':'Select All','selInv':'Select Inverse','selSimilar':'Select Similar','selGrow':'Grow Sel','selShrink':'Shrink Sel','edgeLoop':'Edge Loop','edgeRing':'Edge Ring','faceLoop':'Face Loop','insertEdge':'Insert Edge','deleteEdge':'Delete Edge','deleteFace':'Delete Face','deleteVertex':'Delete Vertex','subdivEdge':'Subdivide Edge','fillHoles':'Fill Holes','handle':'Add Handle',
 'move':'Move','scale':'Scale','array':'Array','mirror':'Mirror','deleteSel':'Delete Selected','flipNormals':'Flip Normals','heal':'Heal','quadRemesh':'Quad Remesh','simplify':'Simplify','planarize':'Planarize','orient':'Orient','cut':'Cut','pinch':'Pinch','bridge':'Bridge','pipe':'Pipes','offset':'Offset','sliceEdges':'Seam','slab':'Paper/Slab','gyroid':'Gyroid','sierpinski':'Sierpinski','hybrid':'Hybrid','lattice':'Lattice'}
def ol(x): return OL.get(x,x)
def pl(x): return PL.get(x,x)
def base_shot(p):
    f="prim__"+p+".jpg"; return f if os.path.exists(os.path.join(SHOTS,f)) else ""
def cell(t1,t2,v,note,shot,base=""):
    return {"t1":t1,"t2":t2,"v":v,"note":note or "","shot":shot or "","base":base or ""}

matrices=OrderedDict()

# 1) Primitive x Operator
PRIMS=['cube','tetrahedron','octahedron','icosahedron','dodecahedron','cuboctahedron','buckyball','torus','cylinder','mobius','spiral','polygon','rossler','sineWave','cymatics','pseudospherical','weave','chainmail','voronoi','tree','gem','maze','maze3d']
OPS=['triangulate','stellate','doubleStellate','extrude','dome','crust','wireframe','menger','catmull','doosabin','loop','honeycomb','dual','truncate','pentagonalize','root3']
g={(r['prim'],r['op']):r for r in pxo}
cells=[[ (lambda r: cell(pl(p),ol(o),r.get('verdict','?'),r.get('note',''),r.get('shot',''),base_shot(p)))(g.get((p,o),{})) for o in OPS] for p in PRIMS]
matrices["pxo"]={"label":"Primitive × Operator (23×16)","sep":"×","rows":[pl(p) for p in PRIMS],"cols":[ol(o) for o in OPS],"cells":cells}

# 2) Pairwise sequences
PTOOLS=['triangulate','stellate','doubleStellate','extrude','dome','crust','wireframe','menger','catmull','doosabin','loop','honeycomb','dual','truncate','pentagonalize','root3']
gp={(r['A'],r['B']):r for r in pairs}
cells=[[ (lambda r: cell(ol(a),ol(b),r.get('verdict','?'),r.get('note',''),r.get('shot','')))(gp.get((a,b),{})) for b in PTOOLS] for a in PTOOLS]
matrices["pairs"]={"label":"Pairwise sequences (A → B, 16×16)","sep":"→","rows":[ol(a) for a in PTOOLS],"cols":[ol(b) for b in PTOOLS],"cells":cells}

# 3) Tool x Modifier (menubar 31 + extra 22)
MODES=['vert','edge','face']; ML={'vert':'Verts','edge':'Edges','face':'Faces'}
tmAll=tm+ox
order=[]; seen=set()
for r in tmAll:
    if r['op'] not in seen: seen.add(r['op']); order.append(r['op'])
gm={(r['op'],r['mode']):r for r in tmAll}
cells=[[ (lambda r: cell(ol(op),ML[m],r.get('verdict','?'),r.get('note',''),r.get('shot','')))(gm.get((op,m),{})) for m in MODES] for op in order]
matrices["toolmod"]={"label":"Tool × Modifier (%d ops × 3)"%len(order),"sep":"·","rows":[ol(op) for op in order],"cols":[ML[m] for m in MODES],"cells":cells}

# overview counts per matrix
def counts(m):
    c=Counter()
    for row in m["cells"]:
        for cl in row: c[cl["v"]]+=1
    return dict(c)
for k,m in matrices.items(): m["counts"]=counts(m)

MJSON=json.dumps(matrices)

HTML=r"""<!doctype html><html><head><meta charset="utf-8"><title>TOPMOD 3D — matrix explorer</title>
<style>
*{box-sizing:border-box} html,body{margin:0;height:100%;background:#0b1018;color:#cfe8ff;font:14px system-ui,sans-serif;overflow:hidden}
#bar{height:48px;display:flex;align-items:center;gap:14px;padding:0 16px;border-bottom:1px solid #1d2a38;background:#0f1620}
#bar h1{font-size:15px;margin:0;font-weight:700;white-space:nowrap}
select{background:#16273a;color:#dff;border:1px solid #2a4a63;border-radius:6px;padding:6px 8px;font-size:13px}
#ov{color:#8fb0c8;font-size:12px}.sp{flex:1}
button{background:#16273a;color:#bfe0ff;border:1px solid #244;border-radius:6px;padding:6px 10px;cursor:pointer;font-size:12px}
#hint{color:#5f86a0;font-size:11px}
#wrap{position:absolute;top:48px;left:0;right:0;bottom:0}
#map{position:absolute;inset:0;overflow:auto;padding:10px}
table{border-collapse:collapse;font-size:10px}
th{position:sticky;background:#0f1620;z-index:2;color:#8fb0c8;font-weight:600;padding:2px}
thead th{top:0;writing-mode:vertical-rl;transform:rotate(180deg);height:96px;vertical-align:bottom;white-space:nowrap;font-size:10px}
tbody th{left:0;text-align:right;padding-right:6px;white-space:nowrap;max-width:120px;overflow:hidden;text-overflow:ellipsis;font-size:11px}
td{width:52px;height:52px;padding:0;cursor:pointer;background:#000;border:3px solid #2e7d4f}
td img{width:100%;height:100%;object-fit:cover;display:block}
td.v-PASS{border-color:#2e7d4f}td.v-PASS-vis{border-color:#1f7a8c}td.v-NOEFFECT{border-color:#caa400}td.v-SKIP-heavy{border-color:#7a52c0}td.v-DESTROYS{border-color:#c06a1f}td.v-FAIL{border-color:#c0303a}td.v-UNVERIFIED{border-color:#3d6a9c}
td.sel{outline:4px solid #00e5ff;outline-offset:-1px;position:relative;z-index:1}
#legend{margin:10px 2px;font-size:11px;color:#7fa6c4}#legend span{margin-right:10px}#legend i{display:inline-block;width:10px;height:10px;border-radius:2px;margin-right:4px;vertical-align:middle}
.dot.PASS{background:#2e7d4f}.dot.PASS-vis{background:#1f7a8c}.dot.NOEFFECT{background:#caa400}.dot.SKIP-heavy{background:#7a52c0}.dot.DESTROYS{background:#c06a1f}.dot.FAIL{background:#c0303a}.dot.UNVERIFIED{background:#3d6a9c}
#win{position:fixed;left:600px;top:110px;width:460px;height:480px;min-width:240px;min-height:240px;background:#0e1722;border:1px solid #293a4d;border-radius:10px;box-shadow:0 18px 60px rgba(0,0,0,.6);display:flex;flex-direction:column;resize:both;overflow:hidden;z-index:50}
#hdr{height:34px;display:flex;align-items:center;gap:8px;padding:0 10px;cursor:move;background:linear-gradient(#16263a,#101c29);border-bottom:1px solid #243a4d;user-select:none}
#hdr .grip{color:#5f86a0}#hdr #title{font-weight:700;font-size:13px;flex:1;white-space:nowrap;overflow:hidden;text-overflow:ellipsis}#hdr .badge{font-weight:700;font-size:10px;padding:2px 8px;border-radius:20px}
#body{flex:1;display:flex;align-items:center;justify-content:center;background:#000;min-height:0;position:relative}
#big{max-width:100%;max-height:100%;object-fit:contain;display:block}
#base{position:absolute;right:8px;bottom:8px;width:84px;height:84px;object-fit:cover;border:1px solid #2a3f55;border-radius:6px;background:#000;opacity:.92}
#baseLbl{position:absolute;right:8px;bottom:94px;font-size:10px;color:#8fb0c8;background:rgba(0,0,0,.5);padding:1px 5px;border-radius:4px}
#foot{padding:8px 10px;border-top:1px solid #243a4d;background:#0e1722}
#note{color:#9fb4c8;font-family:ui-monospace,monospace;font-size:11.5px;min-height:15px}#pos{color:#5f86a0;font-size:11px;margin-top:3px}
.bg-PASS{background:#1e7a34;color:#fff}.bg-PASS-vis{background:#1f7a8c;color:#fff}.bg-NOEFFECT{background:#8a6d00;color:#fff}.bg-SKIP-heavy{background:#5b3a8a;color:#fff}.bg-DESTROYS{background:#9c5700;color:#fff}.bg-FAIL{background:#9c0006;color:#fff}.bg-UNVERIFIED{background:#3d6a9c;color:#fff}
.rsz{position:absolute;right:2px;bottom:2px;color:#456;font-size:11px;pointer-events:none}
#mag{position:absolute;width:170px;height:170px;border-radius:50%;border:2px solid #00e5ff;box-shadow:0 0 0 2px #0e1722,0 8px 24px rgba(0,0,0,.65);pointer-events:none;display:none;background-repeat:no-repeat;background-color:#000;z-index:60}
#zlabel{position:absolute;left:8px;top:8px;font-size:10px;color:#8fb0c8;background:rgba(0,0,0,.45);padding:2px 7px;border-radius:5px;z-index:5}
</style></head><body>
<div id="bar">
  <h1>TOPMOD 3D — Matrix Explorer</h1>
  <select id="pick"></select>
  <div id="ov"></div>
  <span class="sp"></span>
  <div id="hint">◀ ▲ ▼ ▶ navigate · drag header · drag corner to resize · hover render to magnify (wheel=zoom)</div>
  <button id="reset">Reset window</button>
</div>
<div id="wrap"><div id="map"></div></div>
<div id="win">
  <div id="hdr"><span class="grip">⠿</span><span id="title"></span><span class="badge" id="badge"></span></div>
  <div id="body"><span id="zlabel">🔍 hover to magnify · wheel = zoom</span><img id="big" alt=""><div id="baseLbl">base shape</div><img id="base" alt=""><div id="mag"></div><span class="rsz">⤡</span></div>
  <div id="foot"><div id="note"></div><div id="pos"></div></div>
</div>
<script>
const MATRICES=__MJSON__;
const keys=Object.keys(MATRICES);
let mk=keys[0], M=MATRICES[mk], r=0,c=0;
const big=document.getElementById('big'),base=document.getElementById('base'),baseLbl=document.getElementById('baseLbl'),title=document.getElementById('title'),badge=document.getElementById('badge'),note=document.getElementById('note'),pos=document.getElementById('pos'),ov=document.getElementById('ov'),map=document.getElementById('map');
// dropdown
const pick=document.getElementById('pick');
for(const k of keys){ const o=document.createElement('option'); o.value=k; o.textContent=MATRICES[k].label; pick.appendChild(o); }
pick.addEventListener('change',()=>{ mk=pick.value; M=MATRICES[mk]; r=0;c=0; buildGrid(); render(); });
let cells={};
function buildGrid(){
  let h='<table><thead><tr><th></th>';
  for(const col of M.cols) h+='<th>'+col+'</th>';
  h+='</tr></thead><tbody>';
  for(let i=0;i<M.rows.length;i++){ h+='<tr><th>'+M.rows[i]+'</th>';
    for(let j=0;j<M.cols.length;j++){ const d=M.cells[i][j]; const im=d.shot?'<img loading="lazy" src="shots/'+d.shot+'">':''; h+='<td class="v-'+d.v+'" data-r="'+i+'" data-c="'+j+'" title="'+M.rows[i]+' '+M.sep+' '+M.cols[j]+': '+d.v+'">'+im+'</td>'; }
    h+='</tr>'; }
  h+='</tbody></table><div id="legend"><span><i class="dot PASS"></i>PASS</span><span><i class="dot PASS-vis"></i>geom-change</span><span><i class="dot NOEFFECT"></i>no-effect</span><span><i class="dot SKIP-heavy"></i>skipped(heavy)</span><span><i class="dot DESTROYS"></i>destroys</span><span><i class="dot UNVERIFIED"></i>unverified</span><span><i class="dot FAIL"></i>fail</span></div>';
  map.innerHTML=h;
  const cc=M.counts; ov.innerHTML=Object.keys(cc).sort().map(k=>k+' '+cc[k]).join(' · ');
  cells={}; document.querySelectorAll('td[data-r]').forEach(td=>{cells[td.dataset.r+'_'+td.dataset.c]=td; td.addEventListener('click',()=>{r=+td.dataset.r;c=+td.dataset.c;render();});});
}
function render(){
  const d=M.cells[r][c];
  document.querySelectorAll('td.sel').forEach(t=>t.classList.remove('sel'));
  const cur=cells[r+'_'+c]; if(cur){cur.classList.add('sel'); cur.scrollIntoView({block:'nearest',inline:'nearest'});}
  big.src=d.shot?'shots/'+d.shot:''; big.style.visibility=d.shot?'visible':'hidden';
  if(d.base){base.src='shots/'+d.base; base.style.display='block'; baseLbl.style.display='block';} else {base.style.display='none'; baseLbl.style.display='none';}
  title.textContent=d.t1+' '+M.sep+' '+d.t2;
  badge.textContent=d.v; badge.className='badge bg-'+d.v;
  note.textContent=d.note||'';
  pos.textContent='row '+(r+1)+'/'+M.rows.length+'  ·  col '+(c+1)+'/'+M.cols.length;
}
window.addEventListener('keydown',e=>{ let hit=false;
  if(e.key==='ArrowUp'){r=(r-1+M.rows.length)%M.rows.length;hit=true;}
  else if(e.key==='ArrowDown'){r=(r+1)%M.rows.length;hit=true;}
  else if(e.key==='ArrowLeft'){c=(c-1+M.cols.length)%M.cols.length;hit=true;}
  else if(e.key==='ArrowRight'){c=(c+1)%M.cols.length;hit=true;}
  if(hit){e.preventDefault();render();}
});
const win=document.getElementById('win'),hdr=document.getElementById('hdr');
let drag=null;
hdr.addEventListener('mousedown',e=>{drag={x:e.clientX,y:e.clientY,l:win.offsetLeft,t:win.offsetTop};e.preventDefault();});
window.addEventListener('mousemove',e=>{ if(!drag)return; let nl=drag.l+(e.clientX-drag.x),nt=drag.t+(e.clientY-drag.y); nl=Math.max(0,Math.min(innerWidth-80,nl)); nt=Math.max(48,Math.min(innerHeight-40,nt)); win.style.left=nl+'px'; win.style.top=nt+'px'; });
window.addEventListener('mouseup',()=>drag=null);
document.getElementById('reset').addEventListener('click',()=>{win.style.left='600px';win.style.top='110px';win.style.width='460px';win.style.height='480px';});
const body=document.getElementById('body'),mag=document.getElementById('mag'),zlabel=document.getElementById('zlabel');
let ZOOM=2.6; const MAG=170;
function moveMag(e){ const ir=big.getBoundingClientRect();
  if(!big.src||big.style.visibility==='hidden'||e.clientX<ir.left||e.clientX>ir.right||e.clientY<ir.top||e.clientY>ir.bottom){mag.style.display='none';return;}
  const br=body.getBoundingClientRect(); const relX=e.clientX-br.left,relY=e.clientY-br.top,ix=e.clientX-ir.left,iy=e.clientY-ir.top;
  mag.style.display='block'; mag.style.left=(relX-MAG/2)+'px'; mag.style.top=(relY-MAG/2)+'px';
  mag.style.backgroundImage='url('+big.src+')'; mag.style.backgroundSize=(ir.width*ZOOM)+'px '+(ir.height*ZOOM)+'px';
  mag.style.backgroundPosition=(-(ix*ZOOM-MAG/2))+'px '+(-(iy*ZOOM-MAG/2))+'px'; }
body.addEventListener('mousemove',moveMag); body.addEventListener('mouseleave',()=>{mag.style.display='none';});
body.addEventListener('wheel',e=>{e.preventDefault();ZOOM=Math.max(1.5,Math.min(6,ZOOM+(e.deltaY<0?0.3:-0.3)));zlabel.textContent='🔍 '+ZOOM.toFixed(1)+'× · wheel = zoom';moveMag(e);},{passive:false});
buildGrid(); render();
</script></body></html>"""
HTML=HTML.replace("__MJSON__",MJSON)
out=os.path.join(BASE,"viewer_all.html"); open(out,"w").write(HTML)
print("wrote",out)
for k,m in matrices.items(): print(" ",k,m["label"],"->",m["counts"])
