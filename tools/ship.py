#!/usr/bin/env python3
"""TopMod 3D: ship from the master.

TOPMOD.html is the ONE file you edit. The BUILD NUMBER only changes when a build goes to the App Store.

    python3 tools/ship.py                 # sync TOPMOD.html into Xcode, zip to dist/, git commit (build unchanged)
    python3 tools/ship.py --site          # ...and update the portfolio site copy (sync_topmod.py)
    python3 tools/ship.py --version 3.9   # set a new version (wordmark + Xcode)
    python3 tools/ship.py --push          # App Store: next build number, archive, upload to App Store Connect,
                                          #            record it, git tag  appstore-v<ver>-b<build>

The last uploaded build lives in tools/appstore_build.txt; --push uses that + 1, so every upload gets a
fresh build and nothing else ever bumps it.

Writes:
    ios/TopModJS/Resources/topmodjs.html    (copy of TOPMOD.html)
    ios/TOPMOD 3D.xcodeproj/project.pbxproj  (MARKETING_VERSION / CURRENT_PROJECT_VERSION)
    dist/TopModJS-Xcode_v<ver>.zip           (latest working copy; overwritten each run)
    dist/appstore/TopModJS-Xcode_v<ver>_b<build>.zip + .html  (one per App Store upload)
"""
import argparse, os, re, shutil, subprocess, sys, time, zipfile

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
MASTER = os.path.join(ROOT, "TOPMOD.html")
IOS = os.path.join(ROOT, "ios")
PROJ = os.path.join(IOS, "TOPMOD 3D.xcodeproj")
RES = os.path.join(IOS, "TopModJS", "Resources", "topmodjs.html")
PBX = os.path.join(PROJ, "project.pbxproj")
DIST = os.path.join(ROOT, "dist")
UPLOADED = os.path.join(HERE, "appstore_build.txt")
EXPORT_OPTS = os.path.join(HERE, "ExportOptions.plist")
SITE_SYNC = os.path.expanduser("~/AMAXiMAL/website/portfolio/tools/sync_topmod.py")

ap = argparse.ArgumentParser()
ap.add_argument("--version", help="new marketing version, e.g. 3.9")
ap.add_argument("--push", action="store_true", help="archive + upload to App Store Connect (next build number)")
ap.add_argument("--site", action="store_true", help="also update the portfolio site copy")
a = ap.parse_args()

def git(*args, **kw):
    if os.path.isdir(os.path.join(ROOT, ".git")):
        return subprocess.run(["git", "-C", ROOT, *args], check=False, **kw)

html = open(MASTER, encoding="utf-8").read()
pbx = open(PBX, encoding="utf-8").read()

# ---- version: the wordmark in TOPMOD.html is the source of truth ----
m = re.search(r'<span class="ver">v([\d.]+)</span>', html)
if not m: sys.exit('No <span class="ver">vX.Y</span> wordmark found in TOPMOD.html')
ver = m.group(1)
if a.version and a.version != ver:
    html = html.replace(m.group(0), f'<span class="ver">v{a.version}</span>', 1)
    open(MASTER, "w", encoding="utf-8").write(html)
    ver = a.version

uploaded = int(open(UPLOADED).read().strip()) if os.path.exists(UPLOADED) else int(re.search(r"CURRENT_PROJECT_VERSION = (\d+);", pbx).group(1))
build = uploaded + 1 if a.push else int(re.search(r"CURRENT_PROJECT_VERSION = (\d+);", pbx).group(1))

pbx = re.sub(r"MARKETING_VERSION = [\d.]+;", f"MARKETING_VERSION = {ver};", pbx)
pbx = re.sub(r"CURRENT_PROJECT_VERSION = \d+;", f"CURRENT_PROJECT_VERSION = {build};", pbx)
open(PBX, "w", encoding="utf-8").write(pbx)
shutil.copy2(MASTER, RES)

def zip_ios(path):
    with zipfile.ZipFile(path, "w", zipfile.ZIP_DEFLATED) as z:
        for d, dirs, files in os.walk(IOS):
            dirs[:] = [x for x in dirs if x not in ("xcuserdata", "DerivedData", "build")]
            for f in files:
                if f == ".DS_Store": continue
                p = os.path.join(d, f)
                z.write(p, os.path.join("TopModJS", os.path.relpath(p, IOS)))

os.makedirs(DIST, exist_ok=True)
zpath = os.path.join(DIST, f"TopModJS-Xcode_v{ver}.zip")
zip_ios(zpath)
print(f"TopMod 3D v{ver}  (build {build}{'' if a.push else ' — unchanged; bumps only on --push'})")
print(f"  Xcode  -> {RES}")
print(f"  zip    -> {zpath}")

if a.site:
    if os.path.exists(SITE_SYNC): subprocess.run([sys.executable, SITE_SYNC, MASTER], check=False)
    else: print("  site   -> sync_topmod.py not found, skipped")

if not a.push:
    git("add", "-A"); git("commit", "-qm", f"Update v{ver} (working copy, build {build} not uploaded)")
    print(time.strftime("%Y-%m-%d %H:%M")); sys.exit(0)

# ---------------- App Store push ----------------
bdir = os.path.join(DIST, "build"); os.makedirs(bdir, exist_ok=True)
arch = os.path.join(bdir, f"TOPMOD3D_v{ver}_b{build}.xcarchive")
print(f"  archive -> {arch}")
r = subprocess.run(["xcodebuild", "-project", PROJ, "-scheme", "TOPMOD 3D", "-configuration", "Release",
                    "-destination", "generic/platform=iOS", "-archivePath", arch, "-allowProvisioningUpdates", "archive"],
                   stdout=open(os.path.join(bdir, "archive.log"), "w"), stderr=subprocess.STDOUT)
if r.returncode: sys.exit(f"ARCHIVE FAILED — see {bdir}/archive.log")
print("  upload  -> App Store Connect …")
log = os.path.join(bdir, "upload.log")
r = subprocess.run(["xcodebuild", "-exportArchive", "-archivePath", arch, "-exportOptionsPlist", EXPORT_OPTS,
                    "-exportPath", os.path.join(bdir, "export"), "-allowProvisioningUpdates"],
                   stdout=open(log, "w"), stderr=subprocess.STDOUT)
tail = [l for l in open(log).read().splitlines() if "error" in l.lower() or "succeeded" in l.lower() or "Upload" in l][-6:]
print("\n".join("    " + l for l in tail))
if r.returncode: sys.exit("UPLOAD FAILED — build number NOT consumed; fix and run --push again")

open(UPLOADED, "w").write(str(build) + "\n")
adir = os.path.join(DIST, "appstore"); os.makedirs(adir, exist_ok=True)
shutil.copy2(MASTER, os.path.join(adir, f"TOPMOD_v{ver}_b{build}.html"))
zip_ios(os.path.join(adir, f"TopModJS-Xcode_v{ver}_b{build}.zip"))
git("add", "-A"); git("commit", "-qm", f"App Store upload v{ver} build {build}")
git("tag", "-f", f"appstore-v{ver}-b{build}", stdout=subprocess.DEVNULL)
print(f"  UPLOADED v{ver} build {build} — processing at Apple (TestFlight in ~10–30 min)")
print(time.strftime("%Y-%m-%d %H:%M"))
