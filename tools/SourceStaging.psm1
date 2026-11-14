#requires -Version 7.2
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'SourceStaging.Inputs.ps1')
function Assert-Staging([bool]$Condition,[string]$Message){if(!$Condition){throw $Message}}
function Get-StagingHash([string]$Path){(Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()}
function Get-StagingByteHash([byte[]]$Bytes){[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($Bytes)).ToLowerInvariant()}
function ConvertTo-StagingJson($Value){ConvertTo-Json -InputObject $Value -Depth 20 -Compress}
function Write-StagingJson([string]$Path,$Value){[IO.File]::WriteAllText($Path,((ConvertTo-Json -InputObject $Value -Depth 20)+"`n"),[Text.UTF8Encoding]::new($false))}
function Resolve-StagingRoot([string]$Path){
    $p=[IO.Path]::GetFullPath((Resolve-Path -LiteralPath $Path).Path).TrimEnd([IO.Path]::DirectorySeparatorChar)
    Assert-Staging ($p.StartsWith('C:\',[StringComparison]::OrdinalIgnoreCase)) 'This staging candidate accepts C-drive local paths only.'
    $item=Get-Item -LiteralPath $p
    Assert-Staging (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -eq 0) 'Reparse points are not admitted in staging paths.'
    if($item -is [IO.FileInfo]){$item=$item.Directory}
    while($null -ne $item){Assert-Staging (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -eq 0) 'Reparse points are not admitted in staging paths.';$item=$item.Parent}
    $p
}
function Resolve-StagingMember([string]$Root,[string]$Relative){
    Assert-Staging ($Relative -cmatch '^[^:/\\]+(?:/[^:/\\]+)*$' -and @($Relative -split '/'|Where-Object{$_ -in @('.','..','')}).Count -eq 0) "Unsafe relative member: $Relative"
    $p=[IO.Path]::GetFullPath((Join-Path $Root $Relative))
    Assert-Staging ($p.StartsWith($Root+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) 'Member escaped its root.'
    Assert-Staging (Test-Path -LiteralPath $p -PathType Leaf) "Missing member: $Relative"
    [void](Resolve-StagingRoot $p)
    $p
}
function Get-StagingSource([string]$Root){
    $rootPath=Resolve-StagingRoot $Root
    $top=(& git --no-optional-locks -C $rootPath rev-parse --show-toplevel 2>$null)
    Assert-Staging ($LASTEXITCODE -eq 0 -and [IO.Path]::GetFullPath($top.Trim()).TrimEnd([IO.Path]::DirectorySeparatorChar).Equals($rootPath,[StringComparison]::OrdinalIgnoreCase)) 'SourceRoot must be the root of its own Git repository.'
    $changes=@(& git --no-optional-locks -C $rootPath status --porcelain=v1 --untracked-files=all)
    Assert-Staging ($LASTEXITCODE -eq 0 -and $changes.Count -eq 0) 'SourceStaging requires a clean source tree; preserve dirty work elsewhere.'
    $commit=(& git --no-optional-locks -C $rootPath rev-parse HEAD).Trim();$tree=(& git --no-optional-locks -C $rootPath rev-parse 'HEAD^{tree}').Trim()
    Assert-Staging ($commit -cmatch '^[a-f0-9]{40}$' -and $tree -cmatch '^[a-f0-9]{40}$') 'Invalid source identity.'
    $files=@(foreach($rel in @(& git --no-optional-locks -C $rootPath -c core.quotepath=false ls-files)){
        $p=Resolve-StagingMember $rootPath $rel
        [ordered]@{path=$rel;sha256=(Get-StagingHash $p)}
    })
    [ordered]@{commit=$commit;tree=$tree;clean=$true;files=@($files|Sort-Object { $_.path })}
}
function Import-StagingCecil([string]$Path){
    $p=Resolve-StagingRoot $Path
    $loaded=@([AppDomain]::CurrentDomain.GetAssemblies()|Where-Object{$_.GetName().Name -ceq 'Mono.Cecil'})
    Assert-Staging ($loaded.Count -eq 0 -or ($loaded.Count -eq 1 -and $loaded[0].Location -ceq $p)) 'A different metadata reader is already loaded.'
    if($loaded.Count -eq 0){Add-Type -Path $p}
}
function Get-StagingAttribute($Attributes,[string]$Name,[int]$Index=0){
    $a=@($Attributes|Where-Object{$_.AttributeType.FullName -ceq $Name})
    Assert-Staging ($a.Count -eq 1 -and $a[0].ConstructorArguments.Count -gt $Index) "Missing or ambiguous attribute: $Name"
    [string]$a[0].ConstructorArguments[$Index].Value
}
function Get-StagingMetadata($Assembly,[string]$Key){
    $a=@($Assembly.CustomAttributes|Where-Object{$_.AttributeType.FullName -ceq 'System.Reflection.AssemblyMetadataAttribute' -and $_.ConstructorArguments[0].Value -ceq $Key})
    Assert-Staging ($a.Count -eq 1) "Missing or ambiguous build metadata: $Key"
    [string]$a[0].ConstructorArguments[1].Value
}
function Test-StagingIdentity([string]$PluginPath,[string]$CorePath,[string]$ManifestPath,[string]$BuildId,[string]$CecilPath){
    Assert-Staging ($BuildId -cmatch '^[0-9a-f]{32}$') 'Invalid same-build identity.'
    Import-StagingCecil $CecilPath
    $manifest=Get-Content -LiteralPath $ManifestPath -Raw|ConvertFrom-Json
    $v=[string]$manifest.version_number
    Assert-Staging ($manifest.name -ceq 'Azurite' -and $v -cmatch '^\d+\.\d+\.\d+$' -and @($manifest.dependencies).Count -eq 0) 'Manifest identity/dependencies are invalid.'
    Assert-Staging ((Get-StagingHash $CorePath) -cne '76a46c4a896dfbdffd66c2be85e9a0e07d2d0523faaef0841763d997c65df1a5') 'Original Core cannot be admitted as a SourceStaging build.'
    $resolver=[Mono.Cecil.DefaultAssemblyResolver]::new()
    $rp=[Mono.Cecil.ReaderParameters]::new();$rp.AssemblyResolver=$resolver
    $plugin=[Mono.Cecil.AssemblyDefinition]::ReadAssembly($PluginPath,$rp);$core=[Mono.Cecil.AssemblyDefinition]::ReadAssembly($CorePath,$rp)
    try{
        foreach($entry in @(@{a=$plugin;n='Azurite'},@{a=$core;n='Azurite.Core'})){
            $a=$entry.a
            Assert-Staging ($a.Name.Name -ceq $entry.n -and $a.MainModule.Kind -eq [Mono.Cecil.ModuleKind]::Dll) 'Compiler assembly identity is not canonical; renamed experimental DLLs are forbidden.'
            Assert-Staging ($a.Name.Version.ToString() -ceq "$v.0" -and (Get-StagingAttribute $a.CustomAttributes 'System.Reflection.AssemblyFileVersionAttribute') -ceq "$v.0") 'Assembly/file version does not match manifest.'
            Assert-Staging ((Get-StagingMetadata $a 'Azurite.CoreMode') -ceq 'SourceStaging' -and (Get-StagingMetadata $a 'Azurite.SourceBuildId') -ceq $BuildId) 'Plugin/Core were not emitted with this SourceStaging build identity.'
            Assert-Staging ((Get-StagingAttribute $a.CustomAttributes 'System.Reflection.AssemblyInformationalVersionAttribute') -ceq "$v-candidate.source-staging-1") 'Informational version does not match SourceStaging identity.'
        }
        $sdk=Get-StagingMetadata $plugin 'Azurite.BuildSdkVersion';Test-StagingSdkVersion $sdk
        Assert-Staging ((Get-StagingMetadata $core 'Azurite.BuildSdkVersion') -ceq $sdk) 'Plugin/Core compiler SDK metadata differs.'
        $type=@($plugin.MainModule.Types|Where-Object FullName -ceq 'Azurite.Plugin')
        Assert-Staging ($type.Count -eq 1) 'Missing plugin entry.'
        Assert-Staging ((Get-StagingAttribute $type[0].CustomAttributes 'BepInEx.BepInPlugin' 0) -ceq 'halocue.azurite' -and (Get-StagingAttribute $type[0].CustomAttributes 'BepInEx.BepInPlugin' 2) -ceq $v) 'BepInPlugin identity/version differs from manifest.'
        foreach($f in @(@{n='Version';v=$v},@{n='CandidateBuild';v='source-staging-1'})){
            $fields=@($type[0].Fields|Where-Object Name -ceq $f.n)
            Assert-Staging ($fields.Count -eq 1 -and $fields[0].IsLiteral -and $fields[0].Constant -ceq $f.v) 'Plugin version/build constant mismatch.'
        }
        Assert-Staging (@($plugin.MainModule.Types|Where-Object FullName -ceq 'Azurite.AuthoringCardPackPanel').Count -eq 1) 'Compiled authoring panel is missing.'
        Assert-Staging (@($core.MainModule.Types|Where-Object FullName -ceq 'Azurite.Core.CardPackV2Workflow').Count -eq 1) 'Source Core workflow API is missing.'
        $coreRef=@($plugin.MainModule.AssemblyReferences|Where-Object Name -ceq 'Azurite.Core')
        Assert-Staging ($coreRef.Count -eq 1 -and $coreRef[0].FullName -ceq $core.Name.FullName) 'Plugin references a different Core assembly identity.'
        $checked=0
        foreach($m in $plugin.MainModule.GetMemberReferences()){
            if($m.DeclaringType.Scope.Name -cne 'Azurite.Core'){continue}
            # A same-identity sibling is not evidence for the passed Core. Bind
            # the declaring type directly to this exact in-memory Core module.
            $declaring=$m.DeclaringType.GetElementType()
            while($null -ne $declaring.DeclaringType){$declaring=$declaring.DeclaringType}
            $previousScope=$declaring.Scope
            try{$declaring.Scope=$core.MainModule;$resolved=$m.Resolve()}finally{$declaring.Scope=$previousScope}
            Assert-Staging ($null -ne $resolved -and [object]::ReferenceEquals($resolved.Module,$core.MainModule)) "Unresolved source Core member: $($m.FullName)"
            $checked++
        }
        Assert-Staging ($checked -gt 0) 'No actual Core member references were inspected.'
        $render=@($plugin.MainModule.Types|Where-Object FullName -ceq 'Azurite.RenderControlClient')
        Assert-Staging ($render.Count -eq 1 -and @($render[0].Fields|Where-Object{$_.Name -ceq 'ContractName' -and $_.Constant -ceq 'AAVideoExport.Integration.RenderControlV1'}).Count -eq 1) 'Fixed RenderControlV1 consumer contract is absent.'
        $constructors=@($render[0].Methods|Where-Object Name -ceq '.ctor')
        Assert-Staging ($constructors.Count -eq 1 -and $constructors[0].HasBody) 'RenderControlV1 constructor is missing.'
        $il=$constructors[0].Body.Instructions;$versionChecks=@(for($i=0;$i -lt $il.Count;$i++){if([string]$il[$i].Operand -ceq 'ProtocolVersion'){$i}})
        Assert-Staging ($versionChecks.Count -eq 1) 'RenderControlV1 protocol check is missing or ambiguous.'
        $i=$versionChecks[0]
        Assert-Staging ($i+4 -lt $il.Count -and ([string]$il[$i+1].Operand).Contains('ReadGetter<System.Int32>') -and ([string]$il[$i+2].Operand).Contains('System.Func`1<System.Int32>::Invoke()') -and $il[$i+3].OpCode.Code.ToString() -ceq 'Ldc_I4_1' -and $il[$i+4].OpCode.Code.ToString() -in @('Beq','Beq_S')) 'Compiled RenderControlV1 consumer must retain the protocol-major 1 equality guard.'
        Assert-StagingProtocolFlow $constructors[0] $i
        [ordered]@{version=$v;candidateBuild='source-staging-1';pluginIdentity=$plugin.Name.FullName;coreIdentity=$core.Name.FullName;pluginCoreReference=$coreRef[0].FullName;coreMemberReferencesResolved=$checked;renderControlContract='AAVideoExport.Integration.RenderControlV1';protocolMajor=1;sdkVersion=$sdk}
    }finally{$plugin.Dispose();$core.Dispose();$resolver.Dispose()}
}
function Assert-StagingProtocolFlow($Constructor,[int]$Index){
    $il=$Constructor.Body.Instructions;$branch=$Index+4
    Assert-Staging ($Index+8 -lt $il.Count -and [object]::ReferenceEquals($il[$branch].Operand,$il[$Index+8]) -and $il[$Index+5].OpCode.Code.ToString() -ceq 'Ldstr' -and $il[$Index+6].OpCode.Code.ToString() -ceq 'Newobj' -and [string]$il[$Index+6].Operand -ceq 'System.Void System.NotSupportedException::.ctor(System.String)' -and $il[$Index+7].OpCode.Code.ToString() -ceq 'Throw') 'RenderControlV1 mismatch must throw before the equality-success branch target.'
    # A surrounding handler may not swallow the mismatch refusal.
    foreach($h in $Constructor.Body.ExceptionHandlers){
        $start=$il.IndexOf($h.TryStart);$end=if($null -eq $h.TryEnd){$il.Count}else{$il.IndexOf($h.TryEnd)}
        Assert-Staging (!($Index+7 -ge $start -and $Index+7 -lt $end)) 'RenderControlV1 mismatch throw cannot be covered by an exception handler.'
    }
    # Reject alternate entries into the guard and bypasses to a normal return.
    # Traversal excludes the equality branch; no successful constructor return
    # may remain reachable. This checks branch destinations, not four opcodes.
    $seen=[Collections.Generic.HashSet[int]]::new();$queue=[Collections.Generic.Queue[int]]::new();$queue.Enqueue(0)
    while($queue.Count -gt 0){
        $at=$queue.Dequeue();if($at -eq $branch -or !$seen.Add($at)){continue}
        $instruction=$il[$at];$code=$instruction.OpCode.Code.ToString();$flow=$instruction.OpCode.FlowControl.ToString()
        Assert-Staging ($code -cne 'Ret') 'RenderControlV1 protocol equality guard does not dominate successful construction.'
        if($flow -in @('Return','Throw') -or $code -ceq 'Jmp'){continue}
        if($flow -in @('Branch','Cond_Branch')){
            $targets=if($code -ceq 'Switch'){@($instruction.Operand)}else{@($instruction.Operand)}
            foreach($target in $targets){
                $to=$il.IndexOf($target);Assert-Staging ($to -ge 0) 'Invalid protocol control-flow branch target.'
                Assert-Staging (!($to -gt $Index -and $to -le $branch)) 'Alternate branch enters the protocol guard after the version load.'
                $queue.Enqueue($to)
            }
            if($flow -ceq 'Branch'){continue}
        }
        if($at+1 -lt $il.Count){$queue.Enqueue($at+1)}
    }
}
function Test-StagingReceipt([string]$ReceiptPath,[string]$SourceRoot,[string]$CecilPath){
    $root=Resolve-StagingRoot $SourceRoot;$receiptFile=Resolve-StagingRoot $ReceiptPath;$output=Split-Path $receiptFile -Parent
    $r=Get-Content -LiteralPath $receiptFile -Raw|ConvertFrom-Json
    Assert-Staging ($r.schema -eq 3 -and $r.coreMode -ceq 'SourceStaging' -and $r.releaseEligible -is [bool] -and !$r.releaseEligible -and $r.runtimeAcceptance -ceq 'not-performed' -and $r.nativeExecuted -is [bool] -and !$r.nativeExecuted -and $r.configuration -ceq 'Release') 'Receipt mode or native qualification claim is invalid.'
    Test-StagingBuildEnvironment $r.buildEnvironment $r.sdk
    $source=Get-StagingSource $root
    Assert-Staging ((ConvertTo-StagingJson $source) -ceq (ConvertTo-StagingJson $r.source)) 'Receipt source commit/tree/file inventory is stale or mismatched.'
    $paths=@{}
    $roles=@('plugin','core','coreBuild','deps','manifest','icon','gates')
    Assert-Staging (@($r.files.PSObject.Properties).Count -eq $roles.Count) 'Receipt file roles are invalid.'
    foreach($role in $roles){
        $f=$r.files.$role;$p=Resolve-StagingMember $output $f.path
        Assert-Staging ($f.sha256 -cmatch '^[a-f0-9]{64}$' -and (Get-StagingHash $p) -ceq $f.sha256) "Receipt file hash mismatch: $role"
        $paths[$role]=$p
    }
    Assert-Staging ($r.files.core.sha256 -ceq $r.files.coreBuild.sha256) 'Copied Core does not match this producer build.'
    Assert-Staging ($r.files.manifest.sha256 -ceq (Get-StagingHash (Join-Path $root 'assets/manifest.source-staging.json')) -and $r.files.icon.sha256 -ceq (Get-StagingHash (Join-Path $root 'assets/icon.png')) -and $r.files.gates.sha256 -ceq (Get-StagingHash (Join-Path $root 'docs/source-staging-gates.json'))) 'Payload metadata differs from clean source.'
    $seen=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    Assert-Staging (@($r.hostReferences).Count -gt 0) 'Host references are absent.'
    foreach($h in $r.hostReferences){
        Assert-Staging ($seen.Add($h.relative)) 'Duplicate host reference.'
        $p=Resolve-StagingMember (Resolve-StagingRoot $r.hostReferenceRoot) $h.relative
        Assert-Staging ($h.sha256 -cmatch '^[a-f0-9]{64}$' -and (Get-StagingHash $p) -ceq $h.sha256) 'Host reference drift.'
    }
    $evaluated=Get-StagingBuildInputs $root (Resolve-StagingRoot $r.hostReferenceRoot) (Join-Path $output 'build') $r.buildId $r.buildEnvironment
    Assert-Staging ((ConvertTo-StagingJson $evaluated.hostReferences) -ceq (ConvertTo-StagingJson $r.hostReferences)) 'Host reference inventory does not equal the complete evaluated producer inputs.'
    Assert-Staging ((ConvertTo-StagingJson $evaluated.projects) -ceq (ConvertTo-StagingJson $r.projectInputs)) 'Evaluated producer project inputs differ from the receipt.'
    $cecilRef=@($r.hostReferences|Where-Object relative -ceq 'BepInEx/core/Mono.Cecil.dll')
    Assert-Staging ($cecilRef.Count -eq 1 -and (Get-StagingHash $CecilPath) -ceq $cecilRef[0].sha256) 'Metadata reader is not the build-pinned host reference.'
    $actual=Test-StagingIdentity $paths.plugin $paths.core $paths.manifest $r.buildId $CecilPath
    Assert-Staging ($actual.sdkVersion -ceq $r.sdk) 'Receipt SDK differs from both actual compiled assembly SDK identities.'
    Assert-Staging ((ConvertTo-StagingJson $actual) -ceq (ConvertTo-StagingJson $r.identity)) 'Receipt assembly/API identity mismatch.'
    Test-StagingDependencies $paths.deps $actual $evaluated.projects
    Assert-Staging ((Get-StagingHash (Join-Path $root 'references/Azurite.Core.dll')) -ceq '76a46c4a896dfbdffd66c2be85e9a0e07d2d0523faaef0841763d997c65df1a5') 'Preserved original Core changed.'
    [pscustomobject]@{Receipt=$r;Paths=$paths;ReceiptSha256=(Get-StagingHash $receiptFile);OutputRoot=$output}
}
function Assert-StagingNotReleaseQualified {throw 'SourceStaging is not release qualified: ordinary-editor/native, installed pair/profile, full feature, RenderControlV1 lifecycle, installation/rollback and performance evidence is pending. No qualification verifier or waiver is implemented.'}
function Get-StagingPayload($Verified,[string]$SourceRoot){
    $r=$Verified.Receipt;$p=$Verified.Paths
    $payload=[ordered]@{}
    foreach($item in @(@{n='Azurite.dll';p=$p.plugin},@{n='Azurite.Core.dll';p=$p.core},@{n='Azurite.deps.json';p=$p.deps},@{n='manifest.json';p=$p.manifest},@{n='icon.png';p=$p.icon},@{n='QUALIFICATION-PENDING.json';p=$p.gates},@{n='README.md';p=(Join-Path $SourceRoot 'docs/SOURCE-STAGING.md')})){$payload[$item.n]=[IO.File]::ReadAllBytes($item.p)}
    $tools=[ordered]@{sdkVersion=$r.buildEnvironment.sdkVersion;dotnetSha256=$r.buildEnvironment.dotnet.sha256;msbuildSha256=$r.buildEnvironment.msbuild.sha256;compilerSha256=$r.buildEnvironment.compiler.sha256;sdkPropsSha256=$r.buildEnvironment.sdkProps.sha256}
    $record=[ordered]@{schema=3;coreMode='SourceStaging';buildId=$r.buildId;sourceCommit=$r.source.commit;sourceTree=$r.source.tree;sourceTreeClean=$true;sourceFiles=$r.source.files;buildReceiptSha256=$Verified.ReceiptSha256;configuration=$r.configuration;sdk=$r.sdk;buildEnvironment=$tools;projectInputs=$r.projectInputs;identity=$r.identity;pluginSha256=$r.files.plugin.sha256;coreSha256=$r.files.core.sha256;hostReferences=$r.hostReferences;releaseEligible=$false;runtimeAcceptance='not-performed';nativeExecuted=$false}
    $payload['BUILD.json']=[Text.UTF8Encoding]::new($false).GetBytes((ConvertTo-StagingJson $record)+"`n")
    $payload
}
Export-ModuleMember -Function *-Staging*,ConvertTo-StagingJson,Write-StagingJson,Resolve-StagingRoot,Resolve-StagingMember,Import-StagingCecil
