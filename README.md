# DeskSteer

An original Windows remote-desktop application, developed in verified milestones.

**Current source: 0.4.0 remote-input candidate, validation in progress.** Viewing over pinned TLS now has an optional, separately approved keyboard/mouse mode. Milestone 4 is not accepted: real input needs the [two-computer input checks](docs/input-test.md). The input backend has 16 passing recording tests; these do not execute real Windows `SendInput`. Current full-suite results are recorded in [project status](PROJECT_STATUS.md).

Milestone 3 was accepted from the user reporting all LAN checks passed on two authorized Windows computers using the earlier 0.3.0 build. The agent did not inspect that session; OS versions and duration were not supplied. DeskSteer 0.3.1 subsequently passed its branding validation and [Windows CI](https://github.com/Abyxen/DeskSteer/actions/runs/38702563482). These historical results do not establish 0.4.0 acceptance.

Repository: [Abyxen/DeskSteer](https://github.com/Abyxen/DeskSteer), public. Candidate ZIPs supplied in chat are local artifacts, not GitHub release downloads. The historical branded package is `DeskSteer-LAN-0.3.1-win-x64-candidate.zip`; no new 0.4.0 ZIP is claimed yet. Build/package commands below create an artifact locally. Extract a complete self-contained package and run `DeskSteer.App.exe`.

## Run

On Windows with .NET SDK 10.0.402:

```powershell
dotnet restore DeskSteer.slnx --configfile NuGet.Config
dotnet build DeskSteer.slnx -c Release
dotnet test DeskSteer.slnx -c Release
dotnet run --project src/DeskSteer.App
```

The supplied workspace helper `./build.ps1 -Launch` restores, builds, tests and launches using `../../work/dotnet` when present. It changes process environment variables and uses workspace caches/test data; use a fresh terminal afterward. `./package.ps1 -DestinationPath 'C:\path\new-candidate.zip'` validates and creates a self-contained Windows x64 package without overwriting an existing archive.

## Share and control

Follow [LAN setup](docs/lan-test.md) for private IPv4 binding, fingerprint verification, the pairing code and host approval. With the host control checkbox **unchecked**, the Windows picker selects a source for viewing only. Opting in to control instead shares the primary display. The viewer first needs viewing approval, then chooses **Request keyboard and mouse**; the host must grant a separate request.

Keyboard control targets the foreground Windows application; mouse control maps to the primary display. Click the viewer image to focus it. Leaving that focus/window releases control. The host has a persistent topmost indicator with **Revoke control — keep viewing** and **Stop sharing and disconnect**. If registered successfully, Ctrl+Alt+F12 also revokes control. See [input acceptance](docs/input-test.md) for scope and limitations.

Capture/streaming remain capped at 10 FPS with JPEG quality 75 and a 1280×720 preview. Native surfaces above 16 megapixels and frame payloads above 2 MiB are rejected. There is one approved viewer per session, expiring connection details and bounded processing. The app does not change firewall rules, elevate privileges or enable unattended access. Audio, file transfer, internet relays and persistent device trust remain unimplemented.

See [status](PROJECT_STATUS.md), [setup](docs/development-setup.md), [architecture](docs/architecture.md), [protocol](docs/protocol.md), [testing](docs/testing.md) and [security](docs/security.md). This is a development candidate, not a production release.
