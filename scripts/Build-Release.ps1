[CmdletBinding()]
param([string]$Version='0.2.0.0',[string]$Repository='Benighter/BeniZip',[string]$Notes='Compact white archive browser with extraction progress and remaining time. Install with every supported archive type selected. Updates are delivered from GitHub and verified before installation.',[string]$SigningKey=(Join-Path $env:LOCALAPPDATA 'BeniZip-ReleaseKeys\release.private.xml'))
$ErrorActionPreference='Stop'
$projectRoot=Split-Path -Parent $PSScriptRoot
$parsed=[Version]$Version
$tag='v'+$parsed.ToString(3)
$release=Join-Path $projectRoot ('build\release-'+$parsed.ToString(3))
if(Test-Path -LiteralPath $release){throw 'Release output already exists. Keep published releases immutable; choose a new version.'}
if(!(Test-Path -LiteralPath $SigningKey)){throw 'Publishing requires the existing private release key outside the repository. Source builds do not.'}
$rsa=[Security.Cryptography.RSACryptoServiceProvider]::new();$rsa.PersistKeyInCsp=$false
try{
 $rsa.FromXmlString([IO.File]::ReadAllText($SigningKey))
 if($rsa.ToXmlString($false) -ne [IO.File]::ReadAllText((Join-Path $projectRoot 'assets\release-public.xml')).Trim()){throw 'The signing key does not match the pinned release identity.'}
 & (Join-Path $PSScriptRoot 'Build.ps1') -Version $Version | Out-Null
 & (Join-Path $PSScriptRoot 'Build-InstallerTools.ps1') -StageOnly
 New-Item -ItemType Directory -Path $release|Out-Null
 $package=Join-Path $release ('BeniZip-'+$parsed.ToString(3)+'-win-x64.zip')
 Add-Type -AssemblyName System.IO.Compression.FileSystem
 [IO.Compression.ZipFile]::CreateFromDirectory((Join-Path $projectRoot ('build\app-'+$Version)),$package,[IO.Compression.CompressionLevel]::Optimal,$false)
 $payload=[ordered]@{applicationId='BeniZip.Windows.x64';version=$Version;packageUri=('https://github.com/'+$Repository+'/releases/download/'+$tag+'/'+[IO.Path]::GetFileName($package));bytes=(Get-Item -LiteralPath $package).Length;sha256=(Get-FileHash -LiteralPath $package).Hash.ToLowerInvariant();notes=$Notes}|ConvertTo-Json -Compress
 $bytes=[Text.Encoding]::UTF8.GetBytes($payload)
 $envelope=@{payload=[Convert]::ToBase64String($bytes);signature=[Convert]::ToBase64String($rsa.SignData($bytes,[Security.Cryptography.CryptoConfig]::MapNameToOID('SHA256')))}|ConvertTo-Json -Compress
 $manifest=Join-Path $release 'manifest.json';[IO.File]::WriteAllText($manifest,$envelope,[Text.UTF8Encoding]::new($false))
 $compiler='C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
 $references=@('System.dll','System.Core.dll','System.Web.Extensions.dll','System.IO.Compression.dll','System.IO.Compression.FileSystem.dll','System.Xaml.dll','System.Drawing.dll','System.Windows.Forms.dll','C:\Windows\Microsoft.NET\Framework64\v4.0.30319\WPF\WindowsBase.dll','C:\Windows\Microsoft.NET\Framework64\v4.0.30319\WPF\PresentationCore.dll','C:\Windows\Microsoft.NET\Framework64\v4.0.30319\WPF\PresentationFramework.dll')
 $setup=Join-Path $release ('BeniZip-Setup-'+$parsed.ToString(3)+'.exe')
 $options=@('/nologo','/optimize+','/target:winexe','/platform:x64','/langversion:5',('/out:'+$setup),('/win32manifest:'+(Join-Path $projectRoot 'src\app.manifest')),('/win32icon:'+(Join-Path $projectRoot 'assets\BeniZip.ico')),('/resource:'+(Join-Path $projectRoot 'assets\release-public.xml')+',BeniZip.ReleaseKey'),('/resource:'+$package+',BeniZip.Package'),('/resource:'+$manifest+',BeniZip.Manifest'),('/resource:'+(Join-Path $projectRoot 'build\installer-tools\BeniZip.Uninstall.exe')+',BeniZip.Uninstaller'))+@($references|ForEach-Object{'/reference:'+$_})
 & $compiler @options (Join-Path $projectRoot 'src\ArchiveEngine.cs') (Join-Path $projectRoot 'src\Updates.cs') (Join-Path $projectRoot 'src\ArchiveAssociations.cs') (Join-Path $projectRoot 'src\SetupProgram.cs') (Join-Path $projectRoot 'build\Version.txt')
 if($LASTEXITCODE -ne 0){throw 'Setup compilation failed'}
 $checksums=Get-ChildItem -LiteralPath $release -File|Sort-Object Name|ForEach-Object{(Get-FileHash -LiteralPath $_.FullName).Hash.ToLowerInvariant()+'  '+$_.Name}
 [IO.File]::WriteAllLines((Join-Path $release 'SHA256SUMS.txt'),$checksums,[Text.UTF8Encoding]::new($false))
 [pscustomobject]@{Version=$Version;Tag=$tag;Directory=$release;Package=$package;Installer=$setup;Manifest=$manifest}
}finally{$rsa.Dispose()}
