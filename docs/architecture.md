# Architecture

DeskSteer 0.4.0 extends the LAN architecture with optional remote input and composes WPF navigation/preferences, local capture and view-only LAN sharing. `DeskSteer.Core` holds navigation/storage, `IScreenCapture`, encoded-frame models and the shared bounded JPEG header validator. `DeskSteer.Capture` owns WinRT/Direct3D resources. `DeskSteer.Transport` owns private-address policy, certificate trust, framing and session lifetime. `DeskSteer.App` owns WPF views, JPEG encode/decode and explicit approval.

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

Milestone 3 was accepted from user-reported two-computer checks on the earlier 0.3.0 build. The renamed 0.3.1 source passed its full native-enabled local suite; its package verification/CI are tracked separately. The branding rebuild is not a new two-computer acceptance claim. Milestone 4 now implements a remote-input candidate, pending full validation and real two-computer acceptance. Audio, file transfer, persistent device trust, internet coordination, WebRTC and relays remain unimplemented.

## Input authority

The unchecked host control option uses the Windows picker and exposes no input sink. Opting in selects the primary display and constructs DeskSteer.Input.WindowsInputSink. Viewing approval precedes a second keyboard/mouse approval. HostControlSession serializes authority transitions, validates commands, rate-limits input to 200/s and releases remotely owned keys/buttons on revoke/stop. Geometry changes invalidate the input sink rather than remapping an existing control session.

The viewer preview must be focused. It maps through the actual letterboxed image area, paces movement to 33 ms and feeds a 32-command queue. Focus/window deactivation releases control. A topmost host indicator offers revoke-while-viewing and stop-all; it registers Ctrl+Alt+F12 when available. The app runs asInvoker and uses ordinary SendInput under Windows UIPI restrictions. Keyboard targets the foreground application; mouse scope is the primary display. Recording tests verify native command construction without real input injection.
