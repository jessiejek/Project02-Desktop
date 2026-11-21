#!/usr/bin/env bash
# JJQR - AI (Nov 21, 2026) - ad-hoc sign the Mac Catalyst app bundle (sealed resources)
# and replace any leftover macOS-only msalruntime with a Catalyst stub.
set -euo pipefail
APP="${1:?Usage: sign-maccatalyst-app.sh <Clinic Node.app> [entitlements.plist] [stub.c]}"
ENT="${2:-}"
STUB_C="${3:-}"

if [[ ! -d "$APP" ]]; then
  echo "error: app bundle not found: $APP" >&2
  exit 1
fi

BUNDLE="$APP/Contents/MonoBundle"
if [[ -f "$BUNDLE/msalruntime_arm64.dylib" || -f "$BUNDLE/msalruntime.dylib" ]]; then
  if [[ -z "$STUB_C" ]]; then
    echo "error: msalruntime is present but no stub source was given" >&2
    exit 1
  fi
  STUB_OUT="$(mktemp -t msalruntime_stub).dylib"
  xcrun clang -dynamiclib -target arm64-apple-ios17.0-macabi \
    -install_name @executable_path/../../Contents/MonoBundle/msalruntime_arm64.dylib \
    -o "$STUB_OUT" "$STUB_C"
  if [[ -f "$BUNDLE/msalruntime_arm64.dylib" ]]; then
    cp "$STUB_OUT" "$BUNDLE/msalruntime_arm64.dylib"
  fi
  if [[ -f "$BUNDLE/msalruntime.dylib" ]]; then
    cp "$STUB_OUT" "$BUNDLE/msalruntime.dylib"
  fi
  rm -f "$STUB_OUT"
  echo "replaced macOS-only msalruntime with a Mac Catalyst stub"
fi

# Sign nested Mach-O files first, then the bundle (Info.plist + resources sealed).
while IFS= read -r -d '' f; do
  if file -b "$f" | grep -q "Mach-O"; then
    codesign --force --sign - --timestamp=none "$f"
  fi
done < <(find "$APP" -type f -print0)

if [[ -n "$ENT" && -f "$ENT" ]]; then
  codesign --force --deep --sign - --entitlements "$ENT" --timestamp=none "$APP"
else
  codesign --force --deep --sign - --timestamp=none "$APP"
fi

codesign --verify --deep --strict --verbose=2 "$APP"
echo "signature ok: $APP"
