[CmdletBinding()]
param([switch]$StageOnly)
$ErrorActionPreference='Stop'
$projectRoot=Split-Path -Parent $PSScriptRoot
$stage=Join-Path $projectRoot 'build\installer-tools'
$destination=Join-Path $env:LOCALAPPDATA 'BeniZip\tools'
New-Item -ItemType Directory -Path $stage -Force|Out-Null
$output=Join-Path $stage 'BeniZip.Uninstall.exe'
& 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe' /nologo /optimize+ /platform:x64 /target:winexe ('/out:'+$output) ('/win32icon:'+(Join-Path $projectRoot 'assets\BeniZip.ico')) ('/win32manifest:'+(Join-Path $projectRoot 'src\app.manifest')) /reference:System.dll /reference:System.Core.dll /reference:System.Windows.Forms.dll /reference:System.Web.Extensions.dll (Join-Path $projectRoot 'src\UninstallProgram.cs')
if($LASTEXITCODE -ne 0){throw 'Installer helper compilation failed'}
if(!$StageOnly){New-Item -ItemType Directory -Path $destination -Force|Out-Null;Copy-Item -LiteralPath $output -Destination (Join-Path $destination 'BeniZip.Uninstall.exe') -Force}
