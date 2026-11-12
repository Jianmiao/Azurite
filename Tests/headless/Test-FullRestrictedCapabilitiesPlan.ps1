$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot '..\..\tools\headless\FullRestrictedCapabilitiesPlan.psm1') -Force -DisableNameChecking

$script:passed = 0
function Assert([bool]$Condition, [string]$Name) { if (-not $Condition) { throw "FAIL $Name" }; $script:passed++; Write-Output "PASS $Name" }
function Clone($Value) { return ($Value | ConvertTo-Json -Depth 100 -Compress | ConvertFrom-Json) }
function HashText([string]$Value) { $sha=[Security.Cryptography.SHA256]::Create();try{return ([BitConverter]::ToString($sha.ComputeHash([Text.UTF8Encoding]::new($false).GetBytes($Value)))).Replace('-','').ToLowerInvariant()}finally{$sha.Dispose()} }
function PinDigest($Items,[bool]$Files) { $lines=@($Items|ForEach-Object { if($Files){$_.path+'|'+$_.sha256.ToLowerInvariant()}else{[string]$_} });[Array]::Sort($lines,[StringComparer]::Ordinal);return HashText (($lines -join "`n")+"`n") }
function HasFailure($Result,[string]$Needle) { return @($Result.Failures | Where-Object { $_.IndexOf($Needle,[StringComparison]::OrdinalIgnoreCase) -ge 0 }).Count -gt 0 }
function New-Receipt([string]$Kind,$Result,$Plan,$Facts) {
    $content = [pscustomobject][ordered]@{schema=1;kind=$Kind;stage='posttransition';runId=$Plan.RunId;planSha256=$Plan.PlanSha256;contractSha256=$Plan.ContractSha256;ownerSid=$Facts.ownerSid;volumeGuid=$Facts.volumeGuid;result=[pscustomobject]$Result}
    $text = $content | ConvertTo-Json -Depth 40 -Compress
    return [pscustomobject][ordered]@{kind=$Kind;phase='posttransition';runId=$Plan.RunId;planSha256=$Plan.PlanSha256;contractSha256=$Plan.ContractSha256;ownerSid=$Facts.ownerSid;volumeGuid=$Facts.volumeGuid;bytes=[Text.UTF8Encoding]::new($false).GetByteCount($text);sha256=(HashText $text);content=$text}
}

$contract = Read-AzuriteFullCapabilitiesContract
$cache = Get-Content (Join-Path $PSScriptRoot '..\..\artifacts\aa-fr-cache-prerequisites\cache-prerequisites-contract.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$contractPath = Join-Path $PSScriptRoot '..\..\tools\headless\full-restricted-capabilities-contract.json'
$contractBytes = [IO.File]::ReadAllBytes($contractPath)
Assert ($contractBytes[0] -ne 239 -and -not ($contractBytes -contains [byte]13) -and ($contractBytes -contains [byte]10)) 'lf-no-bom-contract'
Assert ($contract.files.Count -eq 451 -and $contract.readEnvelope.fileCount -eq 1062 -and $contract.readEnvelope.directoryCount -eq 156) 'finite-envelope'
Assert ($contract.requestPolicy.parentMask -eq '0x1200AB' -and $contract.requestPolicy.childFileMask -eq '0x13019F' -and $contract.requestPolicy.argumentsFileMask -eq '0x120089' -and -not $contract.requestPolicy.deleteChild -and -not $contract.requestPolicy.createSubdirectory -and $contract.requestPolicy.responseMaxBytes -eq 1048576 -and $contract.requestPolicy.maxTemporaryFiles -eq 1 -and $contract.requestPolicy.postrunValidationRequired) 'request-exact-masks'
Assert (@($contract.mutableLeaves).Count -eq 6 -and @($contract.mutableLeaves | Where-Object { $_.fileMask -ne '0x12019F' }).Count -eq 0) 'six-file-only-mutable-leaves'
Assert ($contract.mutableLeaves[0].finalSha256 -eq 'f2e0f930efda6a7fa292e872760a27cdc9bb1d232cd82d719446622c4dc6ef06' -and $contract.mutableLeaves[4].seedHex -eq '00000000') 'final-config-and-seed'

$runId = '11111111111111111111111111111111'
$sid = $contract.ownerSid
$facts = [pscustomobject][ordered]@{
    schema=1;runId=$runId;labRoot=$contract.labRoot;executable=$contract.executable;operation='capabilities';profile=$contract.profile;modName=$contract.modName;modVersion=$contract.modVersion
    volumeGuid=$contract.stage.destinationVolumeGuid;ownerSid=$sid;controllerSid=$sid;stage=$contract.stage;inputs=[pscustomobject]@{};cache=[pscustomobject]@{}
    environment=[pscustomobject]@{gameAssemblyOverride=$null;unhollowedPathArgument=$false}
    acl=[pscustomobject]@{stageSealPolicySha256=$contract.stage.sealPolicySha256;stageSealsIntact=$true;cacheParentCreationDenied=$true;sharedGrantDelta='None';actualLogonSid='S-1-5-5-1-2'}
    preTransition=[pscustomobject]@{phase='pretransition';runId=$runId;cacheLeaf=$contract.cachePrerequisites.modTheLeaf;cacheLeafExists=$false;unityLogAbsent=$true;seedSha256=$contract.cachePrerequisites.seedSha256;parentSealIntact=$true;runtimeAllowed=$false;observedUtc='2026-11-10T00:00:00Z'}
    transition=$null
}
$stage = Get-Content (Join-Path $PSScriptRoot '..\..\artifacts\editor-normal-install-preparation\source-bound-manifest-v2.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$mutableExisting=@($contract.mutableLeaves|Where-Object { $_.creation -eq 'ExistingOnly' }|ForEach-Object { $_.path })
$immutable=@($stage.entries|Where-Object { $mutableExisting -cnotcontains $_.path })
$facts.inputs = [pscustomobject][ordered]@{stageFilePinDigest=(PinDigest $stage.entries $true);immutableFilePinDigest=(PinDigest $immutable $true);directoryPinDigest=(PinDigest $stage.directories $false);semanticPinCount=451;stageFileCount=1062;stageDirectoryCount=156;stageBytes=[long]3521516451;cacheContractSha256=$contract.cachePrerequisites.contractSha256;cacheReportSha256=$contract.cachePrerequisites.reportSha256;cacheIndexSha256=$contract.cachePrerequisites.indexSha256;warmResultSha256=$contract.cachePrerequisites.warmResultSha256}
$facts.cache = [pscustomobject][ordered]@{chainloaderSha256=$contract.cachePrerequisites.chainloaderSha256;modTheLeaf=$contract.cachePrerequisites.modTheLeaf;modTheLeafAbsent=$true;seedSha256=$contract.cachePrerequisites.seedSha256;stamp=$contract.cachePrerequisites.stamp;orderedInputEventCount=147;unityDllCount=72;generatorVersion='1.5.3.0';cpp2ilVersion='2022.1.0.0';optionalRenameMapPresent=$false;orderedInputsMatchRecorded=$true}
$plan = New-AzuriteFullCapabilitiesPlan -Contract $contract -CachePrerequisites $cache -RunId $runId -Facts $facts
Write-Output ('INTENT_FACTS_SHA256 '+$plan.FactsSha256)
Write-Output ('INTENT_PLAN_SHA256 '+$plan.PlanSha256)
if (-not $plan.PreparationReady) { Write-Output ('PLAN_FAILURES ' + ($plan.Failures -join ' | ')) }
Assert ($plan.PreparationReady -and -not $plan.Ready -and -not $plan.RuntimeAllowed -and -not $plan.WillLaunch -and $plan.Failures.Count -eq 0) 'forward-pretransition-dry-plan'
Assert ($plan.SealTransitions[0].createMode -eq 'CreateNewNoOverwrite' -and $plan.SealTransitions[0].restoreBeforeAA -and $plan.BeforeImages[0].finalSha256 -eq $contract.mutableLeaves[0].finalSha256) 'seal-and-before-image-postcondition'
Assert ($plan.BeforeImages[4].absenceProof -and $plan.BeforeImages[5].absenceProof -and $plan.MutableLeaves[3].creation -eq 'ExistingOnly') 'new-cache-unity-absence-existing-chainloader'
Assert ($plan.Grants.files.Count -eq 1062 -and $plan.Grants.directories.Count -eq 159 -and $plan.Grants.directories[0].path -eq 'F:\AzuriteLabs' -and $plan.Grants.directories[1].path -eq $contract.labRoot) 'all-existing-grant-targets'

# This digest is an explicitly mocked trusted controller input. Production has no approval yet.
$approvalContent = [pscustomobject][ordered]@{schema=1;decision='approved-one-capabilities-run';scope='current-account-full-restricted-capabilities-only';runId=$runId;planSha256=$plan.PlanSha256;contractSha256=$plan.ContractSha256;ownerSid=$sid;volumeGuid=$facts.volumeGuid;labRoot=$plan.LabRoot;executable=$plan.Executable;validFromUtc='2026-11-09T00:00:00Z';issuedUtc='2026-11-10T00:00:10Z';validUntilUtc='2026-11-11T00:00:00Z';toolingManifestSha256=(HashText 'mock-reviewed-tooling-manifest');controlGateSha256=(HashText 'mock-reviewed-preapproval-control-gate')}
$approvalText = $approvalContent | ConvertTo-Json -Depth 30 -Compress
$expectedApprovalSha256 = HashText $approvalText
$approval = [pscustomobject]@{bytes=[Text.UTF8Encoding]::new($false).GetByteCount($approvalText);content=$approvalText}

$grantTargets=@($plan.Grants.files)+@($plan.Grants.directories)
$aclDescriptors=@(for($i=0;$i -lt $grantTargets.Count;$i++){[pscustomobject][ordered]@{path=$grantTargets[$i].path;role=$grantTargets[$i].role;mask=$grantTargets[$i].mask;ownerSid=$sid;descriptorSha256=$(if($grantTargets[$i].path -eq 'BepInEx/cache'){HashText 'mock-cache-parent-before-descriptor'}else{HashText ('mock-descriptor-'+$i)});objectIdentity=('{0:x32}' -f ($i+1))}})
$beforeItems=@(for($i=0;$i -lt 6;$i++){$match=@($aclDescriptors|Where-Object {$_.path -ceq $plan.BeforeImages[$i].path});$existing=-not $plan.BeforeImages[$i].absenceProof;[pscustomobject][ordered]@{path=$plan.BeforeImages[$i].path;initialSha256=$plan.BeforeImages[$i].initialSha256;aclSha256=$(if($existing){$match[0].descriptorSha256}else{HashText ('mock-planned-private-descriptor-'+$i)});fileIdentity=$(if($existing){$match[0].objectIdentity}else{$null});absentBefore=$plan.BeforeImages[$i].absenceProof}})
for($i=0;$i -lt 4;$i++){$match=@($aclDescriptors|Where-Object {$_.path -ceq $beforeItems[$i].path});Assert ($match.Count -eq 1 -and $beforeItems[$i].fileIdentity -ceq $match[0].objectIdentity -and $beforeItems[$i].aclSha256 -ceq $match[0].descriptorSha256) ('positive-existing-overlap-'+$i)}
$post = [ordered]@{phase='posttransition';runId=$runId;ownerSid=$sid;volumeGuid=$facts.volumeGuid;seedSha256=$facts.cache.seedSha256;leaseId='22222222222222222222222222222222';cacheLeafCreateMode='CreateNewNoOverwrite';cacheLeafCreated=$true;cacheLeafSha256=$facts.cache.seedSha256;cacheLeafFileIdentity='33333333333333333333333333333333';cacheParentBeforeDescriptorSha256=(HashText 'mock-cache-parent-before-descriptor');cacheParentRestoredDescriptorSha256=(HashText 'mock-cache-parent-restored-descriptor');unityLogFileIdentity='44444444444444444444444444444444';sealRestored=$true;stageSealsExceptCacheUnchanged=$true;sharedDelta='None';runtimeAllowed=$false;precreatedUnityLogEmpty=$true;requestArgumentsProtected=$true;beforeImageInventorySha256=(HashText ($beforeItems|ConvertTo-Json -Depth 30 -Compress));aclBeforeImageInventorySha256=(HashText ($aclDescriptors|ConvertTo-Json -Depth 30 -Compress));reviewApprovalSha256=$expectedApprovalSha256}
$pathDigest = HashText ((@($plan.MutableLeaves | ForEach-Object { $_.path }) -join "`n") + "`n")
$grantDigest = $plan.GrantDigest
$results = [ordered]@{
    'lease'=[ordered]@{state='released-after-preparation';leaseId=$post.leaseId;runId=$runId;ownerSid=$sid;volumeGuid=$facts.volumeGuid;scope='capabilities-only-preparation';globalMutex='Global\AzuriteRestrictedCli-AA';heldFromUtc='2026-11-10T00:00:10Z';heldUntilUtc='2026-11-10T00:00:11Z';releaseOutcome='success';pendingRunId=$runId}
    'before-image'=[ordered]@{state='captured';inventorySha256=$post.beforeImageInventorySha256;leafCount=6;mutablePathsSha256=$pathDigest;ownerSid=$sid;newCacheLeafAbsent=$true;descriptorEncoding='Utf8NoBomFullOwnerGroupAccessSddl';items=$beforeItems}
    'acl-before-image'=[ordered]@{state='captured';inventorySha256=$post.aclBeforeImageInventorySha256;descriptorCount=1221;cacheParent='BepInEx/cache';cacheParentDescriptorSha256=$post.cacheParentBeforeDescriptorSha256;ownerSid=$sid;descriptorEncoding='Utf8NoBomFullOwnerGroupAccessSddl';descriptors=$aclDescriptors}
    'grant'=[ordered]@{state='applied';grantsSha256=$grantDigest;principalSid=$facts.acl.actualLogonSid;namedMutableMask='0x12019F';requestParentMask='0x1200AB';responseChildMask='0x13019F';argumentsMask='0x120089';sharedDelta='None'}
    'cache-create'=[ordered]@{state='created-new';leaf=$facts.cache.modTheLeaf;absentBefore=$true;createMode='CreateNewNoOverwrite';seedBytes=4;seedSha256=$facts.cache.seedSha256;resultSha256=$post.cacheLeafSha256;fileIdentity=$post.cacheLeafFileIdentity;parent='BepInEx/cache'}
    'seal-restoration'=[ordered]@{state='restored';parent='BepInEx/cache';creationDenyMask='0x6';policySha256=$facts.stage.sealPolicySha256;beforeDescriptorSha256=$post.cacheParentBeforeDescriptorSha256;restoredDescriptorSha256=$post.cacheParentRestoredDescriptorSha256;otherSealsUnchanged=$true}
    'unchanged-inputs'=[ordered]@{state='matched';manifestSha256=$facts.stage.manifestSha256;immutablePinDigest=$plan.ReadEnvelope.immutableFilePinDigest;immutableFileCount=1058;directoryPinDigest=$plan.ReadEnvelope.directoryPinDigest;directoryCount=156}
    'warm-input-order'=[ordered]@{state='matched';computedStamp=$facts.cache.stamp;recordedProofSha256=$facts.inputs.warmResultSha256;eventCount=147;inputFileCount=73;unityDllCount=72;generatorVersion='1.5.3.0';cpp2ilVersion='2022.1.0.0';optionalRenameMapPresent=$false;gameAssemblyOverridePresent=$false;unhollowedPathArgumentPresent=$false}
    'unity-log-precreation'=[ordered]@{state='precreated-empty';path='output/unity.log';baselineBytes=0;fileIdentity=$post.unityLogFileIdentity;fileMask='0x12019F';ownerSid=$sid}
    'response-policy'=[ordered]@{state='prepared';parent='request';parentMask='0x1200AB';childFileMask='0x13019F';argumentsFile='arguments.json';argumentsMask='0x120089';argumentsProtected=$true;createSubdirectory=$false;deleteChild=$false;expectedOutput='response.json';responseMaxBytes=1048576;maxTemporaryFiles=1;postrunValidationRequired=$true;unexpectedOutput='Reject'}
    'review'=[ordered]@{state='positive-approval-bound';approvalSha256=$expectedApprovalSha256;runId=$runId;planSha256=$plan.PlanSha256;ownerSid=$sid;volumeGuid=$facts.volumeGuid}
}
$map = [ordered]@{'lease'='leaseReceiptSha256';'before-image'='beforeImageReceiptSha256';'acl-before-image'='aclBeforeImageReceiptSha256';'grant'='grantReceiptSha256';'cache-create'='cacheLeafCreateReceiptSha256';'seal-restoration'='sealRestorationReceiptSha256';'unchanged-inputs'='unchangedInputsReceiptSha256';'warm-input-order'='warmInputOrderReceiptSha256';'unity-log-precreation'='unityLogPrecreationReceiptSha256';'response-policy'='responsePolicyReceiptSha256';'review'='reviewReceiptSha256';'transition'='receiptSha256'}
$receipts = [Collections.Generic.List[object]]::new()
foreach ($kind in $results.Keys) { $receipt=New-Receipt $kind $results[$kind] $plan $facts; $receipts.Add($receipt); $post[$map[$kind]]=$receipt.sha256 }
$parts = @(@('lease','before-image','acl-before-image','grant','cache-create','seal-restoration','unchanged-inputs','warm-input-order','unity-log-precreation','response-policy','review') | ForEach-Object { $_ + '|' + $post[$map[$_]] })
$transitionResult = [ordered]@{state='complete-for-capabilities-only';phase0CompletionSha256=$facts.stage.completionSha256;receiptSetSha256=(HashText (($parts -join "`n") + "`n"));cacheLeafSha256=$facts.cache.seedSha256;sealRestored=$true;sharedDelta='None'}
$transitionReceipt=New-Receipt 'transition' $transitionResult $plan $facts; $receipts.Add($transitionReceipt); $post.receiptSha256=$transitionReceipt.sha256
$facts.transition = [pscustomobject]$post
$evidence = [pscustomobject][ordered]@{schema=1;scope='full-restricted-capabilities-admission-evidence';stage='posttransition';runId=$runId;planSha256=$plan.PlanSha256;contractSha256=$plan.ContractSha256;ownerSid=$sid;volumeGuid=$facts.volumeGuid;receipts=$receipts.ToArray()}
$admission = [pscustomobject][ordered]@{schema=1;scope='capabilities-only-current-account-full-restricted';runId=$runId;planSha256=$plan.PlanSha256;contractSha256=$plan.ContractSha256;labRoot=$facts.labRoot;executable=$facts.executable;operation='capabilities';volumeGuid=$facts.volumeGuid;ownerSid=$sid;controllerSid=$sid;phase0=$contract.stage;inputHashes=$facts.inputs;cacheProofSha256=$contract.cachePrerequisites.warmResultSha256;evidenceBundleSha256=(HashText ($evidence | ConvertTo-Json -Depth 80 -Compress));approvalSha256=$expectedApprovalSha256;transition=$facts.transition}
function New-ReboundMutation([string]$Kind,[scriptblock]$Change) {
    $badEvidence=Clone $evidence;$badFacts=Clone $facts;$badAdmission=Clone $admission
    $receipt=@($badEvidence.receipts|Where-Object {$_.kind -ceq $Kind})[0]
    $body=$receipt.content|ConvertFrom-Json
    [void](& $Change $body)
    if($Kind -ceq 'before-image'){$body.result.inventorySha256=HashText ($body.result.items|ConvertTo-Json -Depth 30 -Compress);$badFacts.transition.beforeImageInventorySha256=$body.result.inventorySha256}
    if($Kind -ceq 'acl-before-image'){$body.result.inventorySha256=HashText ($body.result.descriptors|ConvertTo-Json -Depth 30 -Compress);$badFacts.transition.aclBeforeImageInventorySha256=$body.result.inventorySha256}
    $receipt.content=$body|ConvertTo-Json -Depth 60 -Compress;$receipt.bytes=[Text.UTF8Encoding]::new($false).GetByteCount($receipt.content);$receipt.sha256=HashText $receipt.content
    $badFacts.transition.($map[$Kind])=$receipt.sha256
    $orderedKinds=@('lease','before-image','acl-before-image','grant','cache-create','seal-restoration','unchanged-inputs','warm-input-order','unity-log-precreation','response-policy','review')
    $parts=@($orderedKinds|ForEach-Object {$_+'|'+$badFacts.transition.($map[$_])})
    $last=@($badEvidence.receipts|Where-Object {$_.kind -ceq 'transition'})[0]
    $terminal=$last.content|ConvertFrom-Json;$terminal.result.receiptSetSha256=HashText (($parts-join [char]10)+[char]10)
    $last.content=$terminal|ConvertTo-Json -Depth 60 -Compress;$last.bytes=[Text.UTF8Encoding]::new($false).GetByteCount($last.content);$last.sha256=HashText $last.content;$badFacts.transition.receiptSha256=$last.sha256
    $badAdmission.transition=$badFacts.transition;$badAdmission.evidenceBundleSha256=HashText ($badEvidence|ConvertTo-Json -Depth 80 -Compress)
    return [pscustomobject]@{Facts=$badFacts;Evidence=$badEvidence;Admission=$badAdmission}
}
$complete = New-AzuriteFullCapabilitiesPlan -Contract $contract -CachePrerequisites $cache -RunId $runId -Facts $facts -Admission $admission -EvidenceBundle $evidence -Approval $approval -ExpectedApprovalSha256 $expectedApprovalSha256
if (-not $complete.Ready) { Write-Output ('POSITIVE_FAILURES ' + ($complete.Failures -join ' | ')) }
Assert ($complete.Ready -and $complete.PlanSha256 -eq $plan.PlanSha256 -and -not $complete.RuntimeAllowed -and -not $complete.WillLaunch) 'forward-complete-proof-remains-dry'
Assert ($complete.AdmissionProof.ownerSid -eq $sid -and $complete.AdmissionProof.approvalHash -eq $expectedApprovalSha256 -and $complete.AdmissionProof.transitionReceiptHash -eq $post.receiptSha256 -and $complete.AdmissionProof.preparationLeaseReleased -and $complete.AdmissionProof.requiresFreshRuntimeLease) 'proof-forward-hash-binding'
foreach($missing in @(@('before-image','items','fileIdentity'),@('before-image','items','path'),@('before-image','items','aclSha256'),@('acl-before-image','descriptors','objectIdentity'),@('acl-before-image','descriptors','path'),@('acl-before-image','descriptors','descriptorSha256'))){
    $inventory=if($missing[0] -ceq 'before-image'){$beforeItems}else{$aclDescriptors}
    $targetIndex=-1;for($i=0;$i -lt $inventory.Count;$i++){if($inventory[$i].path -ceq 'BepInEx/config/BepInEx.cfg'){$targetIndex=$i;break}}
    if($targetIndex -lt 0){throw 'Missing-field control requires the existing BepInEx config overlap.'}
    $bad=New-ReboundMutation $missing[0] {param($body)$body.result.($missing[1])[$targetIndex].PSObject.Properties.Remove($missing[2])}
    $expected='receipt.'+$missing[0]+'.result.'+$missing[1]+'['+$targetIndex+'].'+$missing[2]+' is missing.'
    $structured=$true;$r=$null;try{$r=New-AzuriteFullCapabilitiesPlan $contract $cache $runId $bad.Facts $bad.Admission $bad.Evidence $approval $expectedApprovalSha256}catch{$structured=$false}
    Assert ($structured -and $null -ne $r -and -not $r.Ready -and $null -eq $r.AdmissionProof -and -not $r.RuntimeAllowed -and -not $r.WillLaunch -and $r.Failures.Count -eq 1 -and $r.Failures -ccontains $expected) ('rehashed-missing-'+$missing[0]+'-'+$missing[2]+'-plan-structured-rejection')
    $structured=$true;$r=$null;try{$r=Assert-AzuriteFullCapabilitiesAdmission $contract $plan $bad.Admission $bad.Facts $bad.Evidence $approval $expectedApprovalSha256}catch{$structured=$false}
    Assert ($structured -and $null -ne $r -and -not $r.Valid -and $null -eq $r.Proof -and $r.Failures.Count -eq 1 -and $r.Failures -ccontains $expected) ('rehashed-missing-'+$missing[0]+'-'+$missing[2]+'-admission-structured-rejection')
}
$bad=New-ReboundMutation 'before-image' {param($body)$body.result.items[0].fileIdentity='ffffffffffffffffffffffffffffffff'}
$r=New-AzuriteFullCapabilitiesPlan $contract $cache $runId $bad.Facts $bad.Admission $bad.Evidence $approval $expectedApprovalSha256
Assert (-not $r.Ready -and (HasFailure $r 'receipt.inventoryJoin.BepInEx/config/BepInEx.cfg.fileIdentity') -and -not (HasFailure $r 'transitionHash')) 'rehashed-existing-file-identity-split-rejected'
$bad=New-ReboundMutation 'before-image' {param($body)$body.result.items[0].aclSha256=('e'*64)}
$r=New-AzuriteFullCapabilitiesPlan $contract $cache $runId $bad.Facts $bad.Admission $bad.Evidence $approval $expectedApprovalSha256
Assert (-not $r.Ready -and (HasFailure $r 'receipt.inventoryJoin.BepInEx/config/BepInEx.cfg.descriptorSha256') -and -not (HasFailure $r 'transitionHash')) 'rehashed-existing-descriptor-split-rejected'
$bad=New-ReboundMutation 'lease' {param($body)$body.result.heldFromUtc='2000-01-01T00:00:00Z';$body.result.heldUntilUtc='2000-01-01T00:00:01Z'}
$r=New-AzuriteFullCapabilitiesPlan $contract $cache $runId $bad.Facts $bad.Admission $bad.Evidence $approval $expectedApprovalSha256
Assert (-not $r.Ready -and (HasFailure $r 'receipt.lease.result interval precedes the preapproval baseline') -and -not (HasFailure $r 'transitionHash')) 'rehashed-lease-before-baseline-rejected'
$bad=New-ReboundMutation 'lease' {param($body)$body.result.heldFromUtc='2026-11-10T00:00:05Z';$body.result.heldUntilUtc='2026-11-10T00:00:06Z'}
$r=New-AzuriteFullCapabilitiesPlan $contract $cache $runId $bad.Facts $bad.Admission $bad.Evidence $approval $expectedApprovalSha256
Assert (-not $r.Ready -and (HasFailure $r 'receipt.lease.result.heldFromUtc precedes positive approval issuedUtc') -and -not (HasFailure $r 'transitionHash')) 'rehashed-lease-before-approval-rejected'
$bad=New-ReboundMutation 'lease' {param($body)$body.result.heldUntilUtc='2026-11-12T00:00:00Z'}
$r=New-AzuriteFullCapabilitiesPlan $contract $cache $runId $bad.Facts $bad.Admission $bad.Evidence $approval $expectedApprovalSha256
Assert (-not $r.Ready -and (HasFailure $r 'receipt.lease.result.heldUntilUtc exceeds approval validUntilUtc') -and -not (HasFailure $r 'transitionHash')) 'rehashed-lease-after-expiry-rejected'
$badPostPrep=Clone $facts;$badPostPrep.transition.seedSha256=('e'*64);$r=New-AzuriteFullCapabilitiesPlan -Contract $contract -CachePrerequisites $cache -RunId $runId -Facts $badPostPrep;Assert (-not $r.PreparationReady -and (HasFailure $r 'Facts.transition.seedSha256')) 'preparation-rejects-supplied-wrong-posttransition-seed'
$badTime=Clone $facts;$badTime.preTransition.observedUtc='2026-99-99T00:00:00Z';$r=New-AzuriteFullCapabilitiesPlan -Contract $contract -CachePrerequisites $cache -RunId $runId -Facts $badTime;Assert (-not $r.PreparationReady -and (HasFailure $r 'Facts.preTransition.observedUtc')) 'preparation-rejects-impossible-utc'

$wrong = Clone $facts; $wrong.operation='schema'; $r=New-AzuriteFullCapabilitiesPlan $contract $cache $runId $wrong $admission $evidence $approval $expectedApprovalSha256; Assert (-not $r.Ready -and (HasFailure $r 'Facts.operation')) 'wrong-operation-specific'
$wrong = Clone $facts; $wrong.labRoot='F:\other'; $r=New-AzuriteFullCapabilitiesPlan $contract $cache $runId $wrong $admission $evidence $approval $expectedApprovalSha256; Assert (-not $r.Ready -and (HasFailure $r 'Facts.labRoot')) 'wrong-target-specific'
$wrong = Clone $facts; $wrong.profile='Default'; $r=New-AzuriteFullCapabilitiesPlan $contract $cache $runId $wrong $admission $evidence $approval $expectedApprovalSha256; Assert (-not $r.Ready -and (HasFailure $r 'Facts.profile')) 'wrong-profile-specific'
$wrong = Clone $contract; $wrong.mutableLeaves[0].path='BepInEx/config/../LogOutput.log'; $r=New-AzuriteFullCapabilitiesPlan $wrong $cache $runId $facts $admission $evidence $approval $expectedApprovalSha256; Assert (-not $r.Ready -and (HasFailure $r 'Contract differs')) 'path-traversal-specific'
$wrong = Clone $contract; $wrong.mutableLeaves[0].path='bepinex/config/BepInEx.cfg'; $r=New-AzuriteFullCapabilitiesPlan $wrong $cache $runId $facts $admission $evidence $approval $expectedApprovalSha256; Assert (-not $r.Ready -and (HasFailure $r 'Contract differs')) 'case-ambiguous-path-specific'
$wrong = Clone $facts; $wrong.schema=2; $r=New-AzuriteFullCapabilitiesPlan $contract $cache $runId $wrong $admission $evidence $approval $expectedApprovalSha256; Assert (-not $r.Ready -and (HasFailure $r 'Facts.schema')) 'wrong-facts-schema-specific'
$wrong = Clone $evidence; $wrong.receipts[4].content = $wrong.receipts[4].content + 'x'; $r=New-AzuriteFullCapabilitiesPlan $contract $cache $runId $facts $admission $wrong $approval $expectedApprovalSha256; Assert (-not $r.Ready -and (HasFailure $r 'receipt.cache-create')) 'receipt-content-tamper-specific'
$wrong = Clone $facts; $wrong.transition.cacheLeafSha256=('e'*64); $r=New-AzuriteFullCapabilitiesPlan $contract $cache $runId $wrong $admission $evidence $approval $expectedApprovalSha256; Assert (-not $r.Ready -and (HasFailure $r 'cacheLeafSha256')) 'wrong-cache-digest-specific'
$wrong = Clone $approval; $wrong.content=$wrong.content.Replace('approved-one-capabilities-run','denied-one-capabilities-run'); $r=New-AzuriteFullCapabilitiesPlan $contract $cache $runId $facts $admission $evidence $wrong $expectedApprovalSha256; Assert (-not $r.Ready -and (HasFailure $r 'Approval bytes')) 'negative-approval-specific'
$wrongExpected=HashText 'unrelated-reviewed-approval';$r=New-AzuriteFullCapabilitiesPlan $contract $cache $runId $facts $admission $evidence $approval $wrongExpected;Assert (-not $r.Ready -and (HasFailure $r 'Approval bytes')) 'external-approval-digest-mismatch-specific'
$wrong = Clone $facts; $wrong.ownerSid='S-1-5-21-1-2-3-1001';$wrong.controllerSid=$wrong.ownerSid; $r=New-AzuriteFullCapabilitiesPlan $contract $cache $runId $wrong $admission $evidence $approval $expectedApprovalSha256; Assert (-not $r.Ready -and (HasFailure $r 'Facts.ownerSid')) 'cross-owner-specific'
$wrong = Clone $facts; $wrong.transition.receiptSha256=('a'*64); $r=New-AzuriteFullCapabilitiesPlan $contract $cache $runId $wrong $admission $evidence $approval $expectedApprovalSha256; Assert (-not $r.Ready -and (HasFailure $r 'transitionHash')) 'transition-hash-crossbinding'
$wrong = Clone $facts; $wrong.transition=[pscustomobject]@{unknown=$true};$r=New-AzuriteFullCapabilitiesPlan $contract $cache $runId $wrong $admission $evidence $approval $expectedApprovalSha256;Assert (-not $r.Ready -and (HasFailure $r 'Facts.transition.unknown')) 'unknown-transition-specific'
$wrong = [pscustomobject]@{schema=1};$r=Assert-AzuriteFullCapabilitiesAdmission -Contract $contract -Plan $complete -Admission $admission -Facts $wrong -EvidenceBundle $evidence -Approval $approval -ExpectedApprovalSha256 $expectedApprovalSha256;Assert (-not $r.Valid -and (HasFailure $r 'Facts.runId')) 'direct-malformed-facts-structured'
foreach ($invalid in @('00000000000000000000000000000000','AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA','..\outside')) { $wrong=Clone $complete;$wrong.RunId=$invalid;$r=Assert-AzuriteFullCapabilitiesAdmission -Contract $contract -Plan $wrong -Admission $admission -Facts $facts -EvidenceBundle $evidence -Approval $approval -ExpectedApprovalSha256 $expectedApprovalSha256;Assert (-not $r.Valid -and (HasFailure $r 'Plan.RunId')) ('direct-invalid-run-'+$invalid.Substring(0,1)) }
$wrong=Clone $complete;$wrong.Failures=@('previous failure');$r=Assert-AzuriteFullCapabilitiesAdmission -Contract $contract -Plan $wrong -Admission $admission -Facts $facts -EvidenceBundle $evidence -Approval $approval -ExpectedApprovalSha256 $expectedApprovalSha256;Assert (-not $r.Valid -and (HasFailure $r 'Plan failure flags')) 'failure-plan-rejected'
$wrong=Clone $facts;$wrong.transition=$null;$r=Assert-AzuriteFullCapabilitiesAdmission -Contract $contract -Plan $plan -Admission $admission -Facts $wrong -EvidenceBundle $evidence -Approval $approval -ExpectedApprovalSha256 $expectedApprovalSha256;Assert (-not $r.Valid -and (HasFailure $r 'posttransition evidence is missing')) 'absent-posttransition-rejected'
$wrong=Clone $facts;$wrong.cache.stamp='wrong';$r=New-AzuriteFullCapabilitiesPlan $contract $cache $runId $wrong $admission $evidence $approval $expectedApprovalSha256;Assert (-not $r.Ready -and (HasFailure $r 'Facts.cache.stamp')) 'warm-stamp-specific'
$wrong=Clone $facts;$wrong.environment.gameAssemblyOverride='F:\override';$r=New-AzuriteFullCapabilitiesPlan $contract $cache $runId $wrong $admission $evidence $approval $expectedApprovalSha256;Assert (-not $r.Ready -and (HasFailure $r 'Facts.environment.gameAssemblyOverride')) 'ambient-override-specific'
$wrong=Clone $facts;$wrong.volumeGuid='\\?\Volume{00000000-0000-0000-0000-000000000000}\';$r=New-AzuriteFullCapabilitiesPlan $contract $cache $runId $wrong $admission $evidence $approval $expectedApprovalSha256;Assert (-not $r.Ready -and (HasFailure $r 'Facts.volumeGuid')) 'wrong-volume-specific'
$wrong=Clone $facts;$wrong.stage.runtimeAllowed=$true;$r=New-AzuriteFullCapabilitiesPlan $contract $cache $runId $wrong $admission $evidence $approval $expectedApprovalSha256;Assert (-not $r.Ready -and (HasFailure $r 'Facts.stage.runtimeAllowed')) 'wrong-phase0-specific'
$wrong=Clone $facts;$wrong.cache=$null;$r=Assert-AzuriteFullCapabilitiesAdmission -Contract $contract -Plan $complete -Admission $admission -Facts $wrong -EvidenceBundle $evidence -Approval $approval -ExpectedApprovalSha256 $expectedApprovalSha256;Assert (-not $r.Valid -and (HasFailure $r 'Facts.cache')) 'direct-null-cache-structured'
$wrong=Clone $admission;$wrong.phase0=$null;$r=Assert-AzuriteFullCapabilitiesAdmission -Contract $contract -Plan $complete -Admission $wrong -Facts $facts -EvidenceBundle $evidence -Approval $approval -ExpectedApprovalSha256 $expectedApprovalSha256;Assert (-not $r.Valid -and (HasFailure $r 'Admission.phase0')) 'direct-null-phase0-structured'
$wrong=Clone $approvalContent;$wrong.decision='denied-one-capabilities-run';$wrongText=$wrong|ConvertTo-Json -Depth 30 -Compress;$wrongApproval=[pscustomobject]@{bytes=[Text.UTF8Encoding]::new($false).GetByteCount($wrongText);content=$wrongText};$wrongExpected=HashText $wrongText;$r=New-AzuriteFullCapabilitiesPlan $contract $cache $runId $facts $admission $evidence $wrongApproval $wrongExpected;Assert (-not $r.Ready -and (HasFailure $r 'Approval.content.decision')) 'actual-negative-approval-rejected-even-with-own-digest'
$wrong=Clone $facts;$wrong.transition.leaseId='not-an-identity';$r=New-AzuriteFullCapabilitiesPlan $contract $cache $runId $wrong $admission $evidence $approval $expectedApprovalSha256;Assert (-not $r.Ready -and (HasFailure $r 'Facts.transition.leaseId')) 'malformed-lease-identity-specific'
$wrong=Clone $evidence;$wrong.receipts[2].content=($wrong.receipts[2].content|ConvertFrom-Json|ForEach-Object {$_.result.descriptors[0].path='F:\wrong';$_}|ConvertTo-Json -Depth 50 -Compress);$wrong.receipts[2].bytes=[Text.UTF8Encoding]::new($false).GetByteCount($wrong.receipts[2].content);$wrong.receipts[2].sha256=HashText $wrong.receipts[2].content;$wrongFacts=Clone $facts;$wrongFacts.transition.aclBeforeImageReceiptSha256=$wrong.receipts[2].sha256;$wrongAdmission=Clone $admission;$wrongAdmission.transition=$wrongFacts.transition;$wrongAdmission.evidenceBundleSha256=HashText ($wrong|ConvertTo-Json -Depth 80 -Compress);$r=New-AzuriteFullCapabilitiesPlan $contract $cache $runId $wrongFacts $wrongAdmission $wrong $approval $expectedApprovalSha256;Assert (-not $r.Ready -and (HasFailure $r 'receipt.acl-before-image.result.descriptors[0].path')) 'acl-inventory-wrong-path-specific'
$wrong=Clone $evidence;$body=$wrong.receipts[0].content|ConvertFrom-Json;$body.result.state='failed';$wrong.receipts[0].content=$body|ConvertTo-Json -Depth 30 -Compress;$wrong.receipts[0].bytes=[Text.UTF8Encoding]::new($false).GetByteCount($wrong.receipts[0].content);$wrong.receipts[0].sha256=HashText $wrong.receipts[0].content;$wrongAdmission=Clone $admission;$wrongAdmission.evidenceBundleSha256=HashText ($wrong|ConvertTo-Json -Depth 80 -Compress);$r=New-AzuriteFullCapabilitiesPlan $contract $cache $runId $facts $wrongAdmission $wrong $approval $expectedApprovalSha256;Assert (-not $r.Ready -and (HasFailure $r 'receipt.lease.result.state')) 'per-kind-failed-result-rejected'
$releaseEvidence=Clone $evidence;$releaseFacts=Clone $facts;$releaseAdmission=Clone $admission;$lease=$releaseEvidence.receipts[0];$leaseBody=$lease.content|ConvertFrom-Json;$leaseBody.result.releaseOutcome='release-failed';$lease.content=$leaseBody|ConvertTo-Json -Depth 40 -Compress;$lease.bytes=[Text.UTF8Encoding]::new($false).GetByteCount($lease.content);$lease.sha256=HashText $lease.content;$releaseFacts.transition.leaseReceiptSha256=$lease.sha256
$parts=@(@('lease','before-image','acl-before-image','grant','cache-create','seal-restoration','unchanged-inputs','warm-input-order','unity-log-precreation','response-policy','review')|ForEach-Object { $_+'|'+$releaseFacts.transition.($map[$_]) });$transition=$releaseEvidence.receipts[11];$transitionBody=$transition.content|ConvertFrom-Json;$transitionBody.result.receiptSetSha256=HashText (($parts-join [char]10)+[char]10);$transition.content=$transitionBody|ConvertTo-Json -Depth 40 -Compress;$transition.bytes=[Text.UTF8Encoding]::new($false).GetByteCount($transition.content);$transition.sha256=HashText $transition.content;$releaseFacts.transition.receiptSha256=$transition.sha256;$releaseAdmission.transition=$releaseFacts.transition;$releaseAdmission.evidenceBundleSha256=HashText ($releaseEvidence|ConvertTo-Json -Depth 80 -Compress)
$r=New-AzuriteFullCapabilitiesPlan $contract $cache $runId $releaseFacts $releaseAdmission $releaseEvidence $approval $expectedApprovalSha256;Assert (-not $r.Ready -and (HasFailure $r 'receipt.lease.result.releaseOutcome')) 'failed-preparation-lease-release-rejected'

$allFalseEvidence=Clone $evidence;$allFalseFacts=Clone $facts;$allFalseAdmission=Clone $admission
for($i=0;$i -lt $allFalseEvidence.receipts.Count;$i++){$receipt=$allFalseEvidence.receipts[$i];$body=$receipt.content|ConvertFrom-Json;$body.result=$false;$receipt.content=$body|ConvertTo-Json -Depth 40 -Compress;$receipt.bytes=[Text.UTF8Encoding]::new($false).GetByteCount($receipt.content);$receipt.sha256=HashText $receipt.content;$allFalseFacts.transition.($map[$receipt.kind])=$receipt.sha256}
$allFalseAdmission.transition=$allFalseFacts.transition;$allFalseAdmission.evidenceBundleSha256=HashText ($allFalseEvidence|ConvertTo-Json -Depth 80 -Compress)
$r=New-AzuriteFullCapabilitiesPlan $contract $cache $runId $allFalseFacts $allFalseAdmission $allFalseEvidence $approval $expectedApprovalSha256
Assert (-not $r.Ready -and (HasFailure $r 'receipt.lease.result must be a JSON object')) 'all-false-receipts-rejected-by-results'

foreach($kind in $map.Keys){
    $badEvidence=Clone $evidence;$badFacts=Clone $facts;$badAdmission=Clone $admission
    $receipt=@($badEvidence.receipts|Where-Object { $_.kind -eq $kind })[0]
    $body=$receipt.content|ConvertFrom-Json;$body.result.state='contradictory';$receipt.content=$body|ConvertTo-Json -Depth 40 -Compress;$receipt.bytes=[Text.UTF8Encoding]::new($false).GetByteCount($receipt.content);$receipt.sha256=HashText $receipt.content;$badFacts.transition.($map[$kind])=$receipt.sha256
    if($kind -ne 'transition'){
        $parts=@(@('lease','before-image','acl-before-image','grant','cache-create','seal-restoration','unchanged-inputs','warm-input-order','unity-log-precreation','response-policy','review')|ForEach-Object { $_+'|'+$badFacts.transition.($map[$_]) })
        $last=@($badEvidence.receipts|Where-Object { $_.kind -eq 'transition' })[0];$lastBody=$last.content|ConvertFrom-Json;$lastBody.result.receiptSetSha256=HashText (($parts-join "`n")+"`n");$last.content=$lastBody|ConvertTo-Json -Depth 40 -Compress;$last.bytes=[Text.UTF8Encoding]::new($false).GetByteCount($last.content);$last.sha256=HashText $last.content;$badFacts.transition.receiptSha256=$last.sha256
    }
    $badAdmission.transition=$badFacts.transition;$badAdmission.evidenceBundleSha256=HashText ($badEvidence|ConvertTo-Json -Depth 80 -Compress)
    $r=New-AzuriteFullCapabilitiesPlan $contract $cache $runId $badFacts $badAdmission $badEvidence $approval $expectedApprovalSha256
    Assert (-not $r.Ready -and (HasFailure $r ("receipt.$kind.result.state"))) ('contradictory-'+$kind)
}

$replayedFacts=Clone $facts;$replayedFacts.runId='55555555555555555555555555555555';$replayedFacts.preTransition.runId=$replayedFacts.runId;$replayedFacts.transition.runId=$replayedFacts.runId
$replayedPlan=New-AzuriteFullCapabilitiesPlan $contract $cache $replayedFacts.runId $replayedFacts
$replayedApprovalContent=Clone $approvalContent;$replayedApprovalContent.runId=$replayedFacts.runId;$replayedApprovalContent.planSha256=$replayedPlan.PlanSha256;$replayedApprovalText=$replayedApprovalContent|ConvertTo-Json -Depth 30 -Compress;$replayedExpected=HashText $replayedApprovalText;$replayedApproval=[pscustomobject]@{bytes=[Text.UTF8Encoding]::new($false).GetByteCount($replayedApprovalText);content=$replayedApprovalText};$replayedFacts.transition.reviewApprovalSha256=$replayedExpected
$replayedEvidence=Clone $evidence;$replayedEvidence.runId=$replayedFacts.runId;$replayedEvidence.planSha256=$replayedPlan.PlanSha256
$replayedAdmission=Clone $admission;$replayedAdmission.runId=$replayedFacts.runId;$replayedAdmission.planSha256=$replayedPlan.PlanSha256;$replayedAdmission.transition=$replayedFacts.transition;$replayedAdmission.approvalSha256=$replayedExpected;$replayedAdmission.evidenceBundleSha256=HashText ($replayedEvidence|ConvertTo-Json -Depth 80 -Compress)
$r=New-AzuriteFullCapabilitiesPlan $contract $cache $replayedFacts.runId $replayedFacts $replayedAdmission $replayedEvidence $replayedApproval $replayedExpected
Assert (-not $r.Ready -and (HasFailure $r 'receipt.lease.runId')) 'old-receipts-consistent-new-run-rejected'

$wrong=Clone $facts;$wrong.inputs=$null;$r=Assert-AzuriteFullCapabilitiesAdmission -Contract $contract -Plan $complete -Admission $admission -Facts $wrong -EvidenceBundle $evidence -Approval $approval -ExpectedApprovalSha256 $expectedApprovalSha256;Assert (-not $r.Valid -and (HasFailure $r 'Facts.inputs')) 'direct-null-nested-facts-structured'
$wrong=Clone $complete;$wrong.AdmissionProof=[pscustomobject]@{runId='wrong'};$r=Assert-AzuriteFullCapabilitiesAdmission -Contract $contract -Plan $wrong -Admission $admission -Facts $facts -EvidenceBundle $evidence -Approval $approval -ExpectedApprovalSha256 $expectedApprovalSha256;Assert (-not $r.Valid -and (HasFailure $r 'Plan.AdmissionProof')) 'direct-contradictory-proof-rejected'
$wrong=Clone $evidence;$wrong.receipts[4].content=($wrong.receipts[4].content|ConvertFrom-Json|ForEach-Object {$_.result.seedBytes=0;$_}|ConvertTo-Json -Depth 40 -Compress);$wrong.receipts[4].bytes=[Text.UTF8Encoding]::new($false).GetByteCount($wrong.receipts[4].content);$wrong.receipts[4].sha256=HashText $wrong.receipts[4].content;$wrongFacts=Clone $facts;$wrongFacts.transition.cacheLeafCreateReceiptSha256=$wrong.receipts[4].sha256;$wrongAdmission=Clone $admission;$wrongAdmission.transition=$wrongFacts.transition;$wrongAdmission.evidenceBundleSha256=HashText ($wrong|ConvertTo-Json -Depth 80 -Compress);$r=New-AzuriteFullCapabilitiesPlan $contract $cache $runId $wrongFacts $wrongAdmission $wrong $approval $expectedApprovalSha256;Assert (-not $r.Ready -and (HasFailure $r 'receipt.cache-create.result.seedBytes')) 'wrong-cache-seed-length-specific'

$wrapper=Join-Path $PSScriptRoot '..\..\tools\headless\New-AzuriteFullCapabilitiesPlan.ps1'
$wrapped=& $wrapper -RunId $runId -Facts $facts -Admission $admission -EvidenceBundle $evidence -Approval $approval -ExpectedApprovalSha256 $expectedApprovalSha256
Assert ($wrapped.Ready -and -not $wrapped.RuntimeAllowed -and -not $wrapped.WillLaunch) 'public-wrapper-forward-proof-dry'
$fixtureRoot=Join-Path $PSScriptRoot '..\..\artifacts\aa-fr-planner-preparation\fix4\fixtures'
[void](New-Item -ItemType Directory -Force -Path $fixtureRoot)
$bundleJson=$evidence|ConvertTo-Json -Depth 80 -Compress;$bundleBytes=[Text.UTF8Encoding]::new($false).GetByteCount($bundleJson)
$approvalJson=$approval|ConvertTo-Json -Depth 30 -Compress
$hostTag=if($PSVersionTable.PSVersion.Major -le 5){'ps51'}else{'ps7'}
$bundleDigest=HashText $bundleJson
$bundlePath=Join-Path $fixtureRoot ('bundle-'+$plan.PlanSha256+'-'+$bundleDigest+'-'+$hostTag+'.json');$approvalPath=Join-Path $fixtureRoot ('approval-'+$expectedApprovalSha256+'-'+$hostTag+'.json')
foreach($record in @(@($bundlePath,$bundleJson),@($approvalPath,$approvalJson))){$path=$record[0];$value=$record[1];if(Test-Path -LiteralPath $path){if([IO.File]::ReadAllText($path)-cne $value){throw ('Fixture bytes changed: '+$path)}}else{[IO.File]::WriteAllText($path,$value,[Text.UTF8Encoding]::new($false))}}
$wrapped=& $wrapper -RunId $runId -Facts $facts -Admission $admission -EvidenceBundlePath $bundlePath -ApprovalPath $approvalPath -ExpectedApprovalSha256 $expectedApprovalSha256
Assert ($wrapped.Ready -and $bundleBytes -gt 100000 -and $bundleBytes -lt 4194304) 'public-wrapper-serialized-1221-descriptor-bundle'
$otherTag=if($hostTag -eq 'ps51'){'ps7'}else{'ps51'}
$otherBundlePath=Join-Path $fixtureRoot ('bundle-'+$plan.PlanSha256+'-'+$bundleDigest+'-'+$otherTag+'.json')
$otherApprovalPath=Join-Path $fixtureRoot ('approval-'+$expectedApprovalSha256+'-'+$otherTag+'.json')
if((Test-Path -LiteralPath $otherBundlePath) -and (Test-Path -LiteralPath $otherApprovalPath)){
    Assert ([IO.File]::ReadAllText($otherBundlePath) -ceq $bundleJson -and [IO.File]::ReadAllText($otherApprovalPath) -ceq $approvalJson) 'crosshost-serialized-bytes-identical'
    $crosshost=& $wrapper -RunId $runId -Facts $facts -Admission $admission -EvidenceBundlePath $otherBundlePath -ApprovalPath $otherApprovalPath -ExpectedApprovalSha256 $expectedApprovalSha256
    Assert ($crosshost.Ready -and -not $crosshost.RuntimeAllowed) 'crosshost-wrapper-read-valid'
}else{Write-Output ('CROSSHOST_PENDING '+$otherTag)}
$tooLarge=Join-Path $fixtureRoot 'oversize-bundle-control.bin';if(-not(Test-Path -LiteralPath $tooLarge)){ $s=[IO.File]::Open($tooLarge,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None);try{$s.SetLength(4194305)}finally{$s.Dispose()} }
$oversizeRejected=$false;try{& $wrapper -RunId $runId -Facts $facts -Admission $admission -EvidenceBundlePath $tooLarge -Approval $approval -ExpectedApprovalSha256 $expectedApprovalSha256|Out-Null}catch{$oversizeRejected=$_.Exception.Message -like '*oversized*'}
Assert $oversizeRejected 'public-wrapper-oversize-bundle-rejected-before-read'
$wrapperRejected=$false;try{& $wrapper -RunId $runId -Facts $facts -Admission $admission -EvidenceBundle $evidence -Approval $approval|Out-Null}catch{$wrapperRejected=$_.Exception.Message -like '*independently trusted ExpectedApprovalSha256*'}
Assert $wrapperRejected 'public-wrapper-requires-external-approval-digest'

Write-Output ('BUNDLE_UTF8_BYTES '+$bundleBytes)
Write-Output ("RESULT passed={0} failed=0" -f $script:passed)
