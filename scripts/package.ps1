<# Creates a ready-to-run app + service payload. Never installs the service during packaging. #>
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$output = Join-Path $repoRoot 'artifacts\Seekr'
$serviceOutput = Join-Path $output 'Service'
dotnet publish (Join-Path $repoRoot 'src\Seekr.App\Seekr.App.csproj') -c Release -r win-x64 --self-contained true -o $output --nologo
if ($LASTEXITCODE -ne 0) { throw 'App publish failed.' }
dotnet publish (Join-Path $repoRoot 'src\Seekr.Elevated\Seekr.Elevated.csproj') -c Release -r win-x64 --self-contained true -o $serviceOutput --nologo
if ($LASTEXITCODE -ne 0) { throw 'Service publish failed.' }
& (Join-Path $serviceOutput 'Seekr.Elevated.exe') --check-package
if ($LASTEXITCODE -ne 0) { throw 'Service payload validation failed.' }
Write-Host "Ready: $output\Seekr.App.exe. Open 'Search status' to install/update the service."
