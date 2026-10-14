# Architecture

The WPF application composes navigation, preferences, diagnostics and a capture view through Microsoft.Extensions.DependencyInjection. Plain .NET Core exposes navigation/storage and the `IScreenCapture` contract. The separate Windows Capture project owns WinRT and Direct3D resources; WPF receives managed BGRA frames rather than native surfaces.

```text
App composition root
  ├── MainWindow → NavigationState → PreferencesStore → local JSON
  ├── CaptureView → ICaptureSelection → Windows Graphics Capture picker
  │      └── IScreenCapture → WindowsScreenCapture → bounded latest frame → WPF bitmap
  └── ILoggerProvider → fixed event IDs → Diagnostics → JSON export
```

Capture uses a free-threaded Direct3D11 frame pool with two native buffers. A bounded arrival signal coalesces notifications; processing waits 100 ms between iterations. CPU readback copies BGRA pixels, then nearest-neighbor scaling preserves aspect ratio within 1280×720. The managed channel has one slot and drops the oldest queued frame when the consumer falls behind. Metrics count published frames, dropped frames and resize generations. The FPS metric is an observed average, not a performance guarantee.

Native surfaces are checked before pool allocation and on size changes; the limit is 16,777,216 pixels. The implementation retains native-resolution buffers/readback even when the preview is smaller, so preview dimensions alone do not define total memory use.

A size change disposes the checked-out frame, then the old pool/session, and creates a fresh capture generation. The native resize test found that `Recreate` alone stalled on a static source; replacing the generation restores its initial frame. Cancellation and source closure cancel the producer. A capture object permits one frame consumer; restarting creates a new object. The view clears its bitmap when stopping and disposes capture resources when leaving the page or closing the application.

The normal UI uses the Windows selection picker and keeps the capture border/cursor defaults. Test code obtains capture items only for disposable windows it creates. It verifies native acquisition independently of the manual picker workflow.

Diagnostics retain at most 200 timestamp/fixed-event records. Formatted payloads and exception messages are excluded; capture pixels are never exported. Branding currently lives in `App.xaml`, application strings and project metadata.

## Planned remote path

No transport or input implementation exists yet:

```text
Capture → encoder → bounded authenticated TLS messages → decoder → viewer
Host approval → input permission checks ← viewer input
Disconnect → cancellation and disposal on both endpoints
```

Milestone 3 must define message bounds, identities and trust policy before transport. Device IDs identify endpoints but do not authenticate them. Internet coordination, WebRTC and relays belong to later milestones.
