# Development setup

Use Windows with the .NET 10 SDK and Windows Desktop runtime. The downloaded Windows x64 SDK 10.0.402 includes the desktop tooling used here. Visual Studio is optional for command-line builds. Official installation information: https://learn.microsoft.com/dotnet/core/install/windows . Supported OS matrix: verify against Microsoft's current .NET support policy before distribution; this project has not certified a Windows version matrix.

`global.json` pins 10.0.402 with latest-patch roll-forward. NuGet.Config specifies the public NuGet v3 feed. .NET dependencies were restored and compiled successfully; licenses are summarized in security.md.

Standard commands from the repository:

```powershell
dotnet restore ProjectRemote.slnx --configfile NuGet.Config
dotnet build ProjectRemote.slnx -c Release --no-restore
dotnet test ProjectRemote.slnx -c Release --no-build
dotnet run --project src/RemoteDesktop.App
```

Workspace commands: `./build.ps1` and `./build.ps1 -Launch`. The helper uses single-node MSBuild and workspace caches to accommodate this environment's restrictions. It exits on restore, build or test failure. Build artifacts are not release packages.

For a native executable launch using the workspace SDK, set `DOTNET_ROOT` to the absolute workspace `work/dotnet` directory, then run `src/RemoteDesktop.App/bin/Release/net10.0-windows10.0.19041.0/RemoteDesktop.App.exe`. `PROJECTREMOTE_DATA_DIR` optionally sets a disposable preference directory. There are no credentials or certificates to configure. The application/capture projects target net10.0-windows10.0.19041.0. Native capture tests require an interactive graphics session; see testing.md for opt-in commands.

CI runs restore/build/test on windows-latest. [GitHub Actions run 1](https://github.com/Abyxen/ProjectRemote/actions/runs/38644851962) passed restore, Release compilation and tests; interactive graphics tests are intentionally skipped there. No installer, auto-update channel, self-contained publish or release signing exists yet.
