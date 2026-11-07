param([ValidateSet('Success', 'WrongPid', 'Sleep', 'Flood', 'OrphanPipe', 'NativeSuccess', 'NativeMissingChecks', 'NativeWrongMode', 'NativeStringPid', 'NativeGraphics', 'NativeGraphicsBadHash', 'NativeGraphicsStaleFrame', 'NativeGraphicsTruncated', 'NativeGraphicsCorrupt')][string]$Behavior,
      [string]$HelperPidPath)
$ErrorActionPreference = 'Stop'
if ($Behavior -eq 'Sleep') { Start-Sleep -Seconds 30; exit 0 }
if ($Behavior -eq 'OrphanPipe') {
    $helperInfo = [Diagnostics.ProcessStartInfo]::new((Get-Command pwsh).Source)
    $helperInfo.UseShellExecute = $false
    $helperInfo.CreateNoWindow = $true
    foreach ($argument in @('-NoProfile', '-File', $PSCommandPath, '-Behavior', 'Sleep')) { [void]$helperInfo.ArgumentList.Add($argument) }
    $helper = [Diagnostics.Process]::Start($helperInfo)
    $helper.Id | Set-Content -LiteralPath $HelperPidPath
    exit 0
}
[Console]::Out.WriteLine('fixture stdout')
[Console]::Error.WriteLine('fixture stderr')
if ($Behavior -eq 'Flood') {
    [Console]::Out.WriteLine(('O' * 131072))
    [Console]::Error.WriteLine(('E' * 131072))
}
$reportedPid = $PID
if ($Behavior -eq 'WrongPid') { $reportedPid++ }
$result = @{ runId = $env:AZURITE_RUN_ID; pid = $reportedPid; status = 'passed' }
if ($Behavior -like 'Native*') {
    $result.mode = $env:AZURITE_MODE
    if ($Behavior -eq 'NativeWrongMode') { $result.mode = 'Graphics' }
    $result.scenario = $env:AZURITE_SCENARIO
    $result.accountSid = $env:AZURITE_ACCOUNT_SID
    $result.checks = @(@{ name = 'host.binding'; passed = $true }, @{ name = 'plugin.lifecycle'; passed = $true })
    if ($Behavior -ne 'NativeMissingChecks') { $result.checks += @{ name = 'logic.assertions'; passed = $true } }
    if ($Behavior -eq 'NativeStringPid') { $result.pid = [string]$PID }
    if ($Behavior -like 'NativeGraphics*') {
        $result.checks = @(@{ name = 'host.binding'; passed = $true }, @{ name = 'render.frame'; passed = $true })
        $framePath = Join-Path (Split-Path -Parent $env:AZURITE_RESULT_PATH) 'frame.png'
        $frameBytes = [Convert]::FromBase64String('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+ip1sAAAAASUVORK5CYII=')
        if ($Behavior -eq 'NativeGraphicsTruncated') { $frameBytes = [byte[]]@(137,80,78,71,13,10,26,10,0,0,0,13,73,72,68,82,0,0,0,1,0,0,0,1,8) }
        if ($Behavior -eq 'NativeGraphicsCorrupt') { $frameBytes = [Convert]::FromBase64String('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8AB8AAwMCAH2U7WsAAAAASUVORK5CYII=') }
        [IO.File]::WriteAllBytes($framePath, $frameBytes)
        if ($Behavior -eq 'NativeGraphicsStaleFrame') { (Get-Item -LiteralPath $framePath).LastWriteTimeUtc = [datetime]::UtcNow.AddHours(-1) }
        $hash = (Get-FileHash -LiteralPath $framePath -Algorithm SHA256).Hash
        if ($Behavior -eq 'NativeGraphicsBadHash') { $hash = '0' * 64 }
        $result.frame = @{ path = 'frame.png'; bytes = (Get-Item -LiteralPath $framePath).Length; sha256 = $hash }
    }
}
$result |
    ConvertTo-Json -Compress | Set-Content -LiteralPath $env:AZURITE_RESULT_PATH -Encoding utf8
