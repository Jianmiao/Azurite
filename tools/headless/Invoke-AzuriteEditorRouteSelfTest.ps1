#requires -Version 7.0
param(
 [ValidateSet('Positive','ProbeMissing','InvalidManifest')][string]$Scope='Positive',
 [ValidateRange(10,300)][int]$TimeoutSeconds=120,
 [switch]$PlanOnly,
 [switch]$PrepareOnly
)
$ErrorActionPreference='Stop';Set-StrictMode -Version Latest
Import-Module (Join-Path $PSScriptRoot 'EditorRouteCore.psm1') -DisableNameChecking
if($PlanOnly -and $PrepareOnly){throw 'Choose one preparation mode.'}
Invoke-AzuriteEditorRouteController -Scope $Scope -TimeoutSeconds $TimeoutSeconds -PlanOnly:$PlanOnly -PrepareOnly:$PrepareOnly
