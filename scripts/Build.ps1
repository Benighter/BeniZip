[CmdletBinding()]
param([string]$Version='0.2.1.0')
$ErrorActionPreference='Stop'
$projectRoot=Split-Path -Parent $PSScriptRoot
$buildRoot=Join-Path $projectRoot 'build'
$runtime=Join-Path $buildRoot ('app-'+$Version)
$compiler='C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if(!(Test-Path -LiteralPath $compiler)){throw '.NET Framework C# compiler is missing'}
New-Item -ItemType Directory -Path $runtime,(Join-Path $runtime 'engine'),(Join-Path $runtime 'assets') -Force | Out-Null
$publicKey=Join-Path $projectRoot 'assets\release-public.xml'
if(!(Test-Path -LiteralPath $publicKey)){throw 'The public release verification key is missing. Building never requires the private publishing key.'}
if(!(Test-Path -LiteralPath (Join-Path $projectRoot 'assets\BeniZip.ico'))){
 & python (Join-Path $PSScriptRoot 'Make-Icon.py')
 if($LASTEXITCODE -ne 0){throw 'Icon conversion failed'}
}
Copy-Item -LiteralPath (Join-Path $projectRoot 'assets\BeniZip.ico') -Destination (Join-Path $runtime 'BeniZip.ico')
Copy-Item -LiteralPath (Join-Path $projectRoot 'assets\icon.png') -Destination (Join-Path $runtime 'assets\icon.png')
Get-ChildItem -LiteralPath (Join-Path $projectRoot 'vendor\7zip') -File | Copy-Item -Destination (Join-Path $runtime 'engine')
$notices=[IO.File]::ReadAllText((Join-Path $projectRoot 'LICENSE'))+[Environment]::NewLine+[IO.File]::ReadAllText((Join-Path $projectRoot 'THIRD-PARTY-NOTICES.md'))+[Environment]::NewLine+[IO.File]::ReadAllText((Join-Path $projectRoot 'vendor\7zip\License.txt'))
[IO.File]::WriteAllText((Join-Path $runtime 'engine\License.txt'),$notices,[Text.UTF8Encoding]::new($false))
$versionSource=Join-Path $buildRoot 'Version.txt'
# The generated assembly attributes are a compiler input; .txt avoids confusing disposable output with authored source.
[IO.File]::WriteAllText($versionSource,('[assembly:System.Reflection.AssemblyVersion("'+$Version+'")]'+[Environment]::NewLine+'[assembly:System.Reflection.AssemblyFileVersion("'+$Version+'")]'))
$source=Join-Path $projectRoot 'src'
$references=@('System.dll','System.Core.dll','System.Drawing.dll','System.Windows.Forms.dll','System.Web.Extensions.dll','System.IO.Compression.dll','System.IO.Compression.FileSystem.dll','System.Xaml.dll','C:\Windows\Microsoft.NET\Framework64\v4.0.30319\WPF\WindowsBase.dll','C:\Windows\Microsoft.NET\Framework64\v4.0.30319\WPF\PresentationCore.dll','C:\Windows\Microsoft.NET\Framework64\v4.0.30319\WPF\PresentationFramework.dll')
$common=@('/nologo','/optimize+','/platform:x64','/langversion:5',('/win32manifest:'+(Join-Path $source 'app.manifest')),('/win32icon:'+(Join-Path $projectRoot 'assets\BeniZip.ico')),('/resource:'+$publicKey+',BeniZip.ReleaseKey'),('/resource:'+(Join-Path $source 'Shell.Light.xaml')+',BeniZip.Shell.xaml'))+@($references|ForEach-Object{'/reference:'+$_})
$appSources=@((Join-Path $source 'ArchiveEngine.cs'),(Join-Path $source 'Updates.cs'),(Join-Path $source 'ArchiveAssociations.cs'),(Join-Path $source 'ArchiveWindow.cs'),(Join-Path $source 'ProgressWindow.cs'),(Join-Path $source 'Program.cs'),$versionSource)
& $compiler @common '/target:winexe' ('/out:'+(Join-Path $runtime 'BeniZip.exe')) @appSources
if($LASTEXITCODE -ne 0){throw 'BeniZip build failed'}
& $compiler @common '/target:exe' ('/out:'+(Join-Path $runtime 'BeniZip.Cli.exe')) @appSources
if($LASTEXITCODE -ne 0){throw 'BeniZip CLI build failed'}
& $compiler @common '/target:winexe' ('/out:'+(Join-Path $runtime 'BeniZip.Update.exe')) (Join-Path $source 'ArchiveEngine.cs') (Join-Path $source 'Updates.cs') (Join-Path $source 'UpdateProgram.cs') $versionSource
if($LASTEXITCODE -ne 0){throw 'BeniZip updater build failed'}
foreach($name in @('BeniZip.exe','BeniZip.Cli.exe','BeniZip.Update.exe')){Copy-Item -LiteralPath (Join-Path $source 'app.config') -Destination (Join-Path $runtime ($name+'.config'))}
[pscustomobject]@{Version=$Version;Runtime=$runtime;Sha256=(Get-FileHash -LiteralPath (Join-Path $runtime 'BeniZip.exe')).Hash;EngineVersion=(Get-Item -LiteralPath (Join-Path $runtime 'engine\7z.exe')).VersionInfo.FileVersion}
