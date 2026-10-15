# Security and privacy

## Implemented in DeskSteer 0.3.1

- Windows picker explicitly selects a source; the normal capture border/cursor defaults remain. Local preview stays local. **Approved LAN sharing transmits the selected screen pixels** to the approved viewer over TLS.
- TLS 1.2/1.3 validates an exact SHA-256 certificate pin, expected server name, validity, server-auth usage and a custom-trust chain. The fingerprint must be verified through a separate trusted channel. There is no accept-any-certificate callback.
- Each host creates a fresh 32-byte pairing secret and session certificate. A matching unexpired secret is required before explicit host approval. The claimed viewer name is not authenticated personal identity.
- Pairing expires after five minutes; approval times out after 30 seconds. Five failed attempts end the listener. Each host accepts one approved session, at most one hour. Restart requires new connection details.
- A specific private IPv4/loopback address is required. No public/wildcard binding, DNS/IPv6 endpoint, firewall modification, router forwarding or internet relay is implemented.
- Protocol types, version, sequence and bounded lengths are checked before payload allocation. Frame payloads are at most 2 MiB including metadata; JPEG structure/dimensions are checked before WIC decode, then decoded dimensions are checked again. Preview is at most 1280×720; native capture surfaces are at most 16 megapixels.
- Bounded capture queues, 10 FPS caps, deadlines, heartbeats and cancellation limit resource accumulation. Stop/disconnect, page departure and application closure terminate session work.
- Settings input is bounded to 4096 bytes; atomic replacement/cancellation cleanup preserve preferences. The only preference is a startup-page flag, not authorization or credentials.
- Diagnostics export at most 200 fixed-name events/timestamps. No pixels, endpoints, pins, pairing secrets, viewer names, machine identity or formatted exceptions are included.

Session certificates are generated per host. Schannel-compatible private-key loading uses an in-memory PFX round trip with `UserKeySet`, without `PersistKeySet`; certificate disposal owns cleanup of temporary Windows key backing. The app does not save PFX/key files or install a trusted root. This does not promise secure erasure of managed strings, process memory or all OS-managed backing. Pairing details are cleared from the UI when sessions end.

## Remaining boundaries

Milestone 3 was accepted from user-reported two-computer checks on the earlier 0.3.0 build. DeskSteer 0.3.1 is its branding rebuild; no new two-computer inspection is claimed. Local test results and failures are tracked in [project status](../PROJECT_STATUS.md); no independent security audit, fuzz campaign or production guarantee is claimed.

Remote keyboard/mouse input, audio, file transfer, persistent device identity/trust, unattended access, startup registration, privilege elevation, installers, signed updates and internet traversal are unimplemented. Compromised endpoints and compromised channels used to verify fingerprints are outside this prototype's protection. Local preferences are not encrypted and use last-writer-wins across app instances. Normal launch stores them under `%LOCALAPPDATA%/DeskSteer`; the build helper uses workspace test data.

Dependencies are pinned in project files: Microsoft.Extensions.DependencyInjection/Logging 10.0.0 (MIT), xunit 2.9.3 (Apache-2.0), xunit.runner.visualstudio 3.1.5 (Apache-2.0), Microsoft.NET.Test.Sdk 17.14.1 (MIT). These versions are not a claim that they are newest; review package advisories before distribution.

See [protocol](protocol.md), [threat model](threat-model.md) and [two-computer acceptance](lan-test.md).
