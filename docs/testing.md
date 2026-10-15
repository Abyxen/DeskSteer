# Testing

## Current 0.3 candidate

The complete native-enabled Release run passed with **83 tests: 27 application, 7 core and 49 transport; zero failures and zero skips**. Compilation succeeded with zero warnings/errors. The run includes actual test-owned Windows Graphics Capture → JPEG → TLS → viewer decode, rather than substituting generated frames for the native pipeline. Eight LAN UI checks include the default approval prompt with a 64-character viewer name. See [project status](../PROJECT_STATUS.md) for the recorded run and handoff artifacts.

Transport/JPEG checks cover malformed/oversized/out-of-order messages, wrong pin/code, explicit approval/decline/timeout, no frame production before approval, cancellation/disposal, session expiry and bounded JPEG validation. WPF integration checks exercise real Host/Connect workflows and cleanup. Native tests capture only disposable owned windows. Loopback tests establish the local TLS pipeline; they do not establish two-computer LAN/firewall behavior.

Initial TLS runs exposed a Schannel incompatibility with the generated ECDSA key. The corrected temporary Windows UserKeySet import passed the full run above. No certificate/trust-store installation is required.

## Historical 0.2 evidence

On Windows 11 Home 10.0.26300 with SDK 10.0.402, the 0.2 Release build passed with zero warnings/errors and all 22 local tests passed: 15 application and 7 core, with native capture enabled and no skips. Rendered views and captured PNGs are in `evidence/verified/`.

Core checks cover navigation, preference defaults/persistence, corrupt/oversized settings and cancelled-save cleanup. WPF checks exercise actual controls, all destinations, preference save/reinitialization, allowlisted diagnostics/cancel and rendering at 1140×740 and 820×580. Native tests verify actual pixels, resize, cancellation, source closure, slow-consumer latest-frame behavior and stop/restart. Regressions also cover disposal before enumeration/concurrent disposal, late/cancelled picker results, selection failure and repeated preview starts/stops. Native tests capture only disposable windows they create.

Historical [GitHub Actions run 1](https://github.com/Abyxen/ProjectRemote/actions/runs/38644851962) passed the 0.2 Windows Release build, 7 core tests and 10 application tests; five interactive graphics tests were skipped there. These counts/results do not describe the current 0.3 suite.

The user confirmed Windows-picker cancellation, window/display preview, Stop clearing the image and restart on the current machine. Computer-use inspection was rejected by automatic approval review, so that manual evidence is user-reported separately from automated tests.

## Reproduce current tests

```powershell
./build.ps1
```

Native capture requires an interactive Windows graphics desktop and is skipped by default. To enable it and write disposable test-content evidence:

```powershell
$env:PROJECTREMOTE_CAPTURE_TESTS = '1'
$env:PROJECTREMOTE_EVIDENCE_DIR = Join-Path $PWD 'evidence/local-run'
./build.ps1
Remove-Item Env:PROJECTREMOTE_CAPTURE_TESTS
Remove-Item Env:PROJECTREMOTE_EVIDENCE_DIR
```

TRX files go to ignored `artifacts/test-results`. Evidence for current runs should use a new directory; do not relabel historical renders as new validation. For a self-contained candidate, `./package.ps1` runs the build/test helper before publishing/archiving; see [setup](development-setup.md).

## Acceptance and compatibility

Milestones 1/2 have application/native evidence and user-reported picker/display acceptance. Extended checks remain for keyboard focus, native save dialogs, minimum/maximized layout, multiple monitors and 100%/150%/200% scaling. Off-screen WPF rendering does not establish these configurations.

Milestone 3 requires the full [two-computer LAN checklist](lan-test.md), including actual changing pixels, wrong credentials, decline/approval timeout, stop/disconnect, page/app closure, network interruption and fresh-session restart. No remote-input acceptance is applicable: that feature is absent. No latency/throughput benchmark, resource-leak soak, broad Windows compatibility certification or independent security audit is claimed.
