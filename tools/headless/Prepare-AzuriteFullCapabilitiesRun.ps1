[CmdletBinding(DefaultParameterSetName='Plan')]
param(
 [Parameter(Mandatory,ParameterSetName='BuildHelper')][switch]$BuildHelper,
 [Parameter(Mandatory,ParameterSetName='BuildHelper')][string]$HelperOutputDirectory,
 [Parameter(Mandatory,ParameterSetName='Plan')][Parameter(Mandatory,ParameterSetName='Apply')][string]$SourceManifestPath,
 [Parameter(Mandatory,ParameterSetName='Plan')][Parameter(Mandatory,ParameterSetName='Apply')][string]$ExpectedSourceManifestSha256,
 [Parameter(Mandatory,ParameterSetName='Plan')][Parameter(Mandatory,ParameterSetName='Apply')][string]$ControlPath,
 [Parameter(Mandatory,ParameterSetName='Plan')][Parameter(Mandatory,ParameterSetName='Apply')][string]$ExpectedControlSha256,
 [Parameter(Mandatory,ParameterSetName='Plan')][Parameter(Mandatory,ParameterSetName='Apply')][string]$GatePath,
 [Parameter(Mandatory,ParameterSetName='Plan')][Parameter(Mandatory,ParameterSetName='Apply')][string]$ExpectedGateSha256,
 [Parameter(Mandatory,ParameterSetName='Plan')][Parameter(Mandatory,ParameterSetName='Apply')][string]$ExpectedPlanSha256,
 [Parameter(ParameterSetName='Plan')][Parameter(Mandatory,ParameterSetName='Apply')][string]$ApprovalPath,
 [Parameter(ParameterSetName='Plan')][Parameter(Mandatory,ParameterSetName='Apply')][string]$ExpectedApprovalSha256,
 [Parameter(Mandatory,ParameterSetName='Apply')][switch]$Apply,
 [Parameter(Mandatory,ParameterSetName='Apply')][string]$EvidenceDirectory
)
$ErrorActionPreference='Stop'
Import-Module (Join-Path $PSScriptRoot 'FullRestrictedStageTransition.psm1') -Force -DisableNameChecking
try{
 if($BuildHelper){New-AzuriteStageTransitionHelper -OutputDirectory $HelperOutputDirectory|ConvertTo-Json -Depth 12;exit 0}
 $parameters=@{};foreach($key in $PSBoundParameters.Keys){$parameters[$key]=$PSBoundParameters[$key]}
 $result=Invoke-AzuriteStageTransitionPreparation @parameters
 $result|ConvertTo-Json -Depth 90
 if($Apply -and -not $result.Success){exit 1}
 exit 0
}catch{[pscustomobject]@{status='refused-or-failed';error=$_.Exception.Message;willLaunch=$false;runtimeAllowed=$false;automaticRollbackPerformed=$false}|ConvertTo-Json -Depth 12;exit 1}
