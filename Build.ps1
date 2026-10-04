param([Parameter(Mandatory=$true)][string]$AAInstallPath)
$ErrorActionPreference = 'Stop'
dotnet build (Join-Path $PSScriptRoot 'Plugin/Azurite.csproj') -c Release --nologo ('-p:AAInstallPath=' + $AAInstallPath)
if ($LASTEXITCODE -ne 0) { throw 'Azurite build failed.' }
