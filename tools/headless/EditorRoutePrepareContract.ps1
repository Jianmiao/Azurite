#requires -Version 7.0
param([ValidateSet('Positive','ProbeMissing','InvalidManifest')][string]$Scope='Positive',[switch]$PlanOnly)
$ErrorActionPreference='Stop';Set-StrictMode -Version Latest
foreach($name in @('HeadlessCore.psm1','RestrictedCliCore.psm1','CurrentAccountCli.psm1','EditorRouteCore.psm1')){Import-Module (Join-Path $PSScriptRoot $name) -DisableNameChecking}
$lab='E:\aamod\labs\Azurite-20261108-editor'
$inputs=Read-AzuriteEditorRouteInputs $Scope
$contract=New-AzuriteEditorRouteContract $inputs (Read-AzuriteStartupContract) $Scope
$sid=[Security.Principal.WindowsIdentity]::GetCurrent().User.Value
$principal=[Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
if($principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)){throw 'Preparation requires the unelevated current account.'}
Assert-AzuriteCleanupGateClear $lab
Assert-AzuritePrivateAcl $lab $sid
if([Diagnostics.Process]::GetProcessesByName('AzureArchive').Length){throw 'Another AzureArchive process exists.'}
Assert-AzuritePinnedInstall (Join-Path $lab 'install') $contract
Assert-AzuriteEvidence $contract
$directory=Join-Path $lab 'contracts';$path=Join-Path $directory ('editor-route-'+$Scope+'.json')
if($PlanOnly){return [pscustomobject]@{scope=$Scope;ready=$true;willWrite=$false;willLaunch=$false;installPinCount=@($contract.files).Count;toolPinCount=@($inputs.toolPins.PSObject.Properties).Count;acceptance=$contract.acceptance}}
$mutex=[Threading.Mutex]::new($false,'Global\AzuriteRestrictedCli-AA');$locked=$false
try{
 try{$locked=$mutex.WaitOne(0)}catch [Threading.AbandonedMutexException]{$locked=$true}
 if(-not $locked){throw 'Another AA laboratory run holds the shared lock.'}
 Assert-AzuriteCleanupGateClear $lab
 if([Diagnostics.Process]::GetProcessesByName('AzureArchive').Length){throw 'AzureArchive appeared before preparation.'}
 if(Test-Path -LiteralPath $path){throw 'Existing prepared contract is retained; replacement requires explicit source review and archival.'}
 if(-not(Test-Path -LiteralPath $directory)){[void][IO.Directory]::CreateDirectory($directory);Set-AzuriteLabAcl $lab $directory $sid}else{Assert-AzuriteCurrentPrivatePath $directory $sid}
 Write-AzuriteFlushedJson $path $contract;Set-AzuriteLabAcl $lab $path $sid
 [void](Read-AzuriteEditorRouteContract $Scope)
 [pscustomobject]@{scope=$Scope;prepared=$true;willLaunch=$false;contractSha256=(Get-AzuriteSha256 $path);installPinCount=@($contract.files).Count;toolPinCount=@($inputs.toolPins.PSObject.Properties).Count;acceptance=$contract.acceptance}
}finally{if($locked){$mutex.ReleaseMutex()};$mutex.Dispose()}
