param([switch]$Launch)
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
$workspace = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$localSdk = Join-Path $workspace 'work\dotnet\dotnet.exe'
$sdk = if (Test-Path $localSdk) { $localSdk } else { 'dotnet' }
$env:DOTNET_CLI_HOME = Join-Path $workspace 'work\cli'
$env:NUGET_PACKAGES = Join-Path $workspace 'work\packages'
$env:NUGET_HTTP_CACHE_PATH = Join-Path $workspace 'work\nuget-http'
$env:APPDATA = Join-Path $workspace 'work\build-appdata'
$env:LOCALAPPDATA = Join-Path $workspace 'work\build-localappdata'
$env:TEMP = Join-Path $workspace 'work\temp'
$env:TMP = $env:TEMP
New-Item -ItemType Directory -Force $env:TEMP | Out-Null
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = 'false'
& $sdk restore ProjectRemote.slnx --configfile NuGet.Config --disable-parallel -m:1
if ($LASTEXITCODE) { throw 'Restore failed' }
& $sdk build ProjectRemote.slnx -c Release --no-restore -m:1 --disable-build-servers
if ($LASTEXITCODE) { throw 'Build failed' }
& $sdk test ProjectRemote.slnx -c Release --no-build -m:1 --logger trx --results-directory artifacts/test-results
if ($LASTEXITCODE) { throw 'Tests failed' }
if ($Launch) {
    $env:DOTNET_ROOT = Split-Path (Get-Command $sdk).Source
    $env:PROJECTREMOTE_DATA_DIR = Join-Path $workspace 'work\app-data'
    Start-Process -FilePath 'src\RemoteDesktop.App\bin\Release\net10.0-windows10.0.19041.0\RemoteDesktop.App.exe' -WindowStyle Hidden -Wait
}
