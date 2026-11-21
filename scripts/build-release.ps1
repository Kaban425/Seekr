#Requires -Version 7.2
param(
    [ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version = '1.0.7',
    [ValidateRange(1,65535)][int]$BuildNumber = 8,
    [string]$CertificateThumbprint = '',
    [switch]$RequireSigned,
    [switch]$SkipTests
)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$releaseParent = Join-Path $repoRoot 'artifacts\release'
$null = New-Item -ItemType Directory -Path $releaseParent -Force
$releaseParent = (Resolve-Path -LiteralPath $releaseParent).Path
$releaseRoot = Join-Path $releaseParent ('.staging-'+[Guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path $releaseRoot
$payload = Join-Path $releaseRoot ('Seekr-'+$Version+'-win-x64')
if ($RequireSigned -and -not $CertificateThumbprint) { throw 'A publisher certificate is required for this release.' }
if (-not $SkipTests) {
    dotnet test (Join-Path $repoRoot 'Seekr.slnx') -c Release --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Release tests failed.' }
}
dotnet publish (Join-Path $repoRoot 'src\Seekr.App') -c Release -r win-x64 --self-contained true -p:Version=$Version -p:BuildNumber=$BuildNumber -o $payload --nologo
if ($LASTEXITCODE -ne 0) { throw 'App publish failed.' }
dotnet publish (Join-Path $repoRoot 'src\Seekr.Elevated') -c Release -r win-x64 --self-contained true -p:Version=$Version -p:BuildNumber=$BuildNumber -o (Join-Path $payload 'Service') --nologo
if ($LASTEXITCODE -ne 0) { throw 'Service publish failed.' }
& (Join-Path $payload 'Service\Seekr.Elevated.exe') --check-package
if ($LASTEXITCODE -ne 0) { throw 'Service payload validation failed.' }
Get-ChildItem -LiteralPath $payload -Recurse -File -Filter '*.pdb' | ForEach-Object { Remove-Item -LiteralPath $_.FullName }
Copy-Item -LiteralPath (Join-Path $repoRoot 'LICENSE') -Destination $payload
$licenseDirectory = Join-Path $payload 'Licenses'
$null = New-Item -ItemType Directory -Path $licenseDirectory -Force
Copy-Item -LiteralPath (Join-Path $repoRoot 'src\Seekr.App\Assets\Fonts\OFL.txt') -Destination (Join-Path $licenseDirectory 'PressStart2P-OFL.txt')
$packageKeys = foreach ($project in @('Seekr.App','Seekr.Elevated')) {
    $assets = Get-Content -LiteralPath (Join-Path $repoRoot ('src\'+$project+'\obj\project.assets.json')) -Raw | ConvertFrom-Json -AsHashtable
    foreach ($entry in $assets.libraries.GetEnumerator()) { if ($entry.Value.type -eq 'package') { $entry.Key } }
}
$runtime = Get-Content -LiteralPath (Join-Path $payload 'Seekr.App.runtimeconfig.json') -Raw | ConvertFrom-Json
foreach ($framework in $runtime.runtimeOptions.includedFrameworks) {
    $id = if ($framework.name -eq 'Microsoft.NETCore.App') { 'microsoft.netcore.app.runtime.win-x64' } else { 'microsoft.windowsdesktop.app.runtime.win-x64' }
    $packageKeys += $id+'/'+$framework.version
}
foreach ($key in ($packageKeys | Sort-Object -Unique)) {
    $id,$packageVersion = $key.Split('/')
    $packagePath = Join-Path $env:USERPROFILE ('.nuget\packages\'+$id.ToLowerInvariant()+'\'+$packageVersion)
    if (Test-Path -LiteralPath $packagePath) {
        foreach ($license in Get-ChildItem -LiteralPath $packagePath -File | Where-Object { $_.Name -match '^(license|notice|third-party-notices)' }) {
            Copy-Item -LiteralPath $license.FullName -Destination (Join-Path $licenseDirectory ($id+'-'+$packageVersion+'-'+$license.Name))
        }
    }
}
Copy-Item -LiteralPath (Join-Path $repoRoot 'docs\RELEASE-1.0.md') -Destination (Join-Path $payload 'RELEASE-NOTES.md')
if ($CertificateThumbprint) { & (Join-Path $PSScriptRoot 'sign-release.ps1') -Path $payload -CertificateThumbprint $CertificateThumbprint }
& (Join-Path $PSScriptRoot 'build-installer.ps1') -PayloadDirectory $payload -OutputDirectory $releaseRoot -Version $Version
$msi = Join-Path $releaseRoot ('Seekr-'+$Version+'-win-x64.msi')
if ($CertificateThumbprint) { & (Join-Path $PSScriptRoot 'sign-release.ps1') -Path $msi -CertificateThumbprint $CertificateThumbprint }
$setup = Join-Path $releaseRoot ('Seekr-'+$Version+'-Setup.exe')
& (Join-Path $PSScriptRoot 'build-setup.ps1') -MsiPath $msi -OutputPath $setup -Version $Version -BuildNumber $BuildNumber
if ($CertificateThumbprint) { & (Join-Path $PSScriptRoot 'sign-release.ps1') -Path $setup -CertificateThumbprint $CertificateThumbprint }
$zip = Join-Path $releaseRoot ('Seekr-'+$Version+'-win-x64.zip')
Compress-Archive -Path (Join-Path $payload '*') -DestinationPath $zip -Force
$manifest = [ordered]@{
    version=$Version; build=$BuildNumber; builtUtc=[DateTime]::UtcNow.ToString('O');
    signature=(Get-AuthenticodeSignature -LiteralPath (Join-Path $payload 'Seekr.App.exe')).Status.ToString();
    files=@(@($msi,$zip,$setup) | ForEach-Object { [ordered]@{ name=[IO.Path]::GetFileName($_); sha256=(Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash } })
}
if ($RequireSigned -and $manifest.signature -ne 'Valid') { throw 'Release signature is not valid.' }
$manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $releaseRoot 'release-manifest.json') -Encoding utf8
$finalRoot = Join-Path $releaseParent $Version
$previousRoot = Join-Path $releaseParent ('.previous-'+[Guid]::NewGuid().ToString('N'))
foreach ($candidate in @($releaseRoot,$finalRoot,$previousRoot)) {
    if (-not [IO.Path]::GetFullPath($candidate).StartsWith($releaseParent+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) { throw 'Release target escaped its directory.' }
}
if (Test-Path -LiteralPath $finalRoot) { Move-Item -LiteralPath $finalRoot -Destination $previousRoot }
try { Move-Item -LiteralPath $releaseRoot -Destination $finalRoot }
catch { if(Test-Path -LiteralPath $previousRoot){ Move-Item -LiteralPath $previousRoot -Destination $finalRoot }; throw }
if (Test-Path -LiteralPath $previousRoot) { Remove-Item -LiteralPath $previousRoot -Recurse -Force }
Write-Host ('Release ready: '+$finalRoot+'; signature: '+$manifest.signature)

