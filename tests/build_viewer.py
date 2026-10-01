#!/usr/bin/env python3
"""Interactive arrow-key grid viewer for the Primitive x Operator matrix.
Layout: grid map + overview on the LEFT, a draggable/resizable floating preview window."""
import json, os
from collections import Counter

BASE="/Users/ions/topmod-test"
res=json.load(open(os.path.join(BASE,"primitive_x_operator.json")))
PRIMS=['cube','tetrahedron','octahedron','icosahedron','dodecahedron','cuboctahedron','buckyball','torus','cylinder','mobius','spiral','polygon','rossler','sineWave','cymatics','pseudospherical','weave','chainmail','voronoi','tree','gem','maze','maze3d']
OPS=['triangulate','stellate','doubleStellate','extrude','dome','crust','wireframe','menger','catmull','doosabin','loop','honeycomb','dual','truncate','pentagonalize','root3']
PL={'cube':'Cube','tetrahedron':'Tetrahedron','octahedron':'Octahedron','icosahedron':'Icosahedron','dodecahedron':'Dodecahedron','cuboctahedron':'Cuboctahedron','buckyball':'Buckyball','torus':'Torus','cylinder':'Cylinder','mobius':'Möbius','spiral':'Spiral','polygon':'Polygon','rossler':'Rössler','sineWave':'Sine Wave','cymatics':'Cymatics','pseudospherical':'Pseudospherical','weave':'Weave','chainmail':'Chain mail','voronoi':'Voronoi','tree':'Tree','gem':'GEM','maze':'Maze','maze3d':'Maze 3D'}
OL={'triangulate':'Triangulate','stellate':'Stellate','doubleStellate':'Double Stellate','extrude':'Extrude','dome':'Dome','crust':'Crust','wireframe':'Wireframe','menger':'Menger','catmull':'Catmull-Clark','doosabin':'Doo-Sabin','loop':'Loop','honeycomb':'Honeycomb','dual':'Dual','truncate':'Truncate','pentagonalize':'Pentagonalize','root3':'√3 Subdivision'}
grid={(r['prim'],r['op']):r for r in res}
# base (original) shape image per primitive, if available
def base_shot(p):
    f="prim__"+p+".jpg"
    return f if os.path.exists(os.path.join(BASE,"shots",f)) else ""
data=[]
for p in PRIMS:
    row=[]
    for op in OPS:
        r=grid.get((p,op),{})
        row.append({"prim":PL[p],"op":OL[op],"v":r.get("verdict","?"),"note":r.get("note",""),
                    "shot":r.get("shot",""),"base":base_shot(p)})
    data.append(row)

C=Counter(r.get("verdict") for r in res)
OVERVIEW=f"{len(res)} combos &nbsp;·&nbsp; PASS {C.get('PASS',0)} &nbsp;·&nbsp; no-effect {C.get('NOEFFECT',0)} &nbsp;·&nbsp; skipped-heavy {C.get('SKIP-heavy',0)} &nbsp;·&nbsp; destroys {C.get('DESTROYS',0)} &nbsp;·&nbsp; fail {C.get('FAIL',0)}"

DATA=json.dumps(data); ROWS=json.dumps([PL[p] for p in PRIMS]); COLS=json.dumps([OL[op] for op in OPS])

HTML=r"""<!doctype html><html><head><meta charset="utf-8"><title>TOPMOD 3D — grid viewer</title>
<style>
*{box-sizing:border-box} html,body{margin:0;height:100%;background:#0b1018;color:#cfe8ff;font:14px system-ui,sans-serif;overflow:hidden}
#bar{height:46px;display:flex;align-items:center;gap:16px;padding:0 16px;border-bottom:1px solid #1d2a38;background:#0f1620}
#bar h1{font-size:15px;margin:0;font-weight:700}#bar #ov{color:#8fb0c8;font-size:12px}
#bar .sp{flex:1}#bar button{background:#16273a;color:#bfe0ff;border:1px solid #244;border-radius:6px;padding:5px 10px;cursor:pointer;font-size:12px}
#wrap{position:absolute;top:46px;left:0;right:0;bottom:0}
/* LEFT: map */
#map{position:absolute;top:0;left:0;bottom:0;width:100%;overflow:auto;padding:10px}
table{border-collapse:collapse;font-size:10px}
th{position:sticky;background:#0f1620;z-index:2;color:#8fb0c8;font-weight:600;padding:2px}
thead th{top:0;writing-mode:vertical-rl;transform:rotate(180deg);height:90px;vertical-align:bottom;white-space:nowrap;font-size:10px}
tbody th{left:0;text-align:right;padding-right:6px;white-space:nowrap;max-width:96px;overflow:hidden;text-overflow:ellipsis;font-size:11px}
td{width:52px;height:52px;padding:0;cursor:pointer;background:#000;border:3px solid #2e7d4f}
td img{width:100%;height:100%;object-fit:cover;display:block}
td.v-PASS{border-color:#2e7d4f}td.v-NOEFFECT{border-color:#caa400}td.v-SKIP-heavy{border-color:#7a52c0}td.v-DESTROYS{border-color:#c06a1f}td.v-FAIL{border-color:#c0303a}
td.sel{outline:4px solid #00e5ff;outline-offset:-1px;position:relative;z-index:1}
.dot.PASS{background:#2e7d4f}.dot.NOEFFECT{background:#caa400}.dot.SKIP-heavy{background:#7a52c0}.dot.DESTROYS{background:#c06a1f}.dot.FAIL{background:#c0303a}
#legend{margin:10px 2px;font-size:11px;color:#7fa6c4}
#legend span{margin-right:10px}#legend i{display:inline-block;width:10px;height:10px;border-radius:2px;margin-right:4px;vertical-align:middle}
/* FLOATING preview window */
#win{position:fixed;left:560px;top:120px;width:440px;height:460px;min-width:240px;min-height:240px;
  background:#0e1722;border:1px solid #293a4d;border-radius:10px;box-shadow:0 18px 60px rgba(0,0,0,.6);
  display:flex;flex-direction:column;resize:both;overflow:hidden;z-index:50}
#hdr{height:34px;display:flex;align-items:center;gap:8px;padding:0 10px;cursor:move;background:linear-gradient(#16263a,#101c29);
  border-bottom:1px solid #243span;border-bottom:1px solid #243a4d;user-select:none}
#hdr .grip{color:#5f86a0}#hdr #title{font-weight:700;font-size:13px;flex:1;white-space:nowrap;overflow:hidden;text-overflow:ellipsis}
#hdr .badge{font-weight:700;font-size:10px;padding:2px 8px;border-radius:20px}
#body{flex:1;display:flex;align-items:center;justify-content:center;background:#000;min-height:0;position:relative}
#big{max-width:100%;max-height:100%;object-fit:contain;display:block}
#base{position:absolute;right:8px;bottom:8px;width:84px;height:84px;object-fit:cover;border:1px solid #2a3f55;border-radius:6px;background:#000;opacity:.92}
#baseLbl{position:absolute;right:8px;bottom:94px;font-size:10px;color:#8fb0c8;background:rgba(0,0,0,.5);padding:1px 5px;border-radius:4px}
#foot{padding:8px 10px;border-top:1px solid #243a4d;background:#0e1722}
#note{color:#9fb4c8;font-family:ui-monospace,monospace;font-size:11.5px;min-height:15px}
#pos{color:#5f86a0;font-size:11px;margin-top:3px}
.bg-PASS{background:#1e7a34;color:#fff}.bg-NOEFFECT{background:#8a6d00;color:#fff}.bg-SKIP-heavy{background:#5b3a8a;color:#fff}.bg-DESTROYS{background:#9c5700;color:#fff}.bg-FAIL{background:#9c0006;color:#fff}
.rsz{position:absolute;right:2px;bottom:2px;color:#456;font-size:11px;pointer-events:none}
#mag{position:absolute;width:170px;height:170px;border-radius:50%;border:2px solid #00e5ff;
  box-shadow:0 0 0 2px #0e1722,0 8px 24px rgba(0,0,0,.65);pointer-events:none;display:none;
  background-repeat:no-repeat;background-color:#000;z-index:60}
#zlabel{position:absolute;left:8px;top:8px;font-size:10px;color:#8fb0c8;background:rgba(0,0,0,.45);padding:2px 7px;border-radius:5px;z-index:5}
</style></head><body>
<div id="bar">
  <h1>TOPMOD 3D — Primitive × Operator</h1>
  <div id="ov">__OVERVIEW__</div>
  <span class="sp"></span>
  <div id="ov" style="color:#5f86a0">◀ ▲ ▼ ▶ navigate · drag header to move · drag corner to resize</div>
  <button id="reset">Reset window</button>
</div>
<div id="wrap"><div id="map"></div></div>

<div id="win">
  <div id="hdr"><span class="grip">⠿</span><span id="title"></span><span class="badge" id="badge"></span></div>
  <div id="body"><span id="zlabel">🔍 hover to magnify · wheel = zoom</span><img id="big" alt=""><div id="baseLbl">base shape</div><img id="base" alt=""><div id="mag"></div><span class="rsz">⤡</span></div>
  <div id="foot"><div id="note"></div><div id="pos"></div></div>
</div>

<script>
const DATA=__DATA__, ROWS=__ROWS__, COLS=__COLS__;
let r=0,c=0;
const big=document.getElementById('big'),base=document.getElementById('base'),baseLbl=document.getElementById('baseLbl'),
 title=document.getElementById('title'),badge=document.getElementById('badge'),note=document.getElementById('note'),pos=document.getElementById('pos');
// build grid
const map=document.getElementById('map');
let h='<table><thead><tr><th></th>';
for(const col of COLS) h+='<th>'+col+'</th>';
h+='</tr></thead><tbody>';
for(let i=0;i<ROWS.length;i++){ h+='<tr><th>'+ROWS[i]+'</th>';
  for(let j=0;j<COLS.length;j++){ const d=DATA[i][j]; const im=d.shot?'<img loading="lazy" src="shots/'+d.shot+'">':''; h+='<td class="v-'+d.v+'" data-r="'+i+'" data-c="'+j+'" title="'+ROWS[i]+' × '+COLS[j]+': '+d.v+'">'+im+'</td>'; }
  h+='</tr>'; }
h+='</tbody></table><div id="legend"><span><i class="dot PASS"></i>PASS</span><span><i class="dot NOEFFECT"></i>no-effect</span><span><i class="dot SKIP-heavy"></i>skipped (heavy)</span><span><i class="dot DESTROYS"></i>destroys</span><span><i class="dot FAIL"></i>fail</span></div>';
map.innerHTML=h;
const cells={}; document.querySelectorAll('td[data-r]').forEach(td=>{cells[td.dataset.r+'_'+td.dataset.c]=td; td.addEventListener('click',()=>{r=+td.dataset.r;c=+td.dataset.c;render();});});
function render(){
  const d=DATA[r][c];
  document.querySelectorAll('td.sel').forEach(t=>t.classList.remove('sel'));
  const cur=cells[r+'_'+c]; if(cur){cur.classList.add('sel'); cur.scrollIntoView({block:'nearest',inline:'nearest'});}
  big.src=d.shot?'shots/'+d.shot:''; big.style.visibility=d.shot?'visible':'hidden';
  if(d.base){base.src='shots/'+d.base; base.style.display='block'; baseLbl.style.display='block';} else {base.style.display='none'; baseLbl.style.display='none';}
  title.textContent=d.prim+' × '+d.op;
  badge.textContent=d.v; badge.className='badge bg-'+d.v;
  note.textContent=d.note||'';
  pos.textContent='row '+(r+1)+'/'+ROWS.length+'  ·  col '+(c+1)+'/'+COLS.length;
}
window.addEventListener('keydown',e=>{ let hit=false;
  if(e.key==='ArrowUp'){r=(r-1+ROWS.length)%ROWS.length;hit=true;}
  else if(e.key==='ArrowDown'){r=(r+1)%ROWS.length;hit=true;}
  else if(e.key==='ArrowLeft'){c=(c-1+COLS.length)%COLS.length;hit=true;}
  else if(e.key==='ArrowRight'){c=(c+1)%COLS.length;hit=true;}
  if(hit){e.preventDefault();render();}
});
// draggable window
const win=document.getElementById('win'),hdr=document.getElementById('hdr');
let drag=null;
hdr.addEventListener('mousedown',e=>{drag={x:e.clientX,y:e.clientY,l:win.offsetLeft,t:win.offsetTop};e.preventDefault();});
window.addEventListener('mousemove',e=>{ if(!drag)return;
  let nl=drag.l+(e.clientX-drag.x), nt=drag.t+(e.clientY-drag.y);
  nl=Math.max(0,Math.min(window.innerWidth-80,nl)); nt=Math.max(46,Math.min(window.innerHeight-40,nt));
  win.style.left=nl+'px'; win.style.top=nt+'px'; });
window.addEventListener('mouseup',()=>drag=null);
document.getElementById('reset').addEventListener('click',()=>{win.style.left='560px';win.style.top='120px';win.style.width='440px';win.style.height='460px';});
// zoom magnifier over the preview image
const body=document.getElementById('body'),mag=document.getElementById('mag'),zlabel=document.getElementById('zlabel');
let ZOOM=2.6; const MAG=170;
function moveMag(e){
  const ir=big.getBoundingClientRect();
  if(!big.src || big.style.visibility==='hidden' || e.clientX<ir.left||e.clientX>ir.right||e.clientY<ir.top||e.clientY>ir.bottom){ mag.style.display='none'; return; }
  const br=body.getBoundingClientRect();
  const relX=e.clientX-br.left, relY=e.clientY-br.top;        // cursor within body
  const ix=e.clientX-ir.left, iy=e.clientY-ir.top;            // cursor within image
  mag.style.display='block';
  mag.style.left=(relX-MAG/2)+'px'; mag.style.top=(relY-MAG/2)+'px';
  mag.style.backgroundImage='url('+big.src+')';
  mag.style.backgroundSize=(ir.width*ZOOM)+'px '+(ir.height*ZOOM)+'px';
  mag.style.backgroundPosition=(-(ix*ZOOM-MAG/2))+'px '+(-(iy*ZOOM-MAG/2))+'px';
}
body.addEventListener('mousemove',moveMag);
body.addEventListener('mouseleave',()=>{mag.style.display='none';});
body.addEventListener('wheel',e=>{ e.preventDefault(); ZOOM=Math.max(1.5,Math.min(6,ZOOM+(e.deltaY<0?0.3:-0.3))); zlabel.textContent='🔍 '+ZOOM.toFixed(1)+'× · wheel = zoom'; moveMag(e); },{passive:false});
render();
</script></body></html>"""
HTML=(HTML.replace("__DATA__",DATA).replace("__ROWS__",ROWS).replace("__COLS__",COLS)
        .replace("__OVERVIEW__",OVERVIEW)
        .replace("#243span;border-bottom:1px solid ",""))  # defensive cleanup of stray token
out=os.path.join(BASE,"viewer_pxo.html"); open(out,"w").write(HTML)
print("wrote",out,"cells:",len(res))
