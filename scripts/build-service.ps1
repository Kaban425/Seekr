<#
.SYNOPSIS
    Builds the SeekrIndex service into artifacts\service. Run this WITHOUT administrator rights.
.DESCRIPTION
    Building runs project and NuGet code (MSBuild targets, analyzers). Doing that as administrator
    would let anything that can write to this folder or the NuGet cache run with SYSTEM rights,
    so the build stays unprivileged and install-service.ps1 only copies the result.
#>
$ErrorActionPreference = 'Stop'

$principal = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
if ($principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Run build-service.ps1 from a normal (non-administrator) PowerShell.'
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$output = Join-Path $repoRoot 'artifacts\service'

dotnet publish (Join-Path $repoRoot 'src\Seekr.Elevated\Seekr.Elevated.csproj') -c Release -o $output --nologo
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed.' }

Write-Host "Built service into $output. Now run scripts\install-service.ps1 as administrator."
