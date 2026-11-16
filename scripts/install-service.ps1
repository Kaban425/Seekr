#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Installs or updates the SeekrIndex Windows service (lets Seekr search all drives without admin rights).
.DESCRIPTION
    First run scripts\build-service.ps1 WITHOUT administrator rights. This script only copies the
    built files to Program Files (which normal users cannot modify) and configures the service:
    restart on failure, a per-service SID, and only the privileges it needs.
    Run uninstall-service.ps1 to remove it.
#>
$ErrorActionPreference = 'Stop'

$serviceName = 'SeekrIndex'
$repoRoot = Split-Path -Parent $PSScriptRoot
$source = Join-Path $repoRoot 'artifacts\service'
$target = Join-Path $env:ProgramFiles 'Seekr\Service'
$exe = Join-Path $target 'Seekr.Elevated.exe'
$binaryPath = "`"$exe`""
$privileges = 'SeChangeNotifyPrivilege/SeImpersonatePrivilege/SeBackupPrivilege/SeManageVolumePrivilege'

if (-not (Test-Path (Join-Path $source 'Seekr.Elevated.exe'))) {
    throw "No build found in $source. Run scripts\build-service.ps1 (without administrator rights) first."
}

function Invoke-Sc {
    sc.exe @args | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "sc.exe $($args -join ' ') failed with exit code $LASTEXITCODE." }
}

$existing = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
if ($existing) {
    Write-Host "Stopping existing $serviceName service..."
    Stop-Service -Name $serviceName -Force
}

Write-Host "Copying service to $target ..."
if (Test-Path $target) { Remove-Item -Recurse -Force $target }
New-Item -ItemType Directory -Force $target | Out-Null
Copy-Item -Path (Join-Path $source '*') -Destination $target -Recurse

if ($existing) {
    Invoke-Sc config $serviceName binPath= $binaryPath start= auto
} else {
    New-Service -Name $serviceName -BinaryPathName $binaryPath -DisplayName 'Seekr Index' `
        -Description 'Reads the NTFS file table so Seekr can search all drives without administrator rights.' `
        -StartupType Automatic | Out-Null
}

# Restart after 5 s, 5 s, then 60 s; forget failures after a day.
Invoke-Sc failure $serviceName reset= 86400 actions= restart/5000/restart/5000/restart/60000
Invoke-Sc sidtype $serviceName unrestricted
Invoke-Sc privs $serviceName $privileges

Start-Service -Name $serviceName
Start-Sleep -Seconds 3
$service = Get-Service -Name $serviceName
if ($service.Status -ne 'Running') {
    Write-Warning "$serviceName did not start with the reduced privilege list. Removing the restriction and retrying."
    Remove-ItemProperty -Path "HKLM:\SYSTEM\CurrentControlSet\Services\$serviceName" -Name RequiredPrivileges -ErrorAction SilentlyContinue
    Start-Service -Name $serviceName
}

Get-Service -Name $serviceName
