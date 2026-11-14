#requires -Version 7.2
[CmdletBinding()]
param([Parameter(Mandatory)][string]$BuildReceiptPath,[Parameter(Mandatory)][string]$OtherBuildReceiptPath,
    [Parameter(Mandatory)][string]$ExperimentalPluginPath,[Parameter(Mandatory)][string]$CecilPath,
    [Parameter(Mandatory)][string]$EvidenceDirectory)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
$root=Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
Import-Module (Join-Path $root 'tools/SourceStaging.psm1') -Force
$e=[IO.Path]::GetFullPath($EvidenceDirectory)
Assert-Staging ($e.StartsWith((Join-Path $root 'artifacts')+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase) -and !(Test-Path -LiteralPath $e)) 'Choose a new evidence directory inside source artifacts.'
[void][IO.Directory]::CreateDirectory($e)
$results=[Collections.Generic.List[object]]::new()
function Test([string]$Name,[scriptblock]$Body){try{& $Body;$results.Add(@{name=$Name;passed=$true});"PASS $Name"}catch{$results.Add(@{name=$Name;passed=$false;message=$_.Exception.Message;stack=$_.ScriptStackTrace});"FAIL ${Name}: $($_.Exception.Message)"}}
function Check([bool]$Ok,[string]$Why){if(!$Ok){throw $Why}}
function Reject([scriptblock]$Body,[string]$Pattern){$caught=$false;try{& $Body|Out-Null}catch{$caught=$_.Exception.Message -match $Pattern;if(!$caught){throw "Unexpected failure instead of ${Pattern}: $($_.Exception.Message)"}};Check $caught "Expected rejection: $Pattern"}
function CopyCase([string]$Name){
    $dir=Join-Path $e $Name;[void][IO.Directory]::CreateDirectory($dir)
    $r=Get-Content -LiteralPath $BuildReceiptPath -Raw|ConvertFrom-Json;$old=Split-Path $BuildReceiptPath -Parent
    foreach($f in $r.files.PSObject.Properties){$dest=Join-Path $dir $f.Value.path;[void][IO.Directory]::CreateDirectory((Split-Path $dest -Parent));Copy-Item -LiteralPath (Join-Path $old $f.Value.path) -Destination $dest}
    [pscustomobject]@{dir=$dir;receipt=$r;path=(Join-Path $dir 'source-staging-build.json')}
}
function WriteCase($Case){Write-StagingJson $Case.path $Case.receipt}
function ReplaceCaseFile($Case,[string]$Role,[string]$From){$f=$Case.receipt.files.$Role;$dest=Join-Path $Case.dir $f.path;Copy-Item -LiteralPath $From -Destination $dest -Force;$f.sha256=Get-StagingHash $dest}
function MutateArchive([string]$Name,[scriptblock]$Change,[bool]$Rehash=$true){
    $payload=[Collections.Specialized.OrderedDictionary]::new([StringComparer]::Ordinal);$z=[IO.Compression.ZipFile]::OpenRead($script:goodArchive)
    try{foreach($entry in $z.Entries){$s=$entry.Open();$m=[IO.MemoryStream]::new();try{$s.CopyTo($m);$payload[$entry.FullName]=$m.ToArray()}finally{$m.Dispose();$s.Dispose()}}}finally{$z.Dispose()}
    & $Change $payload
    if($Rehash){$lines=@(foreach($key in $payload.Keys){if(!$key.EndsWith('/SHA256SUMS.txt')){"$(Get-StagingByteHash $payload[$key])  $($key.Substring($key.LastIndexOf('/')+1))"}});$payload[$script:prefix+'SHA256SUMS.txt']=[Text.UTF8Encoding]::new($false).GetBytes(($lines -join "`n")+"`n")}
    $out=Join-Path $e ($Name+'.zip');$fs=[IO.FileStream]::new($out,[IO.FileMode]::CreateNew);$zip=[IO.Compression.ZipArchive]::new($fs,[IO.Compression.ZipArchiveMode]::Create,$false)
    try{foreach($k in $payload.Keys){$entry=$zip.CreateEntry($k);$s=$entry.Open();try{$bytes=[byte[]]$payload[$k];$s.Write($bytes,0,$bytes.Length)}finally{$s.Dispose()}}}finally{$zip.Dispose();$fs.Dispose()}
    $out
}
$originalHash=Get-StagingHash (Join-Path $root 'references/Azurite.Core.dll')
$verified=Test-StagingReceipt $BuildReceiptPath $root $CecilPath
$other=Test-StagingReceipt $OtherBuildReceiptPath $root $CecilPath
$stage=Join-Path $root 'tools/Stage-SourceCandidate.ps1';$archiveValidator=Join-Path $root 'tools/Test-SourceStagingArchive.ps1'
$prefix="Azurite/$($verified.Receipt.identity.version)/"
Test 'real producer output binds all source, host, metadata and API evidence' {
    Check ($verified.Receipt.identity.coreMemberReferencesResolved -gt 0 -and !$verified.Receipt.releaseEligible -and !$verified.Receipt.nativeExecuted) 'Incomplete identity or unsupported acceptance claim.'
    Check ($verified.Receipt.files.core.sha256 -ceq $verified.Receipt.files.coreBuild.sha256) 'Core copies differ.'
    Check ($verified.Receipt.identity.pluginIdentity -like 'Azurite, Version=1.0.40.0,*' -and $verified.Receipt.identity.coreIdentity -like 'Azurite.Core, Version=1.0.40.0,*') 'Version alignment failed.'
}
Test 'real same-source second producer has different build identity and bytes' {
    Check ($verified.Receipt.buildId -cne $other.Receipt.buildId -and $verified.Receipt.files.core.sha256 -cne $other.Receipt.files.core.sha256 -and $verified.Receipt.identity.coreIdentity -ceq $other.Receipt.identity.coreIdentity) 'Independent same-ABI pair was not produced.'
}
Test 'Source candidate stages and verifies complete archive plus sidecar' {
    $r=& $stage -BuildReceiptPath $BuildReceiptPath -OutputDirectory (Join-Path $e 'good-stage') -CecilPath $CecilPath
    Check ($r.Result -ceq 'PASS' -and !$r.ReleaseEligible -and $r.RuntimeAcceptance -ceq 'not-performed' -and $r.PayloadFiles -eq 9) 'Staging archive did not round-trip.'
    $script:goodArchive=$r.Archive;Write-StagingJson (Join-Path $e 'archive-result.json') $r
}
Test 'staging cannot overwrite a prior output' {Reject {& $stage -BuildReceiptPath $BuildReceiptPath -OutputDirectory (Join-Path $e 'good-stage') -CecilPath $CecilPath} 'overwrite'}
Test 'staging cannot escape approved source artifacts root' {Reject {& $stage -BuildReceiptPath $BuildReceiptPath -OutputDirectory (Join-Path $root 'escape-stage') -CecilPath $CecilPath} 'under this source repository artifacts'}
Test 'missing receipt is rejected' {Reject {Test-StagingReceipt (Join-Path $e 'absent.json') $root $CecilPath} 'does not exist|Cannot find'}
Test 'stale source commit is rejected' {$x=CopyCase 'stale-commit';$x.receipt.source.commit='0000000000000000000000000000000000000000';WriteCase $x;Reject {Test-StagingReceipt $x.path $root $CecilPath} 'source commit/tree/file inventory'}
Test 'same commit but different source inventory is rejected' {$x=CopyCase 'stale-input';$x.receipt.source.files[0].sha256='0'*64;WriteCase $x;Reject {Test-StagingReceipt $x.path $root $CecilPath} 'source commit/tree/file inventory'}
Test 'wrong Core mode is rejected' {$x=CopyCase 'wrong-mode';$x.receipt.coreMode='Original';WriteCase $x;Reject {Test-StagingReceipt $x.path $root $CecilPath} 'mode or native qualification'}
Test 'changing a boolean cannot self-certify native acceptance' {$x=CopyCase 'false-qualified';$x.receipt.releaseEligible=$true;$x.receipt.runtimeAcceptance='passed';WriteCase $x;Reject {Test-StagingReceipt $x.path $root $CecilPath} 'mode or native qualification'}
Test 'stale output bytes are rejected before staging' {$x=CopyCase 'tampered-core';[IO.File]::AppendAllText((Join-Path $x.dir $x.receipt.files.core.path),'tampered');WriteCase $x;Reject {Test-StagingReceipt $x.path $root $CecilPath} 'file hash mismatch: core'}
Test 'same ABI different-build Core is rejected even after hash rewrites' {$x=CopyCase 'mixed-core';ReplaceCaseFile $x 'core' $other.Paths.core;ReplaceCaseFile $x 'coreBuild' $other.Paths.coreBuild;WriteCase $x;Reject {Test-StagingReceipt $x.path $root $CecilPath} 'not emitted with this SourceStaging build identity'}
Test 'Original Core substitution is rejected even after hash rewrites' {$x=CopyCase 'original-core';ReplaceCaseFile $x 'core' (Join-Path $root 'references/Azurite.Core.dll');ReplaceCaseFile $x 'coreBuild' (Join-Path $root 'references/Azurite.Core.dll');WriteCase $x;Reject {Test-StagingReceipt $x.path $root $CecilPath} 'Original Core cannot'}
Test 'renamed actual AuthoringSource DLL is rejected' {$x=CopyCase 'renamed-experimental';ReplaceCaseFile $x 'plugin' $ExperimentalPluginPath;WriteCase $x;Reject {Test-StagingReceipt $x.path $root $CecilPath} 'Compiler assembly identity is not canonical'}
Test 'missing source Core API is rejected beyond names and hashes' {
    $x=CopyCase 'broken-api';$cp=Join-Path $x.dir $x.receipt.files.core.path
    $a=[Mono.Cecil.AssemblyDefinition]::ReadAssembly($cp)
    try{$t=@($a.MainModule.Types|Where-Object FullName -ceq 'Azurite.Core.CardPackV2Workflow')[0];$ctor=@($t.Methods|Where-Object Name -ceq '.ctor')[0];$ctor.Name='RemovedConstructor';$a.Write($cp+'.changed')}finally{$a.Dispose()}
    ReplaceCaseFile $x 'core' ($cp+'.changed');ReplaceCaseFile $x 'coreBuild' ($cp+'.changed');WriteCase $x
    Reject {Test-StagingReceipt $x.path $root $CecilPath} 'Unresolved source Core member'
}
Test 'manifest version must match actual compiled identities' {
    $path=Join-Path $e 'bad-manifest.json';$m=Get-Content -LiteralPath $verified.Paths.manifest -Raw|ConvertFrom-Json;$m.version_number='1.0.41';Write-StagingJson $path $m
    Reject {Test-StagingIdentity $verified.Paths.plugin $verified.Paths.core $path $verified.Receipt.buildId $CecilPath} 'version does not match manifest'
}
Test 'compiled consumer protocol-major change is rejected' {
    $x=CopyCase 'wrong-protocol';$pp=Join-Path $x.dir $x.receipt.files.plugin.path
    $a=[Mono.Cecil.AssemblyDefinition]::ReadAssembly($pp)
    try{$t=@($a.MainModule.Types|Where-Object FullName -ceq 'Azurite.RenderControlClient')[0];$ctor=@($t.Methods|Where-Object Name -ceq '.ctor')[0];$il=$ctor.Body.Instructions;for($i=0;$i -lt $il.Count;$i++){if([string]$il[$i].Operand -ceq 'ProtocolVersion'){$il[$i+3].OpCode=[Mono.Cecil.Cil.OpCodes]::Ldc_I4_2}};$a.Write($pp+'.changed')}finally{$a.Dispose()}
    ReplaceCaseFile $x 'plugin' ($pp+'.changed');WriteCase $x
    Reject {Test-StagingReceipt $x.path $root $CecilPath} 'protocol-major 1 equality guard'
}
Test 'receipt member path traversal is rejected' {$x=CopyCase 'traversal-receipt';$x.receipt.files.core.path='../outside.dll';WriteCase $x;Reject {Test-StagingReceipt $x.path $root $CecilPath} 'Unsafe relative member'}
Test 'dirty source cannot reuse an otherwise matching receipt' {
    $path=Join-Path $root 'source-staging-test-dirty.txt';Check (!(Test-Path -LiteralPath $path)) 'Dirty control path already exists.'
    try{[IO.File]::WriteAllText($path,'deliberately untracked source input');Reject {Test-StagingReceipt $BuildReceiptPath $root $CecilPath} 'clean source tree'}finally{[IO.File]::Delete($path)}
}
Test 'rehashed ZIP cannot claim native acceptance' {
    $a=MutateArchive 'fake-native-acceptance' {param($p) $b=[Text.Encoding]::UTF8.GetString($p[$script:prefix+'BUILD.json'])|ConvertFrom-Json;$b.releaseEligible=$true;$b.runtimeAcceptance='passed';$p[$script:prefix+'BUILD.json']=[Text.Encoding]::UTF8.GetBytes(($b|ConvertTo-Json -Depth 20 -Compress)+"`n")}
    Reject {& $archiveValidator -ArchivePath $a -BuildReceiptPath $BuildReceiptPath -CecilPath $CecilPath} 'differs from verified same-build source payload: BUILD.json'
}
Test 'rehashed ZIP cannot mix in a valid second-build Core' {
    $script:otherCoreBytes=[IO.File]::ReadAllBytes($other.Paths.core)
    $a=MutateArchive 'archive-mixed-core' {param($p) $p[$script:prefix+'Azurite.Core.dll']=$script:otherCoreBytes}
    Reject {& $archiveValidator -ArchivePath $a -BuildReceiptPath $BuildReceiptPath -CecilPath $CecilPath} 'differs from verified same-build source payload: Azurite.Core.dll'
}
Test 'ZIP unexpected files and host DLLs are rejected' {
    $a=MutateArchive 'unexpected-host' {param($p) $p[$script:prefix+'Assembly-CSharp.dll']=[byte[]](1,2,3)}
    Reject {& $archiveValidator -ArchivePath $a -BuildReceiptPath $BuildReceiptPath -CecilPath $CecilPath} 'entry count|Unapproved'
}
Test 'ZIP path traversal is rejected even with rebuilt checksums' {
    $a=MutateArchive 'traversal-zip' {param($p) $bytes=$p[$script:prefix+'README.md'];$p.Remove($script:prefix+'README.md');$p[$script:prefix+'../README.md']=$bytes}
    Reject {& $archiveValidator -ArchivePath $a -BuildReceiptPath $BuildReceiptPath -CecilPath $CecilPath} 'Unsafe or outside-root'
}
Test 'ZIP casing ambiguity is rejected' {
    $a=MutateArchive 'case-conflict' {param($p) $p.Remove($script:prefix+'README.md');$p[$script:prefix+'AZURITE.DLL']=$p[$script:prefix+'Azurite.dll']}
    Reject {& $archiveValidator -ArchivePath $a -BuildReceiptPath $BuildReceiptPath -CecilPath $CecilPath} 'entry count|Duplicate|Unapproved'
}
Test 'ZIP checksum coverage cannot omit a file' {
    $a=MutateArchive 'missing-checksum' {param($p) $text=[Text.Encoding]::UTF8.GetString($p[$script:prefix+'SHA256SUMS.txt']);$text=($text -split "`n"|Where-Object{$_ -notmatch '  Azurite.Core.dll$'}) -join "`n";$p[$script:prefix+'SHA256SUMS.txt']=[Text.Encoding]::UTF8.GetBytes($text)} $false
    Reject {& $archiveValidator -ArchivePath $a -BuildReceiptPath $BuildReceiptPath -CecilPath $CecilPath} 'Checksum coverage'
}
Test 'bad archive sidecar is rejected' {
    $a=Join-Path $e 'bad-sidecar.zip';Copy-Item -LiteralPath $script:goodArchive -Destination $a;[IO.File]::WriteAllText(($a+'.sha256'),('0'*64)+'  bad-sidecar.zip')
    Reject {& $archiveValidator -ArchivePath $a -BuildReceiptPath $BuildReceiptPath -CecilPath $CecilPath -RequireSidecar} 'sidecar mismatch'
}
Test 'release qualification is rejected before creating any archive' {
    $out=Join-Path $e 'not-a-release'
    Reject {& $stage -BuildReceiptPath $BuildReceiptPath -OutputDirectory $out -CecilPath $CecilPath -RequireReleaseQualified} 'not release qualified'
    Check (!(Test-Path -LiteralPath $out)) 'Rejected release request created output.'
    Reject {& $archiveValidator -ArchivePath $script:goodArchive -BuildReceiptPath $BuildReceiptPath -CecilPath $CecilPath -RequireReleaseQualified} 'not release qualified'
}
Test 'existing Original validator still refuses the Source pair' {Reject {& (Join-Path $root 'tools/Test-CandidateIdentity.ps1') -PluginPath $verified.Paths.plugin -CorePath $verified.Paths.core -ManifestPath $verified.Paths.manifest -CecilPath $CecilPath} 'differs from the validated original Core'}
Test 'Original reference and clean source remain intact after negative controls' {Check ((Get-StagingHash (Join-Path $root 'references/Azurite.Core.dll')) -ceq $originalHash) 'Original reference changed.';$null=Test-StagingReceipt $BuildReceiptPath $root $CecilPath}
$failed=@($results|Where-Object{!$_.passed}).Count
Write-StagingJson (Join-Path $e 'result.json') ([ordered]@{passed=$results.Count-$failed;failed=$failed;tests=$results;nativeExecuted=$false;releaseEligible=$false;buildReceiptSha256=(Get-StagingHash $BuildReceiptPath);otherBuildReceiptSha256=(Get-StagingHash $OtherBuildReceiptPath)})
"RESULT: $($results.Count-$failed) passed; $failed failed."
if($failed){throw "$failed SourceStaging pipeline checks failed; evidence retained."}
