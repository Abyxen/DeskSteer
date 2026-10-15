param([string]$DestinationPath = (Join-Path $PSScriptRoot '..\DeskSteer-LAN-0.4.0-win-x64.zip'))
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
$destination = [IO.Path]::GetFullPath($DestinationPath)
if (Test-Path -LiteralPath $destination) { throw "Choose a new destination; the archive already exists: $destination" }

# Validate the solution before creating a portable, self-contained Windows build.
& (Join-Path $PSScriptRoot 'build.ps1')
$workspace = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$localSdk = Join-Path $workspace 'work\dotnet\dotnet.exe'
$sdk = if (Test-Path $localSdk) { $localSdk } else { 'dotnet' }
$staging = Join-Path $PSScriptRoot ('artifacts\package\' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $staging -Force | Out-Null
& $sdk publish src/DeskSteer.App/DeskSteer.App.csproj -c Release -r win-x64 --self-contained true -m:1 --disable-build-servers -p:PublishTrimmed=false -p:PublishSingleFile=false "-p:RestoreConfigFile=$PSScriptRoot\NuGet.Config" -o $staging
if ($LASTEXITCODE) { throw 'Portable publish failed' }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'docs\lan-test.md') -Destination (Join-Path $staging 'LAN-TEST.md')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'docs\input-test.md') -Destination (Join-Path $staging 'INPUT-TEST.md')
$notices = Join-Path $staging 'ThirdPartyNotices'
New-Item -ItemType Directory -Path $notices | Out-Null
$runtimeConfig = Get-Content -LiteralPath (Join-Path $staging 'DeskSteer.App.runtimeconfig.json') -Raw | ConvertFrom-Json
$dependencyManifest = Get-Content -LiteralPath (Join-Path $staging 'DeskSteer.App.deps.json') -Raw | ConvertFrom-Json
$packages = @($dependencyManifest.libraries.PSObject.Properties | Where-Object { $_.Value.type -eq 'package' } | ForEach-Object { $_.Name.ToLowerInvariant() })
foreach ($framework in $runtimeConfig.runtimeOptions.includedFrameworks) {
    $packages += $framework.name.ToLowerInvariant() + '.runtime.win-x64/' + $framework.version
}
foreach ($package in ($packages | Sort-Object -Unique)) {
    $packageRoot = Join-Path $env:NUGET_PACKAGES $package
    $noticeFiles = Get-ChildItem -LiteralPath $packageRoot -File | Where-Object { $_.Name -match '^(LICENSE|THIRD-PARTY-NOTICES)(\..*)?$' }
    foreach ($noticeFile in $noticeFiles) {
        $noticeTarget = Join-Path $notices ($package.Replace('/', '-') + '-' + $noticeFile.Name)
        Copy-Item -LiteralPath $noticeFile.FullName -Destination $noticeTarget
    }
}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'docs\security.md') -Destination (Join-Path $notices 'DEPENDENCIES.md')
New-Item -ItemType Directory -Force (Split-Path $destination) | Out-Null
Compress-Archive -Path (Join-Path $staging '*') -DestinationPath $destination -CompressionLevel Optimal
$checksum = Get-FileHash -LiteralPath $destination -Algorithm SHA256
Set-Content -LiteralPath ($destination + '.sha256') -Value ($checksum.Hash.ToLowerInvariant() + '  ' + [IO.Path]::GetFileName($destination)) -Encoding utf8NoBOM
Write-Output "Portable build: $destination"
Write-Output "SHA256: $($checksum.Hash)"
