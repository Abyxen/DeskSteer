# Architecture

Version 0.3.0 composes WPF navigation/preferences, local capture and view-only LAN sharing. `RemoteDesktop.Core` holds navigation/storage, `IScreenCapture`, encoded-frame models and the shared bounded JPEG header validator. `RemoteDesktop.Capture` owns WinRT/Direct3D resources. `RemoteDesktop.Transport` owns private-address policy, certificate trust, framing and session lifetime. `RemoteDesktop.App` owns WPF views, JPEG encode/decode and explicit approval.

```text
App composition root → MainWindow
  ├── NavigationState → PreferencesStore → local JSON
  ├── CaptureView → Windows picker → IScreenCapture → WPF local preview
  ├── LanHostView → Windows picker → selected capture (not yet enumerated)
  │      → private-address TLS listener → token check → explicit host approval
  │      → capture frames → JPEG encoder → bounded protocol frame → TLS
  ├── LanConnectView → pinned TLS/token → approval → bounded JPEG decode → WPF image
  └── fixed diagnostic events → bounded queue → user-selected JSON export
```

Capture uses a free-threaded Direct3D11 frame pool with two native buffers, a coalescing arrival signal and 100 ms processing delay. CPU readback copies BGRA pixels; nearest-neighbor scaling preserves aspect ratio within 1280×720. A one-slot managed channel drops the oldest queued frame when the consumer falls behind. Native surfaces above 16,777,216 pixels are rejected before pool allocation and on size changes. Native-resolution buffers/readback still consume memory even when the preview is smaller.

A size change returns the checked-out frame, disposes its pool/session and starts a fresh generation. Historical native tests showed `Recreate` alone stalled on a static source. Cancellation/source closure stops production; each capture object has one consumer. Leaving a session page or closing the app stops/disposes its resources. The normal UI keeps Windows capture border/cursor defaults; native tests target disposable windows they own.

The LAN host chooses a source before listening, but enumeration/encoding begins only after a valid expiring token and explicit approval. The modeless approval prompt supports cancellation/timeout and labels the viewer name as claimed. A host serves one approved viewer, then closes. Host and viewer expose prominent stop/disconnect controls and active indicators. Local preview and network sharing are separate flows; only approved network sharing transmits pixels.

TLS uses an ephemeral session certificate and an independently verified SHA-256 pin. Windows Schannel requires a compatible key import; the implementation uses an in-memory PFX round trip with `UserKeySet` and no `PersistKeySet`, then disposes the certificate at teardown. Any Windows temporary key backing is lifecycle-managed rather than an application key file. See [protocol](protocol.md) for trust checks, framing, limits and deadlines.

JPEG quality 75 encode/decode runs off the UI thread. The shared baseline-JPEG parser checks dimensions and structure before WIC decode; output dimensions are checked again. Frame payloads including metadata are at most 2 MiB. Sending is capped at 10 FPS; awaited writes and UI dispatch apply backpressure. Observed FPS/byte counters are status information, not benchmark results.

Diagnostics retain at most 200 timestamp/fixed-event records. They omit pixels, endpoints, pins, tokens, viewer names and formatted exceptions. Preferences contain only a startup-page flag. Branding remains distributed across `App.xaml`, strings and project metadata.

Milestone 3 is a candidate awaiting two-computer acceptance. Remote input, audio, file transfer, persistent device trust, internet coordination, WebRTC and relays are unimplemented.
