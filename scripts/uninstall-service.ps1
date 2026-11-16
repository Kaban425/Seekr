#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Stops and removes the SeekrIndex Windows service and its files.
#>
$ErrorActionPreference = 'Stop'

$serviceName = 'SeekrIndex'
$target = Join-Path $env:ProgramFiles 'Seekr\Service'

if (Get-Service -Name $serviceName -ErrorAction SilentlyContinue) {
    Stop-Service -Name $serviceName -Force
    sc.exe delete $serviceName | Out-Null
    Write-Host "Removed $serviceName service."
}

if (Test-Path $target) {
    Remove-Item -Recurse -Force $target
    Write-Host "Deleted $target."
}
