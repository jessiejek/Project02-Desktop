export DOTNET_CLI_HOME=/Users/jessiejayrubi
cd /Users/jessiejayrubi/Documents/Allyn/Project02-desktop/packaging/print-smoke
dotnet build -nologo -v q 2>&1 | grep -E " error " | sort -u
A="bin/Debug/net10.0-maccatalyst/maccatalyst-arm64/ClinicNode Print Smoke.app"; codesign --force --deep --sign - "$A" >/dev/null 2>&1
rm -rf /tmp/cn-smoke; timeout 120 "$A/Contents/MacOS/PrintSmoke" /tmp/cn-smoke >/dev/null 2>&1
cat /tmp/cn-smoke/sizes.txt
O=/Users/jessiejayrubi/Documents/Allyn/Project02-desktop/packaging/print-smoke/out
for f in /tmp/cn-smoke/*.pdf; do b=$(basename $f .pdf); sips -s format png -Z 1000 $f --out $O/$b.png >/dev/null; done; cp /tmp/cn-smoke/*.pdf /tmp/cn-smoke/*.escpos $O/
