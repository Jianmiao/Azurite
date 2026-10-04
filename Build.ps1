param([string]$AAInstallPath = 'F:\AzureArchive_100_fix')
$ErrorActionPreference = 'Stop'
dotnet build (Join-Path $PSScriptRoot 'Plugin/Azurite.csproj') -c Release --nologo ('-p:AAInstallPath=' + $AAInstallPath)
if ($LASTEXITCODE -ne 0) { throw 'Azurite build failed.' }
