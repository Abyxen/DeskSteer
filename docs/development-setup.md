# Development setup

Build on Windows with .NET SDK 10.0.402 and desktop tooling. `global.json` pins that SDK with latest-patch roll-forward; NuGet.Config uses the public NuGet v3 feed. The application/capture target is `net10.0-windows10.0.19041.0`; the project has not certified a complete Windows version/DPI matrix. Native graphics tests require an interactive desktop.

From the repository:

```powershell
dotnet restore DeskSteer.slnx --configfile NuGet.Config
dotnet build DeskSteer.slnx -c Release --no-restore
dotnet test DeskSteer.slnx -c Release --no-build
dotnet run --project src/DeskSteer.App
```

`./build.ps1` restores, builds and tests, failing on any unsuccessful step. `./build.ps1 -Launch` also starts the app. It prefers the supplied SDK at `../../work/dotnet`, uses single-node MSBuild, and isolates SDK/NuGet/application test data in workspace `work/`. It changes process environment variables; use a fresh terminal afterward.

To launch the framework-dependent native executable manually, set `DOTNET_ROOT` to the workspace `work/dotnet` path, then run `src/DeskSteer.App/bin/Release/net10.0-windows10.0.19041.0/DeskSteer.App.exe`. `DESKSTEER_DATA_DIR` can select a disposable preference directory. No credentials or certificates need provisioning: hosting generates fresh connection details at runtime.

## Portable LAN candidate

```powershell
./package.ps1
```

This first invokes `build.ps1`, then publishes a self-contained `win-x64` folder, copies the LAN guide into it and creates `../DeskSteer-LAN-0.3.1-win-x64.zip` plus `.sha256`. A successful package does not need a separate .NET runtime on the destination machine. Extract the full archive and run `DeskSteer.App.exe`; retain its adjacent files. This is an unsigned development candidate, with no installer or automatic updater.

Use `./package.ps1 -DestinationPath 'C:\path\new-candidate.zip'` for a different output. The script refuses to overwrite an existing archive. Native capture tests are opt-in; set the variables in [testing](testing.md) before packaging to include them in its validation step. The named current handoff candidate is `../DeskSteer-LAN-0.3.1-win-x64-candidate.zip`; packaging completed after 78 tests passed with six native checks skipped; its checksum is recorded in the LAN guide. The old accepted 0.3.0 archive is a historical artifact with its original branding.

## LAN configuration and CI

Host binds a specific private IPv4 address and a configurable port (UI default 47731); loopback is for same-machine development. No firewall rule is added automatically. Both computers require the same candidate and trusted network; follow [LAN acceptance](lan-test.md).

Historical [GitHub Actions run 1](https://github.com/Abyxen/DeskSteer/actions/runs/38644851962) passed the 0.2 foundation/capture build and noninteractive tests, with interactive capture intentionally skipped. It does not validate the new 0.3 transport. The renamed source passed Release compilation with zero warnings/errors and all 84 native-enabled local tests (27 application, 7 core, 50 transport), before the final 0.3.1 metadata bump. Historical CI does not establish the renamed head: its next run awaits publication. Packaging and acceptance records belong in [project status](../PROJECT_STATUS.md).
