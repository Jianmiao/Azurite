param([Parameter(Mandatory)][string]$AzuriteRunId, [Parameter(Mandatory)][string]$AzuriteResultPath)
$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($AzuriteRunId) -or [string]::IsNullOrWhiteSpace($AzuriteResultPath)) { throw 'Preflight run metadata is missing.' }
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
$profile = [Environment]::GetFolderPath([Environment+SpecialFolder]::UserProfile)
$localAppData = [Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class AzuriteKnownFolders {
    [DllImport("shell32.dll")] private static extern int SHGetKnownFolderPath(ref Guid id, uint flags, IntPtr token, out IntPtr path);
    public static string LocalAppDataLow() {
        Guid id = new Guid("A520A1A4-1780-4FF6-BD18-167343C5AF16");
        IntPtr path;
        int result = SHGetKnownFolderPath(ref id, 0, IntPtr.Zero, out path);
        if (result != 0) Marshal.ThrowExceptionForHR(result);
        try { return Marshal.PtrToStringUni(path); }
        finally { Marshal.FreeCoTaskMem(path); }
    }
}
'@
$hive = [Microsoft.Win32.Registry]::Users.OpenSubKey($identity.User.Value)
$hiveLoaded = $null -ne $hive
if ($hive) { $hive.Dispose() }
$whoami = Join-Path ([Environment]::SystemDirectory) 'whoami.exe'
$privilegeLines = @(& $whoami /priv /fo csv /nh)
if ($LASTEXITCODE -ne 0) { throw 'Could not inspect child token privileges.' }
$privileges = @($privilegeLines | ForEach-Object {
    if ($_ -match '^"(Se[A-Za-z0-9]+Privilege)"') { $matches[1] }
})
if ($privileges.Count -eq 0) { throw 'Child token privilege inspection returned no entries.' }
$ordinaryPrivileges = @('SeChangeNotifyPrivilege', 'SeShutdownPrivilege', 'SeUndockPrivilege', 'SeIncreaseWorkingSetPrivilege', 'SeTimeZonePrivilege')
$unexpectedPrivileges = @($privileges | Where-Object { $_ -notin $ordinaryPrivileges })
@{
    runId = $AzuriteRunId
    pid = $PID
    status = 'passed'
    identitySid = $identity.User.Value
    isAdministrator = $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
    unexpectedPrivileges = $unexpectedPrivileges
    userProfile = $profile
    localAppData = $localAppData
    localLow = [AzuriteKnownFolders]::LocalAppDataLow()
    environmentProfile = $env:USERPROFILE
    environmentLocalAppData = $env:LOCALAPPDATA
    environmentAppData = $env:APPDATA
    environmentTemp = $env:TEMP
    environmentTmp = $env:TMP
    hiveLoaded = $hiveLoaded
    audioIsolationVerified = $false
} | ConvertTo-Json -Compress | Set-Content -LiteralPath $AzuriteResultPath -Encoding utf8
