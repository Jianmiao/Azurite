#requires -Version 7.0
# Pure contract/receipt/config-file checks. Never stages or launches the lab.
$ErrorActionPreference='Stop';Set-StrictMode -Version Latest
Import-Module (Join-Path $PSScriptRoot 'EditorRouteCore.psm1') -DisableNameChecking
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'));$task=Join-Path $repo '.superpowers\sdd\azurite-authoring-20261108'
$base=Read-AzuriteStartupContract;$baselineBytes=$base|ConvertTo-Json -Depth 30 -Compress
$inputs=Read-AzuriteBoundedJson (Join-Path $PSScriptRoot 'EditorRouteReviewedInputs.candidate.json') 65536
$inputs.admission='reviewed-cli-route-selftest-only' # In-memory pure fixture, never written as approval.
$config=[pscustomobject]@{baselineSha256='F2E0F930EFDA6A7FA292E872760A27CDC9BB1D232CD82D719446622C4DC6EF06';expectedSha256='A0DA59BFA4590A1C3A2B75FF8FF8EE078EF192783606994FF2D1772C358CDBE6';receipt=[pscustomobject]@{path=(Join-Path $repo 'artifacts\early-abort-config\early-abort-config-receipt.json');sha256='45C110175D0E260769A868FE015B254C65092F01A335FA359F54077EBE328337'};report=[pscustomobject]@{path=(Join-Path $task 'early-abort-config-audit.md');sha256='3B37F82EF3BF866F6A190BC46AD7B0CA38071259336366310AF3D5076A6FFC11'};derived=[pscustomobject]@{path=(Join-Path $repo 'artifacts\early-abort-config\BepInEx.early-abort.expected.cfg');sha256='A0DA59BFA4590A1C3A2B75FF8FF8EE078EF192783606994FF2D1772C358CDBE6'}}
$inputs|Add-Member -NotePropertyName invalidManifestConfig -NotePropertyValue $config -Force
$passed=0;$failed=0
function Check([bool]$Value,[string]$Message){if(-not$Value){throw $Message}}
function Deny([scriptblock]$Action){$denied=$false;try{&$Action|Out-Null}catch{$denied=$true};Check $denied 'unapproved config pin or proof accepted'}
function Test([string]$Name,[scriptblock]$Action){try{&$Action;$script:passed++;Write-Output ('PASS '+$Name)}catch{$script:failed++;Write-Output ('FAIL '+$Name+': '+$_.Exception.Message)}}
function ConfigPin($Contract){$pins=@($Contract.files|Where-Object path -CEQ 'BepInEx/config/BepInEx.cfg');Check ($pins.Count -eq 1) 'config pin omitted or duplicated';return $pins[0].sha256}
Test 'only InvalidManifest selects the exact independently derived config digest' {
 $p=New-AzuriteEditorRouteContract $inputs $base Positive;$m=New-AzuriteEditorRouteContract $inputs $base ProbeMissing;$i=New-AzuriteEditorRouteContract $inputs $base InvalidManifest
 Check ((ConfigPin $i) -ceq $config.expectedSha256) 'InvalidManifest did not use derived exact pin'
 Check ((ConfigPin $p) -ceq $config.baselineSha256 -and (ConfigPin $m) -ceq $config.baselineSha256) 'ordinary scope adopted early config'
}
Test '451 base pins change at exactly one position only for InvalidManifest' {
 Check ($base.files.Count -eq 451) 'frozen base pin inventory changed'
 foreach($scope in @('Positive','ProbeMissing','InvalidManifest')){
  $c=New-AzuriteEditorRouteContract $inputs $base $scope;$changes=@()
  for($n=0;$n -lt 451;$n++){Check ($c.files[$n].path -ceq $base.files[$n].path) 'base pin path/order changed';if($c.files[$n].sha256 -cne $base.files[$n].sha256){$changes+=@($c.files[$n].path)}}
  $expectedCount=if($scope -ceq 'ProbeMissing'){453}else{454};Check ($c.files.Count -eq $expectedCount) 'host/harness pin count changed'
  if($scope -ceq 'InvalidManifest'){Check ($changes.Count -eq 1 -and $changes[0] -ceq 'BepInEx/config/BepInEx.cfg') 'config scope changed more or fewer than one base pin'}else{Check ($changes.Count -eq 0) 'ordinary base pins changed'}
 }
 Check (($base|ConvertTo-Json -Depth 30 -Compress) -ceq $baselineBytes) 'builder mutated the pristine base contract'
}
Test 'InvalidManifest binds all three frozen derivation artifacts' {$c=New-AzuriteEditorRouteContract $inputs $base InvalidManifest;foreach($name in @('receipt','report','derived')){$p=@($c.evidence|Where-Object {$_.path -ceq $config.$name.path -and $_.sha256 -ceq $config.$name.sha256});Check ($p.Count -eq 1) 'derived config evidence not bound exactly once'}}
Test 'different baseline derived digest or artifact digest cannot be tolerated' {
 foreach($field in @('baselineSha256','expectedSha256')){$bad=$inputs|ConvertTo-Json -Depth 20|ConvertFrom-Json;$bad.invalidManifestConfig.$field='0'*64;Deny {New-AzuriteEditorRouteContract $bad $base InvalidManifest}}
 $bad=$inputs|ConvertTo-Json -Depth 20|ConvertFrom-Json;$bad.invalidManifestConfig.derived.sha256=$config.baselineSha256;Deny {New-AzuriteEditorRouteContract $bad $base InvalidManifest}
}
Test 'independent receipt report and derived bytes validate against fixed paths' {Assert-AzuriteEditorRouteConfigDerivation $config}
Test 'wrong receipt identity and alternative artifact paths are rejected' {
 $bad=$config|ConvertTo-Json -Depth 10|ConvertFrom-Json;$bad.receipt.sha256='0'*64;Deny {Assert-AzuriteEditorRouteConfigDerivation $bad}
 $bad=$config|ConvertTo-Json -Depth 10|ConvertFrom-Json;$bad.derived.path='E:\aamod\labs\Azurite-20261108-editor\install\BepInEx\config\BepInEx.cfg';Deny {Assert-AzuriteEditorRouteConfigDerivation $bad}
}
Test 'wrong-scope config bytes fail exact hash checks despite equal values' {
 $pristine='E:\aamod\labs\Azurite-20261108-stable\install\BepInEx\config\BepInEx.cfg'
 $p=New-AzuriteEditorRouteContract $inputs $base Positive;$i=New-AzuriteEditorRouteContract $inputs $base InvalidManifest
 Assert-AzuritePreparedHash $pristine (ConfigPin $p);Assert-AzuritePreparedHash $config.derived.path (ConfigPin $i)
 Deny {Assert-AzuritePreparedHash $config.derived.path (ConfigPin $p)};Deny {Assert-AzuritePreparedHash $pristine (ConfigPin $i)}
}
function Triples([string]$Path){
 $section='';$seen=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
 foreach($raw in [IO.File]::ReadAllLines($Path)){$line=$raw.Trim();if(-not$line -or $line.StartsWith('#') -or $line.StartsWith(';')){continue};if($line -match '^\[([^\]]+)\]$'){$section=$matches[1];continue};if($line -notmatch '^([^=]+)=(.*)$' -or -not$section){throw 'Unsupported config syntax'};$key=$section+'.'+$matches[1].Trim();if(-not$seen.Add($key)){throw 'Duplicate config key'};[pscustomobject]@{key=$key;value=$matches[2].Trim()}}
}
Test 'all 27 ordered setting triples are identical independently of byte pins' {
 $left=@(Triples 'E:\aamod\labs\Azurite-20261108-stable\install\BepInEx\config\BepInEx.cfg');$right=@(Triples $config.derived.path)
 Check ($left.Count -eq 27 -and $right.Count -eq 27) 'settings count changed'
 Check (($left|ConvertTo-Json -Compress) -ceq ($right|ConvertTo-Json -Compress)) 'a setting value/order changed'
}
Write-Output ('RESULT passed='+$passed+' failed='+$failed+'; pure/file fixtures only; no AA or lab write')
if($failed){exit 1}
