# Uses immutable native response bytes as a data fixture. Never launches AA.
$ErrorActionPreference='Stop'
foreach($name in @('Microsoft.PowerShell.Security','Microsoft.PowerShell.Management','Microsoft.PowerShell.Utility')){Import-Module (Join-Path $PSHOME ('Modules\'+$name+'\'+$name+'.psd1')) -Force -DisableNameChecking}
$repo=Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
Import-Module (Join-Path $repo 'tools\headless\RestrictedCliCore.psm1') -Force
function Assert($Condition,[string]$Message){if(-not $Condition){throw "FAIL: $Message"}}
function Refuses([scriptblock]$Action,[string]$Message){$failed=$false;try{& $Action|Out-Null}catch{$failed=$true};Assert $failed $Message}
$fixture=Join-Path $PSScriptRoot 'fixtures\native-capabilities-20261108.json'
$provenance=Read-AzuriteBoundedJson (Join-Path $PSScriptRoot 'fixtures\native-capabilities-20261108.provenance.json')
Assert ($provenance.sourceRunId -ceq 'b3231d471fa74fbaa9b297a11efce707' -and $provenance.sourceSha256 -ceq 'A12FC49612372CB6A9F5AB56B86EFEC8F438C585F0BF3E1FAD033F66A787401F' -and (Get-AzuriteSha256 $fixture) -ceq $provenance.sourceSha256 -and (Get-Item $fixture).Length -eq 1710) 'native positive fixture lost raw provenance binding'
$root=Join-Path $repo ('artifacts\native-capabilities-shape\'+[guid]::NewGuid().ToString('N'));[void][IO.Directory]::CreateDirectory($root)
$response=Join-Path $root 'response.json';$started=[datetime]::UtcNow.AddSeconds(-1)
[IO.File]::WriteAllBytes($response,[IO.File]::ReadAllBytes($fixture))
$good=Read-AzuriteCliResponse $response capabilities $started
Assert ($good.Sha256 -ceq $provenance.sourceSha256 -and $good.Data.tools.Count -eq 14 -and $good.Data.cli_mcp_control.commands.Count -eq 3) 'fresh exact native capabilities content was refused'
Assert (-not $good.Data.PSObject.Properties['commands']) 'fixture incorrectly reintroduces data.commands'
Write-Output 'PASS: exact 1710-byte native response validates with tools/limits and nested cli_mcp_control.commands'
function Fresh(){return [IO.File]::ReadAllText($fixture)|ConvertFrom-Json}
function Reject($Value,[string]$Message){[IO.File]::WriteAllText($response,($Value|ConvertTo-Json -Depth 20 -Compress));Refuses {Read-AzuriteCliResponse $response capabilities $started} $Message}
foreach($field in @('api_version','format_version','protocol_version','schema_uri','mcp_authentication','tools','limits','resource_kinds','stable_identity','persistence','project_files','cli_mcp_control')){
 $v=Fresh;$v.data.PSObject.Properties.Remove($field);Reject $v ('partial native data missing '+$field)
}
$v=Fresh;$v.data.tools=@('get_context');Reject $v 'partial tool list'
$v=Fresh;$v.data.tools[1]=$v.data.tools[0];Reject $v 'duplicate declared tools'
$v=Fresh;$v.data.tools=@([pscustomobject]@{name='get_context'});Reject $v 'object tools instead of string declarations'
$v=Fresh;$v.data.limits=[pscustomobject]@{};Reject $v 'empty limits object'
$v=Fresh;$v.data.limits.max_operations='500';Reject $v 'numeric limit must retain integer type'
$v=Fresh;$v.data.limits.max_create_dialogue_lines=999;Reject $v 'incorrect declared native limit'
$v=Fresh;$v.data.limits.stage_slots=@(1,2,3,4,4);Reject $v 'duplicate stage slot'
$v=Fresh;$v.data.resource_kinds=@();Reject $v 'empty resource declarations'
$v=Fresh;$v.data.stable_identity=@('text');Reject $v 'synthetic stable identity'
$v=Fresh;$v.data.persistence.cli='';Reject $v 'empty persistence contract'
$v=Fresh;$v.data.project_files.primary_extension='.json';Reject $v 'wrong project format'
$v=Fresh;$v.data.cli_mcp_control.PSObject.Properties.Remove('commands');$v.data|Add-Member commands @('mcp.start','mcp.status','mcp.stop');Reject $v 'commands at wrong nesting cannot substitute'
$v=Fresh;$v.data.cli_mcp_control.start_idempotent='true';Reject $v 'control boolean must retain type'
$v=Fresh;$v.data.cli_mcp_control.returns_bearer_token=$true;Reject $v 'unexpected bearer-token contract'
$v=Fresh;$v.ok=$false;Reject $v 'failed native envelope'
foreach($raw in @('{broken','{"api_version":"1","ok":true,"data":{},"warnings":[]}','{"api_version":"1","ok":true,"data":{"api_version":"1","format_version":2,"schema_uri":"aap://schema/api/1","commands":["schema","capabilities"]},"warnings":[]}','{"status":"passed","checks":[{"name":"native","passed":true}]}')){[IO.File]::WriteAllText($response,$raw);Refuses {Read-AzuriteCliResponse $response capabilities $started} 'malformed/empty/former-synthetic/generic result refused'}
Write-Output ('PASS: partial/malformed/synthetic capabilities rejected through production validator; PowerShell '+$PSVersionTable.PSVersion+'; '+$root)
