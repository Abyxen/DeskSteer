# LAN protocol v2

DeskSteer 0.4.0 adds separately approved remote input to the earlier LAN transport. Protocol v2 is a candidate under validation; v1 headers are rejected. Both computers must use the same version. User-reported two-computer acceptance applies to the historical 0.3.0 viewing milestone, not this input extension.

## Trust and lifecycle

The listener binds one explicitly selected private IPv4 or loopback address. Public/wildcard addresses, DNS hostnames and IPv6 are rejected. The application does not open firewall rules or router ports.

Each new host creates an ephemeral ECDSA P-256 self-signed server certificate and a cryptographically random 32-byte pairing token. The viewer supplies its SHA-256 certificate fingerprint, verified through a separate trusted channel, before TLS authentication. TLS 1.2/1.3 is enabled. Certificate validation checks the exact pin in constant time, expected server name, validity and server-auth usage, and builds a custom-trust chain using that pinned certificate. It does not blindly accept arbitrary self-signed certificates. Revocation lookup is disabled for this ephemeral pinned trust model.

After TLS, the viewer sends the pairing token and claimed display name. The host checks the token in constant time, checks its expiry and asks for explicit approval. Only approval allows frame enumeration/sending. The display name is not an authenticated personal identity. Decline ends this host session; malformed/invalid attempts are bounded. Each host serves one approved session and then closes its listener. Restart creates a new certificate and token. Schannel compatibility uses a temporary Windows UserKeySet key container through an in-memory PFX round trip, without PersistKeySet. Certificate disposal owns temporary-key cleanup; no certificate/root is installed in the Windows certificate/trust store and the application writes no key/PFX files.

Default deadlines: TLS/initial authentication 10 seconds, host approval 30 seconds, pairing five minutes, session one hour. Read/write operations time out after 15 seconds; both endpoints send heartbeats every three seconds. Five failed attempts end the listener. Cancellation, disconnect, source closure and peer failure terminate processing and release resources.

## Envelope

All integer fields use network byte order (big endian). The 16-byte header is validated before allocating a payload.

| Offset | Size | Field |
|---|---:|---|
| 0 | 4 | ASCII `PRDT` |
| 4 | 2 | Version, currently 2 |
| 6 | 2 | Message type |
| 8 | 4 | Sequence, starts at 1 independently in each direction |
| 12 | 4 | Payload length |

Sequences must match the next expected value; unsupported versions/types, out-of-order values and invalid lengths terminate the exchange. Counters do not wrap. Writes are serialized. TLS protects the connection's integrity; application sequences enforce message order within it.

| Type | Value | Payload |
|---|---:|---|
| Authentication | 1 | 32-byte token followed by strict UTF-8 viewer name; 33–288 bytes total |
| Approved | 2 | Empty |
| Rejected | 3 | Empty |
| Frame | 4 | 16-byte frame metadata followed by JPEG; 20 bytes to 2 MiB total |
| Heartbeat | 5 | Empty |
| Disconnect | 6 | Empty |
| RequestControl | 7 | Empty |
| ControlGranted | 8 | Empty |
| ControlRevoked | 9 | Empty |
| RemoteInput | 10 | Exactly 20 bytes |
| ReleaseControl | 11 | Empty |

Viewer names contain 1–64 UTF-16 code units and at most 256 UTF-8 bytes, excluding control/format/line-separator characters. Only authentication is accepted before host approval. After viewing approval the viewer may request control. Only a control-capable host with separate explicit approval grants authority. A declined/expired/unavailable request produces ControlRevoked. ReleaseControl revokes authority while preserving viewing. Each input command requires current authority, is validated and is limited to 200 commands per host second. Requests are limited to one pending request and one per second. Revocation invalidates late approvals and releases held remote state before subsequent commands may apply.

Frame metadata: signed 32-bit width, signed 32-bit height, signed 64-bit nonnegative source timestamp ticks, then JPEG. Width is 1–1280 and height 1–720. The JPEG itself is at most 2 MiB minus the 16 metadata bytes. Its validated header dimensions must match the envelope before WIC decode; the shared header validator rejects unsupported/ambiguous structures and oversized decompression targets. The decoder verifies output dimensions again.

Encoding uses JPEG quality 75. Capture processing and sending are capped at 10 FPS. Two native capture buffers and a one-frame latest-value channel bound capture buffering; awaited writes and UI dispatch provide backpressure. These are implementation limits, not measured network latency/throughput guarantees.

## Input payload and scope

RemoteInput contains five signed big-endian 32-bit values: kind, X, Y, code, delta. Kinds are Move=1, ButtonDown=2, ButtonUp=3, Wheel=4, KeyDown=5, KeyUp=6. Pointer coordinates are normalized 0–65535. Buttons use codes 1/2/3 for left/right/middle. Wheel delta is a nonzero multiple of 120 within -1200..1200. Keyboard messages use an allowlisted Windows virtual-key code with zero coordinates/delta. Unused fields must be zero as specified by RemoteInputPolicy.

The host serializes authority checks and native input calls. Stop, revoke, disconnect and session cancellation release held remote state. A control-capable host shares its primary display; geometry changes invalidate its sink. Keyboard input targets the foreground application, while pointer coordinates map to the primary display. Ordinary picker capture cannot gain input authority.

The viewer forwards only with approval and focused preview, rejects letterbox margins, paces mouse moves to 33 ms and uses a 32-command queue. Focus/window loss releases control; queue overflow releases rather than accumulating commands. These client-side behaviors supplement host checks and do not replace them.

The native adapter runs asInvoker through SendInput. UIPI may block elevated windows and secure desktops; there is no elevation or bypass. Keys/buttons already physically down at first remote press are ignored. Windows cannot distinguish overlapping physical ownership acquired after a remote down; see the input guide for that limitation.

Audio, file transfer, persistent trust, discovery, relays and NAT traversal have no message types in this version. Real keyboard/mouse behavior still requires the [two-computer input acceptance](input-test.md).
