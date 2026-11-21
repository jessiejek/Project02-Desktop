#!/usr/bin/env bash
# JJQR - AI (Nov 21, 2026) - the one command: build (signing is part of the normal build), verify, launch.
# Usage: scripts/run-mac.sh [--no-launch]
set -euo pipefail
export DOTNET_CLI_HOME="${DOTNET_CLI_HOME:-/Users/jessiejayrubi}"
export PATH="/opt/homebrew/bin:$PATH"
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
PROJ="$ROOT/ClinicNode.App/ClinicNode.App.csproj"
OUT="$ROOT/ClinicNode.App/bin/Debug/net10.0-maccatalyst"

echo "== build (+ ad-hoc sign via AdHocSignAndVerifyMacCatalystApp) =="
dotnet build "$PROJ" -f net10.0-maccatalyst --nologo -v q

APP="$(find "$OUT" -maxdepth 3 -name 'Clinic Node.app' | head -1)"
if [[ -z "$APP" ]]; then echo "error: app bundle not found under $OUT" >&2; exit 1; fi

# Belt and braces: re-sign if the bundle signature is not valid.
if ! codesign --verify --deep --strict "$APP" 2>/dev/null; then
  echo "== re-signing =="
  bash "$ROOT/scripts/sign-maccatalyst-app.sh" "$APP" \
    "$ROOT/ClinicNode.App/Platforms/MacCatalyst/Entitlements.plist" \
    "$ROOT/ClinicNode.App/Platforms/MacCatalyst/msalruntime_stub.c"
fi
codesign --verify --deep --strict "$APP" && echo "signature ok: $APP"

if [[ "${1:-}" == "--no-launch" ]]; then exit 0; fi
pkill -f "Clinic Node.app/Contents/MacOS/ClinicNode.App" 2>/dev/null || true
sleep 0.5
open "$APP"
echo "launched: $APP  (first window can take ~30-50s; demo accounts: doctor@clinic.test, staff@clinic.test, admin@clinic.test; the dev password is DevDataSeeder.DevPassword)"
# JJQR - END
