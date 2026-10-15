# Threat model

Version 0.3.0 adds a private-LAN, view-only transport. This describes implemented boundaries, not a security audit or completed two-device acceptance.

| Asset / threat | Current response | Remaining scope |
|---|---|---|
| Screen pixels shared without approval | Windows picker chooses source; matching pairing token plus explicit host approval required before frame enumeration | User verifies intended viewer; test acceptance on two computers |
| Host impersonation / LAN interception | TLS 1.2/1.3; exact SHA-256 pin, certificate name/lifetime/usage and custom-trust chain checks | Fingerprint must be verified separately; compromised endpoints/trusted channel are outside this boundary |
| Unwanted viewer / stolen code | Fresh 32-byte secret, five-minute expiry, five-attempt limit, explicit approval; one approved viewer | Viewer name is claimed, not identity; do not publish pairing details |
| Reusing old session details | Fresh ephemeral certificate and code on each new host; one approved session then listener closes | No persistent trusted-device identity or pairing store |
| Malformed frames / resource exhaustion | Header/type/length/sequence checks before payload allocation; 2 MiB frame payload; JPEG header/dimension validation before WIC decode; 1280×720 preview limit | No independent fuzz/security audit or long-duration resource benchmark |
| Slow/disconnected peer | Bounded latest-frame buffering, awaited writes/UI dispatch, timeouts, heartbeats, cancellation and one-hour session expiry | LAN conditions and recovery require two-device validation |
| Keystrokes and mouse control | No capture/transmission or protocol messages for remote input | A future milestone must add separate control approval and authorization |
| Public exposure | Explicit private IPv4/loopback binding; no wildcard/public/DNS/IPv6 endpoint, relay or automatic firewall change | User configures Private firewall access; no port forwarding |
| Sensitive diagnostics | Fixed event names/timestamps; no pixels, endpoint, token, pin, viewer name or formatted exceptions | Recheck allowlist whenever events are added |
| Local preference corruption/modification | Bounded JSON input and recoverable error; preferences contain a startup-page flag | Local same-user modification is outside the prototype's protection; never use preferences as authorization |
| Certificate/key exposure | Fresh session identity; in-memory PFX round trip into Schannel-compatible UserKeySet without PersistKeySet; certificate disposal owns temporary Windows key backing cleanup; no application key files | No guarantee of secure erasure in managed/OS memory; compromised host process is outside this boundary |

Stop/disconnect, leaving session pages or closing the app cancels work and disposes session/capture resources. The system capture border remains enabled. There is no unattended access, UAC bypass, hidden session, persistence installation, internet service or remote input.
