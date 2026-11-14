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
function LocalIndex($instruction,[string]$prefix){
    $code=[string]$instruction.OpCode.Code
    if($code -match ('^'+$prefix+'loc_([0-3])$')){return [int]$Matches[1]}
    if($code -in @(($prefix+'loc'),($prefix+'loc_S'))){return [int]$instruction.Operand.Index}
    throw "Expected a $prefix local instruction, saw $code"
}
function AssertConjoinedCallResults($method,[string]$leftName,[string]$rightName){
    $left=@(Calls $method $leftName);$right=@(Calls $method $rightName)
    Assert ($left.Count -eq 1 -and $right.Count -eq 1) 'Expected both actual cleanup producers.'
    # Follow both control-flow outcomes with symbolic producer values. Release
    # builds may retain the first bool on the evaluation stack rather than store
    # a local; merely checking a Boolean return type would miss a discarded result.
    $queue=[Collections.Generic.Queue[object]]::new();$visited=[Collections.Generic.HashSet[string]]::new();$verified=0
    $locals=@(for($n=0;$n -lt $method.Body.Variables.Count;$n++){'?'})
    $queue.Enqueue(@{At=$method.Body.Instructions[0];Stack=@();Locals=$locals;Left=$false;Right=$false})
    while($queue.Count){
        $state=$queue.Dequeue();$i=$state.At;$code=[string]$i.OpCode.Code
        $key="$($i.Offset)|$($state.Stack -join ',')|$($state.Locals -join ',')|$($state.Left)|$($state.Right)"
        if(!$visited.Add($key)){continue};Assert ($visited.Count -le 4096) 'Cleanup control-flow proof exceeded its bound.'
        $stack=[Collections.Generic.List[string]]::new();foreach($value in $state.Stack){$stack.Add($value)}
        $local=@($state.Locals.Clone());$seenLeft=$state.Left;$seenRight=$state.Right
        if($code -eq 'Ret'){
            Assert ($stack.Count -eq 1) 'Cleanup return has an unexpected stack.'
            if($seenLeft -or $seenRight){Assert ($seenLeft -and $seenRight -and $stack[0] -eq '(L&R)') 'Actual cleanup return discards a producer failure.';$verified++}
            else{Assert ($method.Name -ceq 'ReleaseDisabledRendering' -and $stack[0] -eq '1') 'Unexpected cleanup bypass return.'}
            continue
        }
        if($code -eq 'Dup'){$stack.Add($stack[$stack.Count-1])}
        elseif($code -match '^Stloc(?:_[0-3]|_S)?$'){$index=LocalIndex $i 'St';$local[$index]=$stack[$stack.Count-1];$stack.RemoveAt($stack.Count-1)}
        elseif($code -match '^Ldloc(?:_[0-3]|_S)?$'){$stack.Add($local[(LocalIndex $i 'Ld')])}
        elseif($code -eq 'And'){
            Assert ($stack.Count -ge 2) 'Conjunction stack underflow.';$b=$stack[$stack.Count-1];$stack.RemoveAt($stack.Count-1);$a=$stack[$stack.Count-1];$stack.RemoveAt($stack.Count-1)
            $stack.Add('('+((@($a,$b)|Sort-Object)-join '&')+')')
        }
        elseif($code -in @('Call','Callvirt','Newobj')){
            $pop=$i.Operand.Parameters.Count;if($code -ne 'Newobj' -and $i.Operand.HasThis){$pop++}
            Assert ($stack.Count -ge $pop) 'Call stack underflow.';for($n=0;$n -lt $pop;$n++){$stack.RemoveAt($stack.Count-1)}
            if($code -eq 'Newobj' -or $i.Operand.ReturnType.FullName -cne 'System.Void'){
                $value='?';if($i -eq $left[0]){$value='L';$seenLeft=$true};if($i -eq $right[0]){$value='R';$seenRight=$true};$stack.Add($value)
            }
        }
        else{
            $popKind=[string]$i.OpCode.StackBehaviourPop;$pushKind=[string]$i.OpCode.StackBehaviourPush
            Assert ($popKind -ne 'Varpop' -and $pushKind -ne 'Varpush') "Unsupported variable-stack instruction $code."
            $pop=if($popKind -eq 'Pop0'){0}else{($popKind -split '_').Count}
            Assert ($stack.Count -ge $pop) "Stack underflow at $code.";for($n=0;$n -lt $pop;$n++){$stack.RemoveAt($stack.Count-1)}
            $push=if($pushKind -eq 'Push0'){0}else{($pushKind -split '_').Count}
            for($n=0;$n -lt $push;$n++){$stack.Add($(if($code -eq 'Ldc_I4_1'){'1'}elseif($code -eq 'Ldc_I4_0'){'0'}else{'?'}))}
        }
        $flow=[string]$i.OpCode.FlowControl
        if($flow -eq 'Throw'){continue}
        $next=if($flow -eq 'Branch'){@($i.Operand)}elseif($flow -eq 'Cond_Branch'){@($i.Operand)+@($i.Next)}else{@($i.Next)}
        foreach($at in $next){Assert ($null -ne $at) 'Control flow escaped without a return.';$queue.Enqueue(@{At=$at;Stack=$stack.ToArray();Locals=@($local.Clone());Left=$seenLeft;Right=$seenRight})}
    }
    Assert ($verified -gt 0) 'No actual cleanup result path reached a verified conjunction.'
}
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
    Test 'compiled authoring admission is independent of rendering configuration and its disable latch' {
        $local=Method $plugin 'CardPackLocalAllowed'
        $fields=@($local.Body.Instructions|Where-Object{$_.OpCode.Code -eq 'Ldfld'}|ForEach-Object{$_.Operand.Name})
        Assert ('_enabled' -notin $fields -and '_disabledForSession' -notin $fields) 'Rendering state still permanently closes authoring.'
        foreach($required in @('_cardPacksEnabled','_exportSuspended')){Assert ($required -in $fields) "Missing local guard: $required"}
        $addresses=@($local.Body.Instructions|Where-Object{$_.OpCode.Code -eq 'Ldflda'}|ForEach-Object{$_.Operand.Name})
        foreach($required in @('_cardStopping','_cardFaulted','_cardExportHold','_cardRenderingTransition')){Assert ($required -in $addresses) "Missing atomic admission guard: $required"}
    }
    Test 'both actual interval failure branches route through the verified conflict classifier' {
        foreach($name in @('Tick','LateTick')){
            $method=Method $plugin $name
            $intervalCalls=@(Calls $method 'TryApply'|Where-Object{$_.Operand.DeclaringType.FullName -ceq 'Azurite.Core.RenderIntervalLease'})
            Assert ($intervalCalls.Count -eq 1 -and @(Calls $method 'DisableForIntervalFailure').Count -eq 1) "$name lost the actual interval failure route."
            $branch=$intervalCalls[0].Next;$failure=@(Calls $method 'DisableForIntervalFailure')[0]
            Assert ($branch.OpCode.Code -in @('Brtrue','Brtrue_S') -and $failure.Offset -gt $branch.Offset -and $failure.Offset -lt $branch.Operand.Offset) "$name no longer stops rendering only on TryApply failure."
            Assert ($failure.Next.OpCode.Code -eq 'Ret') "$name continues rendering after releasing its owned settings."
        }
        $route=Method $plugin 'DisableForIntervalFailure'
        Before $route 'IsReleasedCardPackIntervalConflict' 'DisableForSession'
        $classifier=@(Calls $route 'IsReleasedCardPackIntervalConflict')[0];$disable=@(Calls $route 'DisableForSession')[0]
        Assert ($classifier.Next -eq $disable) 'Classifier result is not the renderingOnly argument.'
        $proof=Method $plugin 'IsReleasedCardPackIntervalConflict'
        foreach($name in @('get_IsBlocked','get_IsOwned','get_Failure')){
            $calls=@(Calls $proof $name);Assert ($calls.Count -eq 1 -and $calls[0].Operand.DeclaringType.FullName -ceq 'Azurite.Core.RenderIntervalLease') "Conflict proof is not bound to the actual lease $name."
        }
    }
    Test 'critical stop callers retain the default false exemption including late escalation' {
        $critical=@($plugin.Methods|Where-Object{$_.HasBody}|ForEach-Object{
            $caller=$_;foreach($call in @(Calls $caller 'DisableForSession')){if($caller.Name -ne 'DisableForIntervalFailure'){[pscustomobject]@{Caller=$caller;Call=$call}}}
        })
        Assert ($critical.Count -eq 3) 'Unexpected critical stop call-site set.'
        foreach($site in $critical){Assert ($site.Call.Previous.OpCode.Code -eq 'Ldc_I4_0') "Critical caller $($site.Caller.Name) grants a rendering-only exemption."}
        $disable=Method $plugin 'DisableForSession'
        Assert (@(Calls $disable 'RunCardPackRenderingCleanup').Count -eq 1) 'Critical stop does not enter the authoring cleanup guard.'
        Assert (@($disable.Body.Instructions|Where-Object{$_.OpCode.Code -eq 'Ldfld' -and $_.Operand.Name -ceq '_disabledForSession'}).Count -eq 0) 'Render latch can swallow later critical failure.'
        $delegates=@($disable.Body.Instructions|Where-Object{$_.OpCode.Code -eq 'Ldftn'}|ForEach-Object{$_.Operand.Resolve()})
        Assert ($delegates.Count -eq 1 -and @(Calls $delegates[0] 'ReleaseDisabledRendering').Count -eq 1) 'Cleanup guard does not execute the actual renderer release.'
    }
    Test 'configuration disable executes actual cleanup under the temporary admission guard' {
        $tick=Method $plugin 'Tick';$guard=@(Calls $tick 'RunCardPackRenderingCleanup')
        Assert ($guard.Count -eq 1 -and $guard[0].Previous.OpCode.Code -eq 'Ldc_I4_1') 'Configuration disable is not an explicit rendering-only cleanup.'
        Assert (@($tick.Body.Instructions|Where-Object{$_.OpCode.Code -eq 'Ldftn' -and $_.Operand.Name -ceq 'DisableConfiguredRendering'}).Count -eq 1) 'Configuration cleanup delegate is not the actual production method.'
        $cleanup=Method $plugin 'DisableConfiguredRendering';Before $cleanup 'RestoreRendering' 'Wake'
        Assert ($cleanup.ReturnType.FullName -ceq 'System.Boolean') 'Restoration result is not propagated to the guard.'
        $guardMethod=Method $plugin 'RunCardPackRenderingCleanup'
        Assert (@(Calls $guardMethod 'Increment').Count -eq 1 -and @(Calls $guardMethod 'Decrement').Count -eq 1) 'Temporary hold is missing.'
        Assert (@($guardMethod.Body.ExceptionHandlers|Where-Object HandlerType -EQ 'Finally').Count -eq 1) 'Cleanup hold has no finally release.'
        Assert (@(Calls $guardMethod 'CardPackFault').Count -eq 3) 'Critical, unsuccessful or thrown cleanup no longer closes authoring.'
    }
    Test 'Source restoration exempts only the released interval and keeps all other lease restores' {
        $restore=Method $plugin 'RestoreRendering'
        $restores=@(Calls $restore 'Restore')
        Assert ($restores.Count -eq 5) 'An owned rendering restore disappeared.'
        foreach($type in @('PreviewCameraCadence','PreviewTargetLease','RenderIntervalLease','RenderScaleLease','EditorBloomLease')){
            Assert (@($restores|Where-Object{$_.Operand.DeclaringType.Name -ceq $type}).Count -eq 1) "Missing actual $type restore."
        }
        $interval=@($restores|Where-Object{$_.Operand.DeclaringType.Name -ceq 'RenderIntervalLease'})[0]
        $classifier=@(Calls $restore 'IsReleasedCardPackIntervalConflict')
        Assert ($classifier.Count -eq 1 -and $interval.Offset -lt $classifier[0].Offset) 'Interval result is not checked against the released-conflict proof.'
        Assert (@(Calls (Method $stable 'RestoreRendering') 'Restore').Count -eq 5) 'Original rendering restore calls changed.'
    }
    Test 'Original does not reference the new source Core failure API' {
        $newApi=@($original.MainModule.GetMemberReferences()|Where-Object{
            $_ -is [Mono.Cecil.MethodReference] -and $_.DeclaringType.FullName -ceq 'Azurite.Core.RenderIntervalLease' -and $_.Name -ceq 'get_Failure'
        })
        Assert ($newApi.Count -eq 0) 'Original references source-only failure classification.'
        foreach($name in @('DisableForSession','DisableForIntervalFailure','RestoreRendering','DisableConfiguredRendering')){
            $method=Method $stable $name
            Assert (@(Calls $method 'RunCardPackRenderingCleanup').Count -eq 0 -and @(Calls $method 'IsReleasedCardPackIntervalConflict').Count -eq 0) 'Source-only authoring helper leaked into Original.'
        }
    }
    Test 'both actual Plugin stop paths conjoin FPS scroll and rendering cleanup results' {
        foreach($name in @('DisableConfiguredRendering','ReleaseDisabledRendering')){
            $method=Method $plugin $name
            AssertConjoinedCallResults $method 'ReleaseCardPackPerformanceControllers' 'RestoreRendering'
            $controllers=@(Calls $method 'ReleaseCardPackPerformanceControllers')[0]
            $wanted=if($name -ceq 'DisableConfiguredRendering'){'Ldc_I4_0'}else{'Ldc_I4_1'}
            Assert ($controllers.Previous.OpCode.Code -eq $wanted) 'Wrong scroll restore/dispose route.'
        }
        Before (Method $plugin 'DisableConfiguredRendering') 'ReleaseCardPackPerformanceControllers' 'RestoreRendering'
        Before (Method $plugin 'ReleaseDisabledRendering') 'RestoreRendering' 'ReleaseCardPackPerformanceControllers'
    }
    Test 'actual controller aggregation binds result-returning FPS and scroll helpers before dropping references' {
        $method=Method $plugin 'ReleaseCardPackPerformanceControllers'
        $fps=@(Calls $method 'TryDispose'|Where-Object{$_.Operand.DeclaringType.FullName -ceq 'Azurite.FpsController'})
        $scrollDispose=@(Calls $method 'TryDispose'|Where-Object{$_.Operand.DeclaringType.FullName -ceq 'Azurite.EditorScrollTuning'})
        $scrollRestore=@(Calls $method 'TryRestore'|Where-Object{$_.Operand.DeclaringType.FullName -ceq 'Azurite.EditorScrollTuning'})
        Assert ($fps.Count -eq 1 -and $scrollDispose.Count -eq 1 -and $scrollRestore.Count -eq 1) 'Real cleanup result producer is missing.'
        $fpsClear=@($method.Body.Instructions|Where-Object{$_.OpCode.Code -eq 'Stfld' -and $_.Operand.Name -ceq '_fps'})
        $scrollClear=@($method.Body.Instructions|Where-Object{$_.OpCode.Code -eq 'Stfld' -and $_.Operand.Name -ceq '_scroll'})
        Assert ($fpsClear.Count -eq 1 -and $fps[0].Offset -lt $fpsClear[0].Offset) 'FPS reference is dropped before its cleanup result.'
        Assert ($scrollClear.Count -eq 1 -and $scrollDispose[0].Offset -lt $scrollClear[0].Offset -and $scrollRestore[0].Offset -lt $scrollClear[0].Offset) 'Scroll reference is dropped before its cleanup result.'
        Assert (@($method.Body.Instructions|Where-Object{$_.OpCode.Code -eq 'And' -and $_.Next.OpCode.Code -eq 'Ret'}).Count -eq 1) 'FPS and scroll results are not combined.'
    }
    Test 'both modes retain legacy cleanup wrappers and FPS event and ownership release order' {
        foreach($assembly in @($source,$original)){
            $fps=@($assembly.MainModule.Types|Where-Object FullName -ceq 'Azurite.FpsController')[0]
            $scroll=@($assembly.MainModule.Types|Where-Object FullName -ceq 'Azurite.EditorScrollTuning')[0]
            $dispose=Method $fps 'Dispose';$restore=Method $scroll 'Restore';$scrollDispose=Method $scroll 'Dispose'
            Assert ($dispose.ReturnType.FullName -ceq 'System.Void' -and @(Calls $dispose 'TryDispose').Count -eq 1) 'FPS Dispose compatibility changed.'
            Assert ($restore.ReturnType.FullName -ceq 'System.Void' -and @(Calls $restore 'TryRestore').Count -eq 1) 'Scroll Restore compatibility changed.'
            Assert ($scrollDispose.ReturnType.FullName -ceq 'System.Void' -and @(Calls $scrollDispose 'TryDispose').Count -eq 1) 'Scroll Dispose compatibility changed.'
            $tryDispose=Method $fps 'TryDispose';Before $tryDispose 'Unsubscribe' 'RestoreIdleFrameRate';Before $tryDispose 'RestoreIdleFrameRate' 'ReleaseOwned'
        }
    }
    @{passed=$passed;failed=$failed;checks=@($results.ToArray());authoringSha256=(Get-FileHash -LiteralPath $AuthoringAssembly -Algorithm SHA256).Hash.ToLowerInvariant();originalSha256=(Get-FileHash -LiteralPath $OriginalAssembly -Algorithm SHA256).Hash.ToLowerInvariant();evidence='compiled IL routing only; complements production partial behavior tests';nativeExecuted=$false} | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $EvidenceDirectory 'result.json')
}finally{$source.Dispose();$original.Dispose()}
if($failed){throw "$failed compiled wiring checks failed."}
"$passed compiled wiring checks passed."
