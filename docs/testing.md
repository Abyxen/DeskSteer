# Testing

## Current 0.4.0 input candidate

Automated candidate validation passed: 128 tests passed and six native capture tests were intentionally skipped. Sixteen native input tests use a recording backend without real SendInput. Real keyboard/mouse, wheel, foreground targeting, held-state release, revoke/stop and geometry invalidation require the [two-computer input checklist](input-test.md).

The [DeskSteer 0.3.1 CI run](https://github.com/Abyxen/DeskSteer/actions/runs/38702563482) passed for the branding release. It is historical evidence and does not validate the 0.4 input extension.

## Historical DeskSteer branding validation

The renamed source passed the complete native-enabled Release suite: **84 tests (27 application, 7 core, 50 transport), zero failures/skips and zero warnings/errors**, before the final 0.3.1 version-metadata bump. The DeskSteer 0.3.1 package was built after a successful validation run: 78 tests passed (21 application, 7 core, 50 transport), six native checks skipped. Historical 0.3.1 archive SHA-256: `7c32293c65b9e43f9884b07c3cb1eba8519bc7205241ec4c3c9de261f3877915`. The later 0.3.1 branding CI passed; its link is recorded above. Earlier acceptance and artifacts below are historical and do not establish a new two-computer test of the branded rebuild.

## Historical accepted 0.3.0 LAN milestone

The complete native-enabled Release run passed with **83 tests: 27 application, 7 core and 49 transport; zero failures and zero skips**. Compilation succeeded with zero warnings/errors. The run includes actual test-owned Windows Graphics Capture → JPEG → TLS → viewer decode, rather than substituting generated frames for the native pipeline. Eight LAN UI checks include the default approval prompt with a 64-character viewer name. See [project status](../PROJECT_STATUS.md) for the recorded run and handoff artifacts.

A separately added backpressure regression also passed after that run. There are therefore 84 checks with passing evidence across the full 83-test run and the separate one-test run; that original evidence was not a combined run. The subsequent full 84-test branding run is recorded above.

Historical [0.3 Windows CI run](https://github.com/Abyxen/DeskSteer/actions/runs/38700955338) passed 77 tests: 21 application, 7 core and 49 transport. Six native graphics tests were intentionally skipped in CI. These results describe the earlier head; they do not validate the renamed source or new package.

The historical accepted self-contained Windows x64 archive the accepted 0.3.0 candidate (original branding) was built with .NET runtime 10.0.13. Its extraction/startup smoke test passed under its original branding; this is not the DeskSteer 0.3.1 archive. Size: 72,214,743 bytes; SHA-256: `5eda557439d536951020334ca5a900cfd7b37716b0713abf2b6ec4355312128a`.
Transport/JPEG checks cover malformed/oversized/out-of-order messages, wrong pin/code, explicit approval/decline/timeout, no frame production before approval, cancellation/disposal, session expiry and bounded JPEG validation. WPF integration checks exercise real Host/Connect workflows and cleanup. Native tests capture only disposable owned windows. Loopback tests establish the local TLS pipeline; they do not establish two-computer LAN/firewall behavior.

Initial TLS runs exposed a Schannel incompatibility with the generated ECDSA key. The corrected temporary Windows UserKeySet import passed the full run above. No certificate/trust-store installation is required.

## Historical 0.2 evidence

On Windows 11 Home 10.0.26300 with SDK 10.0.402, the 0.2 Release build passed with zero warnings/errors and all 22 local tests passed: 15 application and 7 core, with native capture enabled and no skips. Rendered views and captured PNGs are in `evidence/verified/`.

Core checks cover navigation, preference defaults/persistence, corrupt/oversized settings and cancelled-save cleanup. WPF checks exercise actual controls, all destinations, preference save/reinitialization, allowlisted diagnostics/cancel and rendering at 1140×740 and 820×580. Native tests verify actual pixels, resize, cancellation, source closure, slow-consumer latest-frame behavior and stop/restart. Regressions also cover disposal before enumeration/concurrent disposal, late/cancelled picker results, selection failure and repeated preview starts/stops. Native tests capture only disposable windows they create.

Historical [GitHub Actions run 1](https://github.com/Abyxen/DeskSteer/actions/runs/38644851962) passed the 0.2 Windows Release build, 7 core tests and 10 application tests; five interactive graphics tests were skipped there. These counts/results do not describe the current 0.3 suite.

The user confirmed Windows-picker cancellation, window/display preview, Stop clearing the image and restart on the current machine. Computer-use inspection was rejected by automatic approval review, so that manual evidence is user-reported separately from automated tests.

## Reproduce current tests

```powershell
./build.ps1
```

Native capture requires an interactive Windows graphics desktop and is skipped by default. To enable it and write disposable test-content evidence:

```powershell
$env:DESKSTEER_CAPTURE_TESTS = '1'
$env:DESKSTEER_EVIDENCE_DIR = Join-Path $PWD 'evidence/local-run'
./build.ps1
Remove-Item Env:DESKSTEER_CAPTURE_TESTS
Remove-Item Env:DESKSTEER_EVIDENCE_DIR
```

TRX files go to ignored `artifacts/test-results`. Evidence for current runs should use a new directory; do not relabel historical renders as new validation. For a self-contained candidate, `./package.ps1` runs the build/test helper before publishing/archiving; see [setup](development-setup.md).

## Acceptance and compatibility

Milestones 1/2 have application/native evidence and user-reported picker/display acceptance. Extended checks remain for keyboard focus, native save dialogs, minimum/maximized layout, multiple monitors and 100%/150%/200% scaling. Off-screen WPF rendering does not establish these configurations.

The user explicitly reported "All two-computer LAN checks passed" for the 0.3 candidate on two authorized Windows computers. This accepts Milestone 3 against the [LAN checklist](lan-test.md), including changing pixels, credential rejection, approval/timeout, stop/disconnect, closure, network interruption and restart. This is user-reported acceptance, not agent inspection; OS versions and session duration were not provided. Milestone 4 now has an implemented input candidate; real input acceptance remains pending. No latency/throughput benchmark, resource-leak soak, broad Windows compatibility certification or independent security audit is claimed.
