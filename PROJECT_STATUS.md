# Project status

Updated: 2026-10-15 (Europe/Lisbon)

## Current milestone

Milestone 1 has application-level verification. Milestone 2 local capture acceptance was achieved on this machine: native tests check owned-window pixels, resize, cancellation and restart; the user confirmed Windows-picker cancellation, window/display preview, Stop and restart.

**Milestone 3: LAN viewing acceptance achieved.** Version 0.3.1 includes an explicitly started private-IPv4 listener, pinned TLS, temporary pairing, host approval, bounded JPEG streaming and a real WPF viewer. The user confirmed that all supplied two-computer LAN checks passed, including live content, approval, Stop/disconnect and a fresh connection. This is user-reported acceptance; Windows versions and duration were not supplied. Milestone 4 separate input authorization is now being designed.

## Latest verification

- Windows 11 Home build 10.0.26300; workspace .NET SDK 10.0.402.
- Release build: **0 warnings, 0 errors**.
- Native-enabled suite: **84 passed, 0 failed, 0 skipped** (27 application, 7 core, 50 transport tests) after the DeskSteer rename.
- Real pipeline: disposable test-owned Windows window → Windows Graphics Capture → JPEG → actual TLS loopback → viewer image decoder. Approximate decoded pixels and bounded dimensions verified.
- Actual WPF integration checks cover approval decline without capture, cancellation during a late picker result, viewing/disconnect cleanup, navigation/closing cleanup, secret clearing, and the default approval dialog with a 64-character name.
- Transport checks cover wrong pin/token, expired/rotated credentials, explicit approval, idle heartbeats, partial/stalled handshakes, bounded protocol parsing, Unicode names and session expiry without an active frame reader.
- WPF views rendered at 1140×740 and 820×580. Local TRX files are in ignored `artifacts/test-results`; candidate renders are in ignored `evidence/lan-candidate`.
- [0.3 GitHub CI](https://github.com/Abyxen/DeskSteer/actions/runs/38700955338) passed: 77 tests passed and 6 native graphics tests intentionally skipped; Release build passed. The subsequent native-enabled DeskSteer run includes the slow-receiver regression.

## Fixes in this increment

- Windows Schannel rejected the original ephemeral certificate key. A temporary Windows user key container now supports TLS; certificate disposal removes its backing key, and no certificate/trust-store installation is performed. Pin/name/validity/EKU/chain checks remain enabled.
- Viewer expiry closes TCP even when no consumer starts or a frame iterator is suspended.
- One bounded baseline-JPEG validator is shared by transport and decoder; duplicate/unsupported frame headers and trailing images are rejected before image decoding.
- Viewer fields collapse while receiving frames, keeping Disconnect and the preview visible. Approval text wraps and sizes to preserve the Decline control for long names.
- Navigation and application closing await capture/host/viewer cleanup. Pairing data and preview pixels clear when a session ends.

## Remaining work and limits

- The [two-computer LAN checklist](docs/lan-test.md) passed according to the user. The self-contained candidate ZIP is built, extracted and startup-smoke-tested: 72,214,743 bytes; SHA-256 `5eda557439d536951020334ca5a900cfd7b37716b0713abf2b6ec4355312128a`.
- The view-only prototype is limited to private IPv4/loopback, one approved viewer, 1280×720 and at most 10 sent FPS. These bounds are implementation limits, not benchmark results.
- No remote input, internet coordination/relay, device registration, file transfer, unattended access, installer, signing or automatic update exists. Later milestones remain open.
- No performance benchmark, long-duration leak soak, broad Windows/DPI compatibility certification or independent security audit is claimed.
- Computer-use inspection of the desktop was rejected previously. In-process WPF/native tests and user-reported picker checks are separate evidence.
- Diagnostics retain at most 200 fixed event names/timestamps and exclude screen content, endpoints, viewer names, secrets and formatted exceptions. Preferences contain only a startup-page setting.

## Next concrete action

Begin Milestone 4 permission-gated remote input after the accepted LAN milestone. Continue building, testing, fixing and committing each verified increment; do not mark the overall project complete from this candidate.

## DeskSteer identity

The repository is renamed to Abyxen/DeskSteer. Application identity, namespaces, assemblies, project paths, solution, environment variables, settings directory and TLS hostname now use DeskSteer. GitHub public visibility is confirmed. The 0.3.1 self-contained ZIP is built: SHA-256 `7c32293c65b9e43f9884b07c3cb1eba8519bc7205241ec4c3c9de261f3877915`, 72,214,716 bytes.
