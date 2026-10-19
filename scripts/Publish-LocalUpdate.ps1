[CmdletBinding()]
param([string]$Version='0.1.3.0',[string]$Notes='Compact white archive browser with extraction progress, speed and remaining time. Adding files preserves the archive password settings.')
$ErrorActionPreference='Stop'
$projectRoot=Split-Path -Parent $PSScriptRoot
$runtime=Join-Path $projectRoot ('build\app-'+$Version)
$channelRoot=Join-Path $env:LOCALAPPDATA 'BeniZip\releases'
$keyPath=Join-Path $env:LOCALAPPDATA 'BeniZip-ReleaseKeys\release.private.xml'
if(!(Test-Path -LiteralPath (Join-Path $runtime 'BeniZip.exe'))){throw 'Build this version first'}
New-Item -ItemType Directory -Path $channelRoot -Force|Out-Null
$package=Join-Path $channelRoot ('BeniZip-'+$Version+'-win-x64.zip')
if(Test-Path -LiteralPath $package){throw 'Immutable release already exists. Build a higher version rather than replacing a published package.'}
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::CreateFromDirectory($runtime,$package,[IO.Compression.CompressionLevel]::Optimal,$false)
$payload=[ordered]@{applicationId='BeniZip.Windows.x64';version=$Version;packageUri=([Uri]$package).AbsoluteUri;bytes=(Get-Item -LiteralPath $package).Length;sha256=(Get-FileHash -LiteralPath $package).Hash.ToLowerInvariant();notes=$Notes}|ConvertTo-Json -Compress
$bytes=[Text.Encoding]::UTF8.GetBytes($payload)
$rsa=[Security.Cryptography.RSACryptoServiceProvider]::new()
$rsa.PersistKeyInCsp=$false
$rsa.FromXmlString([IO.File]::ReadAllText($keyPath))
$signature=$rsa.SignData($bytes,[Security.Cryptography.CryptoConfig]::MapNameToOID('SHA256'))
$rsa.Dispose()
$envelope=@{payload=[Convert]::ToBase64String($bytes);signature=[Convert]::ToBase64String($signature)}|ConvertTo-Json -Compress
$manifest=Join-Path $channelRoot 'manifest.json'
[IO.File]::WriteAllText($manifest,$envelope,[Text.UTF8Encoding]::new($false))
$config=@{feed=([Uri]$manifest).AbsoluteUri}|ConvertTo-Json -Compress
[IO.File]::WriteAllText((Join-Path $env:LOCALAPPDATA 'BeniZip\channel.json'),$config,[Text.UTF8Encoding]::new($false))
[pscustomobject]@{Version=$Version;Package=$package;Manifest=$manifest;Sha256=(Get-FileHash -LiteralPath $package).Hash}
