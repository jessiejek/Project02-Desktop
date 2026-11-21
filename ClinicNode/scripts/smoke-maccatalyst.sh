#!/usr/bin/env bash
# JJQR - AI (Nov 21, 2026) - startup smoke: build, verify the Mac Catalyst signature,
# launch the app, and fail if it dies with a crash report.
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
APP_PROJ="$ROOT/ClinicNode.App/ClinicNode.App.csproj"
REPORTS="$HOME/Library/Logs/DiagnosticReports"

echo "== build =="
dotnet build "$APP_PROJ" -f net10.0-maccatalyst --nologo -v q

APP="$(find "$ROOT/ClinicNode.App/bin/Debug/net10.0-maccatalyst" -name 'Clinic Node.app' -o -name 'ClinicNode.App.app' | head -1)"
if [[ -z "$APP" || ! -d "$APP" ]]; then
  echo "error: built app bundle not found" >&2
  exit 1
fi

echo "== codesign --verify =="
codesign --verify --deep --strict --verbose=2 "$APP"

# A macOS (not Catalyst) msalruntime in the bundle is the launch-crash we just fixed.
if otool -L "$APP/Contents/MacOS/"* 2>/dev/null | grep -q 'msalruntime'; then
  for d in "$APP/Contents/MonoBundle/"msalruntime*.dylib; do
    [[ -f "$d" ]] || continue
    if vtool -show "$d" | grep -q "platform MACOS"; then
      echo "error: $d is still a macOS dylib (must be Mac Catalyst or removed)" >&2
      exit 1
    fi
  done
fi

echo "== launch =="
# Snapshot existing crash reports so a new one is obvious.
BEFORE="$(ls -1 "$REPORTS"/ClinicNode.App-*.ips 2>/dev/null || true)"
pkill -f "ClinicNode.App" 2>/dev/null || true
sleep 0.5
open "$APP"
# First launch can spend ~30s in the interpreter while the scene is created.
DEADLINE=$((SECONDS + 45))
PID=""
while (( SECONDS < DEADLINE )); do
  PID="$(pgrep -n -f "Clinic Node.app/Contents/MacOS/ClinicNode.App" || true)"
  if [[ -n "$PID" ]]; then
    break
  fi
  sleep 1
done
if [[ -z "$PID" ]]; then
  echo "error: ClinicNode.App did not start" >&2
  exit 1
fi
# Stay up past scene creation (the previous crash was instant, <1s).
sleep 8
if ! ps -p "$PID" >/dev/null 2>&1; then
  echo "error: ClinicNode.App exited shortly after launch (pid $PID)" >&2
  AFTER="$(ls -1 "$REPORTS"/ClinicNode.App-*.ips 2>/dev/null || true)"
  echo "$AFTER" | diff <(echo "$BEFORE") - || true
  exit 1
fi
AFTER="$(ls -1 "$REPORTS"/ClinicNode.App-*.ips 2>/dev/null || true)"
NEW="$(comm -13 <(echo "$BEFORE") <(echo "$AFTER") || true)"
if [[ -n "$NEW" ]]; then
  echo "error: new crash report after launch:" >&2
  echo "$NEW" >&2
  exit 1
fi
echo "ok: ClinicNode.App is running (pid $PID) with a valid signature and no new crash report"
