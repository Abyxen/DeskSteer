# Project Remote

An original Windows remote-desktop application, developed in verified milestones.

**Current build: 0.3.0 LAN candidate.** Milestones 1 and 2 are verified on the current machine, including the user's manual picker/window/display checks. The application now implements view-only sharing over pinned TLS on a private IPv4 network. **Milestone 3 is accepted based on the user reporting that all LAN checks passed on two authorized Windows computers.** The agent did not inspect that two-computer session; OS versions and duration were not supplied. Release compilation passed with zero warnings/errors; the 83-test native-enabled local run and a separate backpressure regression passed. Milestone 4 is in design; remote input is not implemented. See [project status](PROJECT_STATUS.md) for evidence.

The [self-contained Windows x64 candidate](../ProjectRemote-LAN-0.3.0-win-x64-candidate.zip) bundles .NET runtime 10.0.13; extract all files and run `RemoteDesktop.App.exe`. Archive extraction/startup smoke testing passed. See the [LAN guide](docs/lan-test.md) for setup and its checksum.

## Run

Install the .NET 10 SDK with Windows desktop support, then from this directory:

```powershell
dotnet restore ProjectRemote.slnx --configfile NuGet.Config
dotnet build ProjectRemote.slnx -c Release
dotnet test ProjectRemote.slnx -c Release
dotnet run --project src/RemoteDesktop.App
```

In the supplied workspace, `build.ps1` uses the SDK downloaded to `../../work/dotnet`. Run `./build.ps1 -Launch` to build, test and launch. It isolates build caches and application test data under `../../work`; use a fresh terminal afterward because the script adjusts process environment variables. The SDK is not a system-wide installation.

For a two-computer session, follow [LAN setup and acceptance](docs/lan-test.md). On the host, enter its private IPv4 address and port, select a window/display and send the connection details privately. The viewer verifies the fingerprint separately and requests access; the host must explicitly approve. Stop sharing or Disconnect ends the session. The application does not change firewall rules automatically.

## What is available

- Charcoal WPF shell with six destinations, saved startup preference and bounded diagnostic export.
- Real Windows Graphics Capture with local preview, source resize recovery, stop/restart and disposal.
- One approved viewer per hosting session, TLS 1.2/1.3 with a SHA-256 certificate pin, fresh random pairing code and explicit host approval.
- JPEG quality 75; preview up to 1280×720; capture and sending capped at 10 FPS. Native surfaces above 16 megapixels are rejected. Frame payloads are bounded to 2 MiB including metadata.
- Private IPv4/loopback binding only, expiring pairing details, bounded authentication attempts, heartbeats and session timeouts.
- Separate Core, Capture, Transport and WPF App projects, xUnit tests and Windows CI configuration.

There is no remote keyboard/mouse input, audio, device persistence, file transfer, internet relay or unattended access. Branding remains partly distributed across application resources, strings and metadata. This is a development candidate, not a production release.

See [project status](PROJECT_STATUS.md), [setup](docs/development-setup.md), [architecture](docs/architecture.md), [protocol](docs/protocol.md), [testing](docs/testing.md) and [security](docs/security.md).

