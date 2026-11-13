#requires -Version 7.2
[CmdletBinding()]
param([Parameter(Mandatory)][string]$AAInstallPath,
      [Parameter(Mandatory)][string]$AuthoringAssembly,
      [Parameter(Mandatory)][string]$OriginalAssembly,
      [Parameter(Mandatory)][string]$EvidenceDirectory)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
if(Test-Path -LiteralPath $EvidenceDirectory){throw 'Choose a new evidence directory.'}
[void][IO.Directory]::CreateDirectory([IO.Path]::GetFullPath($EvidenceDirectory))
$reader=[IO.Path]::GetFullPath((Join-Path $AAInstallPath 'BepInEx/core/Mono.Cecil.dll'))
$loaded=@([AppDomain]::CurrentDomain.GetAssemblies()|Where-Object{$_.GetName().Name -eq 'Mono.Cecil'})
if($loaded.Count -gt 1 -or ($loaded.Count -eq 1 -and $loaded[0].Location -cne $reader)){throw 'Different metadata reader is loaded.'}
if(!$loaded.Count){Add-Type -LiteralPath $reader}
$source=[Mono.Cecil.AssemblyDefinition]::ReadAssembly([IO.Path]::GetFullPath($AuthoringAssembly))
$original=[Mono.Cecil.AssemblyDefinition]::ReadAssembly([IO.Path]::GetFullPath($OriginalAssembly))
$passed=0;$failed=0;$results=[Collections.Generic.List[object]]::new()
function Assert([bool]$condition,[string]$message){if(!$condition){throw $message}}
function Test([string]$name,[scriptblock]$body){try{& $body;$script:passed++;$results.Add(@{name=$name;pass=$true});"PASS $name"}catch{$script:failed++;$results.Add(@{name=$name;pass=$false;error=$_.Exception.Message});"FAIL ${name}: $($_.Exception.Message)"}}
function Method($type,[string]$name){$matches=@($type.Methods|Where-Object Name -CEQ $name);Assert ($matches.Count -eq 1) "Missing/ambiguous compiled method $name";$matches[0]}
function Calls($method,[string]$name){@($method.Body.Instructions|Where-Object{$_.OpCode.Code -in @('Call','Callvirt') -and $_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -ceq $name})}
function Before($method,[string]$a,[string]$b){$left=@(Calls $method $a);$right=@(Calls $method $b);Assert ($left.Count -eq 1 -and $right.Count -eq 1 -and $left[0].Offset -lt $right[0].Offset) "Expected $a before $b"}
try{
    $plugin=@($source.MainModule.Types|Where-Object FullName -CEQ 'Azurite.Plugin')[0]
    $stable=@($original.MainModule.Types|Where-Object FullName -CEQ 'Azurite.Plugin')[0]
    Test 'compiled Load binds source opt-in and actual driver GUI route' {
        $load=Method $plugin 'Load';Assert (@(Calls $load 'BindCardPackAuthoring').Count -eq 1) 'Load did not bind opt-in.'
        Assert (@(Calls $load 'BindCardPackDriver').Count -eq 1) 'Load did not bind driver.'
        $bind=Method $plugin 'BindCardPackDriver'
        Assert (@($bind.Body.Instructions|Where-Object{$_.OpCode.Code -eq 'Ldftn' -and $_.Operand.Name -ceq 'DrawCardPackSelection'}).Count -eq 1) 'Wrong GUI callback.'
        Assert (@($bind.Body.Instructions|Where-Object{$_.OpCode.Code -eq 'Stfld' -and $_.Operand.Name -ceq 'GuiTick'}).Count -eq 1) 'GUI delegate not stored in driver.'
    }
    Test 'compiled Tick services authoring before existing early returns' {
        $tick=Method $plugin 'Tick';$call=@(Calls $tick 'TickCardPackAuthoring');Assert ($call.Count -eq 1) 'Authoring Tick not called.'
        $first=@($tick.Body.Instructions|Where-Object{$_.OpCode.Code -ne 'Nop'})[0]
        Assert ($first.OpCode.Code -eq 'Ldarg_0' -and $first.Next -eq $call[0]) 'Authoring is not the first Tick action.'
        Assert ($call[0].Next.OpCode.FlowControl -eq 'Cond_Branch' -and $call[0].Next.Next.OpCode.Code -eq 'Ret') 'Terminal or wrong-thread authoring guard must be able to stop Tick.'
    }
    Test 'compiled export routes preserve restoration and explicit release' {
        Before (Method $plugin 'SuspendForExport') 'SuspendCardPackAuthoring' 'RestoreRendering'
        Assert (@(Calls (Method $plugin 'OnExportReleased') 'ResumeCardPackAuthoring').Count -eq 1) 'Explicit release missing.'
    }
    Test 'compiled Unload preserves owner before disabling or clearing callbacks' {
        $unload=Method $plugin 'Unload';Before $unload 'TryUnloadCardPackAuthoring' 'DisableForSession'
        $call=@(Calls $unload 'TryUnloadCardPackAuthoring')[0]
        Assert ($call.Next.OpCode.FlowControl -eq 'Cond_Branch' -and $call.Next.Next.OpCode.Code -eq 'Ldc_I4_0' -and $call.Next.Next.Next.OpCode.Code -eq 'Ret') 'Incomplete authoring unload must return false.'
        Assert (@($unload.Body.Instructions|Where-Object{$_.OpCode.Code -eq 'Stfld' -and $_.Operand.Name -ceq 'GuiTick'}).Count -eq 1) 'Unload does not clear GUI callback.'
    }
    Test 'both compiled Load terminal callbacks clean authoring immediately' {
        $load=Method $plugin 'Load'
        $callbacks=@($load.Body.Instructions|Where-Object{$_.OpCode.Code -eq 'Ldftn' -and $_.Operand.DeclaringType.FullName -ceq 'Azurite.Plugin'}|ForEach-Object{$_.Operand.Resolve()}|Where-Object{@(Calls $_ 'ShutdownCardPackAuthoring').Count -eq 1})
        Assert ($callbacks.Count -eq 2) 'Both terminal delegates must call shutdown.'
    }
    Test 'Original compiled plugin contains no new authoring registration or root' {
        Assert (@($stable.Methods|Where-Object{$_.Name -like '*CardPack*'}).Count -eq 0) 'Original contains authoring registration methods.'
        Assert (@($stable.Fields|Where-Object{$_.Name -like '_card*'}).Count -eq 0) 'Original contains new authoring state.'
        Assert (@($original.MainModule.Types|Where-Object{$_.FullName -like 'Azurite.AuthoringCardPackHost*'}).Count -eq 0) 'Original contains new native root.'
    }
    @{passed=$passed;failed=$failed;checks=@($results.ToArray());authoringSha256=(Get-FileHash -LiteralPath $AuthoringAssembly -Algorithm SHA256).Hash.ToLowerInvariant();originalSha256=(Get-FileHash -LiteralPath $OriginalAssembly -Algorithm SHA256).Hash.ToLowerInvariant();evidence='compiled IL routing only; complements production partial behavior tests';nativeExecuted=$false} | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $EvidenceDirectory 'result.json')
}finally{$source.Dispose();$original.Dispose()}
if($failed){throw "$failed compiled wiring checks failed."}
"$passed compiled wiring checks passed."
