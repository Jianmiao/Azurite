#requires -Version 7.2
[CmdletBinding()]
param([Parameter(Mandatory)][string]$HostReferenceRoot,[Parameter(Mandatory)][string]$OutputDirectory)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
$env:DOTNET_CLI_TELEMETRY_OPTOUT='1'
$env:DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE='true'
Import-Module (Join-Path $PSScriptRoot 'SourceStaging.psm1') -Force
$root=Resolve-StagingRoot (Split-Path $PSScriptRoot -Parent)
$hostRoot=Resolve-StagingRoot $HostReferenceRoot
$output=[IO.Path]::GetFullPath($OutputDirectory).TrimEnd([IO.Path]::DirectorySeparatorChar)
Assert-Staging ($output.StartsWith((Join-Path $root 'artifacts')+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) 'OutputDirectory must be a new directory under this source repository artifacts/.'
Assert-Staging (!(Test-Path -LiteralPath $output)) 'OutputDirectory already exists; failed attempts are preserved.'
$before=Get-StagingSource $root
[void][IO.Directory]::CreateDirectory($output)
[void](Resolve-StagingRoot $output)
$buildId=[Guid]::NewGuid().ToString('N');$build=Join-Path $output 'build'
$properties=@(Get-StagingProperties $hostRoot $build $buildId)
try{
    $pluginProject=Join-Path $root 'Plugin/Azurite.csproj';$coreProject=Join-Path $root 'Core/Azurite.Core.csproj'
    $environment=Get-StagingBuildEnvironment $root $hostRoot $build $buildId
    $inputs=Get-StagingBuildInputs $root $hostRoot $build $buildId $environment
    $p=$inputs.plugin;$c=$inputs.core
    Assert-Staging ($p.Properties.AssemblyName -ceq 'Azurite' -and $c.Properties.AssemblyName -ceq 'Azurite.Core') 'Incorrect compiled SourceStaging identities.'
    Assert-Staging (@($p.Items.ProjectReference).Count -eq 1 -and [IO.Path]::GetFullPath($p.Items.ProjectReference[0].FullPath) -ceq $coreProject -and @($p.Items.Reference|Where-Object Identity -ceq 'Azurite.Core').Count -eq 0) 'SourceStaging must build exactly its own Core project without the preserved DLL.'
    $tracked=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach($file in $before.files){[void]$tracked.Add((Join-Path $root $file.path))}
    foreach($file in @($p.Items.Compile)+@($c.Items.Compile)){
        Assert-Staging ($tracked.Contains([IO.Path]::GetFullPath($file.FullPath))) "Untracked or external compile input: $($file.FullPath)"
    }
    $hostPins=$inputs.hostReferences;$sdk=$environment.sdkVersion
    $restore=& $environment.dotnet.path exec $environment.msbuild.path $pluginProject -nologo -nr:false -t:Restore @properties ('-p:RestoreConfigFile='+(Join-Path $PSScriptRoot 'source-staging.NuGet.Config')) '-p:RestoreDisableParallel=true' 2>&1
    $code=$LASTEXITCODE;$restore|Set-Content -LiteralPath (Join-Path $output 'restore.log') -Encoding utf8
    Assert-Staging ($code -eq 0) "Offline restore failed ($code); restore.log is retained."
    $messages=& $environment.dotnet.path exec $environment.msbuild.path $pluginProject -nologo -nr:false -t:Rebuild @properties 2>&1
    $code=$LASTEXITCODE;$messages|Set-Content -LiteralPath (Join-Path $output 'build.log') -Encoding utf8
    Assert-Staging ($code -eq 0) "SourceStaging build failed ($code); build.log is retained."
    $pluginPath=[IO.Path]::GetFullPath($p.Properties.TargetPath);$coreBuild=[IO.Path]::GetFullPath($c.Properties.TargetPath)
    $corePath=Join-Path (Split-Path $pluginPath -Parent) 'Azurite.Core.dll';$deps=[IO.Path]::ChangeExtension($pluginPath,'deps.json')
    foreach($file in @($pluginPath,$coreBuild,$corePath,$deps)){Assert-Staging ($file.StartsWith($build+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase) -and (Test-Path -LiteralPath $file -PathType Leaf)) 'Missing or escaped build output.'}
    Assert-Staging ((Get-StagingHash $corePath) -ceq (Get-StagingHash $coreBuild)) 'Plugin output Core differs from same-build Core.'
    Assert-Staging ((ConvertTo-StagingJson (Get-StagingSource $root)) -ceq (ConvertTo-StagingJson $before)) 'Source changed during build.'
    foreach($h in $hostPins){Assert-Staging ((Get-StagingHash (Resolve-StagingMember $hostRoot $h.relative)) -ceq $h.sha256) 'Host reference changed during build.'}
    Test-StagingBuildEnvironment $environment $sdk
    $afterInputs=Get-StagingBuildInputs $root $hostRoot $build $buildId $environment
    Assert-Staging ((ConvertTo-StagingJson $afterInputs.hostReferences) -ceq (ConvertTo-StagingJson $hostPins) -and (ConvertTo-StagingJson $afterInputs.projects) -ceq (ConvertTo-StagingJson $inputs.projects)) 'Evaluated complete build inputs changed during build.'
    $payload=Join-Path $output 'metadata';[void][IO.Directory]::CreateDirectory($payload)
    Copy-Item -LiteralPath (Join-Path $root 'assets/manifest.source-staging.json') -Destination (Join-Path $payload 'manifest.json')
    Copy-Item -LiteralPath (Join-Path $root 'assets/icon.png') -Destination (Join-Path $payload 'icon.png')
    Copy-Item -LiteralPath (Join-Path $root 'docs/source-staging-gates.json') -Destination (Join-Path $payload 'QUALIFICATION-PENDING.json')
    $manifest=Join-Path $payload 'manifest.json';$cecil=Join-Path $hostRoot 'BepInEx/core/Mono.Cecil.dll'
    $identity=Test-StagingIdentity $pluginPath $corePath $manifest $buildId $cecil
    $files=[ordered]@{}
    foreach($item in @(@{n='plugin';p=$pluginPath},@{n='core';p=$corePath},@{n='coreBuild';p=$coreBuild},@{n='deps';p=$deps},@{n='manifest';p=$manifest},@{n='icon';p=(Join-Path $payload 'icon.png')},@{n='gates';p=(Join-Path $payload 'QUALIFICATION-PENDING.json')})){
        $files[$item.n]=[ordered]@{path=[IO.Path]::GetRelativePath($output,$item.p).Replace('\','/');sha256=(Get-StagingHash $item.p)}
    }
    $record=[ordered]@{schema=3;coreMode='SourceStaging';buildId=$buildId;configuration='Release';sdk=$sdk;buildEnvironment=$environment;projectInputs=$inputs.projects;source=$before;identity=$identity;files=$files;hostReferenceRoot=$hostRoot;hostReferences=$hostPins;releaseEligible=$false;runtimeAcceptance='not-performed';nativeExecuted=$false}
    $partial=Join-Path $output 'source-staging-build.partial';Write-StagingJson $partial $record
    $null=Test-StagingReceipt $partial $root $cecil
    $receipt=Join-Path $output 'source-staging-build.json';[IO.File]::Move($partial,$receipt)
    [pscustomobject]@{Result='PASS';Receipt=$receipt;BuildId=$buildId;Plugin=$pluginPath;Core=$corePath;ReleaseEligible=$false;NativeExecuted=$false}
}catch{
    Write-StagingJson (Join-Path $output 'failure.json') ([ordered]@{result='FAIL';message=$_.Exception.Message;stack=$_.ScriptStackTrace;buildId=$buildId;nativeExecuted=$false;releaseEligible=$false})
    throw
}
