/* JJQR - AI (Nov 21, 2026)
 * Microsoft.Identity.Client.NativeInterop ships msalruntime as a macOS (not Mac Catalyst)
 * dylib and a MAUI app that references it is killed at launch on macOS 27
 * (SIGKILL / Code Signature Invalid). The clinic node does not use MSAL. This
 * stub is a last-resort replacement if the dylib still lands in the bundle.
 */
