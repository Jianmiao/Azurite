#requires -Version 7.2
param(
    [Parameter(Mandatory)][string]$AAInstallPath,
    [Parameter(Mandatory)][string]$ArchivePath,
    [Parameter(Mandatory)][string]$BackupDirectory
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if (@(Get-Process AzureArchive -ErrorAction SilentlyContinue).Count -ne 0) { throw 'AA must be closed.' }
$aa = (Resolve-Path -LiteralPath $AAInstallPath).Path
$archive = (Resolve-Path -LiteralPath $ArchivePath).Path
$backup = [IO.Path]::GetFullPath($BackupDirectory)
if (-not $backup.StartsWith('E:\aamod\backups\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Backup must be inside E:\aamod\backups.' }
if (Test-Path -LiteralPath $backup) { throw 'Preserve existing backup; choose a new directory.' }
$validation = & (Join-Path $PSScriptRoot 'Test-ReleaseArchive.ps1') -ArchivePath $archive -RequireSidecar
$version = $validation.Version
$active = [IO.File]::ReadAllText((Join-Path $aa 'ActiveProfile.txt')).Trim()
if ($active -match '[\\/]' -or $active -in @('', '.', '..')) { throw 'Invalid active Profile name.' }
$profilePath = Join-Path $aa ('profiles\' + $active + '\modconfig.json')
$cfgPath = Join-Path $aa ('profiles\' + $active + '\configs\halocue.azurite.cfg')
$profileHash = (Get-FileHash -LiteralPath $profilePath).Hash
$cfgHash = (Get-FileHash -LiteralPath $cfgPath).Hash
$before = [IO.File]::ReadAllText($profilePath)
$profile = $before | ConvertFrom-Json
$entries = @($profile.EnabledMods | Where-Object name -eq 'Azurite')
if ($entries.Count -ne 1) { throw 'Expected exactly one selected Azurite.' }
$previous = $entries[0].version
$mods = [IO.Path]::GetFullPath((Join-Path $aa 'mods'))
$target = [IO.Path]::GetFullPath((Join-Path $mods ('Azurite\' + $version)))
if (-not $target.StartsWith($mods.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Target escaped mods.' }
if (Test-Path -LiteralPath $target) { throw 'Never overwrite an installed same-version folder.' }
New-Item -ItemType Directory -Path $backup | Out-Null
Copy-Item -LiteralPath $profilePath -Destination (Join-Path $backup 'modconfig.json')
Copy-Item -LiteralPath $cfgPath -Destination (Join-Path $backup 'halocue.azurite.cfg')
$stage = Join-Path $backup 'validated-install-stage'
[IO.Compression.ZipFile]::ExtractToDirectory($archive, $stage)
$payload = Join-Path $stage ('Azurite\' + $version)
$manifest = Get-Content -LiteralPath (Join-Path $payload 'manifest.json') -Raw | ConvertFrom-Json
if ($manifest.name -ne 'Azurite' -or $manifest.version_number -ne $version) { throw 'Staging identity mismatch.' }
if (@(Get-Process AzureArchive -ErrorAction SilentlyContinue).Count -ne 0) { throw 'AA started during validation; no installation performed.' }
# New version only: old install and all other Mods remain untouched.
Copy-Item -LiteralPath $payload -Destination $target -Recurse
foreach ($line in Get-Content -LiteralPath (Join-Path $target 'SHA256SUMS.txt')) {
    if ($line -notmatch '^([0-9a-f]{64})  ([^/\\]+)$') { throw 'Malformed installed checksum.' }
    $expected=$Matches[1]; $name=$Matches[2]
    if ((Get-FileHash -LiteralPath (Join-Path $target $name)).Hash.ToLowerInvariant() -ne $expected) { throw ('Installed file mismatch: '+$name) }
}
if ((Get-FileHash -LiteralPath $profilePath).Hash -ne $profileHash -or (Get-FileHash -LiteralPath $cfgPath).Hash -ne $cfgHash) { throw 'Profile changed during installation; new candidate kept inactive.' }
if (@(Get-Process AzureArchive -ErrorAction SilentlyContinue).Count -ne 0) { throw 'AA started; close it before selecting the candidate.' }
$entries[0].version = $version
$profileNew = $profile | ConvertTo-Json -Depth 16
$temporaryProfile = $profilePath + '.azurite-' + [Guid]::NewGuid().ToString('N')
[IO.File]::WriteAllText($temporaryProfile, $profileNew + [Environment]::NewLine, [Text.UTF8Encoding]::new($false))
[IO.File]::Move($temporaryProfile, $profilePath, $true)
if ((Get-FileHash -LiteralPath $cfgPath).Hash -ne $cfgHash) { throw 'Azurite configuration unexpectedly changed.' }
$installedProfile = Get-Content -LiteralPath $profilePath -Raw | ConvertFrom-Json
if (@($installedProfile.EnabledMods | Where-Object { $_.name -eq 'Azurite' -and $_.version -eq $version }).Count -ne 1) { throw 'Profile readback failed.' }
[pscustomobject]@{Version=$version; Previous=$previous; Installed=$target; Backup=$backup; ConfigUnchanged=$true; SHA256=(Get-FileHash -LiteralPath (Join-Path $target 'Azurite.dll')).Hash} | ConvertTo-Json
