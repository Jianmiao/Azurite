# Harmless current-account identity/ACL fixture. Never starts AA or touches its state.
param([Parameter(Mandatory)][string]$ControlPath)
$ErrorActionPreference='Stop'
foreach($name in @('Microsoft.PowerShell.Security','Microsoft.PowerShell.Management','Microsoft.PowerShell.Utility')){Import-Module (Join-Path $PSHOME ('Modules\'+$name+'\'+$name+'.psd1')) -Force -DisableNameChecking}
Import-Module (Join-Path $PSScriptRoot 'HeadlessCore.psm1') -Force -DisableNameChecking
Import-Module (Join-Path $PSScriptRoot 'RestrictedCliCore.psm1') -Force -DisableNameChecking
Import-Module (Join-Path $PSScriptRoot 'CurrentAccountCli.psm1') -Force -DisableNameChecking
$c=Read-AzuriteBoundedJson $ControlPath
Assert-AzuriteCurrentPrivatePath $PSScriptRoot $c.sid
Assert-AzuriteCurrentPrivatePath $ControlPath $c.sid
foreach($entry in $c.hashes.PSObject.Properties){$p=Join-Path $PSScriptRoot $entry.Name;Assert-AzuriteCurrentPrivatePath $p $c.sid;Assert-AzuritePreparedHash $p $entry.Value}
Initialize-AzuriteRestrictedNative -AssemblyPath (Join-Path $PSScriptRoot 'RestrictedCliNative.dll')
$job=[Azurite.Restricted.OwnedJob]::CreateAndJoinCurrent();$exitCode=1
try{
 $job.AnnounceAndAwaitController($c.runId)
 Assert-AzuriteCurrentPrivatePath $c.output $c.sid
 $identityPath=Join-Path $c.output 'identity.json'
 & (Join-Path $PSScriptRoot 'Identity-Preflight.ps1') -AzuriteRunId $c.runId -AzuriteResultPath $identityPath
 $identity=Read-AzuriteBoundedJson $identityPath
 $proof=Assert-AzuriteCurrentIdentity $identity $c.sid $c.profile $c.localLow $c.environment
 Write-AzuriteFlushedJson (Join-Path $c.output 'current-result.json') @{fixtureOnly=$true;sid=$identity.identitySid;identityMode=$proof.identityMode;hiveShared=$proof.hiveShared;accountIsolation=$proof.accountIsolation;runId=$c.runId;pid=$PID}
 $exitCode=0
}catch{[Console]::Error.WriteLine($_.Exception.Message)}
finally{[Console]::Out.Flush();[Console]::Error.Flush();$job.Terminate([uint32]$exitCode)}
