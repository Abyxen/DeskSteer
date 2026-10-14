# Testing

## Recorded local evidence

SDK 10.0.402 on Windows 11 Home 10.0.26300: Release build succeeded with 0 warnings/0 errors. With native capture enabled, 15 application tests and 7 core tests passed, without failures or skips. TRX files are in ignored `artifacts/test-results`; rendered views and capture PNGs are in `evidence/verified/`.

Core tests cover navigation validation/notifications, preference defaults/persistence, corrupt and oversized input, and cancelled-save cleanup. Application tests use actual WPF controls on an STA dispatcher: all navigation destinations, saving/reinitializing preferences, allowlisted export/cancel, corrupt-settings feedback and rendering all six pages at 1140×740 and 820×580. Off-screen rendering does not verify native input, Windows dialogs or DPI configurations.

Native tests create visible disposable windows containing test-owned content. `RealGraphicsCaptureReceivesPixelsResizesAndRestarts` checks actual colored pixels, native resize recovery, cancellation and a fresh capture. `PreviewClearsOnStopAndCanRestart` uses real capture with the WPF preview. The frame scaler also checks bounds, aspect ratio and pixels. Test capture does not target the user's applications or personal desktop.

## Reproduce

For ordinary build/tests:

```powershell
./build.ps1
```

Native capture needs an interactive Windows graphics session and is skipped by default on unsuitable CI/headless sessions. To include it and save evidence:

```powershell
$env:PROJECTREMOTE_CAPTURE_TESTS = '1'
$env:PROJECTREMOTE_EVIDENCE_DIR = Join-Path $PWD 'evidence/local-run'
./build.ps1
Remove-Item Env:PROJECTREMOTE_CAPTURE_TESTS
Remove-Item Env:PROJECTREMOTE_EVIDENCE_DIR
```

A default test run with capture skips establishes only the foundation/scaler checks. No network, remote input, throughput benchmark or resource-leak soak evidence is claimed.

## Manual checks pending

Computer-use inspection of the live application was rejected by automatic approval review. The following remain manual checks:

1. Launch, inspect all six destinations and exercise keyboard Tab/Enter focus.
2. Save preferences, close/relaunch and confirm the start page; exercise the native diagnostic save dialog and cancellation.
3. Check minimum size, maximization and 100%/150%/200% scaling.
4. In Host session, open the Windows picker, cancel it, reopen and select an owned window. Verify actual content and capture status.
5. Resize/move/close that window; verify recovery and stop. Select again, then leave Host session and close the app during capture.
6. Select a display; check preview and stop/restart. Repeat across available monitors and DPI configurations.

Milestone 1 has application-level verification. Full Milestone 2 acceptance awaits the picker/display checks. Milestone 3 LAN acceptance requires two authorized Windows computers; loopback alone cannot establish it.
