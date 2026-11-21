#Requires -Version 7.2
param([Parameter(Mandatory)][string]$MsiPath,[Parameter(Mandatory)][string]$OutputPath,[string]$Version='1.0.7',[int]$BuildNumber=8)
$ErrorActionPreference='Stop'
$repoRoot=Split-Path -Parent $PSScriptRoot
$msi=(Resolve-Path -LiteralPath $MsiPath).Path
$hash=(Get-FileHash -LiteralPath $msi -Algorithm SHA256).Hash
$publish=Join-Path $repoRoot ('artifacts\setup-publish\'+[Guid]::NewGuid().ToString('N'))
dotnet publish (Join-Path $repoRoot 'src\Seekr.Setup') -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:Version=$Version -p:BuildNumber=$BuildNumber "-p:SetupPayload=$msi" "-p:SetupPayloadSha256=$hash" -o $publish --nologo
if($LASTEXITCODE -ne 0){throw 'Setup publish failed.'}
$null=New-Item -ItemType Directory -Path (Split-Path -Parent $OutputPath) -Force
Copy-Item -LiteralPath (Join-Path $publish 'Seekr.Setup.exe') -Destination $OutputPath -Force
$check=Start-Process -FilePath ([IO.Path]::GetFullPath($OutputPath)) -ArgumentList '--validate-package' -PassThru -Wait -WindowStyle Hidden
if($check.ExitCode -ne 0){throw 'Embedded MSI package integrity check failed.'}
Write-Output $OutputPath
