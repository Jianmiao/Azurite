#requires -Version 7.2
[CmdletBinding()]
param([Parameter(Mandatory)][string]$AAInstallPath, [Parameter(Mandatory)][string]$EvidenceDirectory)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
$root=Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$project=Join-Path $root 'Plugin/Azurite.csproj'
$evidence=[IO.Path]::GetFullPath($EvidenceDirectory)
if(Test-Path -LiteralPath $evidence){throw 'Select a new evidence directory; previous evidence is never overwritten.'}
[void][IO.Directory]::CreateDirectory($evidence)
$passed=0; $failed=0
function Check([bool]$Condition,[string]$Message){if(!$Condition){throw $Message}}
function Test([string]$Name,[scriptblock]$Body){try{& $Body; $script:passed++; Write-Output "PASS $Name"}catch{$script:failed++; Write-Output "FAIL ${Name}: $($_.Exception.Message)"}}
function Evaluate([string]$Mode){
    $arguments=@('msbuild',$project,'-nologo',"-p:AAInstallPath=$AAInstallPath",'-p:Configuration=Release',
        '-p:UseArtifactsOutput=true',"-p:ArtifactsPath=$evidence/artifacts",
        '-getProperty:AssemblyName,AzuriteCoreMode,TargetPath,DefineConstants','-getItem:Reference,ProjectReference,Compile')
    if($Mode){$arguments+="-p:AzuriteCoreMode=$Mode"}
    $output=& dotnet @arguments 2>&1
    if($LASTEXITCODE -ne 0){throw ($output -join "`n")}
    $output -join "`n" | ConvertFrom-Json
}
function Validate([string]$Mode,[bool]$Artifacts,[string]$Target='ValidateAzuriteBuildMode',[string[]]$Extra=@()){
    $arguments=@('msbuild',$project,'-nologo',"-p:AAInstallPath=$AAInstallPath","-p:AzuriteCoreMode=$Mode","-target:$Target")
    if($Artifacts){$arguments+=@('-p:UseArtifactsOutput=true',"-p:ArtifactsPath=$evidence/artifacts")}
    $arguments+=$Extra
    $output=& dotnet @arguments 2>&1
    @{Code=$LASTEXITCODE;Text=($output -join "`n")}
}
Test 'default keeps original Core and excludes authoring-only Panel' {
    $result=Evaluate ''; $result|ConvertTo-Json -Depth 10|Set-Content -LiteralPath (Join-Path $evidence 'default-evaluation.json')
    Check ($result.Properties.AssemblyName -ceq 'Azurite' -and $result.Properties.AzuriteCoreMode -ceq 'Original') 'Default identity/mode changed.'
    Check (@($result.Items.ProjectReference).Count -eq 0) 'Default unexpectedly rebuilds Core.'
    $core=@($result.Items.Reference|Where-Object Identity -eq 'Azurite.Core')
    Check ($core.Count -eq 1 -and $core[0].HintPath -ceq '..\references\Azurite.Core.dll') 'Default did not bind original Core.'
    Check (@($result.Items.Compile|Where-Object Identity -match 'AuthoringCardPackPanel.cs$').Count -eq 0) 'Default includes new-API Panel.'
    Check ('AZURITE_AUTHORING_SOURCE' -notin ($result.Properties.DefineConstants -split ';')) 'Default unexpectedly enables source-only Plugin registration.'
}
Test 'explicit authoring mode references real source Core with distinct identity' {
    $result=Evaluate 'AuthoringSource'; $result|ConvertTo-Json -Depth 10|Set-Content -LiteralPath (Join-Path $evidence 'authoring-evaluation.json')
    Check ($result.Properties.AssemblyName -ceq 'Azurite.AuthoringSource') 'Authoring output is indistinguishable from stable Plugin.'
    Check (@($result.Items.Reference|Where-Object Identity -eq 'Azurite.Core').Count -eq 0) 'Authoring still references old Core.'
    $core=@($result.Items.ProjectReference)
    Check ($core.Count -eq 1 -and [IO.Path]::GetFullPath($core[0].FullPath) -ceq [IO.Path]::GetFullPath((Join-Path $root 'Core/Azurite.Core.csproj'))) 'Wrong source Core project.'
    Check (@($result.Items.Compile|Where-Object Identity -match 'AuthoringCardPackPanel.cs$').Count -eq 1) 'Authoring Panel not compiled.'
    Check ('AZURITE_AUTHORING_SOURCE' -in ($result.Properties.DefineConstants -split ';')) 'Authoring mode lacks its explicit Plugin registration symbol.'
    Check ([IO.Path]::GetFullPath($result.Properties.TargetPath).StartsWith($evidence+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) 'Output escaped isolated artifacts root.'
}
Test 'unknown Core modes fail closed' { $result=Validate 'Orignal' $true; Check ($result.Code -ne 0 -and $result.Text.Contains('AzuriteCoreMode must be Original or AuthoringSource')) 'Unknown mode silently selected a dependency.' }
Test 'authoring requires isolated artifact paths' { $result=Validate 'AuthoringSource' $false; Check ($result.Code -ne 0 -and $result.Text.Contains('requires --artifacts-path')) 'Authoring mode can share stable outputs.' }
Test 'authoring cannot silently skip building its source Core' { $result=Validate 'AuthoringSource' $true 'ValidateAzuriteBuildMode' @('-p:BuildProjectReferences=false'); Check ($result.Code -ne 0 -and $result.Text.Contains('must build its source Core')) 'Authoring mode permitted a stale prebuilt project dependency.' }
Test 'authoring rejects standard publish and package targets' { $result=Validate 'AuthoringSource' $true 'RejectAuthoringPackaging'; Check ($result.Code -ne 0 -and $result.Text.Contains('not a release package')) 'Authoring source build was admitted as a release package.' }
Test 'valid original and authoring mode preflights pass' {foreach($mode in @('Original','AuthoringSource')){$result=Validate $mode $true;Check ($result.Code -eq 0) $result.Text}}
[ordered]@{passed=$passed;failed=$failed;launchesAA=$false;runtimeAcceptance='not-performed'}|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $evidence 'result.json')
Write-Output "RESULT: $passed passed; $failed failed."
if($failed){throw "$failed build-mode checks failed; evidence retained at $evidence"}
