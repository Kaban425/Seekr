#Requires -Version 7.2
param(
    [Parameter(Mandatory=$true)][string]$PayloadDirectory,
    [string]$OutputDirectory = '',
    [string]$Version = '1.0.7'
)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$payloadRoot = (Resolve-Path -LiteralPath $PayloadDirectory).Path
if (-not (Test-Path -LiteralPath (Join-Path $payloadRoot 'Seekr.App.exe'))) { throw 'App payload missing.' }
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $repoRoot 'artifacts\release' }
$null = New-Item -ItemType Directory -Path $OutputDirectory -Force
$releaseRoot = (Resolve-Path -LiteralPath $OutputDirectory).Path
$buildRoot = Join-Path $repoRoot 'artifacts\installer-source'
$null = New-Item -ItemType Directory -Path $buildRoot -Force
$toolRoot = Join-Path $repoRoot 'artifacts\build-tools\wix'
$wix = Join-Path $toolRoot 'wix.exe'
if (-not (Test-Path -LiteralPath $wix)) {
    dotnet tool install wix --version 4.0.6 --tool-path $toolRoot
    if ($LASTEXITCODE -ne 0) { throw 'WiX installation failed.' }
}
function XmlEscape([string]$value) { [System.Security.SecurityElement]::Escape($value) }
function StableId([string]$value) { 'I' + [Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($value.ToLowerInvariant()))).Substring(0,30) }
$oldRollForward = $env:DOTNET_ROLL_FORWARD
try {
    $env:DOTNET_ROLL_FORWARD = 'Major'
    Push-Location $repoRoot
    try {
        & $wix extension add WixToolset.UI.wixext/4.0.6
        if ($LASTEXITCODE -ne 0) { throw 'WiX UI extension installation failed.' }
    } finally { Pop-Location }
    $extension = Join-Path $repoRoot '.wix\extensions\WixToolset.UI.wixext\4.0.6\wixext4\WixToolset.UI.wixext.dll'
    $files = Get-ChildItem -LiteralPath $payloadRoot -Recurse -File | Where-Object { $_.Extension -ne '.pdb' } | Sort-Object FullName
    $xml = [Text.StringBuilder]::new()
    $null = $xml.AppendLine('<Wix xmlns="http://wixtoolset.org/schemas/v4/wxs" xmlns:ui="http://wixtoolset.org/schemas/v4/wxs/ui">')
    # A release version is immutable. Rebuilds keep ProductCode and cannot create a second installed product.
    $productHash = [System.Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes('Seekr/product/'+$Version))
    $productCode = [Guid]::new([byte[]]$productHash[0..15]).ToString('B')
    $null = $xml.AppendLine('<Package Name="Seekr" Manufacturer="Seekr contributors" Version="'+(XmlEscape $Version)+'" ProductCode="'+$productCode+'" Language="1049" UpgradeCode="AEF79FF0-167E-434A-82B7-BC81EEBC6778" Scope="perUser">')
    $null = $xml.AppendLine('<MajorUpgrade DowngradeErrorMessage="Уже установлена более новая версия Seekr."/><MediaTemplate EmbedCab="yes" CompressionLevel="high"/>')
    $null = $xml.AppendLine('<Icon Id="SeekrIcon" SourceFile="'+(XmlEscape (Join-Path $repoRoot 'src\Seekr.App\Assets\Brand\Seekr-square.ico'))+'"/><Property Id="ARPPRODUCTICON" Value="SeekrIcon"/>')
    $null = $xml.AppendLine('<Property Id="INSTALLFOLDER"><RegistrySearch Id="PreviousInstallDirectory" Root="HKCU" Key="Software\Seekr" Name="InstallDirectory" Type="raw"/></Property>')
    $null = $xml.AppendLine('<Property Id="SEEKR_INSTALL_DIRECTORY" Secure="yes"/><SetProperty Id="INSTALLFOLDER" Action="ApplySetupDirectory" Value="[SEEKR_INSTALL_DIRECTORY]" After="AppSearch" Sequence="both" Condition="SEEKR_INSTALL_DIRECTORY"/>')
    $null = $xml.AppendLine('<Property Id="ADD_DESKTOP_SHORTCUT" Value="1" Secure="yes"><RegistrySearch Id="PreviousDesktopShortcutPreference" Root="HKCU" Key="Software\Seekr" Name="DesktopShortcutPreference" Type="raw"/></Property><Property Id="DESKTOP_SHORTCUT_SETTING" Value="0"/>')
    $null = $xml.AppendLine('<Property Id="SEEKR_DESKTOP_CHOICE" Secure="yes"/><SetProperty Id="ADD_DESKTOP_SHORTCUT" Action="ApplyDesktopChoice" Value="[SEEKR_DESKTOP_CHOICE]" After="AppSearch" Sequence="both" Condition="SEEKR_DESKTOP_CHOICE = &quot;0&quot; OR SEEKR_DESKTOP_CHOICE = &quot;1&quot;"/><SetProperty Id="ADD_DESKTOP_SHORTCUT" Value="{}" After="ApplyDesktopChoice" Sequence="both" Condition="ADD_DESKTOP_SHORTCUT = &quot;0&quot;"/><SetProperty Id="DESKTOP_SHORTCUT_SETTING" Value="1" Before="CostFinalize" Sequence="execute" Condition="ADD_DESKTOP_SHORTCUT = &quot;1&quot;"/>')
    $null = $xml.AppendLine('<StandardDirectory Id="LocalAppDataFolder"><Directory Id="SeekrPrograms" Name="Programs"><Directory Id="INSTALLFOLDER" Name="Seekr">')
    $components = [Collections.Generic.List[string]]::new()
    $appFileId = ''
    foreach ($file in $files) {
        $relative = [IO.Path]::GetRelativePath($payloadRoot, $file.FullName)
        $id = StableId $relative
        $componentId = 'C'+$id; $fileId = 'F'+$id
        $componentHash = [System.Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes('Seekr/component/'+$relative.ToLowerInvariant()))
        $componentGuid = [Guid]::new([byte[]]$componentHash[0..15]).ToString('B')
        if ($relative -eq 'Seekr.App.exe') { $appFileId = $fileId }
        $parts = $relative.Split([IO.Path]::DirectorySeparatorChar)
        $directoryCount = $parts.Count - 1
        $prefix = ''
        for ($i=0; $i -lt $directoryCount; $i++) {
            $prefix += '/' + $parts[$i]
            # Each file's directory chain is unique; WiX folds identical directory paths.
            $null = $xml.AppendLine('<Directory Id="D'+$id+'_'+$i+'" Name="'+(XmlEscape $parts[$i])+'">')
        }
        $null = $xml.AppendLine('<Component Id="'+$componentId+'" Guid="'+$componentGuid+'"><RegistryValue Root="HKCU" Key="Software\Seekr\InstallerFiles" Name="'+$id+'" Type="integer" Value="1" KeyPath="yes"/><File Id="'+$fileId+'" Source="'+(XmlEscape $file.FullName)+'" KeyPath="no"/>')
        $null = $xml.AppendLine('<RemoveFolder Id="R'+$id+'" On="uninstall"/>')
        $null = $xml.AppendLine('</Component>')
        for ($i=0; $i -lt $directoryCount; $i++) { $null = $xml.AppendLine('</Directory>') }
        $components.Add($componentId)
    }
    $null = $xml.AppendLine('</Directory></Directory></StandardDirectory>')
    $null = $xml.AppendLine('<StandardDirectory Id="ProgramMenuFolder"><Directory Id="SeekrMenu" Name="Seekr"><Component Id="SeekrShortcuts" Guid="*"><RegistryValue Root="HKCU" Key="Software\Seekr" Name="Installed" Type="integer" Value="1" KeyPath="yes"/><RegistryValue Root="HKCU" Key="Software\Seekr" Name="InstallDirectory" Type="string" Value="[INSTALLFOLDER]"/><RegistryValue Root="HKCU" Key="Software\Seekr" Name="DesktopShortcutPreference" Type="string" Value="[DESKTOP_SHORTCUT_SETTING]"/><Shortcut Id="SeekrStart" Name="Seekr" Target="[#'+$appFileId+']" WorkingDirectory="INSTALLFOLDER" Icon="SeekrIcon"/><RemoveFolder Id="RemoveSeekrMenu" On="uninstall"/></Component></Directory></StandardDirectory>')
    $null = $xml.AppendLine('<StandardDirectory Id="DesktopFolder"><Component Id="SeekrDesktopShortcut" Guid="C4C9968E-5229-45C4-93B4-EA350F29B254" Condition="ADD_DESKTOP_SHORTCUT = &quot;1&quot;"><RegistryValue Root="HKCU" Key="Software\Seekr" Name="DesktopShortcut" Type="integer" Value="1" KeyPath="yes"/><Shortcut Id="SeekrDesktop" Name="Seekr" Target="[#'+$appFileId+']" WorkingDirectory="INSTALLFOLDER" Icon="SeekrIcon"/></Component></StandardDirectory>')
    $null = $xml.AppendLine('<Feature Id="MainFeature" Title="Seekr" Level="1"><ComponentRef Id="SeekrShortcuts"/><ComponentRef Id="SeekrDesktopShortcut"/>')
    foreach ($component in $components) { $null = $xml.AppendLine('<ComponentRef Id="'+$component+'"/>') }
    $null = $xml.AppendLine('</Feature><ui:WixUI Id="WixUI_InstallDir" InstallDirectory="INSTALLFOLDER"/><UIRef Id="SeekrShortcutOptions"/><WixVariable Id="WixUILicenseRtf" Value="'+(XmlEscape (Join-Path $buildRoot 'License.rtf'))+'"/></Package></Wix>')
    $license = Get-Content -LiteralPath (Join-Path $repoRoot 'LICENSE') -Raw
    $license = $license.Replace('\','\\').Replace('{','\{').Replace('}','\}').Replace("`r`n",'\par ')
    Set-Content -LiteralPath (Join-Path $buildRoot 'License.rtf') -Value ('{\rtf1\ansi '+$license+'}') -Encoding ascii
    $source = Join-Path $buildRoot 'Seekr.wxs'
    Set-Content -LiteralPath $source -Value $xml.ToString() -Encoding utf8
    $msi = Join-Path $releaseRoot ('Seekr-'+$Version+'-win-x64.msi')
    & $wix build $source (Join-Path $PSScriptRoot 'installer\ShortcutOptions.wxs') -arch x64 -ext $extension -culture ru-ru -pdbtype none -out $msi
    if ($LASTEXITCODE -ne 0) { throw 'MSI build failed.' }
    Write-Output $msi
} finally { $env:DOTNET_ROLL_FORWARD = $oldRollForward }

