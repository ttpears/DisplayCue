$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$buildOutput = Join-Path $repoRoot 'outputs'
dotnet publish (Join-Path $PSScriptRoot 'MonitorHotkeys.csproj') -c Release -r win-x64 --self-contained true -o $buildOutput
if ($LASTEXITCODE -ne 0) { throw "DisplayCue build failed with exit code $LASTEXITCODE." }
Write-Output "Built $(Join-Path $buildOutput 'DisplayCue.exe')"
