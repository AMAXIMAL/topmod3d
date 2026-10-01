#!/usr/bin/env python3
"""Aggregate all non-clean results into an actionable error/issue spreadsheet."""
import json, os, html
from openpyxl import Workbook
from openpyxl.styles import Font, PatternFill, Alignment, Border, Side

BASE="/Users/ions/topmod-test"; SHOTS=os.path.join(BASE,"shots")
def load(f):
    p=os.path.join(BASE,f)
    return json.load(open(p)) if os.path.exists(p) else []

tm   = load("results.json")          # 93 tool x modifier (menubar ops)
ox   = load("operators_extra.json")  # 66 extra operators x modifier
pairs= load("pairs.json")            # 256 pairwise
pxo  = load("primitive_x_operator.json")  # 368 primitive x operator
errs = load("errors.json")           # detailed captured errors

# detailed-error lookup by tool
edet = {e["tool"]: e for e in errs}

OLABEL={'move':'Move/Rotate/Scale','scale':'Uniform Scale','array':'Array','mirror':'Mirror','deleteSel':'Delete Selected',
 'flipNormals':'Flip Normals','heal':'Heal','quadRemesh':'Quad Remesh','simplify':'Simplify','planarize':'Planarize',
 'orient':'Orient to ground','cut':'Cut (plane)','pinch':'Pinch Extrude','bridge':'Bridge (2 faces)','pipe':'Pipes',
 'offset':'Offset Surface','sliceEdges':'Seam','slab':'Paper/Unfold (Slab)','gyroid':'Gyroid','sierpinski':'Sierpinski',
 'hybrid':'Schoen Hybrid','lattice':'Lattice (Cage)','deleteVertex':'Delete Vertex','deleteFace':'Delete Face',
 'deleteEdge':'Delete Edge','handle':'Add Handle','fillHoles':'Fill Holes','insertEdge':'Insert Edge'}

# Collect issues. Group identical (tool,verdict) across modes.
from collections import defaultdict
groups=defaultdict(lambda:{"modes":[],"note":"","shot":None,"source":""})
def add(rec, source):
    op=rec.get("op"); v=rec.get("verdict")
    if v in ("PASS","PASS-vis","MISSING","NOEFFECT") and v!="FAIL":
        pass
    key=(op,v)
    g=groups[key]; g["modes"].append(rec.get("mode","-")); g["note"]=rec.get("note",""); g["op"]=op; g["verdict"]=v
    if rec.get("shot") and os.path.exists(os.path.join(SHOTS,rec["shot"])): g["shot"]=rec["shot"]
    g["source"]=source

ISSUE_VERDICTS={"FAIL","DESTROYS","EMPTY"}
for r in tm:
    if r.get("verdict") in ISSUE_VERDICTS: add(r,"tool x modifier")
for r in ox:
    if r.get("verdict") in ISSUE_VERDICTS: add(r,"extra operators")

# NOEFFECT for interactive/transform tools (potential limitations / UX issues, not crashes)
NEEDS_INTERACTION={"bridge","lattice","orient","cut","move","scale","handle","insertEdge","fillHoles",
                   "deleteEdge","sliceEdges","heal","quadRemesh","planarize","pinch","offset","flipNormals"}
noeff=defaultdict(lambda:{"modes":[]})
for r in tm+ox:
    if r.get("verdict")=="NOEFFECT" and r.get("op") in NEEDS_INTERACTION:
        g=noeff[r["op"]]; g["modes"].append(r.get("mode","-")); g["note"]=r.get("note",""); g["op"]=r["op"]
        if r.get("shot") and os.path.exists(os.path.join(SHOTS,r["shot"])) and not g.get("shot"): g["shot"]=r["shot"]

SEV={"FAIL":"HIGH — crash","DESTROYS":"REVIEW — destructive","EMPTY":"REVIEW"}
SEVC={"HIGH — crash":"FFC7CE","REVIEW — destructive":"FFD9B3","REVIEW":"FFD9B3","LIMITATION":"FFEB9C"}

wb=Workbook(); ws=wb.active; ws.title="Errors & Issues"
cols=["Severity","Tool","op id","Mode(s)","Verdict","Symptom","Error / detail","Location","Likely cause","Suggested fix","Screenshot"]
ws.append(cols)
for c in ws[1]: c.font=Font(bold=True,color="FFFFFF"); c.fill=PatternFill("solid",fgColor="1F2A36"); c.alignment=Alignment(wrap_text=True,vertical="center")

def put(sev,tool,op,modes,verdict,symptom,err,loc,cause,fix,shot):
    ws.append([sev,tool,op,modes,verdict,symptom,err,loc,cause,fix,""])
    rr=ws.max_row
    ws.cell(row=rr,column=1).fill=PatternFill("solid",fgColor=SEVC.get(sev,"FFFFFF"))
    ws.cell(row=rr,column=1).font=Font(bold=True)
    for ci in range(1,12): ws.cell(row=rr,column=ci).alignment=Alignment(wrap_text=True,vertical="top")
    if shot and os.path.exists(os.path.join(SHOTS,shot)):
        sc=ws.cell(row=rr,column=11,value="screenshot"); sc.hyperlink="shots/"+shot; sc.font=Font(color="0563C1",underline="single")
    ws.row_dimensions[rr].height=70

# 1) hard crashes & freezes — from detailed errors.json
shot_for={}
for r in (tm+ox+pxo):
    if r.get("shot") and os.path.exists(os.path.join(SHOTS,r["shot"])):
        shot_for.setdefault(r.get("op"), r["shot"])
for d in errs:
    sev = "HIGH — freeze" if d.get("verdict")=="FREEZE" else "HIGH — crash"
    SEVC.setdefault("HIGH — freeze","FFC7CE")
    put(sev, d.get("toolLabel",d.get("tool","")), d.get("tool",""), d.get("modes",""), d.get("verdict",""),
        d.get("error",""), d.get("error",""), d.get("location",""),
        d.get("likely_cause",""), d.get("suggested_fix",""), shot_for.get(d.get("tool")))
# 2) destructive
for (op,v),g in sorted(groups.items()):
    if v not in ("DESTROYS","EMPTY"): continue
    put("REVIEW — destructive", OLABEL.get(op,op), op, ", ".join(sorted(set(g["modes"]))), v,
        "Mesh emptied when applied with everything selected",
        g["note"], "", "Delete op removes all selected elements -> empty mesh. Likely by design, but confirm there is a guard/undo and that it cannot corrupt state.",
        "Optional: warn or no-op when the deletion would remove the entire mesh.", g["shot"])
# 3) limitations / needs-interaction
for op,g in sorted(noeff.items()):
    put("LIMITATION", OLABEL.get(op,op), op, ", ".join(sorted(set(g["modes"]))), "NOEFFECT",
        "No change via automated (click+SelectAll+Apply) path",
        g.get("note",""), "",
        "Tool needs a manual pick or gesture (e.g. Bridge=exactly 2 faces, Lattice=host shape, Orient/Cut=pick a face/plane, Move/Scale=drag) or a specific selection. Not necessarily a bug.",
        "Confirm intended; consider a hint/disabled-state when prerequisites are unmet.", g.get("shot"))

widths={"A":18,"B":18,"C":12,"D":16,"E":10,"F":26,"G":34,"H":20,"I":46,"J":46,"K":12}
for k,w in widths.items(): ws.column_dimensions[k].width=w
ws.freeze_panes="A2"

# summary sheet
ws2=wb.create_sheet("Summary")
ws2.append(["Run","Total cells","PASS","PASS-vis","NOEFFECT","DESTROYS","FAIL"])
for c in ws2[1]: c.font=Font(bold=True)
def counts(data):
    from collections import Counter; c=Counter(r.get("verdict") for r in data); return c
for name,data in [("Tool x Modifier (menubar 31)",tm),("Pairwise sequences (16x16)",pairs),("Extra operators x modifier",ox),("Primitive x Operator (23x16)",pxo)]:
    c=counts(data)
    ws2.append([name,len(data),c.get("PASS",0)+c.get("PASS-vis",0),c.get("PASS-vis",0),c.get("NOEFFECT",0)+c.get("UNVERIFIED",0),c.get("DESTROYS",0),c.get("FAIL",0)])
ws2.append([])
ws2.append(["SKIP-heavy (freeze-prone, primitive x operator)", counts(pxo).get("SKIP-heavy",0)])
ws2.append(["Confirmed HIGH issues","1) slab (Paper/Unfold) JS crash  2) remesh ops freeze on high-poly primitives — see Errors & Issues tab"])
for c in ["A"]: ws2.column_dimensions[c].width=34
for c in ["B","C","D","E","F","G"]: ws2.column_dimensions[c].width=12

xlsx=os.path.join(BASE,"TOPMOD3D_error_log.xlsx"); wb.save(xlsx)

# HTML
def row_html(sev,tool,op,modes,verdict,err,loc,cause,fix,shot):
    sc={'HIGH — crash':'#9c0006','HIGH — freeze':'#b1003a','REVIEW — destructive':'#9c5700','REVIEW':'#9c5700','LIMITATION':'#8a6d00'}.get(sev,'#444')
    img=f'<a href="shots/{shot}" target="_blank"><img src="shots/{shot}"></a>' if shot and os.path.exists(os.path.join(SHOTS,shot)) else ''
    return f"""<tr><td><span class=sev style="background:{sc}">{sev}</span></td>
<td><b>{html.escape(tool)}</b><br><code>{op}</code></td><td>{modes}</td><td>{verdict}</td>
<td class=err>{html.escape(err)}<div class=loc>{html.escape(loc)}</div></td>
<td>{html.escape(cause)}</td><td class=fix>{html.escape(fix)}</td><td class=sh>{img}</td></tr>"""

rows=[]
for d in errs:
    sev="HIGH — freeze" if d.get("verdict")=="FREEZE" else "HIGH — crash"
    rows.append(row_html(sev,d.get("toolLabel",d.get("tool","")),d.get("tool",""),d.get("modes",""),d.get("verdict",""),d.get("error",""),d.get("location",""),d.get("likely_cause",""),d.get("suggested_fix",""),shot_for.get(d.get("tool"))))
for (op,v),g in sorted(groups.items()):
    if v not in ("DESTROYS","EMPTY"): continue
    rows.append(row_html("REVIEW — destructive",OLABEL.get(op,op),op,", ".join(sorted(set(g["modes"]))),v,g["note"],"","Delete removes all selected elements -> empty mesh (likely by design).","Warn/no-op if deletion would empty the whole mesh.",g["shot"]))
for op,g in sorted(noeff.items()):
    rows.append(row_html("LIMITATION",OLABEL.get(op,op),op,", ".join(sorted(set(g["modes"]))),"NOEFFECT",g.get("note",""),"","Needs a manual pick/gesture or specific selection.","Add hint/disabled-state when prerequisites unmet.",g.get("shot")))

doc=f"""<!doctype html><meta charset=utf-8><title>TOPMOD 3D — error log</title>
<style>
body{{background:#0d1420;color:#cfe8ff;font:13px system-ui,sans-serif;margin:0;padding:22px}}
h1{{font-size:20px;margin:0 0 2px}} .sub{{color:#7fa6c4;font-size:12px;margin:0 0 16px}}
table{{border-collapse:collapse;width:100%}} th,td{{border:1px solid #21303f;padding:8px;vertical-align:top;text-align:left}}
thead th{{background:#16202c;position:sticky;top:0}}
.sev{{color:#fff;font-weight:700;font-size:11px;padding:3px 7px;border-radius:4px;white-space:nowrap}}
code{{color:#6f93ad;font-size:11px}} .err{{font-family:ui-monospace,monospace;color:#ffb4b4;font-size:11.5px}}
.loc{{color:#8aa;font-size:11px;margin-top:3px}} .fix{{color:#bfe6c8}} .sh img{{width:120px;border-radius:5px;background:#000}}
</style>
<h1>TOPMOD 3D — error &amp; issue log</h1>
<p class=sub>Aggregated from {len(tm)+len(ox)+len(pairs)} tested combinations. HIGH = JS crash to fix · REVIEW = destructive, confirm intended · LIMITATION = needs manual interaction (maybe by design).</p>
<table><thead><tr><th>Severity</th><th>Tool</th><th>Mode(s)</th><th>Verdict</th><th>Error / detail</th><th>Likely cause</th><th>Suggested fix</th><th>Shot</th></tr></thead>
<tbody>{''.join(rows)}</tbody></table>
"""
open(os.path.join(BASE,"report_errors.html"),"w").write(doc)
print("XLSX:",xlsx); print("HTML:",os.path.join(BASE,"report_errors.html"))
print("issue rows:",len(rows))
