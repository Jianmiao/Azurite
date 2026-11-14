#requires -Version 7.2
[CmdletBinding()]
param([Parameter(Mandatory)][string]$BuildReceiptPath,[Parameter(Mandatory)][string]$OutputDirectory,
    [Parameter(Mandatory)][string]$CecilPath,[switch]$RequireReleaseQualified)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
Import-Module (Join-Path $PSScriptRoot 'SourceStaging.psm1') -Force
if($RequireReleaseQualified){Assert-StagingNotReleaseQualified}
$root=Resolve-StagingRoot (Split-Path $PSScriptRoot -Parent)
$verified=Test-StagingReceipt $BuildReceiptPath $root $CecilPath
$out=[IO.Path]::GetFullPath($OutputDirectory).TrimEnd([IO.Path]::DirectorySeparatorChar)
Assert-Staging ($out.StartsWith((Join-Path $root 'artifacts')+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) 'Staging output must remain under this source repository artifacts/.'
Assert-Staging (!(Test-Path -LiteralPath $out)) 'Refusing to overwrite a prior staging directory.'
[void][IO.Directory]::CreateDirectory($out);[void](Resolve-StagingRoot $out)
$version=$verified.Receipt.identity.version;$archivePath=Join-Path $out "Azurite-$version.source-staging.zip"
$temporary=Join-Path $out 'staging.partial';$payload=Get-StagingPayload $verified $root
$prefix="Azurite/$version/"
$stream=[IO.FileStream]::new($temporary,[IO.FileMode]::CreateNew,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)
$zip=[IO.Compression.ZipArchive]::new($stream,[IO.Compression.ZipArchiveMode]::Create,$false)
try{
    $lines=[Collections.Generic.List[string]]::new()
    foreach($name in $payload.Keys){
        [byte[]]$bytes=$payload[$name];$lines.Add("$(Get-StagingByteHash $bytes)  $name")
        $entry=$zip.CreateEntry($prefix+$name,[IO.Compression.CompressionLevel]::Optimal);$s=$entry.Open()
        try{$s.Write($bytes,0,$bytes.Length)}finally{$s.Dispose()}
    }
    $sum=[Text.UTF8Encoding]::new($false).GetBytes(($lines -join "`n")+"`n")
    $entry=$zip.CreateEntry($prefix+'SHA256SUMS.txt');$s=$entry.Open();try{$s.Write($sum,0,$sum.Length)}finally{$s.Dispose()}
}finally{$zip.Dispose();$stream.Dispose()}
$args=@{ArchivePath=$temporary;BuildReceiptPath=$BuildReceiptPath;CecilPath=$CecilPath}
$null=& (Join-Path $PSScriptRoot 'Test-SourceStagingArchive.ps1') @args
[IO.File]::Move($temporary,$archivePath)
$digest=Get-StagingHash $archivePath
[IO.File]::WriteAllText(($archivePath+'.sha256'),"$digest  $([IO.Path]::GetFileName($archivePath))`n",[Text.UTF8Encoding]::new($false))
$args.ArchivePath=$archivePath;$args.RequireSidecar=$true
& (Join-Path $PSScriptRoot 'Test-SourceStagingArchive.ps1') @args
