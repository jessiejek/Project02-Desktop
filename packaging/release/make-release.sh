#!/usr/bin/env bash
# packaging/release/make-release.sh - build a Mac release and write the SIGNED update feed.
#JJQR - AI (Nov 22, 2026) - OTA: one command from "version bumped" to "files ready to publish".
# Usage (from Project02-desktop):
#   UPDATE_KEY=../_secrets/update-signing-private.pem REL_DIR=../Project02-releases packaging/release/make-release.sh "Faster consultation" "Fix: ..."
# Result in $REL_DIR:  downloads/ClinicNode-<ver>-arm64.dmg + .zip,  latest.json,  latest.json.sig,  install.sh
# The arguments after the script name become the "what is new" notes. The version is ApplicationDisplayVersion in ClinicNode.App.csproj.
set -euo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"; ROOT="$(cd "$HERE/../.." && pwd)"
REL_DIR="${REL_DIR:-$ROOT/../Project02-releases}"
UPDATE_KEY="${UPDATE_KEY:-$ROOT/../_secrets/update-signing-private.pem}"
MIN_VERSION="${MIN_VERSION:-}"          # set to force everyone below it to update
ROLLOUT="${ROLLOUT:-100}"               # staged rollout percent
BASE="${RELEASE_BASE_URL:-https://raw.githubusercontent.com/jessiejek/Project02-releases/main}"
[[ -f "$UPDATE_KEY" ]] || { echo "error: signing key not found: $UPDATE_KEY" >&2; exit 1; }
PROJ="$ROOT/ClinicNode/ClinicNode.App/ClinicNode.App.csproj"
VERSION="$(sed -n 's:.*<ApplicationDisplayVersion>\(.*\)</ApplicationDisplayVersion>.*:\1:p' "$PROJ" | head -1)"
RID="${RID:-maccatalyst-arm64}"; SHORT="${RID#maccatalyst-}"
echo "== building Clinic Node $VERSION ($SHORT) =="
bash "$ROOT/packaging/mac/build-dmg.sh"
DMG="$ROOT/packaging/out/ClinicNode-$VERSION-$SHORT.dmg"
APP="$(find "$ROOT/ClinicNode/ClinicNode.App/bin/Debug/net10.0-maccatalyst/$RID" -maxdepth 1 -name '*.app' | head -1)"
mkdir -p "$REL_DIR/downloads"
ZIP="$REL_DIR/downloads/ClinicNode-$VERSION-$SHORT.zip"
rm -f "$ZIP"; ditto -c -k --keepParent "$APP" "$ZIP"
cp "$DMG" "$REL_DIR/downloads/"
DMG_OUT="$REL_DIR/downloads/$(basename "$DMG")"
sha() { shasum -a 256 "$1" | cut -d' ' -f1; }
size() { stat -f%z "$1"; }
NOTES_JSON="$(python3 -c 'import json,sys;print(json.dumps(sys.argv[1:]))' "$@")"
python3 - "$VERSION" "$SHORT" "$BASE" "$ZIP" "$DMG_OUT" "$NOTES_JSON" "$MIN_VERSION" "$ROLLOUT" "$REL_DIR" <<'PY'
import json,sys,os,hashlib,datetime
ver,short,base,zp,dp,notes,minv,roll,rel=sys.argv[1:10]
def pkg(p): 
    h=hashlib.sha256(open(p,'rb').read()).hexdigest()
    return {"url":f"{base}/downloads/{os.path.basename(p)}","sha256":h,"size":os.path.getsize(p)}
m={"version":ver,"min_version":minv or None,"released_at":datetime.datetime.now(datetime.timezone.utc).isoformat(timespec="seconds"),
   "notes":json.loads(notes),f"mac_{short}":pkg(zp),f"mac_{short}_dmg":pkg(dp),"rollout_percent":int(roll)}
#JJQR - AI (Nov 23, 2026) - the cloud release also builds the Windows installer; when WIN_SETUP points at it, it goes into the same signed feed as "win_x64"
win=os.environ.get("WIN_SETUP","")
if win and os.path.isfile(win):
    import shutil
    dst=os.path.join(rel,"downloads",os.path.basename(win)); os.makedirs(os.path.dirname(dst),exist_ok=True)
    shutil.copyfile(win,dst); m["win_x64"]=pkg(dst)
#JJQR - END
open(os.path.join(rel,"latest.json"),"w",encoding="utf-8").write(json.dumps(m,indent=2,ensure_ascii=False))
PY
# signature over the EXACT bytes of latest.json (ECDSA P-256 / SHA-256, DER, base64)
openssl dgst -sha256 -sign "$UPDATE_KEY" "$REL_DIR/latest.json" | base64 | tr -d '\n' > "$REL_DIR/latest.json.sig"
cp "$ROOT/packaging/mac/install.sh" "$REL_DIR/install.sh"
sed -i '' "s#^BASE=.*#BASE=\"\${CLINIC_NODE_UPDATES:-$BASE}\"#" "$REL_DIR/install.sh"
echo; echo "Release $VERSION is ready in $REL_DIR:"; ls -l "$REL_DIR" "$REL_DIR/downloads" | grep -v "^total"
echo "Next: commit and push that folder (the public Project02-releases repository)."
#JJQR - END
