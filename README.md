# Project Remote

An original Windows remote-desktop application, developed in verified milestones.

**Current build: 0.2.0 capture preview.** The WPF foundation has application-level tests and rendered evidence. Real Windows Graphics Capture receives pixels, handles resize, and stops/restarts in tests using disposable test-owned windows. The Windows selection picker and whole-display selection still need manual acceptance. Networking, authentication, remote input and pairing are not implemented.

## Run

Install the .NET 10 SDK with Windows desktop support, then from this directory:

```powershell
dotnet restore ProjectRemote.slnx --configfile NuGet.Config
dotnet build ProjectRemote.slnx -c Release
dotnet test ProjectRemote.slnx -c Release
dotnet run --project src/RemoteDesktop.App
```

In the supplied workspace, `build.ps1` uses the SDK downloaded to `../../work/dotnet`. Run `./build.ps1 -Launch` to build, test and launch. It isolates build caches and application test data under `../../work`; use a fresh terminal afterward because the script adjusts process environment variables. The SDK is not a system-wide installation.

Open **Host session**, choose a window or display through the Windows picker, and use **Stop capture** to clear the local preview. This preview does not transmit pixels to another computer.

## What is available

- Charcoal WPF shell with six navigation destinations and keyboard-focus styling.
- Saved startup-page preference, bounded settings-file reads, atomic replacement and cancellation cleanup.
- Bounded structured events and JSON diagnostic export.
- Windows Graphics Capture with a local preview, stop/restart, resize recovery and disposal.
- Processing capped at 10 FPS, preview bounded to 1280×720, two native capture buffers and one queued latest frame. Native surfaces above 16 megapixels are rejected.
- Dependency injection, separate core/capture layers, xUnit tests and Windows CI configuration.

`src/RemoteDesktop.Core` owns navigation, preferences and the `IScreenCapture` boundary. `src/RemoteDesktop.Capture` owns Windows graphics resources. `src/RemoteDesktop.App` owns WPF views and composition. Core and application tests are in `tests/`. Product strings and resources currently remain in the application project; branding is not fully centralized.

See [project status](PROJECT_STATUS.md), [setup](docs/development-setup.md), [architecture](docs/architecture.md), [testing](docs/testing.md) and [security](docs/security.md).

This build is a local capture prototype, not a usable remote-access client or production release.
