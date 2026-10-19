[CmdletBinding()]
param([string]$ReportPath)
$ErrorActionPreference='Stop'
if(!('BeniZipAssociationQuery' -as [type])){
 Add-Type -TypeDefinition @'
using System;
using System.Text;
using System.Runtime.InteropServices;
public static class BeniZipAssociationQuery {
 [DllImport("shlwapi.dll",CharSet=CharSet.Unicode)]
 public static extern int AssocQueryString(uint flags,uint kind,string association,string verb,StringBuilder output,ref uint size);
 public static string Read(string extension,uint kind) {var output=new StringBuilder(4096);uint size=4096;int result=AssocQueryString(0,kind,extension,"open",output,ref size);return result==0?output.ToString():null;}
}
'@
}
$expected=Join-Path $env:LOCALAPPDATA 'Programs\BeniZip\BeniZip.exe'
$rows=foreach($extension in @('.zip','.rar','.7z','.tar','.gz','.tgz','.bz2','.xz','.zst','.cab','.arj','.lzh','.001')){
 $executable=[BeniZipAssociationQuery]::Read($extension,2)
 [pscustomobject]@{Extension=$extension;Executable=$executable;ProgId=[BeniZipAssociationQuery]::Read($extension,20);BeniZip=($executable -eq $expected)}
}
$report=[pscustomobject]@{ExpectedExecutable=$expected;AllHandledByBeniZip=(@($rows|Where-Object{!$_.BeniZip}).Count -eq 0);Associations=@($rows)}
if($ReportPath){$report|ConvertTo-Json -Depth 5|Set-Content -LiteralPath $ReportPath}
$report|ConvertTo-Json -Depth 5
