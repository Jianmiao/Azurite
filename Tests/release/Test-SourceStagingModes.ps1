#requires -Version 7.2
[CmdletBinding()]
param([Parameter(Mandatory)][string]$EvidenceDirectory)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
$root=Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$project=Join-Path $root 'Plugin/Azurite.csproj'
if(Test-Path -LiteralPath $EvidenceDirectory){throw 'Evidence directory already exists.'}
[void][IO.Directory]::CreateDirectory($EvidenceDirectory)
$results=[Collections.Generic.List[object]]::new()
function Check([bool]$Ok,[string]$Why){if(!$Ok){throw $Why}}
function Test([string]$Name,[scriptblock]$Body){try{& $Body;$results.Add(@{name=$Name;passed=$true});"PASS $Name"}catch{$results.Add(@{name=$Name;passed=$false;message=$_.Exception.Message});"FAIL ${Name}: $($_.Exception.Message)"}}
function InvokeMode([string]$Mode,[string]$Target='ValidateAzuriteBuildMode',[string[]]$Extra=@()){
    $a=@('msbuild',$project,'-nologo',"-p:AzuriteCoreMode=$Mode",'-p:UseArtifactsOutput=true',"-p:ArtifactsPath=$EvidenceDirectory/build",'-p:SourceStagingBuildId=11111111111111111111111111111111',"-t:$Target")+$Extra
    $o=& dotnet @a 2>&1; @{code=$LASTEXITCODE;text=($o -join "`n")}
}
Test 'SourceStaging admits an explicitly isolated same-build source pair' {
    $r=InvokeMode 'SourceStaging';Check ($r.code -eq 0) $r.text
    $o=& dotnet msbuild $project -nologo '-p:AzuriteCoreMode=SourceStaging' '-p:UseArtifactsOutput=true' "-p:ArtifactsPath=$EvidenceDirectory/build" '-p:SourceStagingBuildId=11111111111111111111111111111111' '-getProperty:AssemblyName,DefineConstants' '-getItem:Reference,ProjectReference,Compile' 2>&1
    Check ($LASTEXITCODE -eq 0) ($o -join "`n");$e=$o -join "`n"|ConvertFrom-Json
    Check ($e.Properties.AssemblyName -ceq 'Azurite') 'Canonical identity must be emitted by compiler.'
    Check (@($e.Items.ProjectReference).Count -eq 1) 'Source pair must use ProjectReference.'
    Check (@($e.Items.Reference|Where-Object Identity -ceq 'Azurite.Core').Count -eq 0) 'Preserved Core must not become a build dependency.'
    Check (@($e.Items.Compile|Where-Object Identity -match 'AuthoringCardPackPanel.cs$').Count -eq 1) 'Authoring UI must be compiled.'
    Check ('AZURITE_AUTHORING_SOURCE' -in ($e.Properties.DefineConstants -split ';')) 'Source registration must be enabled.'
}
Test 'SourceStaging rejects skipping its Core project' {$r=InvokeMode 'SourceStaging' 'ValidateAzuriteBuildMode' @('-p:BuildProjectReferences=false');Check ($r.code -ne 0) 'Skipped Core was allowed.'}
Test 'SourceStaging rejects missing run identity' {$r=InvokeMode 'SourceStaging' 'ValidateAzuriteBuildMode' @('-p:SourceStagingBuildId=');Check ($r.code -ne 0) 'Missing build identity was allowed.'}
Test 'SourceStaging rejects shared outputs' {$r=InvokeMode 'SourceStaging' 'ValidateAzuriteBuildMode' @('-p:UseArtifactsOutput=false');Check ($r.code -ne 0) 'Shared outputs were allowed.'}
Test 'AuthoringSource still rejects standard packaging' {$r=InvokeMode 'AuthoringSource' 'RejectAuthoringPackaging';Check ($r.code -ne 0 -and $r.text.Contains('not a release package')) 'Experimental release guard changed.'}
Test 'SourceStaging also rejects standard packaging' {$r=InvokeMode 'SourceStaging' 'RejectSourceStagingPackaging';Check ($r.code -ne 0 -and $r.text.Contains('not release qualified')) 'Standard packaging bypassed staging qualification.'}
Test 'Original mode retains its preflight' {$r=InvokeMode 'Original';Check ($r.code -eq 0) $r.text}
Test 'Unknown mode is rejected' {$r=InvokeMode 'SourcStaging';Check ($r.code -ne 0) 'Unknown mode was accepted.'}
$failed=@($results|Where-Object {!$_.passed}).Count
[ordered]@{passed=$results.Count-$failed;failed=$failed;tests=$results;nativeExecuted=$false;releaseEligible=$false}|ConvertTo-Json -Depth 6|Set-Content -LiteralPath (Join-Path $EvidenceDirectory 'result.json') -Encoding utf8
if($failed){throw "$failed source staging mode checks failed."}
