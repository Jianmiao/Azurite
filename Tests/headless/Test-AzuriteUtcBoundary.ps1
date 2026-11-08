# Pure date/JSON/state fixtures only; no AA, credentials, registry writes or daily files.
param([ValidateSet('Native','Snapshot','All')][string]$Part='All')
$ErrorActionPreference='Stop'
foreach($name in @('Microsoft.PowerShell.Security','Microsoft.PowerShell.Management','Microsoft.PowerShell.Utility')){Import-Module (Join-Path $PSHOME ('Modules\'+$name+'\'+$name+'.psd1')) -Force -DisableNameChecking}
$repo=Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
Import-Module (Join-Path $repo 'tools\headless\HeadlessCore.psm1') -Force
Import-Module (Join-Path $repo 'tools\headless\RestrictedCliCore.psm1') -Force
Import-Module (Join-Path $repo 'tools\headless\CurrentAccountCli.psm1') -Force
function Assert($Condition,[string]$Message){if(-not $Condition){throw "FAIL: $Message"}}
function Refuses([scriptblock]$Action,[string]$Message){$failed=$false;try{& $Action|Out-Null}catch{$failed=$true};Assert $failed $Message}
$root=Join-Path $repo ('artifacts\utc-boundary-tests\'+[guid]::NewGuid().ToString('N'));[void][IO.Directory]::CreateDirectory($root)
$native=[datetime]::new(2026,11,8,2,27,32,[DateTimeKind]::Utc).AddTicks(6502066)
$identity=$native.AddTicks(-123456);$bootstrap=$native.AddTicks(-2000000);$observed=$native.AddTicks(2000000)
if($Part -in @('Native','All')){
 $path=Join-Path $root 'native.json'
 Write-AzuriteFlushedJson $path @{aaStartedUtc=$native.ToString('o');profilePathProof=@{checkedUtc=$identity.ToString('o')};deadlineUtc=$native.AddSeconds(120).ToString('o')}
 $read=Read-AzuriteBoundedJson $path
 $window=Get-AzuriteNativeTimeWindow $read $bootstrap $observed
 Assert ($window.nativeStartedUtc.Kind -eq [DateTimeKind]::Utc -and $window.nativeStartedUtc.Ticks -eq $native.Ticks -and $window.identityCheckedUtc.Ticks -eq $identity.Ticks) 'production JSON/native interval conversion shifted time or lost fractional ticks'
 Assert ((ConvertTo-AzuriteUtcInstant $read.deadlineUtc).Ticks -eq $native.AddSeconds(120).Ticks) 'bootstrap deadline lost zone/precision after JSON'
 foreach($offset in @([TimeSpan]::FromMinutes(345),[TimeSpan]::FromHours(-7))){
  $dto=[DateTimeOffset]::new($native).ToOffset($offset)
  Assert ((ConvertTo-AzuriteUtcInstant $dto).Ticks -eq $native.Ticks) 'typed DateTimeOffset instant changed'
  $p=Join-Path $root ([guid]::NewGuid().ToString('N')+'.json');Write-AzuriteFlushedJson $p @{value=$dto.ToString('o')}
  Assert ((ConvertTo-AzuriteUtcInstant (Read-AzuriteBoundedJson $p).value).Ticks -eq $native.Ticks) 'explicit non-UTC offset JSON instant changed'
 }
 Assert ((ConvertTo-AzuriteUtcInstant $native.ToLocalTime()).Ticks -eq $native.Ticks) 'typed local DateTime instant changed'
 Refuses {ConvertTo-AzuriteUtcInstant ([datetime]::SpecifyKind($native,[DateTimeKind]::Unspecified))} 'unspecified DateTime must not acquire an assumed zone'
 foreach($invalid in @('2026-11-08T02:27:32.6502066','2026-11-08','2026-11-08T02:27:32+25:00','11/8/2026 02:27:32')){Refuses {ConvertTo-AzuriteUtcInstant $invalid} 'missing/invalid/implicit time zone must be refused'}
 $missing=Join-Path $root 'missing-zone.json';Write-AzuriteFlushedJson $missing @{value='2026-11-08T02:27:32.6502066'}
 Refuses {ConvertTo-AzuriteUtcInstant (Read-AzuriteBoundedJson $missing).value} 'JSON auto-converted unspecified timestamp must be refused'
 $read.aaStartedUtc=$bootstrap.AddTicks(-1).ToString('o');Refuses {Get-AzuriteNativeTimeWindow $read $bootstrap $observed} 'native start one tick before bootstrap must be stale'
 $read.aaStartedUtc=$native.ToString('o');$read.profilePathProof.checkedUtc=$native.AddTicks(1).ToString('o');Refuses {Get-AzuriteNativeTimeWindow $read $bootstrap $observed} 'identity proof one tick after native start must be stale'
 $read.profilePathProof.checkedUtc=$identity.ToString('o');$read.aaStartedUtc=$observed.AddTicks(1).ToString('o');Refuses {Get-AzuriteNativeTimeWindow $read $bootstrap $observed} 'native start one tick after observed interval must be rejected'
 Write-Output ('PASS: real JSON/native/deadline UTC conversion preserves ticks and explicit offsets; missing zones/stale intervals refused; JSON type '+(Read-AzuriteBoundedJson $path).aaStartedUtc.GetType().FullName)
}
if($Part -in @('Snapshot','All')){
 $dir=Join-Path $root 'shared';[void][IO.Directory]::CreateDirectory($dir);[IO.File]::WriteAllText((Join-Path $dir 'settings.json'),'{}')
 $sid=[Security.Principal.WindowsIdentity]::GetCurrent().User.Value
 $spec=[pscustomobject]@{schema=1;identitySid=$sid;registryPath='Software\foxxlight\AzureArchive';roots=@([pscustomobject]@{label='FixtureState';path=$dir;kind='Directory';required=$true})}
 $reg=[pscustomobject]@{exists=$false;keys=@();values=@()};$id=[guid]::NewGuid().ToString('N')
 $before=New-AzuriteSharedSnapshot $spec $reg $id Before;$before.capturedUtc=$native.ToString('o')
 $p=Join-Path $root 'before.json';Write-AzuriteFlushedJson $p $before;$saved=Read-AzuriteBoundedJson $p
 $after=New-AzuriteSharedSnapshot $spec $reg $id After;$after.capturedUtc=$native.AddTicks(1).ToString('o')
 Assert (Compare-AzuriteSharedSnapshots $saved $after $spec $id).accepted 'saved-before/live-after unchanged snapshot has false date drift'
 $after.capturedUtc=$native.AddTicks(-1).ToString('o');Refuses {Compare-AzuriteSharedSnapshots $saved $after $spec $id} 'saved-before/live-after chronology one tick stale was accepted'
 $after.capturedUtc='2026-11-08T02:27:32.6502066';Refuses {Compare-AzuriteSharedSnapshots $saved $after $spec $id} 'snapshot without explicit zone was accepted'
 $after.capturedUtc=$native.AddTicks(1).ToString('o')
 $fileInstant=ConvertTo-AzuriteUtcInstant $before.files[0].lastWriteUtc
 $after.files[0].lastWriteUtc=[DateTimeOffset]::new($fileInstant).ToOffset([TimeSpan]::FromMinutes(210)).ToString('o')
 Assert (Compare-AzuriteSharedSnapshots $saved $after $spec $id).accepted 'equivalent file mtime offset became a false shared-state change'
 $after.files[0].lastWriteUtc=$fileInstant.AddTicks(1).ToString('o')
 Assert (-not (Compare-AzuriteSharedSnapshots $saved $after $spec $id).accepted) 'one-tick file mtime drift was hidden'
 Write-Output 'PASS: saved-before/live-after state chronology and file timestamps compare exact UTC instants'
}
Write-Output ('PASS UTC boundary fixtures on PowerShell '+$PSVersionTable.PSVersion+'; '+$root)
