# Changelog

## 0.2.0 — 2026-10-14 — local capture preview

- Added an `IScreenCapture` boundary, Windows Graphics Capture implementation and system selection picker.
- Added Host session preview with stop/restart, cancellation, source-close handling and disposal.
- Bounded processing to 10 FPS, preview to 1280×720 and native surfaces to 16 megapixels; retained two native buffers and one queued latest frame.
- Fixed static-window resize recovery by replacing the capture pool/session generation.
- Added actual WPF control/render tests and native capture/preview lifecycle tests using disposable owned windows.
- Verified Release build and local tests; manual picker/display acceptance remains pending. No networking or remote input is implemented.
- Added target-close, bounded-queue, concurrent-disposal and picker-race regression coverage; 22 tests pass locally with native capture enabled.
- Published focused commits to private GitHub hosting and verified Windows CI (17 passed, 5 graphics tests intentionally skipped).

## 0.1.0 — 2026-10-14 — foundation preview

- Added .NET 10 WPF application shell, navigation and original charcoal/teal visual resources.
- Added local startup-page preferences with bounded reads and cancellable saves.
- Added DI composition, structured in-memory diagnostics and JSON export.
- Added seven foundation tests, build helper and Windows CI definition.
- Added milestone, protocol, architecture, security and setup documentation.
- Build/tests verified; interactive UI acceptance was pending at this version. Remote desktop functionality was not implemented.
