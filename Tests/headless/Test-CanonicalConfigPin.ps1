# Data/evidence validation only. No installation write, quarantine recovery or AA launch.
param([string]$PreviousContractPath)
$ErrorActionPreference='Stop'
$repo=Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
Import-Module (Join-Path $repo 'tools\headless\RestrictedCliCore.psm1') -Force
function Assert($Condition,[string]$Message){if(-not $Condition){throw "FAIL: $Message"}}
$contract=Read-AzuriteStartupContract
Assert ($contract.contractId -ceq 'aa-2023.2.22f1-azurite-1.0.38-cli-nosound-v2') 'canonical config revision must be explicit v2'
$cfg='profiles/AzuriteAcceptance/configs/halocue.azurite.cfg'
$oldHash='651944891A2FB6BCE206529B9DDFE0B2B1FC85C9186C94871125C9818193A3A1'
$newHash='0D3CA42AE3AF63C8E147FC2657747F8E6A166CC4F472DF797C0AD43C6249BF38'
$pins=@($contract.files|Where-Object path -CEQ $cfg)
Assert ($pins.Count -eq 1 -and $pins[0].sha256 -ceq $newHash -and $contract.files.Count -eq 451 -and $contract.exactDirectories.Count -eq 10 -and $contract.evidence.Count -eq 16) 'single canonical cfg pin/counts differ'
Assert ($contract.revisionNote -is [string] -and $contract.revisionNote.Length -gt 40) 'reviewed reconstruction rationale missing'
$evidenceRoot='E:\aamod\diagnostics\azurite-plan-20261108\first-native-query'
$expected=[ordered]@{'CONFIG-PIN-AUDIT.md'='488272ED56530985AA0C9BA5E828DA64129CECD4AFC319408402CDD1CF6E46CA';'canonical-reconstruction-proof.json'='DA515D881C8B45D32893149F0F2FCC5CA6A4C103272FC17A8D3F24361208FA67';'halocue.azurite.reconstructed-canonical.cfg'=$newHash}
foreach($name in $expected.Keys){$path=Join-Path $evidenceRoot $name;$entry=@($contract.evidence|Where-Object path -CEQ $path);Assert ($entry.Count -eq 1 -and $entry[0].sha256 -ceq $expected[$name] -and (Get-AzuriteSha256 $path) -ceq $expected[$name]) ('canonical evidence pin differs: '+$name)}
$proof=Read-AzuriteBoundedJson (Join-Path $evidenceRoot 'canonical-reconstruction-proof.json')
Assert ($proof.expectedBytes -eq 11914 -and $proof.expectedSha256.ToUpperInvariant() -ceq $newHash -and $proof.boundCount -eq 55 -and $proof.orphanEntries.Count -eq 2 -and $proof.byteExactMatch -eq $true) 'independent reconstruction proof differs'
if($PreviousContractPath){
 $before=Read-AzuriteBoundedJson $PreviousContractPath
 Assert ($before.contractId -ceq 'aa-2023.2.22f1-azurite-1.0.38-cli-nosound-v1' -and $before.files.Count -eq 451 -and $before.evidence.Count -eq 13) 'expected v1 baseline missing'
 Assert ((@($contract.PSObject.Properties.Name|Where-Object {$_ -cne 'revisionNote'}|Sort-Object) -join '|') -ceq (@($before.PSObject.Properties.Name|Sort-Object) -join '|')) 'unexpected contract root fields changed'
 foreach($field in $before.PSObject.Properties.Name){if($field -notin @('contractId','files','evidence')){Assert (($before.$field|ConvertTo-Json -Depth 25 -Compress) -ceq ($contract.$field|ConvertTo-Json -Depth 25 -Compress)) ('unrelated contract field changed: '+$field)}}
 $changed=0
 for($i=0;$i -lt 451;$i++){
  $a=$before.files[$i];$b=$contract.files[$i]
  if($a.path -ceq $cfg){Assert ($a.sha256 -ceq $oldHash -and $b.path -ceq $cfg -and $b.sha256 -ceq $newHash -and @($a.PSObject.Properties).Count -eq @($b.PSObject.Properties).Count) 'wrong config transition';$changed++}
  else{Assert (($a|ConvertTo-Json -Compress) -ceq ($b|ConvertTo-Json -Compress)) ('unrelated input pin changed: '+$a.path)}
 }
 Assert ($changed -eq 1) 'expected exactly one input pin update'
 for($i=0;$i -lt 13;$i++){Assert (($before.evidence[$i]|ConvertTo-Json -Compress) -ceq ($contract.evidence[$i]|ConvertTo-Json -Compress)) 'pre-existing evidence changed'}
 Write-Output 'PASS: exactly one cfg pin revised; other 450 inputs, ten scopes and thirteen prior evidence entries unchanged'
}
Write-Output 'PASS: explicit v2 contract binds only independently reconstructed canonical cfg plus three approved evidence artifacts'
