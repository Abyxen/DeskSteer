# LAN protocol v1

Implemented in 0.3.0 by `RemoteDesktop.Transport`. This is a candidate protocol for one view-only LAN session; two-computer acceptance remains pending.

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
| 4 | 2 | Version, currently 1 |
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

Viewer names contain 1–64 UTF-16 code units and at most 256 UTF-8 bytes, excluding control/format/line-separator characters. Only authentication is accepted before host approval. During viewing the viewer may send heartbeat/disconnect, never input events.

Frame metadata: signed 32-bit width, signed 32-bit height, signed 64-bit nonnegative source timestamp ticks, then JPEG. Width is 1–1280 and height 1–720. The JPEG itself is at most 2 MiB minus the 16 metadata bytes. Its validated header dimensions must match the envelope before WIC decode; the shared header validator rejects unsupported/ambiguous structures and oversized decompression targets. The decoder verifies output dimensions again.

Encoding uses JPEG quality 75. Capture processing and sending are capped at 10 FPS. Two native capture buffers and a one-frame latest-value channel bound capture buffering; awaited writes and UI dispatch provide backpressure. These are implementation limits, not measured network latency/throughput guarantees.

Remote input, audio, file transfer, persistent trust, discovery, relays and NAT traversal have no message types in this version.
