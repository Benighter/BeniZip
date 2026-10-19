[CmdletBinding()]
param([string]$Version='0.2.0.0',[switch]$OpenDefaults)
$ErrorActionPreference='Stop'
$projectRoot=Split-Path -Parent $PSScriptRoot
$runtime=Join-Path $projectRoot ('build\app-'+$Version)
$installRoot=Join-Path $env:LOCALAPPDATA 'Programs\BeniZip'
if(!(Test-Path -LiteralPath (Join-Path $runtime 'BeniZip.exe'))){throw 'Build this version first'}
$alreadyCopied=$false
if(Test-Path -LiteralPath (Join-Path $installRoot 'BeniZip.exe')){
 $alreadyCopied=$true
 foreach($sourceFile in Get-ChildItem -LiteralPath $runtime -File -Recurse){
  $relative=$sourceFile.FullName.Substring($runtime.Length).TrimStart('\')
  $installedFile=Join-Path $installRoot $relative
  if(!(Test-Path -LiteralPath $installedFile) -or (Get-FileHash -LiteralPath $sourceFile.FullName).Hash -ne (Get-FileHash -LiteralPath $installedFile).Hash){throw 'A different BeniZip version is installed. Use its signed update path.'}
 }
}
New-Item -ItemType Directory -Path $installRoot -Force|Out-Null
if(!$alreadyCopied){Get-ChildItem -LiteralPath $runtime | Copy-Item -Destination $installRoot -Recurse}
& (Join-Path $PSScriptRoot 'Register-App.ps1') -Version $Version
$shortcutShell=New-Object -ComObject WScript.Shell
foreach($shortcutRoot in @([Environment]::GetFolderPath('DesktopDirectory'),(Join-Path ([Environment]::GetFolderPath('StartMenu')) 'Programs'))){
 $shortcut=$shortcutShell.CreateShortcut((Join-Path $shortcutRoot 'BeniZip.lnk'))
 $shortcut.TargetPath=Join-Path $installRoot 'BeniZip.exe'
 $shortcut.WorkingDirectory=$installRoot
 $shortcut.IconLocation=(Join-Path $installRoot 'BeniZip.exe')+',0'
 $shortcut.Description='View, extract and create archives'
 $shortcut.Save()
}
if($OpenDefaults){Start-Process 'ms-settings:defaultapps?registeredAppUser=BeniZip'}
[pscustomobject]@{Installed=$installRoot;Version=$Version;Exe=(Join-Path $installRoot 'BeniZip.exe')}
