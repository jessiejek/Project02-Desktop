#!/usr/bin/env bash
# Builds everything the clinic desktop app runs, into ./payload :
#   payload/api  - the .NET API (published)
#   payload/ui   - the web UI as a self-contained Node server (Next.js "standalone" build)
# Usage:  ./build-payload.sh            (then:  cd ClinicDesktop && dotnet run -- --demo)
set -euo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"
BE="$HERE/../Project02-be"
FE="$HERE/../Project02-fe"
API_PORT=5199   # must match ClinicDesktop/Program.cs

echo "== API =="
rm -rf "$HERE/payload/api"
dotnet publish "$BE/src/ClinicApp.Api/ClinicApp.Api.csproj" -c Release -o "$HERE/payload/api" -nologo -v q

echo "== UI =="
cd "$FE"
# NEXT_PUBLIC_* values are baked into the browser code at build time:
#  - the API address of the LOCAL node
#  - NEXT_PUBLIC_LOCAL_NODE=1 switches printing to a hidden in-page frame (embedded webviews handle pop-ups unpredictably)
NEXT_OUTPUT=standalone NEXT_DIST_DIR=.next-desktop \
NEXT_PUBLIC_API_URL="http://localhost:$API_PORT" NEXT_PUBLIC_API_MODE=dotnet NEXT_PUBLIC_AUTH_MODE=dotnet NEXT_PUBLIC_LOCAL_NODE=1 \
  npx next build --webpack

rm -rf "$HERE/payload/ui"; mkdir -p "$HERE/payload/ui"
cp -R .next-desktop/standalone/. "$HERE/payload/ui/"
mkdir -p "$HERE/payload/ui/.next-desktop"
cp -R .next-desktop/static "$HERE/payload/ui/.next-desktop/static"
cp -R public "$HERE/payload/ui/public"
test -f "$HERE/payload/ui/server.js"
echo "== done: $HERE/payload =="
