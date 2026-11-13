#requires -Version 7.2
[CmdletBinding()]
param([Parameter(Mandatory)][string]$AAInstallPath,[Parameter(Mandatory)][string]$AuthoringReceipt,
    [Parameter(Mandatory)][string]$StablePluginPath,[Parameter(Mandatory)][string]$EvidenceDirectory)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
$root=Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$evidence=[IO.Path]::GetFullPath($EvidenceDirectory)
if(Test-Path -LiteralPath $evidence){throw 'Select a new evidence directory; previous evidence is never overwritten.'}
[void][IO.Directory]::CreateDirectory($evidence)
$receipt=Get-Content -LiteralPath $AuthoringReceipt -Raw|ConvertFrom-Json
$project=Join-Path $root 'Plugin/Azurite.csproj'
$identity=Join-Path $root 'tools/Test-CandidateIdentity.ps1'
$manifest=Join-Path $root 'assets/manifest.json'
$original=Join-Path $root 'references/Azurite.Core.dll'
$artifactPath=Join-Path (Split-Path -Parent ([IO.Path]::GetFullPath($AuthoringReceipt))) 'build'
$passed=0; $failed=0
function Check([bool]$Condition,[string]$Message){if(!$Condition){throw $Message}}
function Test([string]$Name,[scriptblock]$Body){try{& $Body;$script:passed++;Write-Output "PASS $Name"}catch{$script:failed++;Write-Output "FAIL ${Name}: $($_.Exception.Message)"}}
function Hash([string]$Path){(Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()}
function Reject([scriptblock]$Body,[string]$Pattern){$rejected=$false;try{& $Body|Out-Null}catch{$rejected=$_.Exception.Message -match $Pattern};Check $rejected "Expected rejection: $Pattern"}
Test 'source-built Plugin/Core output hashes match the identified same-build pair' {
    Check ($receipt.coreMode -ceq 'AuthoringSource' -and !$receipt.releaseEligible -and !$receipt.launchesAA) 'Build receipt claims wrong mode or readiness.'
    Check ((Hash $receipt.pluginPath) -ceq $receipt.pluginSha256 -and (Hash $receipt.corePath) -ceq $receipt.coreSha256 -and (Hash $receipt.coreBuildPath) -ceq $receipt.coreSha256) 'Built pair hashes differ.'
    Check ((Hash $original) -ceq $receipt.originalCoreSha256) 'Original Core changed.'
}
Test 'stable default output still passes original-Core candidate identity checks' {
    $stableCore=Join-Path (Split-Path -Parent $StablePluginPath) 'Azurite.Core.dll'
    $result=& $identity -PluginPath $StablePluginPath -CorePath $stableCore -ManifestPath $manifest -AAInstallPath $AAInstallPath
    Check ($result.Result -ceq 'PASS' -and $result.CoreMode -ceq 'original' -and (Hash $stableCore) -ceq (Hash $original)) 'Default stable output changed its Core contract.'
    $result|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $evidence 'stable-identity.json')
}
Test 'old packaging identity gate rejects authoring Plugin paired with original Core' {
    Reject { & $identity -PluginPath $receipt.pluginPath -CorePath $original -ManifestPath $manifest -AAInstallPath $AAInstallPath } 'wrong assembly name'
}
Test 'old packaging identity gate rejects the rebuilt Core pair' {
    Reject { & $identity -PluginPath $receipt.pluginPath -CorePath $receipt.corePath -ManifestPath $manifest -AAInstallPath $AAInstallPath } 'differs from the validated original Core'
}
foreach($verb in @('pack','publish')){
    Test "authoring $verb rejects before emitting a package or publish payload" {
        $destination=Join-Path $evidence $verb
        # Exercise the normal build-enabled command. MSBuild may first inspect
        # project outputs with BuildProjectReferences=false; either explicit
        # authoring guard may reject, but no package/publish files may escape.
        $arguments=@($verb,$project,'--no-restore','--nologo','--artifacts-path',$artifactPath,'-o',$destination,
            "-p:AAInstallPath=$AAInstallPath",'-p:AzuriteCoreMode=AuthoringSource','-p:Configuration=Release')
        $output=& dotnet @arguments 2>&1
        $code=$LASTEXITCODE; $log=$output -join "`n"
        $log|Set-Content -LiteralPath (Join-Path $evidence "$verb.log")
        $files=@(if(Test-Path -LiteralPath $destination){Get-ChildItem -LiteralPath $destination -File -Recurse})
        Check ($code -ne 0 -and ($log.Contains('not a release package') -or $log.Contains('must build its source Core'))) "$verb did not reach an explicit authoring guard."
        Check ($files.Count -eq 0) "$verb emitted files before rejecting the authoring build."
    }
}
Test 'authoring build entry refuses to overwrite earlier output and escape artifacts root' {
    $buildScript=Join-Path $root 'tools/Build-AuthoringSource.ps1'
    Reject { & $buildScript -AAInstallPath $AAInstallPath -OutputDirectory (Split-Path -Parent $AuthoringReceipt) } 'already exists'
    Reject { & $buildScript -AAInstallPath $AAInstallPath -OutputDirectory (Join-Path $root 'Plugin/bin/unsafe-authoring-output') } 'under this repository artifacts/'
}
[ordered]@{passed=$passed;failed=$failed;launchesAA=$false;runtimeAcceptance='not-performed'}|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $evidence 'result.json')
Write-Output "RESULT: $passed passed; $failed failed."
if($failed){throw "$failed output-boundary checks failed; evidence retained at $evidence"}
