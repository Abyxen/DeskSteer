# Changelog

## 0.3.1 — 2026-10-15 — DeskSteer branding rebuild

- Renamed the product, UI, solution, projects, namespaces, executable and configuration variables to DeskSteer; entry points are `DeskSteer.slnx` and `DeskSteer.App.exe`.
- Renamed GitHub hosting to Abyxen/DeskSteer; public visibility is confirmed.
- The renamed source passed the full native-enabled Release suite: 84 tests (27 application, 7 core, 50 transport), zero failures/skips and zero build warnings/errors, before the final version-metadata bump.
- Built `DeskSteer-LAN-0.3.1-win-x64-candidate.zip` after 78 package-validation tests passed with six native checks skipped; its checksum is recorded in the LAN guide. Earlier 0.3.0 acceptance/artifact records retain their historical identity.
- Milestone 3 remains accepted from user-reported two-computer checks on the earlier 0.3.0 build. Milestone 4 is design only; remote input is not implemented.

## 0.3.0 — 2026-10-15 — view-only LAN candidate

- Added private IPv4/loopback TCP transport with pinned TLS 1.2/1.3, ephemeral certificate and random expiring pairing code.
- Added explicit host approval before capture streaming, one-viewer sessions, deadlines, heartbeats and cancellation/disposal.
- Added Host/Connect WPF workflows with active indicators, stop/disconnect and transient connection details.
- Added bounded JPEG quality-75 streaming, dimension validation before WIC decoding and received-frame statistics.
- Added versioned framed messages with strict sequence/type/length validation and bounded authentication attempts.
- Added two-computer LAN setup/acceptance guide. Release build passed with zero warnings/errors and all 83 native-enabled local tests passed, including actual capture → JPEG → TLS → viewer decode. The user reported all LAN checks passed on two authorized Windows computers, accepting Milestone 3; the agent did not inspect that session and no OS versions/duration were supplied.
- Added a separate passing backpressure regression after the 83-test full run; 84 checks have passing evidence across those runs, not a combined 84-test run.
- Windows CI passed 77 tests with six native graphics checks skipped; local native evidence remains separate.
- Packaged a self-contained Windows x64 candidate with .NET runtime 10.0.13; extraction/startup smoke testing passed.
- Milestone 4 is in design. No automatic firewall changes, remote input, audio, file transfer or internet relay.

## 0.2.0 — 2026-10-14 — local capture preview

- Added an `IScreenCapture` boundary, Windows Graphics Capture implementation and system selection picker.
- Added Host session preview with stop/restart, cancellation, source-close handling and disposal.
- Bounded processing to 10 FPS, preview to 1280×720 and native surfaces to 16 megapixels; retained two native buffers and one queued latest frame.
- Fixed static-window resize recovery by replacing the capture pool/session generation.
- Added actual WPF control/render tests and native capture/preview lifecycle tests using disposable owned windows.
- Verified Release build and local tests; the user confirmed manual picker/window/display, stop and restart acceptance on the current machine. No networking or remote input is implemented.
- Added target-close, bounded-queue, concurrent-disposal and picker-race regression coverage; 22 tests pass locally with native capture enabled.
- Published focused commits to private GitHub hosting and verified Windows CI (17 passed, 5 graphics tests intentionally skipped).

## 0.1.0 — 2026-10-14 — foundation preview

- Added .NET 10 WPF application shell, navigation and original charcoal/teal visual resources.
- Added local startup-page preferences with bounded reads and cancellable saves.
- Added DI composition, structured in-memory diagnostics and JSON export.
- Added seven foundation tests, build helper and Windows CI definition.
- Added milestone, protocol, architecture, security and setup documentation.
- Build/tests verified; interactive UI acceptance was pending at this version. Remote desktop functionality was not implemented.
