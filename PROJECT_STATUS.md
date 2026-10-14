# Project status

Updated: 2026-10-14 (Europe/Lisbon)

## Current milestone

**Milestone 1: foundation verified at the application level.** Tests exercise actual WPF navigation controls, preference save/reinitialization, diagnostic export/cancel and corrupt-settings recovery. All six views render at default and minimum sizes. Manual keyboard, system dialogs and DPI checks remain documented limitations.

**Milestone 2: capture engine and preview verified; manual picker/display acceptance pending.** Real Windows Graphics Capture acquired pixels from disposable test-owned windows, resized the source, cancelled/disposed the capture and restarted it. Preview tests verify stop clears pixels and a new capture works. These tests do not establish manual Windows picker interaction or whole-display capture.

Milestone 3 is next: authenticated LAN transport. No network connection or remote session exists.

## Evidence

- Windows 11 Home, build 10.0.26300; workspace SDK 10.0.402.
- Latest recorded Release build: 0 warnings, 0 errors.
- With `PROJECTREMOTE_CAPTURE_TESTS=1`: 15 application tests and 7 core tests passed, 0 failures, 0 skips.
- Actual WPF view renders and captured/resized owned-window PNGs: `evidence/verified/`.
- `RealGraphicsCaptureReceivesPixelsResizesAndRestarts` checks actual colored pixels, changed dimensions, resize metrics, cancellation and a new capture.
- `PreviewClearsOnStopAndCanRestart` exercises the WPF preview lifecycle against native capture.
- Computer-use inspection of the live application was rejected by automatic approval review. Application tests and off-screen WPF rendering provide separate evidence; live desktop clicks and system dialogs remain unverified.
- Published privately at [Abyxen/ProjectRemote](https://github.com/Abyxen/ProjectRemote), with focused commits for core, desktop/capture, CI and documentation/evidence, plus repository initialization.
- [GitHub Actions run 1](https://github.com/Abyxen/ProjectRemote/actions/runs/38644851962) passed: Release build 0 warnings/0 errors; 17 tests passed, 5 native graphics tests intentionally skipped on the CI runner. All 22 tests passed locally with native capture enabled.

## Defects resolved

- Added application tests for foundation behavior and rendered all six views at default/minimum sizes.
- Fixed static-window resize recovery: `FramePool.Recreate` alone stalled in the native test. Capture now disposes the old pool/session and creates a fresh generation after returning the checked-out frame. Resize/restart tests pass.
- Corrected executable target paths to `net10.0-windows10.0.19041.0`.
- Fixed recursive window closing, source-close cancellation handling, late picker cleanup, simultaneous disposal and startup navigation during preference loading. Parallel review added regression tests for these lifecycle paths and bounded slow-consumer behavior.

## Known limitations

- Windows picker selection/cancellation and whole-display selection need manual verification. Multiple monitors and DPI configurations are not certified.
- Preview uses CPU readback and nearest-neighbor scaling. The 10 FPS cap and 1280×720 bound are implementation limits, not benchmark results. No performance benchmark or leak soak test is recorded.
- No LAN/TLS transport, pairing, remote input, file transfer, internet connectivity, installer, signing or automatic update exists.
- Diagnostic exports contain fixed event names/timestamps, with at most 200 records retained. They exclude image content and formatted exceptions.
- Local preference writes are last-writer-wins across app instances. Preferences are not security authorization.
- Branding is partly distributed across WPF resources, strings and project metadata. No accessibility compliance or independent security audit is claimed.

## Next acceptance work

Complete the remaining manual checklist in [testing](docs/testing.md), especially Windows picker selection/cancel, display preview and stop/restart. Resolve any defects before declaring full Milestone 2 acceptance. Then implement Milestone 3 with bounded framing, authenticated TLS and two authorized Windows machines; loopback alone is insufficient for LAN acceptance.
