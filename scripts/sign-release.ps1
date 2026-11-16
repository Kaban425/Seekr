#Requires -Version 7.2
param(
    [Parameter(Mandatory=$true)][string]$Path,
    [Parameter(Mandatory=$true)][ValidatePattern('^[0-9a-fA-F]{40}$')][string]$CertificateThumbprint,
    [string]$TimestampUrl = 'http://timestamp.digicert.com'
)
$ErrorActionPreference = 'Stop'
$certificate = Get-Item -LiteralPath ('Cert:\CurrentUser\My\'+$CertificateThumbprint) -ErrorAction SilentlyContinue
$machineStore = $false
if (-not $certificate) { $certificate = Get-Item -LiteralPath ('Cert:\LocalMachine\My\'+$CertificateThumbprint) -ErrorAction SilentlyContinue; $machineStore = $true }
if (-not $certificate -or -not $certificate.HasPrivateKey -or $certificate.NotAfter -le (Get-Date)) { throw 'A valid publisher certificate with its private key is required.' }
$signTool = Get-Command signtool.exe -ErrorAction SilentlyContinue
if ($signTool) { $signToolPath = $signTool.Source }
else {
    $sdkRoot = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
    $signToolPath = Get-ChildItem -Path (Join-Path $sdkRoot '*\x64\signtool.exe') -ErrorAction SilentlyContinue | Sort-Object FullName -Descending | Select-Object -First 1 -ExpandProperty FullName
}
if (-not $signToolPath) { throw 'Install Windows SDK signing tools before signing.' }
$target = Get-Item -LiteralPath $Path
$files = if ($target.PSIsContainer) { Get-ChildItem -LiteralPath $target.FullName -Recurse -File | Where-Object { $_.Name -like 'Seekr*.exe' -or $_.Name -like 'Seekr*.dll' } } else { @($target) }
foreach ($file in $files) {
    $arguments = @('sign','/sha1',$CertificateThumbprint,'/fd','SHA256','/tr',$TimestampUrl,'/td','SHA256')
    if ($machineStore) { $arguments += '/sm' }
    $arguments += $file.FullName
    & $signToolPath @arguments
    if ($LASTEXITCODE -ne 0) { throw ('Signing failed: '+$file.Name) }
    if ((Get-AuthenticodeSignature -LiteralPath $file.FullName).Status -ne 'Valid') { throw ('Signature validation failed: '+$file.Name) }
}
