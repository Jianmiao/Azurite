#requires -Version 7.2
[CmdletBinding()]
param([Parameter(Mandatory)][string]$ArchivePath,[Parameter(Mandatory)][string]$BuildReceiptPath,
    [Parameter(Mandatory)][string]$CecilPath,[switch]$RequireSidecar,[switch]$RequireReleaseQualified)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
Import-Module (Join-Path $PSScriptRoot 'SourceStaging.psm1') -Force
if($RequireReleaseQualified){Assert-StagingNotReleaseQualified}
$root=Resolve-StagingRoot (Split-Path $PSScriptRoot -Parent)
$verified=Test-StagingReceipt $BuildReceiptPath $root $CecilPath
$path=Resolve-StagingRoot $ArchivePath
$expected=Get-StagingPayload $verified $root
$prefix="Azurite/$($verified.Receipt.identity.version)/"
$seen=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
$actual=[Collections.Generic.Dictionary[string,byte[]]]::new([StringComparer]::Ordinal)
$zip=[IO.Compression.ZipFile]::OpenRead($path)
try{
    Assert-Staging ($zip.Entries.Count -eq $expected.Count+1) 'Unexpected staging archive entry count.'
    $total=0L
    foreach($entry in $zip.Entries){
        $n=$entry.FullName
        Assert-Staging ($seen.Add($n)) 'Duplicate or case-conflicting archive member.'
        Assert-Staging ($n.StartsWith($prefix,[StringComparison]::Ordinal) -and !$n.Contains('\') -and !$n.Contains(':') -and @($n.Split('/')|Where-Object {$_ -in @('.','..','')}).Count -eq 0) 'Unsafe or outside-root archive member.'
        $relative=$n.Substring($prefix.Length)
        Assert-Staging ($relative -ceq 'SHA256SUMS.txt' -or $expected.Contains($relative)) "Unapproved archive payload: $relative"
        $total+=$entry.Length;Assert-Staging ($entry.Length -le 16MB -and $total -le 32MB) 'Archive size limit exceeded.'
        $s=$entry.Open();$m=[IO.MemoryStream]::new()
        try{$s.CopyTo($m);Assert-Staging ($m.Length -eq $entry.Length) 'Archive length mismatch.';$actual.Add($relative,$m.ToArray())}finally{$s.Dispose();$m.Dispose()}
    }
}finally{$zip.Dispose()}
$utf8=[Text.UTF8Encoding]::new($false,$true)
$checks=[Collections.Generic.Dictionary[string,string]]::new([StringComparer]::Ordinal)
foreach($line in ($utf8.GetString($actual['SHA256SUMS.txt']) -split '\r?\n')){
    if(!$line){continue}
    Assert-Staging ($line -cmatch '^(?<hash>[a-f0-9]{64})  (?<name>[^/\\]+)$') 'Malformed per-file checksum.'
    $n=$Matches.name;$h=$Matches.hash
    Assert-Staging ($expected.Contains($n) -and !$checks.ContainsKey($n)) 'Unexpected or duplicate checksum member.'
    $checks.Add($n,$h)
}
Assert-Staging ($checks.Count -eq $expected.Count) 'Checksum coverage is incomplete.'
foreach($name in $expected.Keys){
    Assert-Staging ($actual.ContainsKey($name)) "Missing archive member: $name"
    $hash=Get-StagingByteHash $actual[$name]
    Assert-Staging ($hash -ceq $checks[$name]) "Per-file checksum mismatch: $name"
    Assert-Staging ($hash -ceq (Get-StagingByteHash $expected[$name])) "Archive differs from verified same-build source payload: $name"
}
$hash=Get-StagingHash $path;$sidecar=$path+'.sha256'
if($RequireSidecar){Assert-Staging (Test-Path -LiteralPath $sidecar -PathType Leaf) 'Missing archive checksum sidecar.'}
if(Test-Path -LiteralPath $sidecar){Assert-Staging ([IO.File]::ReadAllText($sidecar).Trim() -ceq "$hash  $([IO.Path]::GetFileName($path))") 'Archive checksum sidecar mismatch.'}
[pscustomobject]@{Result='PASS';Archive=$path;SHA256=$hash;PayloadFiles=$actual.Count;SourceCommit=$verified.Receipt.source.commit;SourceTree=$verified.Receipt.source.tree;CoreMode='SourceStaging';BuildId=$verified.Receipt.buildId;ReleaseEligible=$false;RuntimeAcceptance='not-performed';NativeExecuted=$false}
