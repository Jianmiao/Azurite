[CmdletBinding()]
param(
    [Parameter(Mandatory = $false)][string]$ContractPath,
    [Parameter(Mandatory = $false)][string]$CachePrerequisitesPath,
    [Parameter(Mandatory = $true)][string]$RunId,
    [Parameter(Mandatory = $true)]$Facts,
    [Parameter(Mandatory = $false)]$Admission,
    [Parameter(Mandatory = $false)]$EvidenceBundle,
    [Parameter(Mandatory = $false)][string]$EvidenceBundlePath,
    [Parameter(Mandatory = $false)]$Approval,
    [Parameter(Mandatory = $false)][string]$ApprovalPath,
    [Parameter(Mandatory = $false)][string]$ExpectedApprovalSha256
)

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'FullRestrictedCapabilitiesPlan.psm1') -Force -DisableNameChecking
function Read-AzuritePlannerCJson([string]$Path,[long]$MaxBytes) {
    $root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
    $full=[IO.Path]::GetFullPath($Path)
    if (-not $full.StartsWith($root+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetPathRoot($full) -cne [IO.Path]::GetPathRoot($root)) { throw 'Dry wrapper accepts only bounded C: repository evidence.' }
    $item=Get-Item -LiteralPath $full -Force -ErrorAction Stop
    if ($item.PSIsContainer -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 -or $item.Length -lt 2 -or $item.Length -gt $MaxBytes) { throw 'Dry wrapper evidence file is linked, missing, empty, or oversized.' }
    $raw=[IO.File]::ReadAllBytes($full)
    $decoder=[Text.UTF8Encoding]::new($false,$true)
    $text=$decoder.GetString($raw)
    if ($raw[0] -eq 239) { throw 'Dry wrapper evidence must be BOM-free UTF-8.' }
    $value=$text|ConvertFrom-Json -ErrorAction Stop
    if (($value|ConvertTo-Json -Depth 80 -Compress) -cne $text) { throw 'Dry wrapper evidence must be canonical compact JSON.' }
    return $value
}
if ($EvidenceBundlePath -and $null -ne $EvidenceBundle) { throw 'Choose EvidenceBundle or EvidenceBundlePath, not both.' }
if ($ApprovalPath -and $null -ne $Approval) { throw 'Choose Approval or ApprovalPath, not both.' }
if ($EvidenceBundlePath) { $EvidenceBundle=Read-AzuritePlannerCJson $EvidenceBundlePath 4194304 }
if ($ApprovalPath) { $Approval=Read-AzuritePlannerCJson $ApprovalPath 16384 }
if ($null -ne $Admission -and ($null -eq $Approval -or [string]::IsNullOrWhiteSpace($ExpectedApprovalSha256))) {
    throw 'Admission requires actual positive Approval and an independently trusted ExpectedApprovalSha256.'
}

# Paths are accepted for caller ergonomics only; the module validates the pinned C: files
# and never adopts a caller-selected contract or cache artifact.
$contract = Read-AzuriteFullCapabilitiesContract
$cache = Get-Content -LiteralPath (Join-Path $PSScriptRoot '..\..\artifacts\aa-fr-cache-prerequisites\cache-prerequisites-contract.json') -Raw -Encoding UTF8 | ConvertFrom-Json
New-AzuriteFullCapabilitiesPlan -Contract $contract -CachePrerequisites $cache -RunId $RunId -Facts $Facts -Admission $Admission -EvidenceBundle $EvidenceBundle -Approval $Approval -ExpectedApprovalSha256 $ExpectedApprovalSha256
