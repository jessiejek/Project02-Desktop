#!/usr/bin/env bash
# packaging/mac/make-local-signing-identity.sh
#JJQR - AI (Nov 22, 2026) - owner: "I don't like to subscribe". Creates ONE free, self-signed code-signing identity on this Mac ("Clinic Node Local Signing"), valid 20 years.
# Why: every build is signed with the SAME identity, so macOS keychain items (the saved sign-in and this computer's sync registration) stay readable after an update.
# It does NOT make Gatekeeper trust the app on other Macs (that needs Apple's paid program); the installer script avoids that prompt instead (curl does not set the quarantine flag).
# Run once, on the Mac that builds the releases. macOS may ask for your Mac password to trust the certificate.
set -euo pipefail
NAME="${1:-Clinic Node Local Signing}"
if security find-identity -v -p codesigning | grep -q "$NAME"; then
  echo "Identity \"$NAME\" already exists:"; security find-identity -v -p codesigning | grep "$NAME"; exit 0
fi
TMP="$(mktemp -d)"; trap 'rm -rf "$TMP"' EXIT
cat > "$TMP/openssl.cnf" <<CNF
[req]
distinguished_name = dn
x509_extensions = ext
prompt = no
[dn]
CN = $NAME
O = Grace Medical Clinic
[ext]
basicConstraints = critical,CA:false
keyUsage = critical,digitalSignature
extendedKeyUsage = critical,codeSigning
CNF
openssl req -x509 -newkey rsa:2048 -nodes -days 7300 -keyout "$TMP/key.pem" -out "$TMP/cert.pem" -config "$TMP/openssl.cnf" 2>/dev/null
PASS="$(openssl rand -hex 12)"
openssl pkcs12 -export -inkey "$TMP/key.pem" -in "$TMP/cert.pem" -name "$NAME" -out "$TMP/id.p12" -passout "pass:$PASS" 2>/dev/null
security import "$TMP/id.p12" -k "$HOME/Library/Keychains/login.keychain-db" -P "$PASS" -T /usr/bin/codesign -T /usr/bin/security >/dev/null
echo "Trusting the certificate for code signing (macOS may ask for your Mac password)..."
security add-trusted-cert -r trustRoot -p codeSign -k "$HOME/Library/Keychains/login.keychain-db" "$TMP/cert.pem"
echo; echo "Done. Use it like this:"
echo "  SIGN_ID=\"$NAME\" packaging/mac/build-dmg.sh"
security find-identity -v -p codesigning | grep "$NAME" || true
#JJQR - END
