Set-StrictMode -Version Latest

function Get-AzuriteArguments {
    [CmdletBinding()]
    param([Parameter(Mandatory)][ValidateSet('Logic', 'Graphics')][string]$Mode)
    if ($Mode -eq 'Logic') { return @('-batchmode', '-nographics', '-logFile', '-') }
    return @('-batchmode', '-logFile', '-')
}

function Get-AzuriteScenarioContract {
    [CmdletBinding()]
    param([Parameter(Mandatory)][ValidateSet('Logic', 'Graphics')][string]$Mode,
          [Parameter(Mandatory)][string]$Scenario)
    $catalog = @{
        'logic-smoke' = @{ mode = 'Logic'; checks = @('host.binding', 'plugin.lifecycle', 'logic.assertions'); frame = $false }
        'editor-session' = @{ mode = 'Logic'; checks = @('host.binding', 'editor.session', 'editor.native-edit', 'editor.reopen'); frame = $false }
        'graphics-frame' = @{ mode = 'Graphics'; checks = @('host.binding', 'render.frame'); frame = $true }
    }
    if (-not $catalog.ContainsKey($Scenario) -or $catalog[$Scenario].mode -cne $Mode) {
        throw "Unsupported mode/scenario contract: $Mode / $Scenario"
    }
    return [pscustomobject]@{ Mode = $Mode; Scenario = $Scenario
        RequiredChecks = [string[]]$catalog[$Scenario].checks; RequiresFrame = [bool]$catalog[$Scenario].frame }
}

function Read-AzuritePngUInt32 {
    param([byte[]]$Bytes, [int]$Offset)
    return [uint32]((([uint32]$Bytes[$Offset] -shl 24) -bor
        ([uint32]$Bytes[$Offset + 1] -shl 16) -bor
        ([uint32]$Bytes[$Offset + 2] -shl 8) -bor [uint32]$Bytes[$Offset + 3]))
}

function Initialize-AzuritePngCrc {
    if ('Azurite.Headless.PngCrc' -as [type]) { return }
    Add-Type -TypeDefinition @'
namespace Azurite.Headless {
    public static class PngCrc {
        public static uint Compute(byte[] bytes, int offset, int count) {
            uint crc = 0xffffffff;
            for (int i = offset; i < offset + count; i++) {
                crc ^= bytes[i];
                for (int bit = 0; bit < 8; bit++)
                    crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xedb88320 : crc >> 1;
            }
            return ~crc;
        }
    }
}
'@
}

function Test-AzuriteFrameArtifact {
    param([Parameter(Mandatory)]$Frame, [Parameter(Mandatory)][string]$ResultPath,
          [Parameter(Mandatory)][datetime]$StartedUtc)
    if ($Frame -isnot [Collections.IDictionary] -or $Frame.path -isnot [string] -or
        $Frame.path -cnotmatch '^[A-Za-z0-9][A-Za-z0-9._-]*\.png$' -or
        $Frame.bytes -isnot [int] -and $Frame.bytes -isnot [long] -or
        $Frame.bytes -lt 45 -or $Frame.bytes -gt 33554432 -or
        $Frame.sha256 -isnot [string] -or $Frame.sha256 -cnotmatch '^[0-9A-Fa-f]{64}$') { return $false }
    $path = Join-Path (Split-Path -Parent $ResultPath) $Frame.path
    $item = Get-Item -LiteralPath $path -Force -ErrorAction SilentlyContinue
    if (-not $item -or $item.PSIsContainer -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 -or
        $item.Length -ne $Frame.bytes -or $item.LastWriteTimeUtc -lt $StartedUtc) { return $false }
    try {
        $bytes = [IO.File]::ReadAllBytes($path)
        if ([Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)) -cne $Frame.sha256.ToUpperInvariant()) { return $false }
        if (-not ([Linq.Enumerable]::SequenceEqual([byte[]]$bytes[0..7], [byte[]]@(137,80,78,71,13,10,26,10)))) { return $false }
        Initialize-AzuritePngCrc
        $offset = 8
        $width = 0
        $height = 0
        $channels = 0
        $seenHeader = $false
        $seenData = $false
        $seenEnd = $false
        $idat = [IO.MemoryStream]::new()
        try {
            while ($offset -lt $bytes.Length) {
                if ($bytes.Length - $offset -lt 12) { return $false }
                $length = Read-AzuritePngUInt32 $bytes $offset
                if ($length -gt ($bytes.Length - $offset - 12)) { return $false }
                $type = [Text.Encoding]::ASCII.GetString($bytes, $offset + 4, 4)
                $crc = Read-AzuritePngUInt32 $bytes ($offset + 8 + [int]$length)
                if ($crc -ne [Azurite.Headless.PngCrc]::Compute($bytes, $offset + 4, [int]$length + 4)) { return $false }
                if (-not $seenHeader -and $type -cne 'IHDR') { return $false }
                switch -CaseSensitive ($type) {
                    'IHDR' {
                        if ($seenHeader -or $length -ne 13) { return $false }
                        $width = Read-AzuritePngUInt32 $bytes ($offset + 8)
                        $height = Read-AzuritePngUInt32 $bytes ($offset + 12)
                        if ($width -eq 0 -or $height -eq 0 -or $width -gt 4096 -or $height -gt 4096 -or
                            ([long]$width * $height) -gt 4194304 -or $bytes[$offset + 16] -ne 8 -or
                            $bytes[$offset + 18] -ne 0 -or $bytes[$offset + 19] -ne 0 -or $bytes[$offset + 20] -ne 0) { return $false }
                        $channels = switch ($bytes[$offset + 17]) { 0 { 1 } 2 { 3 } 4 { 2 } 6 { 4 } default { 0 } }
                        if ($channels -eq 0) { return $false }
                        $seenHeader = $true
                    }
                    'IDAT' {
                        if (-not $seenHeader -or $seenEnd) { return $false }
                        $idat.Write($bytes, $offset + 8, [int]$length)
                        $seenData = $true
                    }
                    'IEND' {
                        if (-not $seenData -or $length -ne 0 -or ($offset + 12) -ne $bytes.Length) { return $false }
                        $seenEnd = $true
                    }
                    default { if ($type[0] -cmatch '[A-Z]') { return $false } }
                }
                $offset += 12 + [int]$length
            }
            if (-not $seenEnd -or $idat.Length -eq 0) { return $false }
            $idat.Position = 0
            $zlib = [IO.Compression.ZLibStream]::new($idat, [IO.Compression.CompressionMode]::Decompress, $true)
            try {
                $scanline = [byte[]]::new(([int]$width * $channels) + 1)
                for ($row = 0; $row -lt $height; $row++) {
                    $filled = 0
                    while ($filled -lt $scanline.Length) {
                        $read = $zlib.Read($scanline, $filled, $scanline.Length - $filled)
                        if ($read -eq 0) { return $false }
                        $filled += $read
                    }
                    if ($scanline[0] -gt 4) { return $false }
                }
                if ($zlib.ReadByte() -ne -1) { return $false }
            } finally { $zlib.Dispose() }
        } finally { $idat.Dispose() }
        Add-Type -AssemblyName System.Drawing
        $imageStream = [IO.MemoryStream]::new($bytes, $false)
        try {
            $image = [Drawing.Image]::FromStream($imageStream, $true, $true)
            try {
                if ($image.Width -ne $width -or $image.Height -ne $height) { return $false }
                $decoded = [Drawing.Bitmap]::new($image)
                try { [void]$decoded.GetPixel($decoded.Width - 1, $decoded.Height - 1) }
                finally { $decoded.Dispose() }
            } finally { $image.Dispose() }
        } finally { $imageStream.Dispose() }
        return $true
    } catch { return $false }
}

function Get-AzuriteAccountDescription {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$OwnerToken)
    $token = [guid]::Empty
    if (-not [guid]::TryParse($OwnerToken, [ref]$token)) { throw 'Invalid account owner token.' }
    return 'AzuriteLab:' + $token.ToString('N')
}

function Assert-AzuritePrivateAcl {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Path, [Parameter(Mandatory)][string]$ControllerSid,
          [Security.AccessControl.FileSystemSecurity]$AclObject)
    $full = Assert-AzuriteFullPath $Path
    $acl = if ($PSBoundParameters.ContainsKey('AclObject')) { $AclObject } else { Get-Acl -LiteralPath $full -ErrorAction Stop }
    if (-not $acl.AreAccessRulesProtected) { throw "Private ACL inherits permissions: $full" }
    $trusted = @($ControllerSid, 'S-1-5-18', 'S-1-5-32-544')
    $ownerSid = $acl.GetOwner([Security.Principal.SecurityIdentifier]).Value
    if ($ownerSid -notin $trusted) { throw "Private ACL has unexpected owner: $full ($ownerSid)" }
    $isDirectory = Test-Path -LiteralPath $full -PathType Container
    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($entry in $acl.Access) {
        $entrySid = $entry.IdentityReference.Translate([Security.Principal.SecurityIdentifier]).Value
        if ($entry.AccessControlType -ne 'Allow' -or $entrySid -notin $trusted) {
            throw "Private ACL contains an unexpected entry: $full ($entrySid)"
        }
        if (($entry.FileSystemRights -band [Security.AccessControl.FileSystemRights]::FullControl) -ne [Security.AccessControl.FileSystemRights]::FullControl) {
            throw "Private ACL lacks FullControl: $full ($entrySid)"
        }
        $expectedInheritance = if ($isDirectory) { [Security.AccessControl.InheritanceFlags]'ContainerInherit,ObjectInherit' } else { [Security.AccessControl.InheritanceFlags]::None }
        if ($entry.InheritanceFlags -ne $expectedInheritance -or $entry.PropagationFlags -ne [Security.AccessControl.PropagationFlags]::None) {
            throw "Private ACL has unexpected inheritance scope: $full ($entrySid)"
        }
        [void]$seen.Add($entrySid)
    }
    foreach ($sid in $trusted) { if (-not $seen.Contains($sid)) { throw "Private ACL lacks required SID: $full ($sid)" } }
}

function Set-AzuriteLabAcl {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$LabRoot, [Parameter(Mandatory)][string]$Path,
          [Parameter(Mandatory)][string]$ControllerSid, [string]$TestSid,
          [ValidateSet('ReadAndExecute', 'Modify')][string]$TestRights = 'ReadAndExecute')
    $lab = Assert-AzuriteFullPath $LabRoot
    $full = Assert-AzuriteFullPath $Path
    if (-not ($full.Equals($lab, [StringComparison]::OrdinalIgnoreCase) -or (Test-AzuritePathWithin $full $lab))) {
        throw "ACL target escapes the dedicated lab: $full"
    }
    $item = Get-Item -LiteralPath $full -Force -ErrorAction Stop
    $inherit = if ($item.PSIsContainer) { '(OI)(CI)' } else { '' }
    $grants = @(
        ('*' + $ControllerSid + ':' + $inherit + 'F'),
        ('*S-1-5-18:' + $inherit + 'F'),
        ('*S-1-5-32-544:' + $inherit + 'F')
    )
    if ($TestSid) {
        $right = if ($TestRights -eq 'Modify') { 'M' } else { 'RX' }
        $grants += '*' + $TestSid + ':' + $inherit + $right
    }
    & icacls.exe $full '/inheritance:r' | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "ACL inheritance removal failed: $full" }
    $desiredSids = @($ControllerSid, 'S-1-5-18', 'S-1-5-32-544')
    if ($TestSid) { $desiredSids += $TestSid }
    $before = Get-Acl -LiteralPath $full
    $obsolete = @($before.Access | ForEach-Object {
        try { $sid = $_.IdentityReference.Translate([Security.Principal.SecurityIdentifier]).Value }
        catch { $sid = $_.IdentityReference.Value }
        if ($sid -notin $desiredSids) { [pscustomobject]@{ Identity = $sid; Type = $_.AccessControlType.ToString() } }
    } | Sort-Object Identity, Type -Unique)
    & icacls.exe $full '/grant:r' @grants | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "ACL update failed: $full" }
    foreach ($entry in $obsolete) {
        $remove = if ($entry.Type -eq 'Deny') { '/remove:d' } else { '/remove:g' }
        $identity = if ($entry.Identity -match '^S-1-') { '*' + $entry.Identity } else { $entry.Identity }
        & icacls.exe $full $remove $identity | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "Obsolete ACL entry removal failed: $full ($($entry.Identity))" }
    }
    $acl = Get-Acl -LiteralPath $full
    if (-not $acl.AreAccessRulesProtected) { throw "ACL remains inherited: $full" }
    $actualSids = @($acl.Access | ForEach-Object {
        if ($_.IsInherited -or $_.AccessControlType -ne 'Allow') { throw "ACL has unexpected grant type: $full" }
        $_.IdentityReference.Translate([Security.Principal.SecurityIdentifier]).Value
    } | Sort-Object -Unique)
    $expectedSids = @($desiredSids | Sort-Object -Unique)
    if (($actualSids -join '|') -cne ($expectedSids -join '|') -or $acl.Access.Count -ne $expectedSids.Count) {
        throw "ACL retains unexpected grants: $full"
    }
    if (-not $TestSid) {
        Assert-AzuritePrivateAcl -Path $full -ControllerSid $ControllerSid
    } else {
        Assert-AzuriteAclRoot -Path $full -ControllerSid $ControllerSid -TestSid $TestSid
        $testEntries = @($acl.Access | Where-Object {
            $_.IdentityReference.Translate([Security.Principal.SecurityIdentifier]).Value -ceq $TestSid -and
            $_.AccessControlType -eq 'Allow'
        })
        $requiredRight = if ($TestRights -eq 'Modify') { [Security.AccessControl.FileSystemRights]::Modify } else { [Security.AccessControl.FileSystemRights]::ReadAndExecute }
        if (-not @($testEntries | Where-Object { ($_.FileSystemRights -band $requiredRight) -eq $requiredRight }).Count) {
            throw "Dedicated account ACL grant is insufficient: $full"
        }
        if ($TestRights -eq 'ReadAndExecute' -and @($testEntries | Where-Object {
            ($_.FileSystemRights -band [Security.AccessControl.FileSystemRights]::Write) -ne 0
        }).Count) { throw "Read-only tooling ACL grants write: $full" }
    }
}

function Write-AzuritePrivateJson {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$LabRoot, [Parameter(Mandatory)][string]$Path,
          [Parameter(Mandatory)]$Value, [Parameter(Mandatory)][string]$ControllerSid)
    $full = Assert-AzuriteFullPath $Path
    $temporary = $full + '.' + [guid]::NewGuid().ToString('N') + '.tmp'
    try {
        $Value | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $temporary -Encoding utf8 -NoNewline
        Set-AzuriteLabAcl -LabRoot $LabRoot -Path $temporary -ControllerSid $ControllerSid
        Move-Item -LiteralPath $temporary -Destination $full -Force
        Assert-AzuritePrivateAcl -Path $full -ControllerSid $ControllerSid
    } finally { Remove-Item -LiteralPath $temporary -Force -ErrorAction SilentlyContinue }
}

function Test-AzuriteCommittedCredential {
    [CmdletBinding()]
    param([Parameter(Mandatory)]$Manifest, [Parameter(Mandatory)]$CredentialRecord,
          [Parameter(Mandatory)][string]$LabId, [Parameter(Mandatory)][string]$ControllerSid,
          [Parameter(Mandatory)][string]$AccountSid)
    try {
        if ($Manifest.credentialSha256 -cnotmatch '^[0-9A-F]{64}$' -or
            $CredentialRecord.schema -ne 1 -or $CredentialRecord.labId -cne $LabId -or
            $CredentialRecord.controllerSid -cne $ControllerSid -or
            $CredentialRecord.accountSid -cne $AccountSid -or
            [string]::IsNullOrWhiteSpace($CredentialRecord.encryptedPassword)) { return $false }
        $digest = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($CredentialRecord.encryptedPassword)))
        if ($digest -cne $Manifest.credentialSha256) { return $false }
        $restored = ConvertTo-SecureString -String $CredentialRecord.encryptedPassword -ErrorAction Stop
        if (-not $restored) { return $false }
        $restored.Dispose()
        return $true
    } catch { return $false }
}

function Get-AzuriteLocalMembershipSids {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$AccountSid)
    $memberships = [Collections.Generic.List[string]]::new()
    foreach ($group in @(Get-LocalGroup -ErrorAction Stop)) {
        foreach ($member in @(Get-LocalGroupMember -Group $group.Name -ErrorAction Stop)) {
            if ($member.SID.Value -ceq $AccountSid) { $memberships.Add($group.SID.Value) }
        }
    }
    return $memberships.ToArray()
}

function Assert-AzuriteLowPrivilegeGroups {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$AccountSid, [string[]]$MembershipSids)
    if (-not $PSBoundParameters.ContainsKey('MembershipSids')) {
        $MembershipSids = @(Get-AzuriteLocalMembershipSids -AccountSid $AccountSid)
    }
    if ($MembershipSids.Count -ne 1 -or $MembershipSids[0] -cne 'S-1-5-32-545') {
        throw "Dedicated account has unexpected local group membership: $($MembershipSids -join ', ')"
    }
}

function Invoke-AzuriteAccountProvision {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$OwnerToken,
          [Parameter(Mandatory)][Security.SecureString]$Password,
          $ExistingAccount, [string]$RecordedSid,
          [Parameter(Mandatory)][scriptblock]$CreateUser,
          [Parameter(Mandatory)][scriptblock]$ReadMemberships,
          [Parameter(Mandatory)][scriptblock]$AddUsersMember,
          [Parameter(Mandatory)][scriptblock]$ResetPassword,
          [bool]$ResetExistingPassword = $true)
    $description = Get-AzuriteAccountDescription -OwnerToken $OwnerToken
    $wasExisting = $null -ne $ExistingAccount
    if ($wasExisting) {
        if (-not $ExistingAccount.Enabled -or $ExistingAccount.Description -cne $description -or
            ($RecordedSid -and $ExistingAccount.SID.Value -cne $RecordedSid)) {
            throw 'Existing AzuriteTest account is not manifest-owned.'
        }
        $account = $ExistingAccount
    } else {
        if ($RecordedSid) { throw 'Manifest refers to a deleted account; manual review is required.' }
        $account = & $CreateUser 'AzuriteTest' $description $Password
        if (-not $account -or -not $account.SID.Value) { throw 'Account creation did not return a SID.' }
    }
    $membershipSids = @(& $ReadMemberships $account.SID.Value)
    if (@($membershipSids | Where-Object { $_ -cne 'S-1-5-32-545' }).Count -gt 0) {
        throw 'Dedicated account has unexpected local group membership.'
    }
    if ($membershipSids.Count -eq 0) {
        & $AddUsersMember $account.SID.Value
        $membershipSids = @(& $ReadMemberships $account.SID.Value)
    }
    Assert-AzuriteLowPrivilegeGroups -AccountSid $account.SID.Value -MembershipSids $membershipSids
    if ($wasExisting -and $ResetExistingPassword) { & $ResetPassword 'AzuriteTest' $Password }
    return $account
}

function Assert-AzuriteFullPath {
    param([Parameter(Mandatory)][string]$Path)
    if ($Path -notmatch '^[A-Za-z]:\\' -or $Path -match '(^|[\\/])\.\.([\\/]|$)') {
        throw "An absolute local path without parent traversal is required: $Path"
    }
    $full = [IO.Path]::GetFullPath($Path)
    $cursor = [IO.Path]::GetPathRoot($full)
    foreach ($part in $full.Substring($cursor.Length).Split([char]'\', [StringSplitOptions]::RemoveEmptyEntries)) {
        $cursor = Join-Path $cursor $part
        $item = Get-Item -LiteralPath $cursor -Force -ErrorAction SilentlyContinue
        if ($item) {
            if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "Reparse path is prohibited: $cursor"
            }
        }
    }
    return $full
}

function Test-AzuritePathWithin {
    param([string]$Child, [string]$Parent)
    return $Child.StartsWith($Parent.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)
}

function Assert-AzuriteLabPaths {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$LabRoot,
        [Parameter(Mandatory)][string]$InstallPath,
        [Parameter(Mandatory)][string]$DailyInstallPath
    )
    $lab = Assert-AzuriteFullPath $LabRoot
    $install = Assert-AzuriteFullPath $InstallPath
    $daily = Assert-AzuriteFullPath $DailyInstallPath
    if (-not (Test-AzuritePathWithin $install $lab)) { throw 'Install must be inside the dedicated lab.' }
    if ($lab.Equals($daily, [StringComparison]::OrdinalIgnoreCase) -or
        (Test-AzuritePathWithin $lab $daily) -or (Test-AzuritePathWithin $daily $lab) -or
        $install.Equals($daily, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Lab and daily installation overlap.'
    }
    return $install
}

function Assert-AzuriteNoReparseTree {
    param([Parameter(Mandatory)][string]$Path)
    $full = Assert-AzuriteFullPath $Path
    if (-not (Test-Path -LiteralPath $full -PathType Container)) { throw "Directory missing: $full" }
    $links = Get-ChildItem -LiteralPath $full -Force -Recurse -Attributes ReparsePoint -ErrorAction Stop |
        Select-Object -First 1
    if ($links) { throw "Lab contains a reparse link: $($links.FullName)" }
}

function Assert-AzuriteAclTree {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Path, [Parameter(Mandatory)][string]$ControllerSid, [string]$TestSid)
    $root = Assert-AzuriteFullPath $Path
    if (-not (Test-Path -LiteralPath $root -PathType Container)) { throw "ACL tree missing: $root" }
    $trusted = @('S-1-5-18', 'S-1-5-32-544', $ControllerSid)
    if ($TestSid) { $trusted += $TestSid }
    foreach ($item in @(Get-ChildItem -LiteralPath $root -Force -Recurse -ErrorAction Stop)) {
        $acl = Get-Acl -LiteralPath $item.FullName -ErrorAction Stop
        if ($acl.AreAccessRulesProtected) { throw "Protected descendant ACL: $($item.FullName)" }
        $ownerSid = ([Security.Principal.NTAccount]::new($acl.Owner)).Translate([Security.Principal.SecurityIdentifier]).Value
        if ($ownerSid -notin $trusted) { throw "Unexpected descendant owner: $($item.FullName) ($ownerSid)" }
        foreach ($entry in $acl.Access) {
            if ($entry.IsInherited) { continue }
            $entrySid = $entry.IdentityReference.Translate([Security.Principal.SecurityIdentifier]).Value
            if ($entry.AccessControlType -eq 'Deny') { throw "Explicit descendant deny: $($item.FullName) ($entrySid)" }
            $writeRights = [Security.AccessControl.FileSystemRights]'WriteData,AppendData,WriteExtendedAttributes,WriteAttributes,DeleteSubdirectoriesAndFiles,Delete,ChangePermissions,TakeOwnership'
            if ($entrySid -notin $trusted -and ($entry.FileSystemRights -band $writeRights) -ne 0) {
                throw "Broad descendant write access: $($item.FullName) ($entrySid)"
            }
        }
    }
}

function Assert-AzuriteAclRoot {
    param([Parameter(Mandatory)][string]$Path, [Parameter(Mandatory)][string]$ControllerSid, [string]$TestSid)
    $acl = Get-Acl -LiteralPath $Path -ErrorAction Stop
    $trusted = @('S-1-5-18', 'S-1-5-32-544', $ControllerSid)
    if ($TestSid) { $trusted += $TestSid }
    $ownerSid = ([Security.Principal.NTAccount]::new($acl.Owner)).Translate([Security.Principal.SecurityIdentifier]).Value
    if ($ownerSid -notin $trusted) { throw "Unexpected ACL root owner: $Path ($ownerSid)" }
    foreach ($entry in $acl.Access) {
        if ($entry.IsInherited) { continue }
        $entrySid = $entry.IdentityReference.Translate([Security.Principal.SecurityIdentifier]).Value
        if ($entry.AccessControlType -eq 'Deny') { throw "Explicit ACL root deny: $Path ($entrySid)" }
        $writeRights = [Security.AccessControl.FileSystemRights]'WriteData,AppendData,WriteExtendedAttributes,WriteAttributes,DeleteSubdirectoriesAndFiles,Delete,ChangePermissions,TakeOwnership'
        if ($entrySid -notin $trusted -and ($entry.FileSystemRights -band $writeRights) -ne 0) {
            throw "Broad ACL root write access: $Path ($entrySid)"
        }
    }
}

function Assert-AzuriteToolingAcl {
    param([Parameter(Mandatory)][string]$Path, [Parameter(Mandatory)][string]$ControllerSid)
    $acl = Get-Acl -LiteralPath $Path -ErrorAction Stop
    $writers = @('S-1-5-18', 'S-1-5-32-544', $ControllerSid)
    $ownerSid = $acl.GetOwner([Security.Principal.SecurityIdentifier]).Value
    if ($ownerSid -notin $writers) { throw "Unexpected tooling owner: $Path ($ownerSid)" }
    $writeRights = [Security.AccessControl.FileSystemRights]'WriteData,AppendData,WriteExtendedAttributes,WriteAttributes,DeleteSubdirectoriesAndFiles,Delete,ChangePermissions,TakeOwnership'
    foreach ($entry in $acl.Access) {
        $entrySid = $entry.IdentityReference.Translate([Security.Principal.SecurityIdentifier]).Value
        if ($entry.AccessControlType -eq 'Deny') { throw "Tooling ACL contains a deny: $Path ($entrySid)" }
        if ($entrySid -notin $writers -and ($entry.FileSystemRights -band $writeRights) -ne 0) {
            throw "Tooling grants write access beyond trusted owners: $Path ($entrySid)"
        }
    }
}

function Get-AzuriteSetupPrerequisites {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$LabRoot, [Parameter(Mandatory)][string]$ControllerSid,
          [Parameter(Mandatory)][string]$PreflightSource, [string]$TestSid)
    $failures = [Collections.Generic.List[string]]::new()
    $install = Join-Path $LabRoot 'install'
    $required = @(
        @{ path = $install; kind = 'Container' },
        @{ path = (Join-Path $install 'AzureArchive.exe'); kind = 'Leaf' },
        @{ path = (Join-Path $install 'ActiveProfile.txt'); kind = 'Leaf' },
        @{ path = (Join-Path $install 'profiles\AzuriteAcceptance\modconfig.json'); kind = 'Leaf' },
        @{ path = (Join-Path $install 'profiles\AzuriteAcceptance\configs'); kind = 'Container' },
        @{ path = $PreflightSource; kind = 'Leaf' }
    )
    foreach ($need in $required) {
        if (-not (Test-Path -LiteralPath $need.path -PathType $need.kind)) { $failures.Add("Required $($need.kind) missing: $($need.path)") }
    }
    $profileFile = Join-Path $install 'ActiveProfile.txt'
    if (Test-Path -LiteralPath $profileFile -PathType Leaf) {
        if ((Get-Content -LiteralPath $profileFile -Raw).Trim() -cne 'AzuriteAcceptance') { $failures.Add("Wrong active profile: $profileFile") }
    }
    foreach ($directory in @('workspace', 'user-data', 'runs', 'tooling', 'secrets')) {
        $path = Join-Path $LabRoot $directory
        if ((Test-Path -LiteralPath $path) -and -not (Test-Path -LiteralPath $path -PathType Container)) { $failures.Add("Directory path is occupied by a file: $path") }
    }
    foreach ($file in @('account.manifest.json', 'secrets\controller.credential.json', 'tooling\Identity-Preflight.ps1')) {
        $path = Join-Path $LabRoot $file
        if ((Test-Path -LiteralPath $path) -and -not (Test-Path -LiteralPath $path -PathType Leaf)) { $failures.Add("File path is occupied by a directory: $path") }
    }
    $systemPowerShell = Join-Path ([Environment]::SystemDirectory) 'WindowsPowerShell\v1.0\powershell.exe'
    if (-not (Test-Path -LiteralPath $systemPowerShell -PathType Leaf)) {
        $failures.Add("Machine-wide Windows PowerShell missing: $systemPowerShell")
    } else {
        try {
            [void](Assert-AzuriteFullPath $systemPowerShell)
            $shellAcl = Get-Acl -LiteralPath $systemPowerShell
            $usersRead = $false
            foreach ($entry in $shellAcl.Access) {
                try { $entrySid = $entry.IdentityReference.Translate([Security.Principal.SecurityIdentifier]).Value }
                catch { continue }
                if ($entry.AccessControlType -eq 'Allow' -and $entrySid -ceq 'S-1-5-32-545' -and
                    ($entry.FileSystemRights -band [Security.AccessControl.FileSystemRights]::ReadAndExecute) -eq [Security.AccessControl.FileSystemRights]::ReadAndExecute) {
                    $usersRead = $true
                    break
                }
            }
            if (-not $usersRead) { $failures.Add("Machine-wide PowerShell lacks Users ReadAndExecute: $systemPowerShell") }
        } catch { $failures.Add("Machine-wide PowerShell access cannot be verified: $systemPowerShell") }
    }
    foreach ($directory in @('install', 'workspace', 'user-data', 'runs')) {
        $path = Join-Path $LabRoot $directory
        if (Test-Path -LiteralPath $path -PathType Container) {
            try { Assert-AzuriteAclRoot -Path $path -ControllerSid $ControllerSid -TestSid $TestSid }
            catch { $failures.Add($_.Exception.Message) }
            try { Assert-AzuriteAclTree -Path $path -ControllerSid $ControllerSid -TestSid $TestSid }
            catch { $failures.Add($_.Exception.Message) }
        }
    }
    $tooling = Join-Path $LabRoot 'tooling'
    if (Test-Path -LiteralPath $tooling -PathType Container) {
        try { Assert-AzuriteToolingAcl -Path $tooling -ControllerSid $ControllerSid }
        catch { $failures.Add($_.Exception.Message) }
        $expectedPreflight = Join-Path $tooling 'Identity-Preflight.ps1'
        foreach ($item in @(Get-ChildItem -LiteralPath $tooling -Force -Recurse -ErrorAction Stop)) {
            if (-not $item.FullName.Equals($expectedPreflight, [StringComparison]::OrdinalIgnoreCase) -or $item.PSIsContainer) {
                $failures.Add("Unexpected tooling entry: $($item.FullName)")
            }
            try { Assert-AzuriteToolingAcl -Path $item.FullName -ControllerSid $ControllerSid }
            catch { $failures.Add($_.Exception.Message) }
        }
    }
    try { Assert-AzuriteAclRoot -Path $LabRoot -ControllerSid $ControllerSid -TestSid $TestSid }
    catch { $failures.Add($_.Exception.Message) }
    return [pscustomobject]@{ Ready = $failures.Count -eq 0; Failures = $failures.ToArray(); SystemPowerShell = $systemPowerShell }
}

function Initialize-AzuriteWindowProbe {
    if ('Azurite.Headless.WindowProbe' -as [type]) { return }
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
namespace Azurite.Headless {
    public static class WindowProbe {
        private delegate bool EnumCallback(IntPtr hWnd, IntPtr data);
        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumCallback callback, IntPtr data);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        public static bool HasVisibleWindow(int processId) {
            bool found = false;
            EnumWindows((window, data) => {
                uint pid;
                GetWindowThreadProcessId(window, out pid);
                if (pid == (uint)processId && IsWindowVisible(window)) found = true;
                return !found;
            }, IntPtr.Zero);
            return found;
        }
        public static bool HasForegroundWindow(int processId) {
            IntPtr window = GetForegroundWindow();
            if (window == IntPtr.Zero) return false;
            uint pid;
            GetWindowThreadProcessId(window, out pid);
            return pid == (uint)processId;
        }
    }
}
'@
}

function Invoke-AzuriteChild {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$FilePath,
        [Parameter(Mandatory)][string[]]$ArgumentList,
        [Parameter(Mandatory)][string]$WorkingDirectory,
        [Parameter(Mandatory)][string]$RunId,
        [Parameter(Mandatory)][string]$ResultPath,
        [Parameter(Mandatory)][ValidateRange(1, 3600)][int]$TimeoutSeconds,
        [pscredential]$Credential,
        [hashtable]$ChildEnvironment,
        [hashtable]$NativeExpectation,
        [switch]$UseArgumentMetadata,
        [switch]$PlanOnly
    )
    $executable = Assert-AzuriteFullPath $FilePath
    $working = Assert-AzuriteFullPath $WorkingDirectory
    $artifact = Assert-AzuriteFullPath $ResultPath
    if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) { throw "Executable missing: $executable" }
    if (-not (Test-Path -LiteralPath $working -PathType Container)) { throw "Working directory missing: $working" }
    if (Test-Path -LiteralPath $artifact) { throw 'Result path must be new for this run.' }
    if (-not (Test-Path -LiteralPath (Split-Path -Parent $artifact) -PathType Container)) { throw 'Result parent directory missing.' }
    if ($Credential -and (-not $UseArgumentMetadata -or $ChildEnvironment)) {
        throw 'Credentialed children require argument metadata and no explicit controller environment.'
    }
    $startInfo = [Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $executable
    $startInfo.WorkingDirectory = $working
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    foreach ($argument in $ArgumentList) { [void]$startInfo.ArgumentList.Add($argument) }
    if ($UseArgumentMetadata) {
        foreach ($pair in @(@('AzuriteRunId', $RunId), @('AzuriteResultPath', $artifact))) {
            [void]$startInfo.ArgumentList.Add('-' + $pair[0])
            [void]$startInfo.ArgumentList.Add([string]$pair[1])
        }
        if ($NativeExpectation) {
            foreach ($pair in @(@('AzuriteMode', $NativeExpectation.Mode), @('AzuriteScenario', $NativeExpectation.Scenario), @('AzuriteAccountSid', $NativeExpectation.AccountSid))) {
                [void]$startInfo.ArgumentList.Add('-' + $pair[0])
                [void]$startInfo.ArgumentList.Add([string]$pair[1])
            }
        }
    } else {
        if ($ChildEnvironment) {
        foreach ($key in $ChildEnvironment.Keys) {
            if ($key -notin @('AZURITE_MODE', 'AZURITE_SCENARIO', 'AZURITE_ACCOUNT_SID')) { throw "Unsupported child environment key: $key" }
            $startInfo.Environment[$key] = [string]$ChildEnvironment[$key]
        }
        }
        $startInfo.Environment['AZURITE_RUN_ID'] = $RunId
        $startInfo.Environment['AZURITE_RESULT_PATH'] = $artifact
    }
    if ($Credential) {
        $startInfo.UserName = $Credential.UserName
        $startInfo.Domain = '.'
        $startInfo.Password = $Credential.Password
        $startInfo.LoadUserProfile = $true
    }
    if ($PlanOnly) {
        return [pscustomobject]@{ FilePath = $executable; Arguments = @($startInfo.ArgumentList)
            EnvironmentSource = if ($Credential) { 'TargetProfile' } else { 'ControllerFixture' }
            ExplicitEnvironmentCount = if ($UseArgumentMetadata) { 0 } else { $startInfo.Environment.Count }
            WillLaunch = $false }
    }
    Initialize-AzuriteWindowProbe
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    $startedAt = [datetime]::UtcNow
    $timedOut = $false
    $windowViolation = $false
    $started = $false
    $watch = [Diagnostics.Stopwatch]::StartNew()
    try {
        if (-not $process.Start()) { throw 'Child process did not start.' }
        $started = $true
        $ownedPid = $process.Id
        $stdoutTask = $process.StandardOutput.ReadToEndAsync()
        $stderrTask = $process.StandardError.ReadToEndAsync()
        try {
            if (-not $process.HasExited) { $process.PriorityClass = [Diagnostics.ProcessPriorityClass]::BelowNormal }
        } catch {
            if (-not $process.HasExited) { throw 'Could not lower child priority.' }
        }
        while (-not $process.HasExited) {
            if ([Azurite.Headless.WindowProbe]::HasVisibleWindow($ownedPid) -or
                [Azurite.Headless.WindowProbe]::HasForegroundWindow($ownedPid)) {
                $windowViolation = $true
                break
            }
            if ($watch.Elapsed.TotalSeconds -ge $TimeoutSeconds) { $timedOut = $true; break }
            Start-Sleep -Milliseconds 50
            $process.Refresh()
        }
        if (($timedOut -or $windowViolation) -and -not $process.HasExited) { $process.Kill($false) }
        if (-not $process.WaitForExit(1000)) { throw 'Owned child did not exit after cleanup.' }
        $remainingMilliseconds = [Math]::Max(0, [int](($TimeoutSeconds - $watch.Elapsed.TotalSeconds) * 1000))
        $streams = [Threading.Tasks.Task]::WhenAll([Threading.Tasks.Task[]]@($stdoutTask, $stderrTask))
        if (-not $streams.Wait($remainingMilliseconds)) { $timedOut = $true }
        $stdout = if ($stdoutTask.IsCompletedSuccessfully) { $stdoutTask.GetAwaiter().GetResult() } else { '' }
        $stderr = if ($stderrTask.IsCompletedSuccessfully) { $stderrTask.GetAwaiter().GetResult() } else { '' }
        if ($timedOut) {
            $process.StandardOutput.Close()
            $process.StandardError.Close()
        }
        $valid = $false
        $result = $null
        if (-not $timedOut -and -not $windowViolation -and $process.ExitCode -eq 0 -and
            (Test-Path -LiteralPath $artifact -PathType Leaf)) {
            $file = Get-Item -LiteralPath $artifact
            if ($file.LastWriteTimeUtc -ge $startedAt) {
                try {
                    $result = Get-Content -LiteralPath $artifact -Raw | ConvertFrom-Json -AsHashtable
                    $valid = ($result -is [Collections.IDictionary] -and $result.runId -is [string] -and
                        $result.runId -ceq $RunId -and ($result.pid -is [int] -or $result.pid -is [long]) -and
                        $result.pid -eq $ownedPid -and $result.status -is [string] -and $result.status -ceq 'passed')
                    if ($valid -and $NativeExpectation) {
                        $contract = Get-AzuriteScenarioContract -Mode $NativeExpectation.Mode -Scenario $NativeExpectation.Scenario
                        $checks = @($result.checks)
                        $names = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
                        $valid = ($result.mode -ceq $NativeExpectation.Mode -and
                            $result.mode -is [string] -and $result.scenario -is [string] -and
                            $result.scenario -ceq $NativeExpectation.Scenario -and
                            $result.accountSid -is [string] -and $result.accountSid -ceq $NativeExpectation.AccountSid -and
                            $result.checks -is [array] -and $checks.Count -gt 0)
                        foreach ($check in $checks) {
                            if (-not $valid -or $check -isnot [Collections.IDictionary] -or $check.name -isnot [string] -or
                                [string]::IsNullOrWhiteSpace($check.name) -or $check.passed -isnot [bool] -or
                                $check.passed -cne $true -or -not $names.Add([string]$check.name)) {
                                $valid = $false
                                break
                            }
                        }
                        foreach ($required in $contract.RequiredChecks) { if (-not $names.Contains($required)) { $valid = $false } }
                        if ($valid -and $contract.RequiresFrame) { $valid = Test-AzuriteFrameArtifact -Frame $result.frame -ResultPath $artifact -StartedUtc $startedAt }
                    }
                } catch { $valid = $false }
            }
        }
        return [pscustomobject]@{
            Pid = $ownedPid; ExitCode = $process.ExitCode; TimedOut = $timedOut
            WindowViolation = $windowViolation; ResultValid = $valid; Result = $result
            Stdout = $stdout; Stderr = $stderr; StartedUtc = $startedAt.ToString('o')
        }
    } finally {
        if ($started -and -not $process.HasExited) { $process.Kill($false); [void]$process.WaitForExit(5000) }
        $process.Dispose()
    }
}

Export-ModuleMember -Function Get-AzuriteArguments, Get-AzuriteScenarioContract, Get-AzuriteAccountDescription, Invoke-AzuriteAccountProvision, Get-AzuriteLocalMembershipSids, Test-AzuriteCommittedCredential, Assert-AzuritePrivateAcl, Set-AzuriteLabAcl, Write-AzuritePrivateJson, Assert-AzuriteLowPrivilegeGroups, Assert-AzuriteFullPath, Assert-AzuriteLabPaths, Assert-AzuriteNoReparseTree, Assert-AzuriteAclTree, Get-AzuriteSetupPrerequisites, Invoke-AzuriteChild
