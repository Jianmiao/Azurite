#requires -Version 7.2
[CmdletBinding()]
param([Parameter(Mandatory)][string]$SourceRoot,[Parameter(Mandatory)][string]$BuildReceiptPath,[Parameter(Mandatory)][string]$EvidenceDirectory)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
Import-Module (Join-Path $SourceRoot 'tools/SourceStaging.psm1') -Force
$r=Get-Content -LiteralPath $BuildReceiptPath -Raw|ConvertFrom-Json
$cecil=Join-Path $r.hostReferenceRoot 'BepInEx/core/Mono.Cecil.dll'
Import-StagingCecil $cecil
Assert-Staging (!(Test-Path -LiteralPath $EvidenceDirectory)) 'Evidence already exists.'
[void][IO.Directory]::CreateDirectory($EvidenceDirectory)
$results=[Collections.Generic.List[object]]::new()
$baselineOutput=Split-Path $BuildReceiptPath -Parent
function Case([string]$Name,[scriptblock]$Change,[bool]$WantAccept=$false){
    $dir=Join-Path $EvidenceDirectory $Name;[void][IO.Directory]::CreateDirectory($dir)
    $record=Get-Content -LiteralPath $BuildReceiptPath -Raw|ConvertFrom-Json
    foreach($f in $record.files.PSObject.Properties){$to=Join-Path $dir $f.Value.path;[void][IO.Directory]::CreateDirectory((Split-Path $to -Parent));Copy-Item -LiteralPath (Join-Path $baselineOutput $f.Value.path) -Destination $to}
    & $Change $record $dir
    $receipt=Join-Path $dir 'source-staging-build.json';Write-StagingJson $receipt $record
    $out=Join-Path $SourceRoot ('artifacts/review-fix1-'+(Split-Path $EvidenceDirectory -Leaf)+'-'+$Name)
    $accepted=$false;$message='';$archive=$null
    try{$archive=& (Join-Path $SourceRoot 'tools/Stage-SourceCandidate.ps1') -BuildReceiptPath $receipt -OutputDirectory $out -CecilPath $cecil;$accepted=$true}catch{$message=$_.Exception.Message}
    $pattern=switch -Regex ($Name){
        '^broken-core' {'Unresolved source Core member';break}
        '^protocol-' {'protocol.*guard|mismatch must throw|Alternate branch';break}
        '^host-' {'Host reference inventory|Duplicate host reference';break}
        '^sdk-' {'SDK identity|SDK receipt identity|Recorded SDK|compiler SDK|producer tool';break}
        '^dependency-' {'Dependency';break}
        default {''}
    }
    $passed=($accepted -eq $WantAccept) -and ($WantAccept -or !$pattern -or $message -match $pattern)
    $results.Add([ordered]@{name=$Name;accepted=$accepted;expectedAccepted=$WantAccept;passed=$passed;message=$message;archive=$archive;receipt=$receipt})
    Write-StagingJson (Join-Path $dir 'case-result.json') $results[$results.Count-1]
    "$(if($passed){'PASS'}else{'FAIL'}) ${Name}: accepted=$accepted $message"
}
Case 'unmodified-production-baseline' {param($record,$dir)} $true
Case 'broken-core-resolver-good-sibling' {
    param($record,$dir)
    $p=Join-Path $dir $record.files.core.path;$broken=Join-Path (Split-Path $p -Parent) 'BrokenCore.dll'
    $a=[Mono.Cecil.AssemblyDefinition]::ReadAssembly($p)
    try{$type=@($a.MainModule.Types|Where-Object FullName -ceq 'Azurite.Core.CardPackV2Workflow')[0];$ctor=@($type.Methods|Where-Object Name -ceq '.ctor')[0];$ctor.Name='RemovedConstructor';$a.Write($broken)}finally{$a.Dispose()}
    foreach($role in @('core','coreBuild')){$new=$record.files.$role.path.Replace('/Azurite.Core.dll','/BrokenCore.dll');$to=Join-Path $dir $new;if($to -cne $broken){Copy-Item -LiteralPath $broken -Destination $to};$record.files.$role.path=$new;$record.files.$role.sha256=Get-StagingHash $to}
}
function AlterProtocol($Record,[string]$Directory,[string]$Change){
    $p=Join-Path $Directory $Record.files.plugin.path;$a=[Mono.Cecil.AssemblyDefinition]::ReadAssembly($p)
    try{
        $type=@($a.MainModule.Types|Where-Object FullName -ceq 'Azurite.RenderControlClient')[0];$ctor=@($type.Methods|Where-Object Name -ceq '.ctor')[0];$il=$ctor.Body.Instructions
        $i=@(for($j=0;$j -lt $il.Count;$j++){if([string]$il[$j].Operand -ceq 'ProtocolVersion'){$j}})[0]
        if($Change -ceq 'pop'){$il[$i+7].OpCode=[Mono.Cecil.Cil.OpCodes]::Pop}
        elseif($Change -ceq 'target'){$il[$i+4].Operand=$il[$i+5]}
        elseif($Change -ceq 'skip'){$il[$i-1].OpCode=[Mono.Cecil.Cil.OpCodes]::Br;$il[$i-1].Operand=$il[$i+8]}
        $a.Write($p+'.changed')
    }finally{$a.Dispose()}
    Copy-Item -LiteralPath ($p+'.changed') -Destination $p -Force;$Record.files.plugin.sha256=Get-StagingHash $p
}
Case 'protocol-reject-throw-to-pop' {param($record,$dir) AlterProtocol $record $dir 'pop'}
Case 'protocol-success-branch-target-changed' {param($record,$dir) AlterProtocol $record $dir 'target'}
Case 'protocol-check-unconditionally-skipped' {param($record,$dir) AlterProtocol $record $dir 'skip'}
Case 'host-reference-inventory-cecil-only' {param($record,$dir) $record.hostReferences=@($record.hostReferences|Where-Object relative -ceq 'BepInEx/core/Mono.Cecil.dll')}
Case 'host-reference-inventory-one-removed' {param($record,$dir) $record.hostReferences=@($record.hostReferences|Select-Object -Skip 1)}
Case 'sdk-erased' {param($record,$dir) $record.sdk=''}
Case 'sdk-malformed' {param($record,$dir) $record.sdk='not-an-sdk'}
Case 'sdk-valid-but-contradictory' {param($record,$dir) $record.sdk='99.0.999'}
if('buildEnvironment' -in $r.PSObject.Properties.Name){
    Case 'sdk-environment-version-contradiction' {param($record,$dir) $record.buildEnvironment.sdkVersion='99.0.999'}
    Case 'sdk-controlled-compiler-hash-mismatch' {param($record,$dir) $record.buildEnvironment.compiler.sha256='0'*64}
    Case 'sdk-receipt-cannot-select-a-launcher' {param($record,$dir) $record.buildEnvironment.dotnet.path=(Join-Path $dir 'untrusted-launcher.exe')}
    Case 'host-reference-identity-alias' {param($record,$dir) $record.hostReferences[0].identity='Unrelated.Assembly'}
}
function ChangeDeps($Record,[string]$Directory,[scriptblock]$Change){$p=Join-Path $Directory $Record.files.deps.path;$d=Get-Content -LiteralPath $p -Raw|ConvertFrom-Json;& $Change $d;Write-StagingJson $p $d;$Record.files.deps.sha256=Get-StagingHash $p}
Case 'dependency-core-version-mismatch' {param($record,$dir) $p=Join-Path $dir $record.files.deps.path;[IO.File]::WriteAllText($p,[IO.File]::ReadAllText($p).Replace('Azurite.Core/1.0.40','Azurite.Core/9.9.99'),[Text.UTF8Encoding]::new($false));$record.files.deps.sha256=Get-StagingHash $p}
Case 'dependency-runtime-target-mismatch' {param($record,$dir) ChangeDeps $record $dir {param($d) $d.runtimeTarget.name='.NETCoreApp,Version=v99.0'}}
Case 'dependency-core-edge-mismatch' {param($record,$dir) ChangeDeps $record $dir {param($d) $d.targets.'.NETCoreApp,Version=v6.0'.'Azurite/1.0.40'.dependencies.'Azurite.Core'='9.9.99'}}
Case 'dependency-core-runtime-version-mismatch' {param($record,$dir) ChangeDeps $record $dir {param($d) $d.targets.'.NETCoreApp,Version=v6.0'.'Azurite.Core/1.0.40'.runtime.'Azurite.Core.dll'.assemblyVersion='9.9.99.0'}}
Case 'dependency-unexpected-project' {param($record,$dir) ChangeDeps $record $dir {param($d) $d.libraries|Add-Member -NotePropertyName 'Extra/1.0.0' -NotePropertyValue ([pscustomobject]@{type='project';serviceable=$false;sha512=''})}}

# Pure managed behavior control uses only the actual consumer's reflection API.
# No AA, Unity, actual AAVE provider, process-control or native entry is invoked.
$env:TEMP=Join-Path $EvidenceDirectory 'managed-temp';$env:TMP=$env:TEMP;[void][IO.Directory]::CreateDirectory($env:TEMP)
Add-Type -TypeDefinition @'
namespace AAVideoExport.Integration {
    public static class RenderControlV1 {
        public static int Major = 1;
        public static int ProtocolVersion { get { return Major; } }
        public static bool IsReady { get { return true; } }
        public static bool IsRenderingOwned { get { return false; } }
        public static event System.Action BeforeAcquire { add {} remove {} }
        public static event System.Action AfterRelease { add {} remove {} }
    }
}
'@
$provider=[AAVideoExport.Integration.RenderControlV1].Assembly;$managed=[Collections.Generic.List[object]]::new()
foreach($entry in @(@{name='original';path=(Join-Path $baselineOutput $r.files.plugin.path)},@{name='throw-to-pop';path=(Join-Path (Join-Path $EvidenceDirectory 'protocol-reject-throw-to-pop') $r.files.plugin.path)})){
    $assembly=[Reflection.Assembly]::Load([IO.File]::ReadAllBytes($entry.path));$type=$assembly.GetType('Azurite.RenderControlClient',$true);$method=$type.GetMethod('TryBind',[Reflection.BindingFlags]'Public,Static')
    foreach($major in @(1,0,2)){
        [AAVideoExport.Integration.RenderControlV1]::Major=$major;$arguments=[object[]]@($provider,[Action]{},$null,$null,$null,'');$binding=$method.Invoke($null,$arguments)
        if($null -ne $arguments[4]){([IDisposable]$arguments[4]).Dispose()}
        $expected=if($entry.name -ceq 'throw-to-pop' -or $major -eq 1){'Bound'}else{'Invalid'}
        $managed.Add([ordered]@{name=$entry.name;major=$major;binding=$binding.ToString();expected=$expected;passed=$binding.ToString() -ceq $expected;reason=$arguments[5]})
    }
}
$failed=@($results|Where-Object {!$_.passed}).Count+@($managed|Where-Object {!$_.passed}).Count
Write-StagingJson (Join-Path $EvidenceDirectory 'results.json') ([ordered]@{cases=$results;managedBehavior=$managed;passed=$results.Count+$managed.Count-$failed;failed=$failed;nativeExecuted=$false;releaseEligible=$false})
"RESULT: $($results.Count+$managed.Count-$failed) passed; $failed failed."
if($failed){throw "$failed review regressions failed; prior evidence preserved."}
