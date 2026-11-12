Set-StrictMode -Version Latest

# This module reads frozen C: evidence and constructs data. It has no lab, ACL, token,
# process, registry, or child-environment operations.
$script:ContractSha256 = '319a0e0a4b2b603f7aaf651250fa7553620bb918b513700e38b028a31f79f7cd'
$script:FrozenOwnerSid = 'S-1-5-21-222280291-4039000738-1914192245-1001'
$script:RepoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$script:ContractRelative = 'tools/headless/full-restricted-capabilities-contract.json'
$script:CacheRelative = 'artifacts/aa-fr-cache-prerequisites/cache-prerequisites-contract.json'
$script:StageRelative = 'artifacts/editor-normal-install-preparation/source-bound-manifest-v2.json'

function Get-PlanHash([string]$Text) {
    $sha = [Security.Cryptography.SHA256]::Create()
    try {
        $bytes = [Text.UTF8Encoding]::new($false).GetBytes($Text)
        return ([BitConverter]::ToString($sha.ComputeHash($bytes))).Replace('-', '').ToLowerInvariant()
    } finally { $sha.Dispose() }
}

function ConvertFrom-PlanJson([string]$Text) {
    if ((Get-Command ConvertFrom-Json).Parameters.ContainsKey('DateKind')) {
        return ($Text | ConvertFrom-Json -DateKind String -ErrorAction Stop)
    }
    return ($Text | ConvertFrom-Json -ErrorAction Stop)
}

function Get-PlanUtcTicks($Value) {
    if ($Value -is [DateTimeOffset]) { return [long]$Value.UtcDateTime.Ticks }
    if ($Value -is [datetime]) {
        if ($Value.Kind -eq [DateTimeKind]::Unspecified) { throw 'UTC observation has unspecified DateTime Kind.' }
        return [long]$Value.ToUniversalTime().Ticks
    }
    if ($Value -isnot [string] -or $Value -cnotmatch '^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d{1,7})?Z$') { throw 'UTC observation needs explicit ISO Z time.' }
    return [long][DateTimeOffset]::Parse($Value,[Globalization.CultureInfo]::InvariantCulture).UtcDateTime.Ticks
}

function Read-FrozenJson([string]$Relative, [string]$ExpectedSha256) {
    if ($Relative -cnotmatch '^[A-Za-z0-9_.-]+(/[A-Za-z0-9_.-]+)*$' -or $Relative -match '(^|/)\.\.?(/|$)') {
        throw "Invalid frozen evidence path: $Relative"
    }
    $path = [IO.Path]::GetFullPath((Join-Path $script:RepoRoot $Relative))
    if (-not $path.StartsWith($script:RepoRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or
        [IO.Path]::GetPathRoot($path) -cne [IO.Path]::GetPathRoot($script:RepoRoot)) {
        throw "Frozen evidence path escaped the C: repository: $Relative"
    }
    $item = Get-Item -LiteralPath $path -Force -ErrorAction Stop
    if ($item.PSIsContainer -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 -or $item.Length -lt 2 -or $item.Length -gt 1048576) {
        throw "Frozen evidence is linked, empty, or oversized: $Relative"
    }
    $bytes = [IO.File]::ReadAllBytes($path)
    $sha = [Security.Cryptography.SHA256]::Create()
    try { $actual = ([BitConverter]::ToString($sha.ComputeHash($bytes))).Replace('-', '').ToLowerInvariant() }
    finally { $sha.Dispose() }
    if ($actual -cne $ExpectedSha256) { throw "Frozen evidence SHA-256 differs: $Relative" }
    if ($Relative -ceq $script:ContractRelative -and ($bytes[0] -eq 239 -or [Array]::IndexOf($bytes, [byte]13) -ge 0)) {
        throw 'Capabilities contract must be BOM-free UTF-8 with LF line endings.'
    }
    $utf8 = [Text.UTF8Encoding]::new($false, $true)
    try { return (ConvertFrom-PlanJson ($utf8.GetString($bytes))) }
    catch { throw "Frozen evidence JSON is invalid: $Relative" }
}

function Assert-FrozenFile([string]$Relative, [string]$ExpectedSha256) {
    if ($Relative -cnotmatch '^[A-Za-z0-9_.-]+(/[A-Za-z0-9_.-]+)*$' -and $Relative -cnotmatch '^\.superpowers/[A-Za-z0-9_.-]+(/[A-Za-z0-9_.-]+)*$') { throw "Invalid frozen evidence path: $Relative" }
    $path = [IO.Path]::GetFullPath((Join-Path $script:RepoRoot $Relative))
    if (-not $path.StartsWith($script:RepoRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw "Frozen evidence path escaped the C: repository: $Relative" }
    $item = Get-Item -LiteralPath $path -Force -ErrorAction Stop
    if ($item.PSIsContainer -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw "Frozen evidence is linked or a directory: $Relative" }
    $sha = [Security.Cryptography.SHA256]::Create()
    try { $actual = ([BitConverter]::ToString($sha.ComputeHash([IO.File]::ReadAllBytes($path)))).Replace('-', '').ToLowerInvariant() }
    finally { $sha.Dispose() }
    if ($actual -cne $ExpectedSha256) { throw "Frozen evidence SHA-256 differs: $Relative" }
}

function Test-Fields($Value, [string[]]$Names, [string]$Label, [Collections.Generic.List[string]]$Failures) {
    if ($null -eq $Value -or $Value -isnot [pscustomobject]) {
        $Failures.Add("$Label must be a JSON object.")
        return $false
    }
    $actual = @($Value.PSObject.Properties | ForEach-Object { $_.Name })
    foreach ($name in $Names) { if ($actual -cnotcontains $name) { $Failures.Add("$Label.$name is missing.") } }
    foreach ($name in $actual) { if ($Names -cnotcontains $name) { $Failures.Add("$Label.$name is unknown.") } }
    return ($actual.Count -eq $Names.Count -and @($Failures | Where-Object { $_ -like "$Label.*" }).Count -eq 0)
}

function Check-Pin($Actual, $Expected, [string]$Label, [Collections.Generic.List[string]]$Failures) {
    $equal = $false
    if ($null -eq $Expected) { $equal = $null -eq $Actual }
    elseif ($Expected -is [bool]) { $equal = $Actual -is [bool] -and $Actual -eq $Expected }
    elseif ($Expected -is [string]) { $equal = $Actual -is [string] -and $Actual -ceq $Expected }
    elseif ($Expected -is [int] -or $Expected -is [long]) {
        $equal = ($Actual -is [int] -or $Actual -is [long]) -and $Actual -eq $Expected
    }
    if (-not $equal) { $Failures.Add("$Label differs from the frozen binding.") }
}

function Test-Sha([string]$Value, [string]$Label, [Collections.Generic.List[string]]$Failures) {
    if ($Value -cnotmatch '^[0-9a-f]{64}$' -or $Value -ceq ('0' * 64)) { $Failures.Add("$Label must be a nonzero lowercase SHA-256.") }
}

function Test-RunId([string]$Value, [string]$Label, [Collections.Generic.List[string]]$Failures) {
    if ($Value -cnotmatch '^[0-9a-f]{32}$' -or $Value -ceq ('0' * 32)) { $Failures.Add("$Label must be a nonzero lowercase 32-hex identifier."); return $false }
    return $true
}

function Test-CanonicalHash([string]$Text, [string]$Expected, [string]$Label, [Collections.Generic.List[string]]$Failures) {
    $actual = Get-PlanHash $Text
    if ($actual -cne $Expected) { $Failures.Add("$Label canonical content hash differs."); return $false }
    return $true
}

function Test-Approval($Approval, [string]$ExpectedApprovalSha256, $Plan, $Facts, [Collections.Generic.List[string]]$Failures) {
    if ($ExpectedApprovalSha256 -isnot [string] -or $ExpectedApprovalSha256 -cnotmatch '^[0-9a-f]{64}$' -or $ExpectedApprovalSha256 -ceq ('0' * 64)) {
        $Failures.Add('ExpectedApprovalSha256 must come from a separate trusted reviewed input.'); return $null
    }
    if (-not (Test-Fields $Approval @('bytes','content') 'Approval' $Failures)) { return $null }
    if ($Approval.content -isnot [string] -or $Approval.bytes -isnot [int] -and $Approval.bytes -isnot [long] -or $Approval.bytes -lt 2 -or $Approval.bytes -gt 16384) {
        $Failures.Add('Approval content/byte bound is invalid.'); return $null
    }
    if ([Text.UTF8Encoding]::new($false).GetByteCount($Approval.content) -ne $Approval.bytes -or (Get-PlanHash $Approval.content) -cne $ExpectedApprovalSha256) {
        $Failures.Add('Approval bytes differ from separately trusted expected digest.'); return $null
    }
    try { $actual = ConvertFrom-PlanJson $Approval.content }
    catch { $Failures.Add('Approval content is invalid JSON.'); return $null }
    $names = @('schema','decision','scope','runId','planSha256','contractSha256','ownerSid','volumeGuid','labRoot','executable','validFromUtc','issuedUtc','validUntilUtc','toolingManifestSha256','controlGateSha256')
    if (-not (Test-Fields $actual $names 'Approval.content' $Failures)) { return $null }
    if (($actual | ConvertTo-Json -Depth 30 -Compress) -cne $Approval.content) { $Failures.Add('Approval content is not canonical JSON.'); return $null }
    Check-Pin $actual.schema 1 'Approval.content.schema' $Failures
    Check-Pin $actual.decision 'approved-one-capabilities-run' 'Approval.content.decision' $Failures
    Check-Pin $actual.scope 'current-account-full-restricted-capabilities-only' 'Approval.content.scope' $Failures
    Check-Pin $actual.runId $Plan.RunId 'Approval.content.runId' $Failures
    Check-Pin $actual.planSha256 $Plan.PlanSha256 'Approval.content.planSha256' $Failures
    Check-Pin $actual.contractSha256 $script:ContractSha256 'Approval.content.contractSha256' $Failures
    Check-Pin $actual.ownerSid $script:FrozenOwnerSid 'Approval.content.ownerSid' $Failures
    Check-Pin $actual.volumeGuid $Facts.volumeGuid 'Approval.content.volumeGuid' $Failures
    Check-Pin $actual.labRoot $Plan.LabRoot 'Approval.content.labRoot' $Failures
    Check-Pin $actual.executable $Plan.Executable 'Approval.content.executable' $Failures
    Test-Sha $actual.toolingManifestSha256 'Approval.content.toolingManifestSha256' $Failures
    Test-Sha $actual.controlGateSha256 'Approval.content.controlGateSha256' $Failures
    $issued=$observed=$from=$until=$null
    try {
        foreach ($time in @($actual.validFromUtc,$actual.issuedUtc,$actual.validUntilUtc)) { if ($time -isnot [string] -or $time -cnotmatch '^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}Z$') { throw 'Noncanonical approval UTC time.' } }
        $from = Get-PlanUtcTicks $actual.validFromUtc
        $issued = Get-PlanUtcTicks $actual.issuedUtc
        $until = Get-PlanUtcTicks $actual.validUntilUtc
        $observed = Get-PlanUtcTicks $Facts.preTransition.observedUtc
        if ($from -ge $until -or $observed -lt $from -or $observed -gt $until) { throw 'Outside approval interval.' }
    } catch { $Failures.Add('Approval declared validity does not cover the explicit pretransition observation.') }
    if ($null -ne $issued -and $null -ne $observed -and ($issued -lt $observed -or $issued -lt $from -or $issued -gt $until)) { $Failures.Add('Approval.content.issuedUtc is outside the preapproval baseline and validity sequence.') }
    return $actual
}

function Test-ReceiptResult([string]$Kind, $Result, $Plan, $Facts, [string]$ApprovalSha256, $ApprovalObject, $ReceiptHashes, [Collections.Generic.List[string]]$Failures) {
    $t = $Facts.transition
    $pathDigest = Get-PlanHash ((@($Plan.MutableLeaves | ForEach-Object { $_.path }) -join "`n") + "`n")
    $grantDigest = Get-GrantDigest $Plan.Grants
    $heldFrom = if ($Result -is [pscustomobject] -and $Result.PSObject.Properties['heldFromUtc']) { $Result.heldFromUtc } else { $null }
    $heldUntil = if ($Result -is [pscustomobject] -and $Result.PSObject.Properties['heldUntilUtc']) { $Result.heldUntilUtc } else { $null }
    $spec = switch ($Kind) {
        'lease' { [ordered]@{state='released-after-preparation';leaseId=$t.leaseId;runId=$Plan.RunId;ownerSid=$script:FrozenOwnerSid;volumeGuid=$Facts.volumeGuid;scope='capabilities-only-preparation';globalMutex='Global\AzuriteRestrictedCli-AA';heldFromUtc=$heldFrom;heldUntilUtc=$heldUntil;releaseOutcome='success';pendingRunId=$Plan.RunId}; break }
        'before-image' { [ordered]@{state='captured';inventorySha256=$t.beforeImageInventorySha256;leafCount=6;mutablePathsSha256=$pathDigest;ownerSid=$script:FrozenOwnerSid;newCacheLeafAbsent=$true;descriptorEncoding='Utf8NoBomFullOwnerGroupAccessSddl'}; break }
        'acl-before-image' { [ordered]@{state='captured';inventorySha256=$t.aclBeforeImageInventorySha256;descriptorCount=1221;cacheParent='BepInEx/cache';cacheParentDescriptorSha256=$t.cacheParentBeforeDescriptorSha256;ownerSid=$script:FrozenOwnerSid;descriptorEncoding='Utf8NoBomFullOwnerGroupAccessSddl'}; break }
        'grant' { [ordered]@{state='applied';grantsSha256=$grantDigest;principalSid=$Facts.acl.actualLogonSid;namedMutableMask='0x12019F';requestParentMask='0x1200AB';responseChildMask='0x13019F';argumentsMask='0x120089';sharedDelta='None'}; break }
        'cache-create' { [ordered]@{state='created-new';leaf=$Facts.cache.modTheLeaf;absentBefore=$true;createMode='CreateNewNoOverwrite';seedBytes=4;seedSha256=$Plan.SealTransitions[0].seedSha256;resultSha256=$t.cacheLeafSha256;fileIdentity=$t.cacheLeafFileIdentity;parent='BepInEx/cache'}; break }
        'seal-restoration' { [ordered]@{state='restored';parent='BepInEx/cache';creationDenyMask='0x6';policySha256=$Facts.stage.sealPolicySha256;beforeDescriptorSha256=$t.cacheParentBeforeDescriptorSha256;restoredDescriptorSha256=$t.cacheParentRestoredDescriptorSha256;otherSealsUnchanged=$true}; break }
        'unchanged-inputs' { [ordered]@{state='matched';manifestSha256=$Facts.stage.manifestSha256;immutablePinDigest=$Plan.ReadEnvelope.immutableFilePinDigest;immutableFileCount=1058;directoryPinDigest=$Plan.ReadEnvelope.directoryPinDigest;directoryCount=156}; break }
        'warm-input-order' { [ordered]@{state='matched';computedStamp=$Facts.cache.stamp;recordedProofSha256=$Facts.inputs.warmResultSha256;eventCount=147;inputFileCount=73;unityDllCount=72;generatorVersion='1.5.3.0';cpp2ilVersion='2022.1.0.0';optionalRenameMapPresent=$false;gameAssemblyOverridePresent=$false;unhollowedPathArgumentPresent=$false}; break }
        'unity-log-precreation' { [ordered]@{state='precreated-empty';path='output/unity.log';baselineBytes=0;fileIdentity=$t.unityLogFileIdentity;fileMask='0x12019F';ownerSid=$script:FrozenOwnerSid}; break }
        'response-policy' { [ordered]@{state='prepared';parent='request';parentMask='0x1200AB';childFileMask='0x13019F';argumentsFile='arguments.json';argumentsMask='0x120089';argumentsProtected=$true;createSubdirectory=$false;deleteChild=$false;expectedOutput='response.json';responseMaxBytes=1048576;maxTemporaryFiles=1;postrunValidationRequired=$true;unexpectedOutput='Reject'}; break }
        'review' { [ordered]@{state='positive-approval-bound';approvalSha256=$ApprovalSha256;runId=$Plan.RunId;planSha256=$Plan.PlanSha256;ownerSid=$script:FrozenOwnerSid;volumeGuid=$Facts.volumeGuid}; break }
        'transition' {
            $orderedKinds = @('lease','before-image','acl-before-image','grant','cache-create','seal-restoration','unchanged-inputs','warm-input-order','unity-log-precreation','response-policy','review')
            $parts = @($orderedKinds | ForEach-Object { $_ + '|' + $ReceiptHashes[$_] })
            [ordered]@{state='complete-for-capabilities-only';phase0CompletionSha256=$Facts.stage.completionSha256;receiptSetSha256=(Get-PlanHash (($parts -join "`n") + "`n"));cacheLeafSha256=$Facts.cache.seedSha256;sealRestored=$true;sharedDelta='None'}; break
        }
        default { $Failures.Add("receipt.$Kind has no result policy."); return }
    }
    $names = @($spec.Keys)
    if ($Kind -eq 'before-image') { $names += 'items' }
    if ($Kind -eq 'acl-before-image') { $names += 'descriptors' }
    if (-not (Test-Fields $Result $names "receipt.$Kind.result" $Failures)) { return }
    foreach ($name in $spec.Keys) { Check-Pin $Result.$name $spec[$name] "receipt.$Kind.result.$name" $Failures }
    if ($Kind -eq 'lease') {
        try {
            foreach ($name in @('heldFromUtc','heldUntilUtc')) { if ($Result.$name -isnot [string] -or $Result.$name -cnotmatch '^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}Z$') { throw 'Noncanonical UTC.' } }
            $start=[DateTimeOffset]::Parse($Result.heldFromUtc,[Globalization.CultureInfo]::InvariantCulture)
            $end=[DateTimeOffset]::Parse($Result.heldUntilUtc,[Globalization.CultureInfo]::InvariantCulture)
            if ($end -lt $start) { throw 'Lease release precedes hold.' }
        } catch { $Failures.Add('receipt.lease.result interval is invalid.') }
        try {
            $baseline=Get-PlanUtcTicks $Facts.preTransition.observedUtc
            $approvedAt=Get-PlanUtcTicks $ApprovalObject.issuedUtc
            $expires=Get-PlanUtcTicks $ApprovalObject.validUntilUtc
            $acquired=Get-PlanUtcTicks $Result.heldFromUtc
            $released=Get-PlanUtcTicks $Result.heldUntilUtc
            if ($acquired -lt $baseline -or $released -lt $baseline) { $Failures.Add('receipt.lease.result interval precedes the preapproval baseline.') }
            if ($acquired -lt $approvedAt) { $Failures.Add('receipt.lease.result.heldFromUtc precedes positive approval issuedUtc.') }
            if ($released -gt $expires) { $Failures.Add('receipt.lease.result.heldUntilUtc exceeds approval validUntilUtc.') }
        } catch { $Failures.Add('receipt.lease.result chronology cannot be verified.') }
    }
    if ($Kind -eq 'before-image' -and $Result.items -is [array]) {
        if ($Result.items.Count -ne 6) { $Failures.Add('receipt.before-image.result.items must list exactly six leaves.') }
        else {
            for ($i=0; $i -lt 6; $i++) {
                $item=$Result.items[$i]; $expectedItem=$Plan.BeforeImages[$i]
                if (Test-Fields $item @('path','initialSha256','aclSha256','fileIdentity','absentBefore') "receipt.before-image.result.items[$i]" $Failures) {
                    Check-Pin $item.path $expectedItem.path "receipt.before-image.result.items[$i].path" $Failures
                    Check-Pin $item.initialSha256 $expectedItem.initialSha256 "receipt.before-image.result.items[$i].initialSha256" $Failures
                    Check-Pin $item.absentBefore $expectedItem.absenceProof "receipt.before-image.result.items[$i].absentBefore" $Failures
                    Test-Sha $item.aclSha256 "receipt.before-image.result.items[$i].aclSha256" $Failures
                    if ($expectedItem.absenceProof) { Check-Pin $item.fileIdentity $null "receipt.before-image.result.items[$i].fileIdentity" $Failures }
                    elseif ($item.fileIdentity -isnot [string] -or $item.fileIdentity -cnotmatch '^[0-9a-f]{32}$') { $Failures.Add("receipt.before-image.result.items[$i].fileIdentity is malformed.") }
                }
            }
            Check-Pin $Result.inventorySha256 (Get-PlanHash ($Result.items | ConvertTo-Json -Depth 30 -Compress)) 'receipt.before-image.result.inventorySha256' $Failures
        }
    } elseif ($Kind -eq 'before-image') { $Failures.Add('receipt.before-image.result.items is missing or malformed.') }
    if ($Kind -eq 'acl-before-image' -and $Result.descriptors -is [array]) {
        if ($Result.descriptors.Count -ne 1221) { $Failures.Add('receipt.acl-before-image.result.descriptors must list all 1,221 existing grant targets.') }
        else {
            $targets=@($Plan.Grants.files) + @($Plan.Grants.directories)
            for ($i=0; $i -lt 1221; $i++) {
                $item=$Result.descriptors[$i]
                if (Test-Fields $item @('path','role','mask','ownerSid','descriptorSha256','objectIdentity') "receipt.acl-before-image.result.descriptors[$i]" $Failures) {
                    Check-Pin $item.path $targets[$i].path "receipt.acl-before-image.result.descriptors[$i].path" $Failures
                    Check-Pin $item.role $targets[$i].role "receipt.acl-before-image.result.descriptors[$i].role" $Failures
                    Check-Pin $item.mask $targets[$i].mask "receipt.acl-before-image.result.descriptors[$i].mask" $Failures
                    Check-Pin $item.ownerSid $script:FrozenOwnerSid "receipt.acl-before-image.result.descriptors[$i].ownerSid" $Failures
                    Test-Sha $item.descriptorSha256 "receipt.acl-before-image.result.descriptors[$i].descriptorSha256" $Failures
                    if ($item.objectIdentity -isnot [string] -or $item.objectIdentity -cnotmatch '^[0-9a-f]{32}$') { $Failures.Add("receipt.acl-before-image.result.descriptors[$i].objectIdentity is malformed.") }
                    if ($item.path -ceq 'BepInEx/cache') { Check-Pin $item.descriptorSha256 $t.cacheParentBeforeDescriptorSha256 'receipt.acl-before-image.result.cacheParentDescriptorSha256' $Failures }
                }
            }
            Check-Pin $Result.inventorySha256 (Get-PlanHash ($Result.descriptors | ConvertTo-Json -Depth 30 -Compress)) 'receipt.acl-before-image.result.inventorySha256' $Failures
        }
    } elseif ($Kind -eq 'acl-before-image') { $Failures.Add('receipt.acl-before-image.result.descriptors is missing or malformed.') }
}

function Test-ReceiptBundle($EvidenceBundle, $Plan, $Facts, [string]$ApprovalSha256, $ApprovalObject, [Collections.Generic.List[string]]$Failures) {
    $bundleNames = @('schema','scope','stage','runId','planSha256','contractSha256','ownerSid','volumeGuid','receipts')
    if (-not (Test-Fields $EvidenceBundle $bundleNames 'EvidenceBundle' $Failures)) { return }
    $expected = [ordered]@{schema=1;scope='full-restricted-capabilities-admission-evidence';stage='posttransition';runId=$Plan.RunId;planSha256=$Plan.PlanSha256;contractSha256=$script:ContractSha256;ownerSid=$script:FrozenOwnerSid;volumeGuid=$Facts.volumeGuid}
    foreach ($name in $expected.Keys) { Check-Pin $EvidenceBundle.$name $expected[$name] "EvidenceBundle.$name" $Failures }
    $hashFields = [ordered]@{'lease'='leaseReceiptSha256';'before-image'='beforeImageReceiptSha256';'acl-before-image'='aclBeforeImageReceiptSha256';'grant'='grantReceiptSha256';'cache-create'='cacheLeafCreateReceiptSha256';'seal-restoration'='sealRestorationReceiptSha256';'unchanged-inputs'='unchangedInputsReceiptSha256';'warm-input-order'='warmInputOrderReceiptSha256';'unity-log-precreation'='unityLogPrecreationReceiptSha256';'response-policy'='responsePolicyReceiptSha256';'review'='reviewReceiptSha256';'transition'='receiptSha256'}
    if ($EvidenceBundle.receipts -isnot [array] -or $EvidenceBundle.receipts.Count -ne $hashFields.Count) { $Failures.Add('EvidenceBundle.receipts must contain exactly 12 required kinds.'); return }
    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $hashes = @{}; $results = @{}
    foreach ($receipt in $EvidenceBundle.receipts) {
        $names = @('kind','phase','runId','planSha256','contractSha256','ownerSid','volumeGuid','bytes','sha256','content')
        if (-not (Test-Fields $receipt $names 'EvidenceBundle.receipt' $Failures)) { continue }
        if ($receipt.kind -isnot [string] -or -not $hashFields.Contains($receipt.kind) -or -not $seen.Add($receipt.kind)) { $Failures.Add('EvidenceBundle receipt kind is unknown or duplicated.'); continue }
        $kind = $receipt.kind
        foreach ($name in @('phase','runId','planSha256','contractSha256','ownerSid','volumeGuid')) {
            $wanted = if ($name -eq 'phase') { 'posttransition' } elseif ($name -eq 'runId') { $Plan.RunId } elseif ($name -eq 'planSha256') { $Plan.PlanSha256 } elseif ($name -eq 'contractSha256') { $script:ContractSha256 } elseif ($name -eq 'ownerSid') { $script:FrozenOwnerSid } else { $Facts.volumeGuid }
            Check-Pin $receipt.$name $wanted "receipt.$kind.$name" $Failures
        }
        if ($receipt.content -isnot [string] -or $receipt.bytes -isnot [int] -and $receipt.bytes -isnot [long] -or $receipt.bytes -lt 2 -or $receipt.bytes -gt 1048576) { $Failures.Add("receipt.$kind byte/content bound differs."); continue }
        if ([Text.UTF8Encoding]::new($false).GetByteCount($receipt.content) -ne $receipt.bytes) { $Failures.Add("receipt.$kind UTF-8 byte count differs.") }
        $actualHash = Get-PlanHash $receipt.content
        Check-Pin $receipt.sha256 $actualHash "receipt.$kind.sha256" $Failures
        Check-Pin $receipt.sha256 $Facts.transition.($hashFields[$kind]) "receipt.$kind.transitionHash" $Failures
        $hashes[$kind] = $actualHash
        try { $content = ConvertFrom-PlanJson $receipt.content }
        catch { $Failures.Add("receipt.$kind content is invalid JSON."); continue }
        if (($content | ConvertTo-Json -Depth 60 -Compress) -cne $receipt.content) { $Failures.Add("receipt.$kind content is not canonical JSON."); continue }
        $contentNames = @('schema','kind','stage','runId','planSha256','contractSha256','ownerSid','volumeGuid','result')
        if (-not (Test-Fields $content $contentNames "receipt.$kind.content" $Failures)) { continue }
        Check-Pin $content.schema 1 "receipt.$kind.content.schema" $Failures
        foreach ($name in @('kind','runId','planSha256','contractSha256','ownerSid','volumeGuid')) { Check-Pin $content.$name $receipt.$name "receipt.$kind.content.$name" $Failures }
        Check-Pin $content.stage 'posttransition' "receipt.$kind.content.stage" $Failures
        $results[$kind] = $content.result
    }
    foreach ($kind in $hashFields.Keys) { if (-not $seen.Contains($kind)) { $Failures.Add("EvidenceBundle receipt kind missing: $kind") } }
    if ($seen.Count -eq $hashFields.Count) { foreach ($kind in $hashFields.Keys) { if ($results.ContainsKey($kind)) { Test-ReceiptResult $kind $results[$kind] $Plan $Facts $ApprovalSha256 $ApprovalObject $hashes $Failures } } }
    # Do not join inventory items whose receipt validation already failed.
    if ($Failures.Count) { return }
    if ($results.ContainsKey('before-image') -and $results.ContainsKey('acl-before-image')) {
        $before=$results['before-image'];$acl=$results['acl-before-image']
        if ($before -is [pscustomobject] -and $acl -is [pscustomobject] -and $before.PSObject.Properties['items'] -and $acl.PSObject.Properties['descriptors'] -and $before.items -is [array] -and $acl.descriptors -is [array] -and $before.items.Count -eq 6 -and $acl.descriptors.Count -eq 1221) {
            foreach($leaf in @($Plan.MutableLeaves|Where-Object {$_.creation -ceq 'ExistingOnly'})){
                $left=@($before.items|Where-Object {$_.path -ceq $leaf.path})
                $right=@($acl.descriptors|Where-Object {$_.path -ceq $leaf.path})
                if($left.Count -ne 1 -or $right.Count -ne 1){$Failures.Add("receipt.inventoryJoin.$($leaf.path) existing leaf is missing or duplicated.");continue}
                Check-Pin $left[0].fileIdentity $right[0].objectIdentity "receipt.inventoryJoin.$($leaf.path).fileIdentity" $Failures
                Check-Pin $left[0].aclSha256 $right[0].descriptorSha256 "receipt.inventoryJoin.$($leaf.path).descriptorSha256" $Failures
            }
        }
    }
}

function Test-EvidenceBundleBounds($EvidenceBundle, [Collections.Generic.List[string]]$Failures) {
    if (-not (Test-Fields $EvidenceBundle @('schema','scope','stage','runId','planSha256','contractSha256','ownerSid','volumeGuid','receipts') 'EvidenceBundle' $Failures)) { return $false }
    if ($EvidenceBundle.receipts -isnot [array] -or $EvidenceBundle.receipts.Count -ne 12) { $Failures.Add('EvidenceBundle.receipts must contain exactly 12 required kinds.'); return $false }
    $total=0L
    foreach ($receipt in $EvidenceBundle.receipts) {
        if ($null -eq $receipt -or $receipt -isnot [pscustomobject] -or $null -eq $receipt.PSObject.Properties['content'] -or $receipt.content -isnot [string]) { $Failures.Add('EvidenceBundle receipt content is malformed.'); return $false }
        $total += [Text.UTF8Encoding]::new($false).GetByteCount($receipt.content)
        if ($total -gt 2097152 -or $receipt.content.Length -gt 1048576) { $Failures.Add('EvidenceBundle total or receipt content exceeds finite bounds.'); return $false }
    }
    if ([Text.UTF8Encoding]::new($false).GetByteCount(($EvidenceBundle | ConvertTo-Json -Depth 80 -Compress)) -gt 4194304) { $Failures.Add('EvidenceBundle serialized JSON exceeds 4 MiB.'); return $false }
    return $true
}

function Test-Relative([string]$Path) {
    return $Path -is [string] -and $Path.Length -gt 0 -and $Path -notmatch '[\\:\x00-\x1F]' -and
        $Path -notmatch '(^|/)\.\.?(/|$)' -and $Path -notmatch '^/' -and $Path -notmatch '/$'
}

function Get-PinDigest($Items, [string]$Kind) {
    $lines = [Collections.Generic.List[string]]::new()
    foreach ($item in $Items) {
        if ($Kind -ceq 'File') { $lines.Add($item.path + '|' + $item.sha256.ToLowerInvariant()) }
        else { $lines.Add([string]$item) }
    }
    $sorted = $lines.ToArray()
    [Array]::Sort($sorted, [StringComparer]::Ordinal)
    return Get-PlanHash (($sorted -join "`n") + "`n")
}

function Get-GrantDigest($Grants) {
    $lines=[Collections.Generic.List[string]]::new()
    $lines.Add('AZFRG1')
    foreach($row in $Grants.files){$lines.Add('F|'+$row.path+'|'+$row.sha256+'|'+$row.role+'|'+$row.mask+'|'+$row.principal+'|'+$row.inheritance)}
    foreach($row in $Grants.directories){$lines.Add('D|'+$row.path+'|'+$row.role+'|'+$row.mask+'|'+$row.principal+'|'+$row.inheritance)}
    foreach($name in @('parentPath','principal','parentMask','parentInheritance','childFileMask','childInheritance','argumentsFile','argumentsFileMask','temporaryResponseRule','unexpectedOutput')){$lines.Add('R|'+$name+'|'+[string]$Grants.request.$name)}
    foreach($name in @('responseMaxBytes','maxTemporaryFiles')){$lines.Add('R|'+$name+'|'+$Grants.request.$name.ToString([Globalization.CultureInfo]::InvariantCulture))}
    foreach($name in @('argumentsProtected','createSubdirectory','deleteChild','postrunValidationRequired')){$lines.Add('R|'+$name+'|'+([string]$Grants.request.$name).ToLowerInvariant())}
    foreach($name in @('systemInputGrant','sharedStateGrant')){$lines.Add('G|'+$name+'|'+[string]$Grants.$name)}
    return Get-PlanHash (($lines.ToArray()-join [char]10)+[char]10)
}

function Get-FrozenInputs($Contract) {
    $stage = Read-FrozenJson $script:StageRelative $Contract.stage.manifestSha256
    $cache = Read-FrozenJson $script:CacheRelative $Contract.cachePrerequisites.contractSha256
    return [pscustomobject]@{Stage = $stage; Cache = $cache}
}

function Read-AzuriteFullCapabilitiesContract {
    $contract = Read-FrozenJson $script:ContractRelative $script:ContractSha256
    $top = @('schema','contractId','ownerSid','labRoot','installRoot','executable','profile','modName','modVersion','operation','acceptance','cliEnvironment','argv','sidecar','files','exactDirectories','evidence','stage','readEnvelope','cachePrerequisites','mutableLeaves','requestPolicy','transitionPolicy')
    $errors = [Collections.Generic.List[string]]::new()
    [void](Test-Fields $contract $top 'Contract' $errors)
    if ($errors.Count) { throw ($errors -join ' ') }
    if ($contract.schema -ne 1 -or $contract.contractId -cne 'aa-2023.2.22f1-azurite-1.0.38-current-account-full-restricted-capabilities-v1' -or $contract.ownerSid -cne $script:FrozenOwnerSid -or
        $contract.labRoot -cne 'F:\AzuriteLabs\Editor-20261108-normal' -or
        $contract.executable -cne 'F:\AzuriteLabs\Editor-20261108-normal\install\AzureArchive.exe' -or
        $contract.operation -cne 'capabilities' -or $contract.stage.runtimeAllowed -isnot [bool] -or $contract.stage.runtimeAllowed) {
        throw 'Capabilities contract identity, operation, or historical stage state differs.'
    }
    if ($contract.files.Count -ne 451 -or $contract.readEnvelope.fileCount -ne 1062 -or $contract.readEnvelope.directoryCount -ne 156 -or
        $contract.readEnvelope.bytes -ne 3521516451 -or $contract.readEnvelope.requestChildFileMask -ne '0x13019F' -or $contract.stage.destinationVolumeGuid -cne '\\?\Volume{a7c77c39-d000-4faf-bbf8-cc8ca0c743cb}\') {
        throw 'Capabilities contract envelope or volume differs.'
    }
    $inputs = Get-FrozenInputs $contract
    if ($inputs.Stage.entries.Count -ne 1062 -or $inputs.Stage.directories.Count -ne 156 -or $inputs.Stage.runtimeAllowed -isnot [bool] -or $inputs.Stage.runtimeAllowed -or
        $inputs.Cache.stage_runtime_allowed -isnot [bool] -or $inputs.Cache.stage_runtime_allowed -or
        $inputs.Cache.selected_profile -cne $contract.profile -or $inputs.Cache.warm_interop.computed_stamp -cne $contract.cachePrerequisites.stamp -or
        $inputs.Cache.warm_interop.pinned_stamp -cne $contract.cachePrerequisites.stamp -or $inputs.Cache.seed.sha256 -cne $contract.cachePrerequisites.seedSha256) {
        throw 'Frozen stage/cache content differs from the capabilities contract.'
    }
    $index = @{}
    $casePaths = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($entry in $inputs.Stage.entries) {
        if (-not (Test-Relative $entry.path) -or -not $casePaths.Add($entry.path)) { throw 'Stage file path is noncanonical or case-ambiguous.' }
        $index[$entry.path] = $entry.sha256
    }
    $casePaths.Clear()
    foreach ($directory in $inputs.Stage.directories) {
        if (-not (Test-Relative $directory) -or -not $casePaths.Add($directory)) { throw 'Stage directory path is noncanonical or case-ambiguous.' }
    }
    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($pin in $contract.files) {
        if (-not (Test-Relative $pin.path) -or -not $seen.Add($pin.path) -or -not $index.ContainsKey($pin.path) -or
            $index[$pin.path] -cne $pin.sha256.ToLowerInvariant()) { throw "Semantic CLI pin differs from stage: $($pin.path)" }
    }
    $bepConfig = $contract.mutableLeaves | Where-Object { $_.path -ceq 'BepInEx/config/BepInEx.cfg' } | Select-Object -First 1
    if ($null -eq $bepConfig -or $bepConfig.finalSha256 -cne 'f2e0f930efda6a7fa292e872760a27cdc9bb1d232cd82d719446622c4dc6ef06') { throw 'BepInEx config final pin is missing or differs.' }
    if ($contract.requestPolicy.childFileMask -cne '0x13019F' -or $contract.requestPolicy.argumentsFileMask -cne '0x120089' -or $contract.requestPolicy.deleteChild -ne $false -or $contract.requestPolicy.createSubdirectory -ne $false -or $contract.requestPolicy.responseMaxBytes -ne 1048576 -or $contract.requestPolicy.maxTemporaryFiles -ne 1 -or $contract.requestPolicy.postrunValidationRequired -ne $true) { throw 'Request policy mask, bounds, or parent restrictions differ.' }
    foreach ($evidence in $contract.evidence) { Assert-FrozenFile $evidence.path $evidence.sha256 }
    return $contract
}

function Test-ApprovedInput($Value, $Approved, [string]$Label, [Collections.Generic.List[string]]$Failures) {
    if ($null -eq $Value -or $Value -isnot [pscustomobject]) { $Failures.Add("$Label is missing or malformed."); return $false }
    $json = $Value | ConvertTo-Json -Depth 60 -Compress
    if ($Label -ceq 'CachePrerequisites' -and $null -ne $Value.PSObject.Properties['observation_utc'] -and $Value.observation_utc -isnot [string]) {
        try {
            if ((Get-PlanUtcTicks $Value.observation_utc) -ne (Get-PlanUtcTicks $Approved.observation_utc)) { throw 'Different UTC instant.' }
            $normalized=ConvertFrom-PlanJson $json
            $normalized.observation_utc=$Approved.observation_utc
            $json=$normalized|ConvertTo-Json -Depth 60 -Compress
        } catch { $Failures.Add("$Label observation_utc differs from frozen C: evidence."); return $false }
    }
    $expected = $Approved | ConvertTo-Json -Depth 60 -Compress
    if ($json -cne $expected) { $Failures.Add("$Label differs from frozen C: evidence."); return $false }
    return $true
}

function Test-FactsShape($Facts, [Collections.Generic.List[string]]$Failures) {
    $top = @('schema','runId','labRoot','executable','operation','profile','modName','modVersion','volumeGuid','ownerSid','controllerSid','stage','inputs','cache','environment','acl','preTransition','transition')
    if (-not (Test-Fields $Facts $top 'Facts' $Failures)) { return $false }
    $shapes = @(
        @('stage', @('manifestSha256','postverifySha256','completionSha256','journalSha256','rootMarkerSha256','parentMarkerSha256','startReceiptSha256','approvalSha256','sealPolicySha256','sealCompletionSha256','destinationVolumeGuid','status','runtimeAllowed')),
        @('inputs', @('stageFilePinDigest','immutableFilePinDigest','directoryPinDigest','semanticPinCount','stageFileCount','stageDirectoryCount','stageBytes','cacheContractSha256','cacheReportSha256','cacheIndexSha256','warmResultSha256')),
        @('cache', @('chainloaderSha256','modTheLeaf','modTheLeafAbsent','seedSha256','stamp','orderedInputEventCount','unityDllCount','generatorVersion','cpp2ilVersion','optionalRenameMapPresent','orderedInputsMatchRecorded')),
        @('environment', @('gameAssemblyOverride','unhollowedPathArgument')),
        @('acl', @('stageSealPolicySha256','stageSealsIntact','cacheParentCreationDenied','sharedGrantDelta','actualLogonSid')),
        @('preTransition', @('phase','runId','cacheLeaf','cacheLeafExists','unityLogAbsent','seedSha256','parentSealIntact','runtimeAllowed','observedUtc'))
    )
    foreach ($shape in $shapes) { [void](Test-Fields $Facts.($shape[0]) $shape[1] "Facts.$($shape[0])" $Failures) }
    if ($null -ne $Facts.transition) {
        $names = @('phase','runId','receiptSha256','leaseReceiptSha256','beforeImageReceiptSha256','aclBeforeImageReceiptSha256','beforeImageInventorySha256','aclBeforeImageInventorySha256','unchangedInputsReceiptSha256','warmInputOrderReceiptSha256','sealRestorationReceiptSha256','grantReceiptSha256','reviewReceiptSha256','reviewApprovalSha256','unityLogPrecreationReceiptSha256','responsePolicyReceiptSha256','cacheLeafCreateReceiptSha256','seedSha256','ownerSid','volumeGuid','leaseId','cacheLeafCreateMode','cacheLeafCreated','cacheLeafSha256','cacheLeafFileIdentity','cacheParentBeforeDescriptorSha256','cacheParentRestoredDescriptorSha256','unityLogFileIdentity','sealRestored','stageSealsExceptCacheUnchanged','sharedDelta','runtimeAllowed','precreatedUnityLogEmpty','requestArgumentsProtected')
        [void](Test-Fields $Facts.transition $names 'Facts.transition' $Failures)
    }
    return ($Failures.Count -eq 0)
}

function Test-PreparationFacts($Facts, $Contract, $Cache, $Envelope, [string]$RunId, [Collections.Generic.List[string]]$Failures) {
    $names = @('schema','runId','labRoot','executable','operation','profile','modName','modVersion','volumeGuid','ownerSid','controllerSid','stage','inputs','cache','environment','acl','preTransition','transition')
    if (-not (Test-Fields $Facts $names 'Facts' $Failures)) { return }
    Check-Pin $Facts.schema 1 'Facts.schema' $Failures
    Check-Pin $Facts.runId $RunId 'Facts.runId' $Failures
    foreach ($name in @('labRoot','executable','operation','profile','modName','modVersion')) { Check-Pin $Facts.$name $Contract.$name "Facts.$name" $Failures }
    Check-Pin $Facts.volumeGuid $Contract.stage.destinationVolumeGuid 'Facts.volumeGuid' $Failures
    Check-Pin $Facts.ownerSid $script:FrozenOwnerSid 'Facts.ownerSid' $Failures
    Check-Pin $Facts.controllerSid $Facts.ownerSid 'Facts.controllerSid' $Failures
    $stageNames = @($Contract.stage.PSObject.Properties | ForEach-Object { $_.Name })
    if (Test-Fields $Facts.stage $stageNames 'Facts.stage' $Failures) {
        foreach ($name in $stageNames) { Check-Pin $Facts.stage.$name $Contract.stage.$name "Facts.stage.$name" $Failures }
    }
    $inputNames = @('stageFilePinDigest','immutableFilePinDigest','directoryPinDigest','semanticPinCount','stageFileCount','stageDirectoryCount','stageBytes','cacheContractSha256','cacheReportSha256','cacheIndexSha256','warmResultSha256')
    if (Test-Fields $Facts.inputs $inputNames 'Facts.inputs' $Failures) {
        $expected = [ordered]@{stageFilePinDigest=$Envelope.stageFilePinDigest;immutableFilePinDigest=$Envelope.immutableFilePinDigest;directoryPinDigest=$Envelope.directoryPinDigest;semanticPinCount=451;stageFileCount=1062;stageDirectoryCount=156;stageBytes=[long]3521516451;cacheContractSha256=$Contract.cachePrerequisites.contractSha256;cacheReportSha256=$Contract.cachePrerequisites.reportSha256;cacheIndexSha256=$Contract.cachePrerequisites.indexSha256;warmResultSha256=$Contract.cachePrerequisites.warmResultSha256}
        foreach ($name in $inputNames) { Check-Pin $Facts.inputs.$name $expected[$name] "Facts.inputs.$name" $Failures }
    }
    $cacheNames = @('chainloaderSha256','modTheLeaf','modTheLeafAbsent','seedSha256','stamp','orderedInputEventCount','unityDllCount','generatorVersion','cpp2ilVersion','optionalRenameMapPresent','orderedInputsMatchRecorded')
    if (Test-Fields $Facts.cache $cacheNames 'Facts.cache' $Failures) {
        $expected = [ordered]@{chainloaderSha256=$Contract.cachePrerequisites.chainloaderSha256;modTheLeaf=$Contract.cachePrerequisites.modTheLeaf;modTheLeafAbsent=$true;seedSha256=$Contract.cachePrerequisites.seedSha256;stamp=$Contract.cachePrerequisites.stamp;orderedInputEventCount=147;unityDllCount=72;generatorVersion='1.5.3.0';cpp2ilVersion='2022.1.0.0';optionalRenameMapPresent=$false;orderedInputsMatchRecorded=$true}
        foreach ($name in $cacheNames) { Check-Pin $Facts.cache.$name $expected[$name] "Facts.cache.$name" $Failures }
    }
    if (Test-Fields $Facts.environment @('gameAssemblyOverride','unhollowedPathArgument') 'Facts.environment' $Failures) {
        Check-Pin $Facts.environment.gameAssemblyOverride $null 'Facts.environment.gameAssemblyOverride' $Failures
        Check-Pin $Facts.environment.unhollowedPathArgument $false 'Facts.environment.unhollowedPathArgument' $Failures
    }
    if (Test-Fields $Facts.acl @('stageSealPolicySha256','stageSealsIntact','cacheParentCreationDenied','sharedGrantDelta','actualLogonSid') 'Facts.acl' $Failures) {
        Check-Pin $Facts.acl.stageSealPolicySha256 $Contract.stage.sealPolicySha256 'Facts.acl.stageSealPolicySha256' $Failures
        Check-Pin $Facts.acl.stageSealsIntact $true 'Facts.acl.stageSealsIntact' $Failures
        Check-Pin $Facts.acl.cacheParentCreationDenied $true 'Facts.acl.cacheParentCreationDenied' $Failures
        Check-Pin $Facts.acl.sharedGrantDelta 'None' 'Facts.acl.sharedGrantDelta' $Failures
        if ($Facts.acl.actualLogonSid -isnot [string] -or $Facts.acl.actualLogonSid -cnotmatch '^S-1-5-5-\d+-\d+$') { $Failures.Add('Facts.acl.actualLogonSid is malformed.') }
    }
    if (Test-Fields $Facts.preTransition @('phase','runId','cacheLeaf','cacheLeafExists','unityLogAbsent','seedSha256','parentSealIntact','runtimeAllowed','observedUtc') 'Facts.preTransition' $Failures) {
        Check-Pin $Facts.preTransition.phase 'pretransition' 'Facts.preTransition.phase' $Failures
        Check-Pin $Facts.preTransition.runId $Facts.runId 'Facts.preTransition.runId' $Failures
        Check-Pin $Facts.preTransition.cacheLeaf $Contract.cachePrerequisites.modTheLeaf 'Facts.preTransition.cacheLeaf' $Failures
        Check-Pin $Facts.preTransition.cacheLeafExists $false 'Facts.preTransition.cacheLeafExists' $Failures
        Check-Pin $Facts.preTransition.unityLogAbsent $true 'Facts.preTransition.unityLogAbsent' $Failures
        Check-Pin $Facts.preTransition.seedSha256 $Contract.cachePrerequisites.seedSha256 'Facts.preTransition.seedSha256' $Failures
        Check-Pin $Facts.preTransition.parentSealIntact $true 'Facts.preTransition.parentSealIntact' $Failures
        Check-Pin $Facts.preTransition.runtimeAllowed $false 'Facts.preTransition.runtimeAllowed' $Failures
        try { [void](Get-PlanUtcTicks $Facts.preTransition.observedUtc) } catch { $Failures.Add('Facts.preTransition.observedUtc is not a real explicit UTC instant.') }
    }
    if ($null -ne $Facts.transition) {
        $transitionNames = @('phase','runId','receiptSha256','leaseReceiptSha256','beforeImageReceiptSha256','aclBeforeImageReceiptSha256','beforeImageInventorySha256','aclBeforeImageInventorySha256','unchangedInputsReceiptSha256','warmInputOrderReceiptSha256','sealRestorationReceiptSha256','grantReceiptSha256','reviewReceiptSha256','reviewApprovalSha256','unityLogPrecreationReceiptSha256','responsePolicyReceiptSha256','cacheLeafCreateReceiptSha256','seedSha256','ownerSid','volumeGuid','leaseId','cacheLeafCreateMode','cacheLeafCreated','cacheLeafSha256','cacheLeafFileIdentity','cacheParentBeforeDescriptorSha256','cacheParentRestoredDescriptorSha256','unityLogFileIdentity','sealRestored','stageSealsExceptCacheUnchanged','sharedDelta','runtimeAllowed','precreatedUnityLogEmpty','requestArgumentsProtected')
        if (Test-Fields $Facts.transition $transitionNames 'Facts.transition' $Failures) {
            Check-Pin $Facts.transition.phase 'posttransition' 'Facts.transition.phase' $Failures
            Check-Pin $Facts.transition.runId $RunId 'Facts.transition.runId' $Failures
            Check-Pin $Facts.transition.ownerSid $script:FrozenOwnerSid 'Facts.transition.ownerSid' $Failures
            Check-Pin $Facts.transition.volumeGuid $Contract.stage.destinationVolumeGuid 'Facts.transition.volumeGuid' $Failures
            Check-Pin $Facts.transition.seedSha256 $Contract.cachePrerequisites.seedSha256 'Facts.transition.seedSha256' $Failures
            Check-Pin $Facts.transition.cacheLeafSha256 $Contract.cachePrerequisites.seedSha256 'Facts.transition.cacheLeafSha256' $Failures
            Check-Pin $Facts.transition.cacheLeafCreateMode 'CreateNewNoOverwrite' 'Facts.transition.cacheLeafCreateMode' $Failures
            foreach ($name in @('cacheLeafCreated','sealRestored','stageSealsExceptCacheUnchanged','precreatedUnityLogEmpty','requestArgumentsProtected')) { Check-Pin $Facts.transition.$name $true "Facts.transition.$name" $Failures }
            Check-Pin $Facts.transition.sharedDelta 'None' 'Facts.transition.sharedDelta' $Failures
            Check-Pin $Facts.transition.runtimeAllowed $false 'Facts.transition.runtimeAllowed' $Failures
            foreach ($name in @('leaseId','cacheLeafFileIdentity','unityLogFileIdentity')) {
                if ($Facts.transition.$name -isnot [string] -or $Facts.transition.$name -cnotmatch '^[0-9a-f]{32}$' -or $Facts.transition.$name -ceq ('0'*32)) { $Failures.Add("Facts.transition.$name is not a concrete lowercase 32-hex identity.") }
            }
            foreach ($name in @('receiptSha256','leaseReceiptSha256','beforeImageReceiptSha256','aclBeforeImageReceiptSha256','beforeImageInventorySha256','aclBeforeImageInventorySha256','unchangedInputsReceiptSha256','warmInputOrderReceiptSha256','sealRestorationReceiptSha256','grantReceiptSha256','reviewReceiptSha256','reviewApprovalSha256','unityLogPrecreationReceiptSha256','responsePolicyReceiptSha256','cacheLeafCreateReceiptSha256','cacheParentBeforeDescriptorSha256','cacheParentRestoredDescriptorSha256')) { Test-Sha $Facts.transition.$name "Facts.transition.$name" $Failures }
        }
    }
}

function New-BasePlan($Contract, $Facts, [string]$RunId) {
    $inputs = Get-FrozenInputs $Contract
    $existingMutable = @($Contract.mutableLeaves | Where-Object { $_.creation -ceq 'ExistingOnly' } | ForEach-Object { $_.path })
    $immutable = @($inputs.Stage.entries | Where-Object { $existingMutable -cnotcontains $_.path })
    $envelope = [ordered]@{
        manifestPath = $Contract.readEnvelope.manifestPath
        manifestSha256 = $Contract.stage.manifestSha256
        fileCount = 1062
        directoryCount = 156
        bytes = [long]3521516451
        semanticPinCount = 451
        stageFilePinDigest = Get-PinDigest $inputs.Stage.entries 'File'
        immutableFilePinDigest = Get-PinDigest $immutable 'File'
        directoryPinDigest = Get-PinDigest $inputs.Stage.directories 'Directory'
    }
    $fileGrants = [Collections.Generic.List[object]]::new()
    foreach ($entry in $inputs.Stage.entries) {
        $mutable = $Contract.mutableLeaves | Where-Object { $_.path -ceq $entry.path } | Select-Object -First 1
        $role = 'ReadOnlyInput'; $mask = '0x120089'
        if ($null -ne $mutable) { $role = $mutable.role; $mask = '0x12019F' }
        elseif ($entry.path -cne 'UnityCrashHandler64.exe' -and $entry.path -cmatch '\.(dll|exe)$') { $role = 'ReadExecuteInput'; $mask = '0x1200A9' }
        elseif ($entry.path -ceq 'UnityCrashHandler64.exe') { $role = 'RetainedCrashHandlerInputNoLaunchApproval' }
        $fileGrants.Add([pscustomobject][ordered]@{path=$entry.path;sha256=$entry.sha256;role=$role;mask=$mask;principal='ActualLogonSidOnly';inheritance='None'})
    }
    $directoryGrants = [Collections.Generic.List[object]]::new()
    $directoryGrants.Add([pscustomobject][ordered]@{path='F:\AzuriteLabs';role='ParentLabTraverse';mask='0x1200A9';principal='ActualLogonSidOnly';inheritance='None'})
    $directoryGrants.Add([pscustomobject][ordered]@{path=$Contract.labRoot;role='LabRootTraverse';mask='0x1200A9';principal='ActualLogonSidOnly';inheritance='None'})
    $directoryGrants.Add([pscustomobject][ordered]@{path='.';role='InstallRootTraverse';mask='0x1200A9';principal='ActualLogonSidOnly';inheritance='None'})
    foreach ($directory in $inputs.Stage.directories) {
        $directoryGrants.Add([pscustomobject][ordered]@{path=$directory;role='ListTraverseOnly';mask='0x1200A9';principal='ActualLogonSidOnly';inheritance='None'})
    }
    $before = @($Contract.mutableLeaves | ForEach-Object {
        [pscustomobject][ordered]@{path=$_.path;initialSha256=$_.stageSha256;finalSha256=$(if ($_.PSObject.Properties['finalSha256']) { $_.finalSha256 } else { $null });bytesAndAcl=$true;absenceProof=($_.creation -ceq 'ControllerCreateNewOnly' -or $_.creation -ceq 'ControllerPrecreateEmpty');controllerOwnedHistory=$true}
    })
    $factsSha = Get-IntentFactsHash $Facts
    $grants=[pscustomobject][ordered]@{files=$fileGrants.ToArray();directories=$directoryGrants.ToArray();request=$Contract.requestPolicy;systemInputGrant='None';sharedStateGrant='None'}
    return [pscustomobject][ordered]@{
        Mode = 'CurrentAccountFullRestrictedCapabilities'
        LabRoot = $Contract.labRoot
        Executable = $Contract.executable
        Command = 'capabilities'
        RunId = $RunId
        ContractSha256 = $script:ContractSha256
        FactsSha256 = $factsSha
        ReadEnvelope = [pscustomobject]$envelope
        Grants = $grants
        GrantDigest = Get-GrantDigest $grants
        MutableLeaves = $Contract.mutableLeaves
        RequestPolicy = $Contract.requestPolicy
        BeforeImages = $before
        SealTransitions = @([pscustomobject][ordered]@{parent='BepInEx/cache';controllerOnly=$true;removeOnlyDenyMask='0x6';createLeaf=$Contract.cachePrerequisites.modTheLeaf;createMode='CreateNewNoOverwrite';seedSha256=$Contract.cachePrerequisites.seedSha256;restoreBeforeAA=$true})
        RequiredAdmissionBindings = @('exactRunId','planSha256','contractSha256','phase0Chain','volumeGuid','ownerSid','controllerSid','preparationLeaseReleasedReceiptSha256','freshRuntimeLeaseRequired','separatePositiveApprovalSha256','transitionReceiptSha256','beforeImageInventorySha256','aclBeforeImageInventorySha256','cacheLeafCreateReceiptSha256','grantReceiptSha256','sealRestorationReceiptSha256','unchangedInputsReceiptSha256','warmInputOrderReceiptSha256','unityLogPrecreationReceiptSha256','responsePolicyReceiptSha256','zeroSharedDelta')
    }
}

function Get-BasePlanSha($Base) {
    $lines=[Collections.Generic.List[string]]::new()
    foreach($value in @('AZFRP2',$Base.Mode,$Base.LabRoot,$Base.Executable,$Base.Command,$Base.RunId,$Base.ContractSha256,$Base.FactsSha256,$Base.ReadEnvelope.manifestSha256,$Base.ReadEnvelope.stageFilePinDigest,$Base.ReadEnvelope.immutableFilePinDigest,$Base.ReadEnvelope.directoryPinDigest,$Base.GrantDigest)){$lines.Add([string]$value)}
    foreach($row in $Base.Grants.files){$lines.Add('F|'+$row.path+'|'+$row.sha256+'|'+$row.role+'|'+$row.mask+'|'+$row.principal+'|'+$row.inheritance)}
    foreach($row in $Base.Grants.directories){$lines.Add('D|'+$row.path+'|'+$row.role+'|'+$row.mask+'|'+$row.principal+'|'+$row.inheritance)}
    foreach($row in $Base.MutableLeaves){
        $initial=if($null -eq $row.stageSha256){'-'}else{$row.stageSha256}
        $final=if($row.PSObject.Properties['finalSha256'] -and $null -ne $row.finalSha256){$row.finalSha256}else{'-'}
        $lines.Add('M|'+$row.path+'|'+$row.role+'|'+$row.creation+'|'+$initial+'|'+$final+'|'+$row.fileMask)
    }
    foreach($value in @($Base.RequestPolicy.parentMask,$Base.RequestPolicy.childFileMask,$Base.RequestPolicy.argumentsFileMask,$Base.RequestPolicy.responseMaxBytes.ToString([Globalization.CultureInfo]::InvariantCulture),$Base.RequestPolicy.maxTemporaryFiles.ToString([Globalization.CultureInfo]::InvariantCulture),$Base.SealTransitions[0].parent,$Base.SealTransitions[0].createLeaf,$Base.SealTransitions[0].createMode,$Base.SealTransitions[0].seedSha256)){$lines.Add('P|'+[string]$value)}
    foreach($value in $Base.RequiredAdmissionBindings){$lines.Add('R|'+$value)}
    return Get-PlanHash (($lines.ToArray() -join [char]10)+[char]10)
}

function Get-IntentFactsHash($Facts) {
    if ($null -eq $Facts -or $Facts -isnot [pscustomobject]) { return $null }
    $projection = [ordered]@{}
    foreach ($name in @('schema','runId','labRoot','executable','operation','profile','modName','modVersion','volumeGuid','ownerSid','controllerSid','stage','inputs','cache','environment','acl','preTransition')) {
        if ($null -ne $Facts.PSObject.Properties[$name]) { $projection[$name] = $Facts.$name }
    }
    if ($null -ne $Facts.PSObject.Properties['preTransition'] -and $Facts.preTransition -is [pscustomobject] -and $Facts.preTransition.PSObject.Properties['observedUtc']) {
        $pre=[ordered]@{}
        foreach($property in $Facts.preTransition.PSObject.Properties){if($property.Name -cne 'observedUtc'){$pre[$property.Name]=$property.Value}}
        try {$pre['observedUtcTicks']=Get-PlanUtcTicks $Facts.preTransition.observedUtc}catch{$pre['observedUtcTicks']=$null}
        $projection['preTransition']=[pscustomobject]$pre
    }
    return Get-PlanHash ($projection | ConvertTo-Json -Depth 60 -Compress)
}

function New-AzuriteFullCapabilitiesPlan {
    [CmdletBinding()]
    param([Parameter(Mandatory)]$Contract, [Parameter(Mandatory)]$CachePrerequisites,
          [Parameter(Mandatory)][string]$RunId, $Facts, $Admission, $EvidenceBundle, $Approval, [string]$ExpectedApprovalSha256)
    $failures = [Collections.Generic.List[string]]::new()
    $approved = $null
    try { $approved = Read-AzuriteFullCapabilitiesContract }
    catch { $failures.Add("Frozen C: contract/evidence rejected: $($_.Exception.Message)") }
    if ($null -ne $approved) { [void](Test-ApprovedInput $Contract $approved 'Contract' $failures) }
    $cache = $null
    if ($null -ne $approved) {
        try { $cache = Read-FrozenJson $script:CacheRelative $approved.cachePrerequisites.contractSha256 }
        catch { $failures.Add("Frozen C: cache contract rejected: $($_.Exception.Message)") }
    }
    if ($null -ne $cache) { [void](Test-ApprovedInput $CachePrerequisites $cache 'CachePrerequisites' $failures) }
    $runOk = Test-RunId $RunId 'RunId' $failures
    $factsShapeOk = Test-FactsShape $Facts $failures
    $base = $null
    if ($null -ne $approved -and $runOk -and $factsShapeOk) {
        try { $base = New-BasePlan $approved $Facts $RunId }
        catch { $failures.Add("Read envelope construction failed: $($_.Exception.Message)") }
    }
    if ($null -ne $base -and $null -ne $approved -and $null -ne $cache -and $factsShapeOk) {
        Test-PreparationFacts $Facts $approved $cache $base.ReadEnvelope $RunId $failures
    }
    $preparationReady = $failures.Count -eq 0
    $planHash = if ($null -ne $base) { Get-BasePlanSha $base } else { $null }
    $proof = $null
    if ($null -eq $Admission) { }
    elseif ($preparationReady) {
        $candidate = [pscustomobject]([ordered]@{})
        foreach ($property in $base.PSObject.Properties) { $candidate | Add-Member -NotePropertyName $property.Name -NotePropertyValue $property.Value }
        $candidate | Add-Member -NotePropertyName PlanSha256 -NotePropertyValue $planHash
        $candidate | Add-Member -NotePropertyName PreparationReady -NotePropertyValue $preparationReady
        $candidate | Add-Member -NotePropertyName Ready -NotePropertyValue $false
        $candidate | Add-Member -NotePropertyName RuntimeAllowed -NotePropertyValue $false
        $candidate | Add-Member -NotePropertyName WillLaunch -NotePropertyValue $false
        $candidate | Add-Member -NotePropertyName AdmissionProof -NotePropertyValue $null
        $candidate | Add-Member -NotePropertyName Failures -NotePropertyValue @()
        $proof = Assert-AzuriteFullCapabilitiesAdmission -Contract $approved -Plan $candidate -Admission $Admission -Facts $Facts -EvidenceBundle $EvidenceBundle -Approval $Approval -ExpectedApprovalSha256 $ExpectedApprovalSha256
        foreach ($failure in $proof.Failures) { $failures.Add($failure) }
    } else { $failures.Add('Admission cannot be checked until preparation facts are complete.') }
    if ($null -eq $base) {
        $base = [pscustomobject][ordered]@{Mode='CurrentAccountFullRestrictedCapabilities';LabRoot='F:\AzuriteLabs\Editor-20261108-normal';Executable='F:\AzuriteLabs\Editor-20261108-normal\install\AzureArchive.exe';Command='capabilities';RunId=$RunId;ContractSha256=$script:ContractSha256;FactsSha256=$null;ReadEnvelope=$null;Grants=$null;GrantDigest=$null;MutableLeaves=@();RequestPolicy=$null;BeforeImages=@();SealTransitions=@();RequiredAdmissionBindings=@()}
    }
    $base | Add-Member -NotePropertyName PlanSha256 -NotePropertyValue $planHash
    $base | Add-Member -NotePropertyName PreparationReady -NotePropertyValue $preparationReady
    $base | Add-Member -NotePropertyName Ready -NotePropertyValue ($preparationReady -and $null -ne $proof -and $proof.Valid)
    $base | Add-Member -NotePropertyName RuntimeAllowed -NotePropertyValue $false
    $base | Add-Member -NotePropertyName WillLaunch -NotePropertyValue $false
    $base | Add-Member -NotePropertyName AdmissionProof -NotePropertyValue $(if ($null -ne $proof -and $proof.Valid) { $proof.Proof } else { $null })
    $base | Add-Member -NotePropertyName Failures -NotePropertyValue $failures.ToArray()
    return $base
}

function Assert-AzuriteFullCapabilitiesAdmission {
    [CmdletBinding()]
    param([Parameter(Mandatory)]$Contract, [Parameter(Mandatory)]$Plan, $Admission, $Facts, $EvidenceBundle, $Approval, [string]$ExpectedApprovalSha256)
    $failures = [Collections.Generic.List[string]]::new()
    $approved = $null
    try { $approved = Read-AzuriteFullCapabilitiesContract }
    catch { $failures.Add("Frozen C: contract/evidence rejected: $($_.Exception.Message)") }
    if ($null -ne $approved) { [void](Test-ApprovedInput $Contract $approved 'Contract' $failures) }
    if ($null -eq $approved -or $null -eq $Plan -or $Plan -isnot [pscustomobject]) {
        $failures.Add('Plan is missing or malformed.')
        return [pscustomobject]@{Valid=$false;Proof=$null;Failures=$failures.ToArray()}
    }
    $planNames = @('Mode','LabRoot','Executable','Command','RunId','ContractSha256','FactsSha256','ReadEnvelope','Grants','GrantDigest','MutableLeaves','RequestPolicy','BeforeImages','SealTransitions','RequiredAdmissionBindings','PlanSha256','PreparationReady','Ready','RuntimeAllowed','WillLaunch','AdmissionProof','Failures')
    if (-not (Test-Fields $Plan $planNames 'Plan' $failures)) { return [pscustomobject]@{Valid=$false;Proof=$null;Failures=$failures.ToArray()} }
    if (-not (Test-RunId $Plan.RunId 'Plan.RunId' $failures)) { return [pscustomobject]@{Valid=$false;Proof=$null;Failures=$failures.ToArray()} }
    if (-not (Test-FactsShape $Facts $failures)) { return [pscustomobject]@{Valid=$false;Proof=$null;Failures=$failures.ToArray()} }
    if ($Plan.Failures -isnot [array] -or $Plan.Failures.Count -ne 0 -or $Plan.PreparationReady -isnot [bool] -or $Plan.Ready -isnot [bool] -or $Plan.RuntimeAllowed -isnot [bool] -or $Plan.WillLaunch -isnot [bool] -or ($Plan.Ready -and $null -eq $Plan.AdmissionProof) -or (-not $Plan.Ready -and $null -ne $Plan.AdmissionProof)) { $failures.Add('Plan failure flags or proof state contradict complete preparation.'); return [pscustomobject]@{Valid=$false;Proof=$null;Failures=$failures.ToArray()} }
    if ($Plan.PreparationReady -eq $false -or $Plan.RuntimeAllowed -ne $false -or $Plan.WillLaunch -ne $false) {
        $failures.Add('Plan is a failure-state or launch-enabled plan.'); return [pscustomobject]@{Valid=$false;Proof=$null;Failures=$failures.ToArray()}
    }
    if ($null -eq $Facts -or $Facts -isnot [pscustomobject]) { $failures.Add('Facts are missing or malformed.'); return [pscustomobject]@{Valid=$false;Proof=$null;Failures=$failures.ToArray()} }
    $expectedBase = New-BasePlan $approved $Facts $Plan.RunId
    Test-PreparationFacts $Facts $approved $null $expectedBase.ReadEnvelope $Plan.RunId $failures
    if ($failures.Count) { return [pscustomobject]@{Valid=$false;Proof=$null;Failures=$failures.ToArray()} }
    if ($null -eq $Facts.transition) { $failures.Add('Facts.transition posttransition evidence is missing.'); return [pscustomobject]@{Valid=$false;Proof=$null;Failures=$failures.ToArray()} }
    $expectedHash = Get-BasePlanSha $expectedBase
    Check-Pin $Plan.PlanSha256 $expectedHash 'Plan.PlanSha256' $failures
    foreach ($name in @($expectedBase.PSObject.Properties | ForEach-Object { $_.Name })) {
        $actualJson = if ($null -ne $Plan.PSObject.Properties[$name]) { $Plan.$name | ConvertTo-Json -Depth 60 -Compress } else { $null }
        $expectedJson = $expectedBase.$name | ConvertTo-Json -Depth 60 -Compress
        if ($actualJson -cne $expectedJson) { $failures.Add("Plan.$name differs from the frozen policy.") }
    }
    $transitionNames = @('phase','runId','receiptSha256','leaseReceiptSha256','beforeImageReceiptSha256','aclBeforeImageReceiptSha256','beforeImageInventorySha256','aclBeforeImageInventorySha256','unchangedInputsReceiptSha256','warmInputOrderReceiptSha256','sealRestorationReceiptSha256','grantReceiptSha256','reviewReceiptSha256','reviewApprovalSha256','unityLogPrecreationReceiptSha256','responsePolicyReceiptSha256','cacheLeafCreateReceiptSha256','seedSha256','ownerSid','volumeGuid','leaseId','cacheLeafCreateMode','cacheLeafCreated','cacheLeafSha256','cacheLeafFileIdentity','cacheParentBeforeDescriptorSha256','cacheParentRestoredDescriptorSha256','unityLogFileIdentity','sealRestored','stageSealsExceptCacheUnchanged','sharedDelta','runtimeAllowed','precreatedUnityLogEmpty','requestArgumentsProtected')
    $admissionNames = @('schema','scope','runId','planSha256','contractSha256','labRoot','executable','operation','volumeGuid','ownerSid','controllerSid','phase0','inputHashes','cacheProofSha256','evidenceBundleSha256','approvalSha256','transition')
    if (-not (Test-Fields $Admission $admissionNames 'Admission' $failures)) {
        return [pscustomobject]@{Valid=$false;Proof=$null;Failures=$failures.ToArray()}
    }
    Check-Pin $Admission.schema 1 'Admission.schema' $failures
    Check-Pin $Admission.scope 'capabilities-only-current-account-full-restricted' 'Admission.scope' $failures
    foreach ($name in @('runId','labRoot','executable','volumeGuid','ownerSid','controllerSid')) { Check-Pin $Admission.$name $Facts.$name "Admission.$name" $failures }
    Check-Pin $Admission.operation 'capabilities' 'Admission.operation' $failures
    Check-Pin $Admission.planSha256 $expectedHash 'Admission.planSha256' $failures
    Check-Pin $Admission.contractSha256 $script:ContractSha256 'Admission.contractSha256' $failures
    $stageNames = @($approved.stage.PSObject.Properties | ForEach-Object { $_.Name })
    if (Test-Fields $Admission.phase0 $stageNames 'Admission.phase0' $failures) {
        foreach ($name in $stageNames) { Check-Pin $Admission.phase0.$name $approved.stage.$name "Admission.phase0.$name" $failures }
    }
    $inputNames = @($Facts.inputs.PSObject.Properties | ForEach-Object { $_.Name })
    if (Test-Fields $Admission.inputHashes $inputNames 'Admission.inputHashes' $failures) {
        foreach ($name in $inputNames) { Check-Pin $Admission.inputHashes.$name $Facts.inputs.$name "Admission.inputHashes.$name" $failures }
    }
    Check-Pin $Admission.cacheProofSha256 $approved.cachePrerequisites.warmResultSha256 'Admission.cacheProofSha256' $failures
    if ($null -eq $EvidenceBundle -or $null -eq $Approval -or [string]::IsNullOrEmpty($ExpectedApprovalSha256)) { $failures.Add('EvidenceBundle, actual positive Approval, and separate ExpectedApprovalSha256 are required.') }
    else {
        $approvedInput = Test-Approval $Approval $ExpectedApprovalSha256 $Plan $Facts $failures
        Check-Pin $Admission.approvalSha256 $ExpectedApprovalSha256 'Admission.approvalSha256' $failures
        Check-Pin $Facts.transition.reviewApprovalSha256 $ExpectedApprovalSha256 'Facts.transition.reviewApprovalSha256' $failures
        if (Test-EvidenceBundleBounds $EvidenceBundle $failures) {
            Check-Pin $Admission.evidenceBundleSha256 (Get-PlanHash ($EvidenceBundle | ConvertTo-Json -Depth 80 -Compress)) 'Admission.evidenceBundleSha256' $failures
            if ($null -ne $approvedInput) { Test-ReceiptBundle $EvidenceBundle $Plan $Facts $ExpectedApprovalSha256 $approvedInput $failures }
        }
    }
    $factsTransitionOk = Test-Fields $Facts.transition $transitionNames 'Facts.transition' $failures
    $admissionTransitionOk = Test-Fields $Admission.transition $transitionNames 'Admission.transition' $failures
    if ($factsTransitionOk -and $admissionTransitionOk) {
        foreach ($name in $transitionNames) { Check-Pin $Admission.transition.$name $Facts.transition.$name "Admission.transition.$name" $failures }
        Check-Pin $Facts.transition.runId $Facts.runId 'Facts.transition.runId' $failures
        Check-Pin $Facts.transition.ownerSid $Facts.ownerSid 'Facts.transition.ownerSid' $failures
        Check-Pin $Facts.transition.volumeGuid $Facts.volumeGuid 'Facts.transition.volumeGuid' $failures
        Check-Pin $Facts.transition.seedSha256 $approved.cachePrerequisites.seedSha256 'Facts.transition.seedSha256' $failures
        Check-Pin $Facts.transition.phase 'posttransition' 'Facts.transition.phase' $failures
        Check-Pin $Facts.transition.cacheLeafCreateMode 'CreateNewNoOverwrite' 'Facts.transition.cacheLeafCreateMode' $failures
        Check-Pin $Facts.transition.cacheLeafCreated $true 'Facts.transition.cacheLeafCreated' $failures
        Check-Pin $Facts.transition.cacheLeafSha256 $approved.cachePrerequisites.seedSha256 'Facts.transition.cacheLeafSha256' $failures
        foreach ($name in @('leaseId','cacheLeafFileIdentity','unityLogFileIdentity')) { if ($Facts.transition.$name -isnot [string] -or $Facts.transition.$name -cnotmatch '^[0-9a-f]{32}$' -or $Facts.transition.$name -ceq ('0'*32)) { $failures.Add("Facts.transition.$name is not a concrete lowercase 32-hex identity.") } }
        foreach ($name in @('sealRestored','stageSealsExceptCacheUnchanged','precreatedUnityLogEmpty','requestArgumentsProtected')) { Check-Pin $Facts.transition.$name $true "Facts.transition.$name" $failures }
        Check-Pin $Facts.transition.sharedDelta 'None' 'Facts.transition.sharedDelta' $failures
        Check-Pin $Facts.transition.runtimeAllowed $false 'Facts.transition.runtimeAllowed' $failures
        foreach ($name in @('receiptSha256','leaseReceiptSha256','beforeImageReceiptSha256','aclBeforeImageReceiptSha256','beforeImageInventorySha256','aclBeforeImageInventorySha256','unchangedInputsReceiptSha256','warmInputOrderReceiptSha256','sealRestorationReceiptSha256','grantReceiptSha256','reviewReceiptSha256','reviewApprovalSha256','unityLogPrecreationReceiptSha256','responsePolicyReceiptSha256','cacheLeafCreateReceiptSha256','cacheParentBeforeDescriptorSha256','cacheParentRestoredDescriptorSha256')) {
            Test-Sha $Facts.transition.$name "Facts.transition.$name" $failures
        }
        if ($Facts.transition.receiptSha256 -ceq $Facts.transition.reviewApprovalSha256 -or
            $Facts.transition.leaseReceiptSha256 -ceq $Facts.transition.reviewApprovalSha256) {
            $failures.Add('Transition, lease, and separate review receipts must be distinct.')
        }
    }
    $valid = $failures.Count -eq 0
    $proof = if ($valid) { [pscustomobject][ordered]@{
        runId=$Facts.runId;planHash=$expectedHash;contractHash=$script:ContractSha256;stageChainHashes=$approved.stage;evidenceBundleHash=(Get-PlanHash ($EvidenceBundle | ConvertTo-Json -Depth 80 -Compress));approvalHash=$ExpectedApprovalSha256;toolingManifestHash=$approvedInput.toolingManifestSha256;controlGateHash=$approvedInput.controlGateSha256;
        cacheProofHash=$approved.cachePrerequisites.warmResultSha256;controllerSid=$Facts.controllerSid;ownerSid=$Facts.ownerSid;
        volumeGuid=$Facts.volumeGuid;transitionReceiptHash=$Facts.transition.receiptSha256;
        beforeImageInventoryHash=$Facts.transition.beforeImageInventorySha256;aclBeforeImageInventoryHash=$Facts.transition.aclBeforeImageInventorySha256;
        leaseReceiptHash=$Facts.transition.leaseReceiptSha256;reviewApprovalHash=$Facts.transition.reviewApprovalSha256;
        grantReceiptHash=$Facts.transition.grantReceiptSha256;unchangedInputsReceiptHash=$Facts.transition.unchangedInputsReceiptSha256;
        warmInputOrderReceiptHash=$Facts.transition.warmInputOrderReceiptSha256;cacheLeafCreateReceiptHash=$Facts.transition.cacheLeafCreateReceiptSha256;
        sealsRestoredProofHash=$Facts.transition.sealRestorationReceiptSha256;receiptHashes=[pscustomobject][ordered]@{lease=$Facts.transition.leaseReceiptSha256;beforeImage=$Facts.transition.beforeImageReceiptSha256;aclBeforeImage=$Facts.transition.aclBeforeImageReceiptSha256;grant=$Facts.transition.grantReceiptSha256;cacheCreate=$Facts.transition.cacheLeafCreateReceiptSha256;sealRestoration=$Facts.transition.sealRestorationReceiptSha256;unchangedInputs=$Facts.transition.unchangedInputsReceiptSha256;warmInputOrder=$Facts.transition.warmInputOrderReceiptSha256;unityLogPrecreation=$Facts.transition.unityLogPrecreationReceiptSha256;responsePolicy=$Facts.transition.responsePolicyReceiptSha256;review=$Facts.transition.reviewReceiptSha256;transition=$Facts.transition.receiptSha256};cacheLeafIdentity=$Facts.transition.cacheLeafFileIdentity;unityLogIdentity=$Facts.transition.unityLogFileIdentity;preparationLeaseReleased=$true;requiresFreshRuntimeLease=$true;runtimeAllowed=$false;willLaunch=$false
    } } else { $null }
    if ($valid -and $Plan.Ready -and (($Plan.AdmissionProof | ConvertTo-Json -Depth 60 -Compress) -cne ($proof | ConvertTo-Json -Depth 60 -Compress))) {
        $failures.Add('Plan.AdmissionProof differs from the verified forward proof.'); $valid=$false; $proof=$null
    }
    return [pscustomobject]@{Valid=$valid;Proof=$proof;Failures=$failures.ToArray()}
}

Export-ModuleMember -Function Read-AzuriteFullCapabilitiesContract, New-AzuriteFullCapabilitiesPlan, Assert-AzuriteFullCapabilitiesAdmission
