# Focused harmless regression cases for independent review R1/R2/R3.
param([ValidateSet('Paths','Errors','All')][string]$Part='All')
$ErrorActionPreference='Stop'
$repo=Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
Import-Module (Join-Path $repo 'tools\headless\RestrictedCliCore.psm1') -Force
function Assert($Condition,[string]$Message){if(-not $Condition){throw "FAIL: $Message"}}
function Refuses([scriptblock]$Action,[string]$Message){$failed=$false;try{& $Action|Out-Null}catch{$failed=$true};Assert $failed $Message}
$root=Join-Path $repo ('artifacts\restricted-cli-fix1\'+[guid]::NewGuid().ToString('N'));[void][IO.Directory]::CreateDirectory($root)
if($Part -in @('Paths','All')){
 $target=Join-Path $root 'Target';$controller=Join-Path $root 'Controller'
 foreach($p in @($controller,$target,(Join-Path $target 'AppData\Local'),(Join-Path $target 'AppData\LocalLow'),(Join-Path $target 'AppData\Roaming'),(Join-Path $target 'AppData\Local\Temp'))){[void][IO.Directory]::CreateDirectory($p)}
 $identity=[pscustomobject]@{identitySid='S-1-5-21-1-2-3-1002';isAdministrator=$false;hiveLoaded=$true;unexpectedPrivileges=@();userProfile=$target;localAppData=(Join-Path $target 'AppData\Local');localLow=(Join-Path $target 'AppData\LocalLow');environmentProfile=$target;environmentLocalAppData=(Join-Path $target 'AppData\Local');environmentAppData=(Join-Path $target 'AppData\Roaming');environmentTemp=(Join-Path $target 'AppData\Local\Temp');environmentTmp=(Join-Path $target 'AppData\Local\Temp')}
 $baseline=$identity|ConvertTo-Json -Depth 4 -Compress
 $proof=Assert-AzuriteTargetIdentity $identity $identity.identitySid 'S-1-5-21-1-2-3-1001' $controller
 foreach($field in @('localLow','environmentAppData','environmentTemp','environmentTmp')){
  $old=$identity.$field;$identity.$field=$target+'\..\Controller\AppData\Local\Temp'
  Refuses {Assert-AzuriteTargetIdentity $identity $identity.identitySid 'S-1-5-21-1-2-3-1001' $controller} ('R2 traversal accepted in '+$field)
  $identity.$field=$old
 }
 $alias=Join-Path $target 'Alias';New-Item -ItemType Junction -Path $alias -Target $controller|Out-Null
 try{$identity.environmentTemp=Join-Path $alias 'Temp';Refuses {Assert-AzuriteTargetIdentity $identity $identity.identitySid 'S-1-5-21-1-2-3-1001' $controller} 'R2 reparse ancestor accepted'}finally{$identity.environmentTemp=Join-Path $target 'AppData\Local\Temp';Remove-Item -LiteralPath $alias -Force}
 Assert (($identity|ConvertTo-Json -Depth 4 -Compress) -ceq $baseline) 'R2 validator rewrote inherited path values'
 Assert ($proof.filesystemVerified -eq $true) 'R2 target filesystem proof missing'
 Assert-AzuriteTargetIdentityRecord $identity $proof $identity.identitySid 'S-1-5-21-1-2-3-1001' $controller
 $proof.filesystemVerified=$false
 Refuses {Assert-AzuriteTargetIdentityRecord $identity $proof $identity.identitySid 'S-1-5-21-1-2-3-1001' $controller} 'R2 parent accepted unverified target filesystem proof'
 Write-Output 'PASS R2: canonical traversal/overlap/reparse refusal, target filesystem proof, unchanged inherited paths'
}
if($Part -in @('Errors','All')){
 Initialize-AzuriteRestrictedNative
 $absent=Join-Path $root 'definitely-missing-bootstrap.exe'
 $outer=[Azurite.Restricted.ProcessPump]::Run((New-AzuriteProcessInfo -FilePath $absent -Arguments @() -WorkingDirectory $root),1000,$null,4096,@(),1048576)
 Assert ($outer.Pid -eq 0 -and -not [string]::IsNullOrWhiteSpace($outer.Error)) 'R3 harmless missing-executable fixture lacks native startup error'
 Assert ([bool](Get-Command Get-AzuriteOuterProcessStatus -ErrorAction SilentlyContinue)) 'R3 controller diagnostic adapter is missing'
 $status=Get-AzuriteOuterProcessStatus $outer
 $path=Join-Path $root 'controller-result.json';Write-AzuriteFlushedJson $path @{bootstrapProcess=$status}
 $retained=Read-AzuriteBoundedJson $path
 Assert ($retained.bootstrapProcess.failed -and $retained.bootstrapProcess.error -match 'definitely-missing-bootstrap' -and $retained.bootstrapProcess.reason.Contains($retained.bootstrapProcess.error)) 'R3 startup detail did not survive controller reporting path'
 $outer.Error="fixture password=do-not-retain`ncredential:do-not-retain`0native failure"
 $clean=Get-AzuriteOuterProcessStatus $outer
 Assert ($clean.error -notmatch 'do-not-retain' -and $clean.error -notmatch '[\x00-\x1f]' -and $clean.error -match 'native failure') 'R3 diagnostic sanitization failed'
 Write-Output 'PASS R3: harmless startup error retained in controller JSON/reason with sensitive/control text sanitized'
}
Write-Output ('PASS focused fix1 '+$Part+'; artifacts '+$root)
