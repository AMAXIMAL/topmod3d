#!/usr/bin/env python3
"""TopMod 3D: ship from the master.

TOPMOD.html is the ONE file you edit. This script carries it everywhere else:

    python3 tools/ship.py              # copy into Xcode, bump the build number, zip to dist/
    python3 tools/ship.py --site       # ...and push it to the portfolio site (sync_topmod.py)
    python3 tools/ship.py --version 3.8  # set a new version (wordmark + Xcode), build carries on
    python3 tools/ship.py --no-bump    # rebuild the zip without bumping the build

Writes:
    ios/TopModJS/Resources/topmodjs.html   (copy of TOPMOD.html)
    ios/TOPMOD 3D.xcodeproj/project.pbxproj (MARKETING_VERSION / CURRENT_PROJECT_VERSION)
    dist/TopModJS-Xcode_v<ver>_b<build>.zip
    dist/TOPMOD_v<ver>_b<build>.html        (snapshot of the shipped web app)
and a git commit if this folder is a git repo.
"""
import argparse, os, re, shutil, subprocess, sys, time, zipfile

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
MASTER = os.path.join(ROOT, "TOPMOD.html")
IOS = os.path.join(ROOT, "ios")
RES = os.path.join(IOS, "TopModJS", "Resources", "topmodjs.html")
PBX = os.path.join(IOS, "TOPMOD 3D.xcodeproj", "project.pbxproj")
DIST = os.path.join(ROOT, "dist")
SITE_SYNC = os.path.expanduser("~/AMAXiMAL/website/portfolio/tools/sync_topmod.py")

ap = argparse.ArgumentParser()
ap.add_argument("--version", help="new marketing version, e.g. 3.8")
ap.add_argument("--build", type=int, help="explicit build number")
ap.add_argument("--no-bump", action="store_true", help="keep the current build number")
ap.add_argument("--site", action="store_true", help="also update the portfolio site copy")
a = ap.parse_args()

html = open(MASTER, encoding="utf-8").read()
pbx = open(PBX, encoding="utf-8").read()

# ---- version: wordmark in TOPMOD.html is the source of truth ----
m = re.search(r'<span class="ver">v([\d.]+)</span>', html)
if not m: sys.exit("No <span class=\"ver\">vX.Y</span> wordmark found in TOPMOD.html")
ver = m.group(1)
if a.version and a.version != ver:
    html = html.replace(m.group(0), f'<span class="ver">v{a.version}</span>', 1)
    open(MASTER, "w", encoding="utf-8").write(html)
    ver = a.version

# ---- build number ----
cur = int(re.search(r"CURRENT_PROJECT_VERSION = (\d+);", pbx).group(1))
build = a.build if a.build else (cur if a.no_bump else cur + 1)

pbx = re.sub(r"MARKETING_VERSION = [\d.]+;", f"MARKETING_VERSION = {ver};", pbx)
pbx = re.sub(r"CURRENT_PROJECT_VERSION = \d+;", f"CURRENT_PROJECT_VERSION = {build};", pbx)
open(PBX, "w", encoding="utf-8").write(pbx)

# ---- web app into the Xcode bundle ----
shutil.copy2(MASTER, RES)

# ---- dist ----
os.makedirs(DIST, exist_ok=True)
tag = f"v{ver}_b{build}"
shutil.copy2(MASTER, os.path.join(DIST, f"TOPMOD_{tag}.html"))
zpath = os.path.join(DIST, f"TopModJS-Xcode_{tag}.zip")
with zipfile.ZipFile(zpath, "w", zipfile.ZIP_DEFLATED) as z:
    for d, dirs, files in os.walk(IOS):
        dirs[:] = [x for x in dirs if x not in ("xcuserdata", "DerivedData", "build")]
        for f in files:
            if f == ".DS_Store": continue
            p = os.path.join(d, f)
            z.write(p, os.path.join("TopModJS", os.path.relpath(p, IOS)))

print(f"TopMod 3D v{ver} (build {build})")
print(f"  Xcode  -> {RES}")
print(f"  zip    -> {zpath}  ({os.path.getsize(zpath):,} bytes)")

# ---- site ----
if a.site:
    if os.path.exists(SITE_SYNC):
        subprocess.run([sys.executable, SITE_SYNC, MASTER], check=False)
    else:
        print("  site   -> sync_topmod.py not found, skipped")

# ---- git ----
if os.path.isdir(os.path.join(ROOT, ".git")):
    subprocess.run(["git", "-C", ROOT, "add", "-A"], check=False)
    subprocess.run(["git", "-C", ROOT, "commit", "-qm", f"Ship v{ver} build {build}"], check=False)
    subprocess.run(["git", "-C", ROOT, "tag", "-f", tag], check=False, stdout=subprocess.DEVNULL)
    print(f"  git    -> committed + tagged {tag}")
print(time.strftime("%Y-%m-%d %H:%M"))
