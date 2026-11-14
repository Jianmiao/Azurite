# SourceStaging's controlled local build environment and dependency contract.
# These pins prove local consistency, not independent signed attestation.
function Get-StagingProperties([string]$HostRoot,[string]$Artifacts,[string]$BuildId){
    @('-p:AzuriteCoreMode=SourceStaging','-p:Configuration=Release','-p:UseArtifactsOutput=true',"-p:ArtifactsPath=$Artifacts","-p:AAInstallPath=$HostRoot","-p:SourceStagingBuildId=$BuildId",'-p:NuGetAudit=false','-p:ImportDirectoryBuildProps=false','-p:ImportDirectoryBuildTargets=false','-p:UseSharedCompilation=false')
}
function Test-StagingSdkVersion($Version){
    Assert-Staging ($Version -is [string] -and $Version -cmatch '^[1-9][0-9]*\.[0-9]+\.[0-9]{3,}(?:-[0-9A-Za-z]+(?:[.-][0-9A-Za-z]+)*)?$') 'SDK identity must be a nonempty valid SDK version string.'
}
function Get-StagingBuildEnvironment([string]$Root,[string]$HostRoot,[string]$Artifacts,[string]$BuildId){
    $dotnet=Resolve-StagingRoot (Get-Command dotnet -CommandType Application -ErrorAction Stop).Source
    $properties=@(Get-StagingProperties $HostRoot $Artifacts $BuildId)
    Push-Location -LiteralPath $Root
    try{
        $sdk=(& $dotnet --version).Trim();Assert-Staging ($LASTEXITCODE -eq 0) 'SDK version discovery failed.';Test-StagingSdkVersion $sdk
        $o=& $dotnet msbuild (Join-Path $Root 'Plugin/Azurite.csproj') -nologo @properties '-getProperty:NETCoreSdkVersion,MSBuildToolsPath,MSBuildSDKsPath' 2>&1
        Assert-Staging ($LASTEXITCODE -eq 0) ('SDK environment discovery failed: '+($o -join "`n"))
        $p=($o -join "`n"|ConvertFrom-Json).Properties
    }finally{Pop-Location}
    $sdkRoot=Resolve-StagingRoot (Join-Path (Split-Path $dotnet -Parent) ('sdk/'+$sdk))
    Assert-Staging ($p.NETCoreSdkVersion -ceq $sdk -and [IO.Path]::GetFullPath($p.MSBuildToolsPath).TrimEnd('\') -ceq $sdkRoot -and [IO.Path]::GetFullPath($p.MSBuildSDKsPath).TrimEnd('\') -ceq (Join-Path $sdkRoot 'Sdks')) 'SDK discovery and evaluated tool paths disagree.'
    $record=[ordered]@{schema=1;sdkVersion=$sdk;sdkRoot=$sdkRoot}
    foreach($entry in @(@{n='dotnet';p=$dotnet},@{n='msbuild';p=(Join-Path $sdkRoot 'MSBuild.dll')},@{n='compiler';p=(Join-Path $sdkRoot 'Roslyn/bincore/csc.dll')},@{n='sdkProps';p=(Join-Path $sdkRoot 'Microsoft.NETCoreSdk.BundledVersions.props')})){
        $p=Resolve-StagingRoot $entry.p;$record[$entry.n]=[ordered]@{path=$p;sha256=(Get-StagingHash $p)}
    }
    [pscustomobject]$record
}
function Test-StagingBuildEnvironment($Environment,[string]$Sdk){
    Test-StagingSdkVersion $Sdk
    Assert-Staging ($Environment.schema -eq 1 -and $Environment.sdkVersion -is [string] -and $Environment.sdkVersion -ceq $Sdk) 'SDK receipt identity differs from the recorded producer environment.'
    # The receipt must not choose an arbitrary executable to run. Anchor only
    # the launcher to the caller's trusted dotnet, not the caller's current SDK.
    $trustedDotnet=Resolve-StagingRoot (Get-Command dotnet -CommandType Application -ErrorAction Stop).Source
    Assert-Staging ($Environment.dotnet.path -is [string] -and $Environment.dotnet.path -ceq $trustedDotnet) 'Recorded producer tool launcher differs from the trusted caller dotnet.'
    $sdkRoot=Resolve-StagingRoot $Environment.sdkRoot
    foreach($name in @('dotnet','msbuild','compiler','sdkProps')){
        $pin=$Environment.$name
        Assert-Staging ($pin.path -is [string] -and $pin.sha256 -is [string] -and $pin.sha256 -cmatch '^[a-f0-9]{64}$') 'Invalid producer tool pin.'
        $path=Resolve-StagingRoot $pin.path
        Assert-Staging ((Get-StagingHash $path) -ceq $pin.sha256) "Recorded producer tool changed: $name"
    }
    $rootFromDotnet=Join-Path (Split-Path $Environment.dotnet.path -Parent) ('sdk/'+$Sdk)
    Assert-Staging ($sdkRoot -ceq $rootFromDotnet -and $Environment.msbuild.path -ceq (Join-Path $sdkRoot 'MSBuild.dll') -and $Environment.compiler.path -ceq (Join-Path $sdkRoot 'Roslyn/bincore/csc.dll') -and $Environment.sdkProps.path -ceq (Join-Path $sdkRoot 'Microsoft.NETCoreSdk.BundledVersions.props')) 'Recorded SDK tool layout is inconsistent.'
}
function Get-StagingBuildInputs([string]$Root,[string]$HostRoot,[string]$Artifacts,[string]$BuildId,$Environment){
    Test-StagingBuildEnvironment $Environment $Environment.sdkVersion
    $properties=@(Get-StagingProperties $HostRoot $Artifacts $BuildId)
    $evaluations=@{};$summaries=[ordered]@{};$pins=[Collections.Generic.List[object]]::new();$seen=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach($project in @(@{role='plugin';path='Plugin/Azurite.csproj';assembly='Azurite'},@{role='core';path='Core/Azurite.Core.csproj';assembly='Azurite.Core'})){
        $path=Join-Path $Root $project.path
        # Use the exact SDK captured by the producer. Do not ask whichever SDK
        # happens to be the current default during later receipt validation.
        $o=& $Environment.dotnet.path exec $Environment.msbuild.path $path -nologo @properties '-getProperty:NETCoreSdkVersion,MSBuildToolsPath,MSBuildSDKsPath,TargetPath,AssemblyName,TargetFramework,TargetFrameworkIdentifier,TargetFrameworkVersion,Version,AssemblyVersion,FileVersion' '-getItem:Compile,Reference,ProjectReference' 2>&1
        Assert-Staging ($LASTEXITCODE -eq 0) ('Pinned SDK evaluation failed: '+($o -join "`n"))
        $e=$o -join "`n"|ConvertFrom-Json;$p=$e.Properties;$evaluations[$project.role]=$e
        Assert-Staging ($p.NETCoreSdkVersion -ceq $Environment.sdkVersion -and [IO.Path]::GetFullPath($p.MSBuildToolsPath).TrimEnd('\') -ceq $Environment.sdkRoot -and [IO.Path]::GetFullPath($p.MSBuildSDKsPath).TrimEnd('\') -ceq (Join-Path $Environment.sdkRoot 'Sdks')) 'Evaluated SDK differs from recorded producer tools.'
        Assert-Staging ($p.AssemblyName -ceq $project.assembly -and $p.TargetFramework -ceq 'netcoreapp6.0' -and $p.TargetFrameworkIdentifier -ceq '.NETCoreApp' -and $p.TargetFrameworkVersion -ceq 'v6.0') 'Unsupported producer assembly or runtime target.'
        $summaries[$project.role]=[ordered]@{project=$project.path;assemblyName=$p.AssemblyName;version=$p.Version;assemblyVersion=$p.AssemblyVersion;fileVersion=$p.FileVersion;targetFramework=$p.TargetFramework;runtimeTarget=($p.TargetFrameworkIdentifier+',Version='+$p.TargetFrameworkVersion)}
        foreach($reference in @($e.Items.Reference)){
            Assert-Staging ($reference.Identity -is [string] -and $reference.HintPath -is [string] -and $reference.HintPath.Length -gt 0) 'Host reference requires an explicit identity and HintPath.'
            $full=Resolve-StagingRoot $reference.HintPath
            Assert-Staging ($full.StartsWith($HostRoot+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) 'Evaluated host reference escaped the selected mirror.'
            $relative=[IO.Path]::GetRelativePath($HostRoot,$full).Replace('\','/')
            Assert-Staging ($seen.Add($project.path+'|'+$reference.Identity)) 'Duplicate evaluated reference identity.'
            $pins.Add([ordered]@{project=$project.path;identity=$reference.Identity;relative=$relative;sha256=(Get-StagingHash $full)})
        }
    }
    $p=$evaluations.plugin;$c=$evaluations.core
    Assert-Staging (@($p.Items.ProjectReference).Count -eq 1 -and [IO.Path]::GetFullPath($p.Items.ProjectReference[0].FullPath) -ceq (Join-Path $Root 'Core/Azurite.Core.csproj') -and @($c.Items.ProjectReference).Count -eq 0 -and @($p.Items.Reference|Where-Object Identity -ceq 'Azurite.Core').Count -eq 0) 'Evaluated project graph is not the single same-source Core pair.'
    [pscustomobject]@{plugin=$p;core=$c;projects=[pscustomobject]$summaries;hostReferences=@($pins|Sort-Object { $_.project },{ $_.identity },{ $_.relative })}
}
function Assert-StagingJsonKeys($Object,[string[]]$Expected,[string]$Label){
    Assert-Staging ($Object -is [pscustomobject]) "Dependency graph $Label must be an object."
    $actual=@($Object.PSObject.Properties|ForEach-Object Name|Sort-Object);$wanted=@($Expected|Sort-Object)
    Assert-Staging (($actual -join '|') -ceq ($wanted -join '|')) "Dependency graph $Label has missing or unexpected members."
}
function Test-StagingDependencies([string]$Path,$Identity,$Projects){
    $d=Get-Content -LiteralPath $Path -Raw|ConvertFrom-Json
    $v=$Identity.version;$pluginKey='Azurite/'+$v;$coreKey='Azurite.Core/'+$v;$target=$Projects.plugin.runtimeTarget
    Assert-Staging ($Projects.plugin.version -ceq $v -and $Projects.core.version -ceq $v -and $Projects.core.runtimeTarget -ceq $target -and $Projects.plugin.assemblyVersion -ceq "$v.0" -and $Projects.core.assemblyVersion -ceq "$v.0" -and $Projects.plugin.fileVersion -ceq "$v.0" -and $Projects.core.fileVersion -ceq "$v.0") 'Dependency project versions differ from the actual compiled pair.'
    Assert-StagingJsonKeys $d @('runtimeTarget','compilationOptions','targets','libraries') 'root'
    Assert-StagingJsonKeys $d.runtimeTarget @('name','signature') 'runtimeTarget'
    Assert-Staging ($d.runtimeTarget.name -ceq $target -and $d.runtimeTarget.signature -ceq '') 'Dependency runtime target differs from the evaluated project.'
    Assert-StagingJsonKeys $d.compilationOptions @() 'compilationOptions'
    Assert-StagingJsonKeys $d.targets @($target) 'targets'
    Assert-StagingJsonKeys $d.libraries @($pluginKey,$coreKey) 'libraries'
    $graph=$d.targets.$target;Assert-StagingJsonKeys $graph @($pluginKey,$coreKey) 'selected target projects'
    foreach($key in @($pluginKey,$coreKey)){
        $library=$d.libraries.$key;Assert-StagingJsonKeys $library @('type','serviceable','sha512') ('library '+$key)
        Assert-Staging ($library.type -ceq 'project' -and $library.serviceable -is [bool] -and !$library.serviceable -and $library.sha512 -ceq '') 'Dependency library is not the expected source project.'
    }
    $plugin=$graph.$pluginKey;$core=$graph.$coreKey
    Assert-StagingJsonKeys $plugin @('dependencies','runtime') 'plugin target'
    Assert-StagingJsonKeys $plugin.dependencies @('Azurite.Core') 'plugin dependency edges'
    Assert-Staging ($plugin.dependencies.'Azurite.Core' -ceq $v) 'Dependency Core edge version differs from the actual pair.'
    Assert-StagingJsonKeys $plugin.runtime @('Azurite.dll') 'plugin runtime assets'
    Assert-StagingJsonKeys $plugin.runtime.'Azurite.dll' @() 'plugin runtime identity'
    Assert-StagingJsonKeys $core @('runtime') 'Core target'
    Assert-StagingJsonKeys $core.runtime @('Azurite.Core.dll') 'Core runtime assets'
    $runtime=$core.runtime.'Azurite.Core.dll';Assert-StagingJsonKeys $runtime @('assemblyVersion','fileVersion') 'Core runtime identity'
    Assert-Staging ($runtime.assemblyVersion -ceq "$v.0" -and $runtime.fileVersion -ceq "$v.0") 'Dependency runtime assembly/file versions differ from the actual Core.'
}
