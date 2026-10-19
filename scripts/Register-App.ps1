[CmdletBinding()]
param([string]$Version='0.1.3.0')
$ErrorActionPreference='Stop'
$installRoot=Join-Path $env:LOCALAPPDATA 'Programs\BeniZip'
$app=Join-Path $installRoot 'BeniZip.exe'
$dataRoot=Join-Path $env:LOCALAPPDATA 'BeniZip'
if(!(Test-Path -LiteralPath $app)){throw 'Install BeniZip first'}
$extensions=@('.zip','.rar','.7z','.tar','.gz','.tgz','.bz2','.xz','.zst','.cab','.arj','.lzh','.001')
function Set-LiteralRegistry([string]$path,[string]$name,[object]$value,[Microsoft.Win32.RegistryValueKind]$kind=[Microsoft.Win32.RegistryValueKind]::String){
 if($path -notmatch '^Software\\(Classes\\|BeniZip\\|RegisteredApplications$|Microsoft\\Windows\\CurrentVersion\\Uninstall\\BeniZip$)'){throw 'Unexpected registry boundary'}
 $registryKey=[Microsoft.Win32.Registry]::CurrentUser.CreateSubKey($path)
 try{$registryKey.SetValue($name,$value,$kind)}finally{$registryKey.Dispose()}
}
New-Item -ItemType Directory -Path $dataRoot -Force|Out-Null
$backup=Join-Path $dataRoot 'association-backup.json'
if(!(Test-Path -LiteralPath $backup)){
 $snapshot=foreach($ext in $extensions){
  $registryKey=[Microsoft.Win32.Registry]::CurrentUser.OpenSubKey('Software\Classes\'+$ext)
  $previous=$null;if($registryKey){$previous=$registryKey.GetValue('');$registryKey.Dispose()}
  $choiceKey=[Microsoft.Win32.Registry]::CurrentUser.OpenSubKey('Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\'+$ext+'\UserChoice')
  $choice=$null;if($choiceKey){$choice=$choiceKey.GetValue('ProgId');$choiceKey.Dispose()}
  [pscustomobject]@{Extension=$ext;PerUserClass=$previous;UserChoice=$choice}
 }
 $snapshot|ConvertTo-Json|Set-Content -LiteralPath $backup
}
Set-LiteralRegistry 'Software\Classes\BeniZip.Archive' '' 'BeniZip archive'
Set-LiteralRegistry 'Software\Classes\BeniZip.Archive\DefaultIcon' '' ('"'+$app+'",0')
Set-LiteralRegistry 'Software\Classes\BeniZip.Archive\shell\open\command' '' ('"'+$app+'" "%1"')
Set-LiteralRegistry 'Software\Classes\BeniZip.Archive\Application' 'ApplicationName' 'BeniZip'
Set-LiteralRegistry 'Software\Classes\BeniZip.Archive\Application' 'ApplicationDescription' 'View, extract, test and create archives'
Set-LiteralRegistry 'Software\Classes\BeniZip.Archive\Application' 'ApplicationIcon' ($app+',0')
$applicationMetadata=[Microsoft.Win32.Registry]::CurrentUser.OpenSubKey('Software\Classes\BeniZip.Archive\Application',$true)
if($applicationMetadata){if($applicationMetadata.GetValue('AppUserModelID') -eq 'Bennet.BeniZip'){$applicationMetadata.DeleteValue('AppUserModelID',$false)};$applicationMetadata.Dispose()}
Set-LiteralRegistry 'Software\Classes\Applications\BeniZip.exe' 'FriendlyAppName' 'BeniZip'
Set-LiteralRegistry 'Software\Classes\Applications\BeniZip.exe\shell\open\command' '' ('"'+$app+'" "%1"')
Set-LiteralRegistry 'Software\Classes\Applications\BeniZip.exe\Application' 'ApplicationName' 'BeniZip'
Set-LiteralRegistry 'Software\Classes\Applications\BeniZip.exe\Application' 'ApplicationIcon' ($app+',0')
Set-LiteralRegistry 'Software\BeniZip\Capabilities' 'ApplicationName' 'BeniZip'
Set-LiteralRegistry 'Software\BeniZip\Capabilities' 'ApplicationDescription' 'View, extract, test and create archives'
Set-LiteralRegistry 'Software\BeniZip\Capabilities' 'ApplicationIcon' ('"'+$app+'",0')
foreach($ext in $extensions){
 Set-LiteralRegistry 'Software\Classes\Applications\BeniZip.exe\SupportedTypes' $ext ''
 Set-LiteralRegistry 'Software\BeniZip\Capabilities\FileAssociations' $ext 'BeniZip.Archive'
 Set-LiteralRegistry ('Software\Classes\'+$ext) '' 'BeniZip.Archive'
 Set-LiteralRegistry ('Software\Classes\'+$ext+'\OpenWithProgids') 'BeniZip.Archive' ''
}
Set-LiteralRegistry 'Software\RegisteredApplications' 'BeniZip' 'Software\BeniZip\Capabilities'
$uninstaller=Join-Path $dataRoot 'tools\BeniZip.Uninstall.exe'
if(!(Test-Path -LiteralPath $uninstaller)){& (Join-Path $PSScriptRoot 'Build-InstallerTools.ps1')}
$uninstallKey='Software\Microsoft\Windows\CurrentVersion\Uninstall\BeniZip'
Set-LiteralRegistry $uninstallKey 'DisplayName' 'BeniZip'
Set-LiteralRegistry $uninstallKey 'DisplayVersion' ((Get-Item -LiteralPath $app).VersionInfo.FileVersion)
Set-LiteralRegistry $uninstallKey 'Publisher' 'Bennet'
Set-LiteralRegistry $uninstallKey 'InstallLocation' $installRoot
Set-LiteralRegistry $uninstallKey 'DisplayIcon' ($app+',0')
Set-LiteralRegistry $uninstallKey 'UninstallString' ('"'+$uninstaller+'"')
Set-LiteralRegistry $uninstallKey 'NoModify' 1 ([Microsoft.Win32.RegistryValueKind]::DWord)
Set-LiteralRegistry $uninstallKey 'NoRepair' 1 ([Microsoft.Win32.RegistryValueKind]::DWord)
$appliesTo=($extensions|ForEach-Object{'System.FileExtension:="'+$_+'"'}) -join ' OR '
foreach($entry in @(@{Name='BeniZip.Open';Label='Open with BeniZip';Flag=''},@{Name='BeniZip.Extract';Label='Extract with BeniZip…';Flag='--extract'},@{Name='BeniZip.ExtractHere';Label='Extract here with BeniZip';Flag='--extract-here'})){
 # Registry API paths are literal. The '*' file-class key must never expand as a provider wildcard.
 $path='Software\Classes\*\shell\'+$entry.Name
 Set-LiteralRegistry $path '' $entry.Label
 Set-LiteralRegistry $path 'AppliesTo' $appliesTo
 Set-LiteralRegistry $path 'Icon' $app
 Set-LiteralRegistry ($path+'\command') '' ('"'+$app+'" '+$entry.Flag+' "%1"')
}
foreach($class in @('*','Directory')){
 $path='Software\Classes\'+$class+'\shell\BeniZip.Create'
 Set-LiteralRegistry $path '' 'Compress with BeniZip…'
 Set-LiteralRegistry $path 'Icon' $app
 Set-LiteralRegistry ($path+'\command') '' ('"'+$app+'" --create "%1"')
}
if(!('BeniZipShellNotify' -as [type])){Add-Type -TypeDefinition 'using System; using System.Runtime.InteropServices; public static class BeniZipShellNotify { [DllImport("shell32.dll")] public static extern void SHChangeNotify(uint e, uint f, IntPtr a, IntPtr b); }'}
[BeniZipShellNotify]::SHChangeNotify(0x08000000,0x3000,[IntPtr]::Zero,[IntPtr]::Zero)
[pscustomobject]@{Registered='BeniZip';Version=$Version;Types=$extensions.Count;PreviousChoices=$backup}
