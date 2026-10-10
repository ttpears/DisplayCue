$ErrorActionPreference = 'Stop'
# Includes authenticated loopback requests with an in-memory key; preserves the real pairing key.
dotnet run --project (Join-Path $PSScriptRoot '..\tests\DisplayCue.Tests.csproj') -c Release -r win-x64
if ($LASTEXITCODE -ne 0) { throw 'DisplayCue handoff/peer regression checks failed.' }
