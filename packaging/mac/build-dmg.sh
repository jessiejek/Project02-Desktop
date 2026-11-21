#!/usr/bin/env bash
# JJQR - AI (Nov 21, 2026) - build the Mac Catalyst app (Release, self-contained) and wrap it in a drag-to-Applications DMG.
# Usage:
#   packaging/mac/build-dmg.sh                                   # ad-hoc signed DMG (this Mac / testing only)
#   SIGN_ID="Developer ID Application: NAME (TEAMID)" packaging/mac/build-dmg.sh            # signed for distribution
#   SIGN_ID="Developer ID Application: NAME (TEAMID)" NOTARY_PROFILE=clinicnode packaging/mac/build-dmg.sh   # + notarize & staple
# Optional: RID=maccatalyst-arm64 (default) | maccatalyst-x64 ; OUT=packaging/out
set -euo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"
ROOT="$(cd "$HERE/../.." && pwd)"
PROJ="$ROOT/ClinicNode/ClinicNode.App/ClinicNode.App.csproj"
ENT="$ROOT/ClinicNode/ClinicNode.App/Platforms/MacCatalyst/Entitlements.plist"
RID="${RID:-maccatalyst-arm64}"
OUT="${OUT:-$ROOT/packaging/out}"
SIGN_ID="${SIGN_ID:-}"
NOTARY_PROFILE="${NOTARY_PROFILE:-}"

VERSION="$(sed -n 's:.*<ApplicationDisplayVersion>\(.*\)</ApplicationDisplayVersion>.*:\1:p' "$PROJ" | head -1)"
VERSION="${VERSION:-0.0.0}"
mkdir -p "$OUT"

echo "== build Release $RID (self-contained) =="
dotnet build "$PROJ" -c Release -f net10.0-maccatalyst -r "$RID" -p:SelfContained=true --nologo -v q

APP="$(find "$ROOT/ClinicNode/ClinicNode.App/bin/Release/net10.0-maccatalyst/$RID" -maxdepth 1 -name '*.app' | head -1)"
[[ -d "$APP" ]] || { echo "error: .app not found" >&2; exit 1; }
echo "app: $APP"

if [[ -n "$SIGN_ID" ]]; then
  echo "== sign with Developer ID (hardened runtime) =="
  # nested code first, then the bundle with the app's entitlements
  while IFS= read -r -d '' f; do
    if file -b "$f" | grep -q "Mach-O"; then
      codesign --force --options runtime --timestamp --sign "$SIGN_ID" "$f"
    fi
  done < <(find "$APP/Contents" -type f \( -name '*.dylib' -o -perm -111 \) ! -path '*/MacOS/*' -print0)
  codesign --force --options runtime --timestamp --entitlements "$ENT" --sign "$SIGN_ID" "$APP"
fi
codesign --verify --deep --strict --verbose=1 "$APP"

echo "== DMG =="
STAGE="$(mktemp -d)"
cp -R "$APP" "$STAGE/"
ln -s /Applications "$STAGE/Applications"
DMG="$OUT/ClinicNode-$VERSION-${RID#maccatalyst-}.dmg"
rm -f "$DMG"
hdiutil create -volname "Clinic Node" -srcfolder "$STAGE" -ov -format UDZO "$DMG" >/dev/null
rm -rf "$STAGE"

if [[ -n "$SIGN_ID" ]]; then
  codesign --force --timestamp --sign "$SIGN_ID" "$DMG"
  if [[ -n "$NOTARY_PROFILE" ]]; then
    echo "== notarize (can take a few minutes) =="
    xcrun notarytool submit "$DMG" --keychain-profile "$NOTARY_PROFILE" --wait
    xcrun stapler staple "$DMG"
    spctl -a -t open --context context:primary-signature -vv "$DMG" || true
  fi
fi
hdiutil verify "$DMG" >/dev/null
echo "ok: $DMG"
# JJQR - END
