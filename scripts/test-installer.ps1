#Requires -Version 7.2
param([Parameter(Mandatory=$true)][string]$ReleaseDirectory)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$release = (Resolve-Path -LiteralPath $ReleaseDirectory).Path
$version = (Get-Content -LiteralPath (Join-Path $release 'release-manifest.json') -Raw | ConvertFrom-Json).version
$parsedVersion = [Version]$version
$upgradeVersion = '{0}.{1}.{2}' -f $parsedVersion.Major,$parsedVersion.Minor,($parsedVersion.Build+1)
$msi = Join-Path $release ('Seekr-'+$version+'-win-x64.msi')
$payload = Join-Path $release ('Seekr-'+$version+'-win-x64')
function Products {
    @(Get-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\*','HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\*' -ErrorAction SilentlyContinue | Where-Object DisplayName -eq 'Seekr' | Sort-Object PSChildName -Unique)
}
if ((Products).Count -gt 0) { throw 'An installed Seekr product exists; installer test will not modify it.' }
$shortcut = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\Seekr\Seekr.lnk'
if (Test-Path -LiteralPath $shortcut) { throw 'An existing shortcut must not be overwritten by this test.' }
$desktopShortcut = Join-Path ([Environment]::GetFolderPath('Desktop')) 'Seekr.lnk'
if (Test-Path -LiteralPath $desktopShortcut) { throw 'An existing desktop shortcut must not be overwritten by this test.' }
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('Seekr-installer-test-'+[Guid]::NewGuid().ToString('N'))
$appRoot = Join-Path $testRoot 'Программа'
$logRoot = Join-Path $repoRoot 'artifacts\release-checks'
$null = New-Item -ItemType Directory -Path $testRoot,$logRoot -Force
$installed = $false
$activeMsi = $msi
function RunMsi([string[]]$arguments,[string]$label,[int[]]$expected=@(0)) {
    $log = Join-Path $logRoot ($label+'.log')
    $allArguments = $arguments + @('/qn','/norestart','/L*v',('"'+$log+'"'))
    $process = Start-Process -FilePath (Join-Path $env:WINDIR 'System32\msiexec.exe') -ArgumentList $allArguments -PassThru -Wait -WindowStyle Hidden
    if ($process.ExitCode -notin $expected) { throw ($label+' failed: '+$process.ExitCode+'; '+$log) }
    Write-Output ('PASS '+$label+' ('+$process.ExitCode+')')
}
try {
    RunMsi @('/i',('"'+$msi+'"'),('INSTALLFOLDER="'+$appRoot+'"'),'ADD_DESKTOP_SHORTCUT=0') 'install-no-desktop-shortcut'
    $installed = $true
    if ((Test-Path -LiteralPath $desktopShortcut) -or -not (Test-Path -LiteralPath $shortcut)) { throw 'Desktop shortcut opt-out failed.' }
    $upgradeRoot = Join-Path $logRoot 'upgrade-fixture'
    & (Join-Path $PSScriptRoot 'build-installer.ps1') -PayloadDirectory $payload -OutputDirectory $upgradeRoot -Version $upgradeVersion
    $upgradeMsi = Join-Path $upgradeRoot ('Seekr-'+$upgradeVersion+'-win-x64.msi')
    RunMsi @('/i',('"'+$upgradeMsi+'"'),('INSTALLFOLDER="'+$appRoot+'"')) 'upgrade-preserves-no-desktop-shortcut'
    $activeMsi = $upgradeMsi
    if (Test-Path -LiteralPath $desktopShortcut) { throw 'Upgrade ignored saved desktop shortcut preference.' }
    RunMsi @('/x',('"'+$upgradeMsi+'"')) 'uninstall-no-desktop-shortcut'
    $installed = $false
    $activeMsi = $msi
    RunMsi @('/i',('"'+$msi+'"'),('INSTALLFOLDER="'+$appRoot+'"')) 'install'
    $installed = $true
    if (-not (Test-Path -LiteralPath (Join-Path $appRoot 'Seekr.App.exe'))) { throw 'Installed executable missing.' }
    if ((Products).Count -ne 1 -or -not (Test-Path -LiteralPath $shortcut)) { throw 'Installer registration or shortcut missing.' }
    if (-not (Test-Path -LiteralPath $desktopShortcut)) { throw 'Desktop shortcut missing.' }
    $shell = New-Object -ComObject WScript.Shell
    if ($shell.CreateShortcut($desktopShortcut).TargetPath -ne (Join-Path $appRoot 'Seekr.App.exe')) { throw 'Desktop shortcut target incorrect.' }
    Write-Output 'PASS desktop shortcut target'
    $userFile = Join-Path $appRoot 'user-test-document.txt'
    Set-Content -LiteralPath $userFile -Value 'must survive uninstall'
    $rebuildRoot = Join-Path $logRoot 'rebuild'
    & (Join-Path $PSScriptRoot 'build-installer.ps1') -PayloadDirectory $payload -OutputDirectory $rebuildRoot -Version $version
    RunMsi @('/i',('"'+(Join-Path $rebuildRoot ('Seekr-'+$version+'-win-x64.msi'))+'"'),('INSTALLFOLDER="'+$appRoot+'"')) 'same-version-rebuild' @(0,1638)
    if ((Products).Count -ne 1) { throw 'Same-version rebuild created duplicate products.' }
    RunMsi @('/i',('"'+$upgradeMsi+'"'),('INSTALLFOLDER="'+$appRoot+'"')) 'upgrade'
    $activeMsi = $upgradeMsi
    if ((Products).Count -ne 1 -or (Products)[0].DisplayVersion -ne $upgradeVersion) { throw 'Upgrade did not replace the older product.' }
    RunMsi @('/i',('"'+$msi+'"')) 'downgrade-blocked' @(1603,1638)
    RunMsi @('/x',('"'+$activeMsi+'"')) 'uninstall'
    $installed = $false
    if ((Products).Count -ne 0 -or (Test-Path -LiteralPath $shortcut)) { throw 'Uninstall left product registration or shortcut.' }
    if (Test-Path -LiteralPath $desktopShortcut) { throw 'Uninstall left desktop shortcut.' }
    if ((Test-Path -LiteralPath (Join-Path $appRoot 'Seekr.App.exe')) -or -not (Test-Path -LiteralPath $userFile)) { throw 'Uninstall did not preserve unowned data correctly.' }
    Write-Output 'PASS user document preserved'
} finally {
    if ($installed) { RunMsi @('/x',('"'+$activeMsi+'"')) 'cleanup-uninstall' }
    $resolvedTestRoot = [IO.Path]::GetFullPath($testRoot)
    $tempParent = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\')+'\'
    if (-not $resolvedTestRoot.StartsWith($tempParent,[StringComparison]::OrdinalIgnoreCase) -or -not ([IO.Path]::GetFileName($resolvedTestRoot)).StartsWith('Seekr-installer-test-')) { throw 'Unsafe cleanup target.' }
    if (Test-Path -LiteralPath $resolvedTestRoot) { Remove-Item -LiteralPath $resolvedTestRoot -Recurse -Force }
}
